using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using Helper = Neo.SmartContract.Framework.Helper;
using System;
using System.ComponentModel;
using System.Numerics;

namespace Nep5_Contract
{
    public class ContractCoinPool : SmartContract
    {

        //nep5 资金池是一个存放SGAS的合约
        //存放在这个资金池里面的SGAS资产，需要登记存在某个块上
        //然后根据用户持有的NNC,进行分配


        //UTXO NNC  c12c6ccc5be5235b90822c4feee70645b9d0bac0636b07bd1d68e34ba8804747
        //反序 474780a84be3681dbd076b63c0bad0b94506e7ee4f2c82905b23e55bcc6c2cc1
        private static readonly byte[] utxo_nnc_id = Helper.HexToBytes("474780a84be3681dbd076b63c0bad0b94506e7ee4f2c82905b23e55bcc6c2cc1");
        //nep5 func
        //SGAS合约地址
        [Appcall("4ac464f84f50d3f902c2f0ca1658bfaa454ddfbf")]
        static extern object sgasCall(string method, object[] arr);

        public class TransferInfo
        {
            public byte[] from;
            public byte[] to;
            public BigInteger value;
        }

        static TransferInfo getTxIn(byte[] txid)
        {
            var keytx = new byte[] { 0x12 }.Concat(txid);
            var v = Storage.Get(Storage.CurrentContext, keytx).AsBigInteger();
            if (v == 0)//如果這個交易已經處理過，就當get不到
            {
                object[] _p = new object[1];
                _p[0] = txid;
                var info = sgasCall("getTXInfo", _p);
                if (((object[])info).Length == 3)
                    return info as TransferInfo;
            }
            var tInfo = new TransferInfo();
            tInfo.from = new byte[0];
            return tInfo;
        }

        //dict<0x12+txid,num> num=1,已经标记过的转账
        //dict<0x11+blockid,num> 每个块上可以领的奖金，由于每个块都来处理量太大，暂定每一万块的奖金记录在一起 
        //dict<0x13+txid+n,num> 已经花费掉的
        static readonly byte[] quadZero = new byte[] { 0, 0, 0, 0 };
        public static bool setSGASIn(byte[] txid)
        {
            var tx = getTxIn(txid);
            if (tx.from.Length == 0)
                return false;
            if (tx.to.AsBigInteger() == ExecutionEngine.ExecutingScriptHash.AsBigInteger())
            {
                var keytx = new byte[] { 0x12 }.Concat(txid);
                var n = Storage.Get(Storage.CurrentContext, keytx).AsBigInteger();
                if (n == 1)//这笔txid已经被用掉了
                    return false;

                // 記錄這個txid處理過了，只處理一次
                Storage.Put(Storage.CurrentContext, keytx, 1);

                // 存錢,每隔多少块儿存一次
                var height = (Blockchain.GetHeight() / 10000) * 10000;
                var bheight = ((BigInteger)height).AsByteArray().Concat(quadZero).Range(0, 4);
                var key = new byte[] { 0x11 }.Concat(bheight);
                var money = Storage.Get(Storage.CurrentContext, key).AsBigInteger();
                money += tx.value;
                Storage.Put(Storage.CurrentContext, key, money);
            }
            return false;
        }

        public static BigInteger countSGASOnBlock(BigInteger blockid)
        {
            var height = (Blockchain.GetHeight() / 10000) * 10000;
            var bheight = ((BigInteger)height).AsByteArray().Concat(quadZero).Range(0, 4);
            var key = new byte[] { 0x11 }.Concat(bheight);
            var money = Storage.Get(Storage.CurrentContext, key).AsBigInteger();
            return money;
        }

        public static bool claim(uint fromheight, int fromindex, int n, uint toheight, int toindex, int inputN)
        {
            // 檢查輸出的資源對不對
            var starttx = Blockchain.GetBlock(fromheight).GetTransaction(fromindex);
            var output = starttx.GetOutputs()[n];
            if (Runtime.CheckWitness(output.ScriptHash) == false)//只能自己领取自己的
            {
                return false;
            }
            var endtx = Blockchain.GetBlock(toheight).GetTransaction(toindex);
            var input = endtx.GetInputs()[inputN];
            // 输入输出不匹配
            if (input.PrevIndex != n || input.PrevHash.AsBigInteger() != starttx.Hash.AsBigInteger())
            {
                return false;
            }
            // 资产种类不对
            if (output.AssetId.AsBigInteger() != utxo_nnc_id.AsBigInteger())
            {
                return false;
            }
            var binn = ((BigInteger)n).AsByteArray().Concat(quadZero).Range(0, 2);
            var utxokey = new byte[] { 0x13 }.Concat(starttx.Hash).Concat(binn);

            uint begin = (fromheight / 10000) * 10000;
            uint end = (toheight / 10000) * 10000;

            var hasClaim = Storage.Get(Storage.CurrentContext, utxokey).AsBigInteger();

            if (hasClaim >= end)// 领奖高度要是已经达到销毁高度，这个utxo无法再领取了
            {
                return false;
            }

            // begin 和 end 必须相差两个跨度，这个循环自动排除上面相等的情况
            // 限制循环长度
            if (hasClaim == 0)
                hasClaim = begin + 10000;
            var endClaim = (BigInteger)end;
            if ((endClaim - hasClaim) > 150000)//15个循环也就30天吧
                endClaim = hasClaim + 150000;
            BigInteger canget = 0;
            for (var i = hasClaim; i < endClaim; i += 10000)
            {
                var bheight = ((BigInteger)i).AsByteArray().Concat(quadZero).Range(0, 4);
                var key = new byte[] { 0x11 }.Concat(bheight);
                var money = Storage.Get(Storage.CurrentContext, key).AsBigInteger();//0.1gas

                //如果能领就领了他
                canget += money / 100000000 * output.Value;//money除以发行量
            }
            // 标记这个utxo领过奖了
            Storage.Put(Storage.CurrentContext, utxokey, endClaim);

            var info = new object[3];
            info[0] = ExecutionEngine.ExecutingScriptHash;
            info[1] = output.ScriptHash;
            info[2] = canget;
            sgasCall("transfer_app", info);
            return true;
        }
        public static object Main(string method, object[] args)
        {
            var magicstr = "2018-05-14";

            if (Runtime.Trigger == TriggerType.Verification)//取钱才会涉及这里
            {
                //不允许资金离开
                return false;
            }
            else if (Runtime.Trigger == TriggerType.Application)
            {
                //必须在入口函数取得callscript，调用脚本的函数，也会导致执行栈变化，再取callscript就晚了
                var callscript = ExecutionEngine.CallingScriptHash;

                //标记进钱
                if (method == "setSGASIn")
                {
                    var txid = (byte[])args[0];
                    return setSGASIn(txid);
                }
                //检查一个块里的钱
                if (method == "countSGASOnBlock")
                {
                    var blockid = (BigInteger)args[0];
                    return countSGASOnBlock(blockid);
                }
                //领取自己的
                if (method == "claim")
                {
                    //utxo产生信息
                    uint fromheight = (uint)args[0];
                    int fromindex = (int)args[1];
                    int n = (int)args[2];
                    //utxo销毁信息
                    uint toheight = (uint)args[3];
                    int toindex = (int)args[4];
                    int inputN = (int)args[5];
                    return claim(fromheight, fromindex, n, toheight, toindex, inputN);
                }

            }
            return false;
        }

    }
}
