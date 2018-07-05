using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using Helper = Neo.SmartContract.Framework.Helper;
using System;
using System.ComponentModel;
using System.Numerics;

namespace dapp_nnc
{
    public class nnc : SmartContract
    {
        /*存储结构有     
         * map(address,balance)   存储地址余额   key = 0x11+address
         * map(txid,TransferInfo) 存储交易详情   key = 0x13+txid
        */
        public delegate void deleTransfer(byte[] from, byte[] to, BigInteger value);
        [DisplayName("transfer")]
        public static event deleTransfer Transferred;

        public class TransferInfo
        {
            public byte[] from;
            public byte[] to;
            public BigInteger value;
        }

        static readonly byte[] superAdmin = Helper.ToScriptHash("ALjSnMZidJqd18iQaoCgFun6iqWRm2cVtj");//管理员
        static readonly byte[] doublezero = new byte[2] { 0x00, 0x00 };
        public static string name()
        {
            return "NEO Name Credit";
        }
        public static string symbol()
        {
            return "NNC";
        }
        private const ulong factor = 100;//精度2
        private const ulong totalCoin = 10 * 100000000 * factor;//发行量10亿
        public static byte decimals()
        {
            return 2;
        }
        //nnc 发行总量
        public static BigInteger totalSupply()
        {
            return Storage.Get(Storage.CurrentContext, "totalSupply").AsBigInteger();
        }

        public static BigInteger balanceOf(byte[] who)
        {
            var keyAddress = new byte[] { 0x11 }.Concat(who);
            return Storage.Get(Storage.CurrentContext, keyAddress).AsBigInteger();
        }

        public static bool transfer(byte[] from, byte[] to, BigInteger value)
        {
            if (from.Length != 20 || to.Length != 20)
                return false;

            if (value <= 0) return false;

            if (from == to) return true;

            //付款方
            if (from.Length > 0)
            {
                var keyFrom = new byte[] { 0x11 }.Concat(from);
                BigInteger from_value = Storage.Get(Storage.CurrentContext, keyFrom).AsBigInteger();
                if (from_value < value) return false;
                if (from_value == value)
                    Storage.Delete(Storage.CurrentContext, keyFrom);
                else
                    Storage.Put(Storage.CurrentContext, keyFrom, from_value - value);
            }
            //收款方
            if (to.Length > 0)
            {
                var keyTo = new byte[] { 0x11 }.Concat(to);
                BigInteger to_value = Storage.Get(Storage.CurrentContext, keyTo).AsBigInteger();
                Storage.Put(Storage.CurrentContext, keyTo, to_value + value);
            }
            //记录交易信息
            setTxInfo(from, to, value);
            //notify
            Transferred(from, to, value);
            return true;
        }
        private static void setTxInfo(byte[] from, byte[] to, BigInteger value)
        {
            TransferInfo info = new TransferInfo();
            info.from = from;
            info.to = to;
            info.value = value;

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
            var keytxid = new byte[] { 0x12 }.Concat(txid);
            Storage.Put(Storage.CurrentContext, keytxid, txinfo);
        }

        public static TransferInfo getTxInfo(byte[] txid)
        {
            byte[] keytxid = new byte[] { 0x13 }.Concat(txid);
            byte[] v = Storage.Get(Storage.CurrentContext, keytxid);
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

            //序列化暂时还不适用
            //return Helper.Deserialize(v) as TransferInfo;
        }

        public static object Main(string method, object[] args)
        {
            var magicstr = "20180628";

            if (Runtime.Trigger == TriggerType.Verification)//取钱才会涉及这里
            {
                return false;
            }
            else if (Runtime.Trigger == TriggerType.VerificationR)
            {
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
                if (method == "deploy")
                {
                    if (args.Length != 1) return false;
                    if (!Runtime.CheckWitness(superAdmin)) return false;
                    byte[] total_supply = Storage.Get(Storage.CurrentContext, "totalSupply");
                    if (total_supply.Length != 0) return false;
                    var keySuperAdmin = new byte[] { 0x11 }.Concat(superAdmin);
                    Storage.Put(Storage.CurrentContext, keySuperAdmin, totalCoin);
                    Storage.Put(Storage.CurrentContext, "totalSupply", totalCoin);

                    Transferred(null, superAdmin, totalCoin);
                }
                if (method == "balanceOf")
                {
                    if (args.Length != 1) return 0;
                    byte[] who = (byte[])args[0];
                    if (who.Length != 20)
                        return false;
                    return balanceOf(who);
                }
                if (method == "transfer")
                {
                    if (args.Length != 3) return false;
                    byte[] from = (byte[])args[0];
                    byte[] to = (byte[])args[1];
                    BigInteger value = (BigInteger)args[2];
                    //没有from签名，不让转
                    if (!Runtime.CheckWitness(from))
                        return false;
                    //如果有跳板调用，不让转
                    if (ExecutionEngine.EntryScriptHash.AsBigInteger() != callscript.AsBigInteger())
                        return false;
                    //如果to是不可收钱合约,不让转   
                    //if (!IsPayable(to)) return false;

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
                if (method == "getTxInfo")
                {
                    if (args.Length != 1) return 0;
                    byte[] txid = (byte[])args[0];
                    return getTxInfo(txid);
                }
            #region 升级合约,耗费490,仅限管理员
            if (method == "upgrade")
                {
                    //不是管理员 不能操作
                    if (!Runtime.CheckWitness(superAdmin))
                        return false;

                    if (args.Length != 1 && args.Length != 9)
                        return false;

                    byte[] script = Blockchain.GetContract(ExecutionEngine.ExecutingScriptHash).Script;
                    byte[] new_script = (byte[])args[0];
                    //如果传入的脚本一样 不继续操作
                    if (script == new_script)
                        return false;

                    byte[] parameter_list = new byte[] { 0x07, 0x10 };
                    byte return_type = 0x05;
                    bool need_storage = (bool)(object)05;
                    string name = "nnc";
                    string version = "1";
                    string author = "NEL";
                    string email = "0";
                    string description = "nnc";

                    if (args.Length == 9)
                    {
                        parameter_list = (byte[])args[1];
                        return_type = (byte)args[2];
                        need_storage = (bool)args[3];
                        name = (string)args[4];
                        version = (string)args[5];
                        author = (string)args[6];
                        email = (string)args[7];
                        description = (string)args[8];
                    }
                    Contract.Migrate(new_script, parameter_list, return_type, need_storage, name, version, author, email, description);
                    return true;
                }
                #endregion
            }

            return false;
        }

        //public static bool IsPayable(byte[] to)
        //{
        //    var c = Blockchain.GetContract(to);
        //    if (c.Equals(null))
        //        return true;
        //    return c.IsPayable;
        //}
    }
}
