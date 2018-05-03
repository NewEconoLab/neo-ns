using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using Helper = Neo.SmartContract.Framework.Helper;
using System;
using System.ComponentModel;
using System.Numerics;

namespace Nep5_Contract
{
    public class ContractNep55Gas : SmartContract
    {
        //在nep5标准上追加几个要求，暂定nep5.5标准
        //1.对接口("transfer",[from,to,value]) 检查 entry 和 callscript 一致性，禁止跳板
        //2.追加接口("transfer_app",[from,to,value])，当from == callscript 时通过,将鉴权扩展到应用合约
        //3.追加接口("gettxinfo",[txid]),返回[from,to,value],每笔转账都写入记录，使用当前txid做key
        //    智能合约可以用此接口检查一笔NEP5转账的详情，只能查到已经发生过的交易

        //nep5.5gas 加入用gas兑换的部分，和退回gas的功能
        //4.追加接口("mintTokens",[])，自动将当前交易的输出中的GAS兑换为等量的该NEP5资产
        //逻辑和mintTokens是一样的，就保持一致吧
        //5.追加接口("exchangeUTXO",[who]),自动将当前交易输出中的gas，标记为who可提取，同时销毁who的等量NEP5资产
        //      之后可发起一笔转账 input为这个标记的utxo，output 为who，取走其中的GAS

        //storage1 map<address:hash160,balancce:biginteger>     //nep5余额表
        //storage2 map<txid:hash256,balance:txinfo>             //nep5交易信息表
        //storage3 map<utxo:hash256+n,targetaddress:hash256>    //utxo授权表

        //nep5 notify
        public delegate void deleTransfer(byte[] from, byte[] to, BigInteger value);
        [DisplayName("transfer")]
        public static event deleTransfer Transferred;

        //gas 0x602c79718b16e442de58778e148d0b1084e3b2dffd5de6b7b16cee7969282de7
        //反序  e72d286979ee6cb1b7e65dfddfb2e384100b8d148e7758de42e4168b71792c60
        private static readonly byte[] gas_asset_id = Helper.HexToBytes("e72d286979ee6cb1b7e65dfddfb2e384100b8d148e7758de42e4168b71792c60");
        //nep5 func
        public static BigInteger totalSupply()
        {
            return Storage.Get(Storage.CurrentContext, "totalSupply").AsBigInteger();
        }
        public static string name()
        {
            return "NEP5.5 Coin With GAS 1:1";
        }
        public static string symbol()
        {
            return "SGAS";
        }
        private const ulong factor = 100000000;
        private const ulong totalCoin = 100000000 * factor;
        public static byte decimals()
        {
            return 8;
        }
        public static BigInteger balanceOf(byte[] address)
        {
            return Storage.Get(Storage.CurrentContext, address).AsBigInteger();
        }
        public static bool transfer(byte[] from, byte[] to, BigInteger value)
        {
            if (value <= 0) return false;

            if (from == to) return true;

            //付款方
            if (from.Length > 0)
            {
                BigInteger from_value = Storage.Get(Storage.CurrentContext, from).AsBigInteger();
                if (from_value < value) return false;
                if (from_value == value)
                    Storage.Delete(Storage.CurrentContext, from);
                else
                    Storage.Put(Storage.CurrentContext, from, from_value - value);
            }
            //收款方
            if (to.Length > 0)
            {
                BigInteger to_value = Storage.Get(Storage.CurrentContext, to).AsBigInteger();
                Storage.Put(Storage.CurrentContext, to, to_value + value);
            }
            //记录交易信息
            setTxInfo(from, to, value);
            //notify
            Transferred(from, to, value);
            return true;
        }

        public class TransferInfo
        {
            public byte[] from;
            public byte[] to;
            public BigInteger value;
        }
        //函数进栈损耗太大，不要这么实现了
        //private static byte[] byteLen(BigInteger n)
        //{
        //    byte[] v = n.AsByteArray();
        //    if (v.Length > 2)
        //        throw new Exception("not support");
        //    if (v.Length < 2)
        //        v = v.Concat(new byte[1] { 0x00 });
        //    if (v.Length < 2)
        //        v = v.Concat(new byte[1] { 0x00 });
        //    return v;
        //}
        public static TransferInfo getTXInfo(byte[] txid)
        {
            byte[] v = Storage.Get(Storage.CurrentContext, txid);
            if (v.Length == 0)
                return null;

            //老式实现方法
            TransferInfo info = new TransferInfo();
            int seek = 0;
            var fromlen = (int)v.Range(seek, 2).AsBigInteger();
            seek += 2;
            info.from = v.Range(seek, fromlen);
            seek += fromlen;
            var tolen = (int)v.Range(seek, 2).AsBigInteger();
            seek += 2;
            info.to = v.Range(seek, tolen);
            seek += tolen;
            var valuelen = (int)v.Range(seek, 2).AsBigInteger();
            seek += 2;
            info.value = v.Range(seek, valuelen).AsBigInteger();
            return info;

            //新式实现方法只要一行
            // return Helper.Deserialize(v) as TransferInfo;
        }
        //把doublezero定义出来就好了，...... 要查编译器了
        static readonly byte[] doublezero = new byte[2] { 0x00, 0x00 };
        private static void setTxInfo(byte[] from, byte[] to, BigInteger value)
        {
            //因为testnet 还在2.6，限制

            TransferInfo info = new TransferInfo();
            info.from = from;
            info.to = to;
            info.value = value;

            //用一个老式实现法

            //优化的拼包方法

            var data = info.from;
            var lendata = ((BigInteger)data.Length).AsByteArray().Concat(doublezero).Range(0, 2);
            //lendata是数据长度得bytearray，因为bigint长度不固定，统一加两个零，然后只取前面两个字节
            //为什么要两个字节，因为bigint是含有符号位得，统一加个零安全，要不然长度129取一个字节就是负数了
            var txinfo = lendata.Concat(data);

            data = info.to;
            lendata = ((BigInteger)data.Length).AsByteArray().Concat(doublezero).Range(0, 2);
            txinfo = txinfo.Concat(lendata).Concat(data);

            data = value.AsByteArray();
            lendata = ((BigInteger)data.Length).AsByteArray().Concat(doublezero).Range(0, 2);
            txinfo = txinfo.Concat(lendata).Concat(data);
            //新式实现方法只要一行
            //byte[] txinfo = Helper.Serialize(info);

            var txid = (ExecutionEngine.ScriptContainer as Transaction).Hash;
            Storage.Put(Storage.CurrentContext, txid, txinfo);
        }
        public static bool mintTokens()
        {
            var tx = ExecutionEngine.ScriptContainer as Transaction;

            //获取投资人，谁要换gas
            byte[] who = null;
            TransactionOutput[] reference = tx.GetReferences();
            for (var i = 0; i < reference.Length; i++)
            {
                if (reference[i].AssetId.AsBigInteger() == gas_asset_id.AsBigInteger())
                {
                    who = reference[i].ScriptHash;
                    break;
                }
            }

            TransactionOutput[] outputs = tx.GetOutputs();
            ulong value = 0;
            // get the total amount of Gas
            // 获取转入智能合约地址的Gas总量
            foreach (TransactionOutput output in outputs)
            {
                if (output.ScriptHash == ExecutionEngine.ExecutingScriptHash &&
                    output.AssetId.AsBigInteger() == gas_asset_id.AsBigInteger())
                {
                    value += (ulong)output.Value;
                }
            }

            //改变总量
            var total_supply = Storage.Get(Storage.CurrentContext, "totalSupply").AsBigInteger();
            total_supply += value;
            Storage.Put(Storage.CurrentContext, "totalSupply", total_supply);

            //1:1 不用换算
            return transfer(null, who, value);

        }
        //退款
        public static bool refund(byte[] who)
        {
            var tx = ExecutionEngine.ScriptContainer as Transaction;
            var outputs = tx.GetOutputs();
            //退的不是gas，不行
            if (outputs[0].AssetId.AsBigInteger() != gas_asset_id.AsBigInteger())
                return false;
            //不是转给自身，不行
            if (outputs[0].ScriptHash.AsBigInteger() != ExecutionEngine.ExecutingScriptHash.AsBigInteger())
                return false;


            //当前的交易已经名花有主了，不行
            byte[] target = getRefundTarget(tx.Hash);
            if (target.Length > 0)
                return false;

            //尝试销毁一定数量的金币
            var count = outputs[0].Value;
            bool b = transfer(who, null, count);
            if (!b)
                return false;

            //标记这个utxo归我所有
            byte[] coinid = tx.Hash.Concat(new byte[] { 0, 0 });
            Storage.Put(Storage.CurrentContext, coinid, who);
            //改变总量
            var total_supply = Storage.Get(Storage.CurrentContext, "totalSupply").AsBigInteger();
            total_supply -= count;
            Storage.Put(Storage.CurrentContext, "totalSupply", total_supply);

            return true;
        }
        public static byte[] getRefundTarget(byte[] txid)
        {
            byte[] coinid = txid.Concat(new byte[] { 0, 0 });
            byte[] target = Storage.Get(Storage.CurrentContext, coinid);
            return target;
        }
        public static object Main(string method, object[] args)
        {
            var magicstr = "2018-04-10";

            if (Runtime.Trigger == TriggerType.Verification)//取钱才会涉及这里
            {
                //return Runtime.CheckWitness(SuperAdmin);
                var tx = ExecutionEngine.ScriptContainer as Transaction;
                var curhash = ExecutionEngine.ExecutingScriptHash;
                var inputs = tx.GetInputs();
                var outputs = tx.GetOutputs();

                //检查输入是不是有被标记过
                for (var i = 0; i < inputs.Length; i++)
                {
                    byte[] coinid = inputs[i].PrevHash.Concat(new byte[] { 0, 0 });
                    if (inputs[i].PrevIndex == 0)//如果utxo n为0 的话，是有可能是一个标记utxo的
                    {
                        byte[] target = Storage.Get(Storage.CurrentContext, coinid);
                        if (target.Length > 0)
                        {
                            if (inputs.Length > 1 || outputs.Length != 1)//使用标记coin的时候只允许一个输入\一个输出
                                return false;

                            //如果只有一个输入，一个输出，并且目的转账地址就是授权地址
                            //允许转账
                            if (outputs[0].ScriptHash.AsBigInteger() == target.AsBigInteger())
                                return true;
                            else//否则不允许
                                return false;
                        }
                    }
                }
                //走到这里没跳出，说明输入都没有被标记
                var refs = tx.GetReferences();
                BigInteger inputcount = 0;
                for (var i = 0; i < refs.Length; i++)
                {
                    if (refs[i].AssetId.AsBigInteger() != gas_asset_id.AsBigInteger())
                        return false;//不允许操作除gas以外的

                    if (refs[i].ScriptHash.AsBigInteger() != curhash.AsBigInteger())
                        return false;//不允许混入其它地址

                    inputcount += refs[i].Value;
                }
                //检查有没有钱离开本合约
                BigInteger outputcount = 0;
                for (var i = 0; i < outputs.Length; i++)
                {
                    if (outputs[i].ScriptHash.AsBigInteger() != curhash.AsBigInteger())
                    {
                        return false;
                    }
                    outputcount += outputs[i].Value;
                }
                if (outputcount != inputcount)
                    return false;
                //没有资金离开本合约地址，允许
                return true;
            }
            else if (Runtime.Trigger == TriggerType.Application)
            {
                //必须在入口函数取得callscript，调用脚本的函数，也会导致执行栈变化，再取callscript就晚了
                var callscript = ExecutionEngine.CallingScriptHash;

                //this is in nep5
                if (method == "totalSupply") return totalSupply();
                if (method == "name") return name();
                if (method == "symbol") return symbol();
                if (method == "decimals") return decimals();
                if (method == "balanceOf")
                {
                    if (args.Length != 1) return 0;
                    byte[] account = (byte[])args[0];
                    return balanceOf(account);
                }
                if (method == "transfer")
                {
                    if (args.Length != 3) return false;
                    byte[] from = (byte[])args[0];
                    byte[] to = (byte[])args[1];
                    if (from == to)
                        return true;
                    if (from.Length == 0 || to.Length == 0)
                        return false;


                    BigInteger value = (BigInteger)args[2];

                    //没有from签名，不让转
                    if (!Runtime.CheckWitness(from))
                        return false;
                    //如果有跳板调用，不让转
                    if (ExecutionEngine.EntryScriptHash.AsBigInteger() != callscript.AsBigInteger())
                        return false;

                    return transfer(from, to, value);
                }
                if (method == "transfer_app")
                {
                    if (args.Length != 3) return false;
                    byte[] from = (byte[])args[0];
                    byte[] to = (byte[])args[1];
                    BigInteger value = (BigInteger)args[2];

                    //如果from 不是 传入脚本 不让转
                    if (from.AsBigInteger() != callscript.AsBigInteger())
                        return false;

                    return transfer(from, to, value);
                }
                if (method == "getTXInfo")
                {
                    if (args.Length != 1) return 0;
                    byte[] txid = (byte[])args[0];
                    return getTXInfo(txid);
                }
                if (method == "mintTokens")
                {
                    if (args.Length != 0) return 0;
                    return mintTokens();
                }
                if (method == "refund")
                {
                    if (args.Length != 1) return 0;
                    byte[] who = (byte[])args[0];
                    if (!Runtime.CheckWitness(who))
                        return false;
                    return refund(who);
                }
                if (method == "getRefundTarget")
                {
                    if (args.Length != 1) return 0;
                    byte[] hash = (byte[])args[0];
                    return getRefundTarget(hash);
                }
            }
            return false;
        }

    }
}
