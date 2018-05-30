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
         * map(blockHeight,totoalMoney)   存储到当前块为止收取的所有系统费用  key = 0x12 + blockHeight
         * map(address,Info)   存储地址信息   key = 0x11+address
         * map("CoinPoolInfo",CoinPoolInfo) 存储奖池的信息   key = "CoinPoolInfo"
        */
        public delegate void deleTransfer(byte[] from, byte[] to, BigInteger value);
        [DisplayName("transfer")]
        public static event deleTransfer Transferred;

        public delegate void deleTest(Info info);
        [DisplayName("justtest")]
        public static event deleTest justTest;

        public class TransferInfo
        {
            public byte[] from;
            public byte[] to;
            public BigInteger value;
        }

        public class Info
        {
            public BigInteger balance;//nnc资产数
            public uint block;//nnc资产数最后变动时间
            public BigInteger cancaim;//可以领取的分红（sgas）
        }

        public class CoinPoolInfo
        {
            public BigInteger balance;//奖池余额
            public BigInteger lastblock; //最后一个有数据的块
            public BigInteger fullblock;//最后一个有完整数据的块
        }



        static readonly byte[] superAdmin = Helper.ToScriptHash("ALjSnMZidJqd18iQaoCgFun6iqWRm2cVtj");//管理员

        // sgas合约地址
        // sgas转账
        [Appcall("e52a08c20986332ad8dccf9ded38cc493878064a")]
        static extern object sgasCall(string method, object[] arr);

        static readonly byte[] quadZero = new byte[] { 0, 0, 0, 0 };

        public static string name()
        {
            return "NEP5 Coin NNC";
        }
        public static string symbol()
        {
            return "NNC";
        }
        private const ulong factor = 1;
        private const ulong totalCoin = 100000000 * factor;
        public static byte decimals()
        {
            return 0;
        }

        //nnc 发行总量
        public static BigInteger totalSupply()
        {
            return Storage.Get(Storage.CurrentContext, "totalSupply").AsBigInteger();
        }

        public static BigInteger balanceOf(byte[] who)
        {
            var addressInfo = getInfo(who);
            return addressInfo.balance;
        }
        public static BigInteger canClaimCount(byte[] who)
        {
            var addressInfo = getInfo(who);
            return addressInfo.cancaim;
        }


        public static bool transfer(byte[] from, byte[] to, BigInteger value)
        {
            if (value <= 0) return false;

            if (from == to) return true;

            //获得当前块的高度
            var height = Blockchain.GetHeight();

            //付款方
            if (from.Length > 0)
            {
                var keyFrom = new byte[] { 0x11 }.Concat(from);
                Info fromInfo = getInfo(from);
                var from_value = fromInfo.balance;
                if (from_value < value) return false;
                //var canClaim = getCanClaim(fromInfo.block, value);
                fromInfo.cancaim += 0;
                fromInfo.block = height;
                fromInfo.balance = from_value - value;
                //更新from的info值
                Storage.Put(Storage.CurrentContext,keyFrom,Helper.Serialize(fromInfo));
            }
            //收款方
            if (to.Length > 0)
            {
                var keyTo = new byte[] { 0x11 }.Concat(to);
                Info toInfo = getInfo(to);
                //var canClaim = getCanClaim(toInfo.block, value);
                toInfo.cancaim += 0;
                toInfo.block = height;
                toInfo.balance += value;
                //更新to的info值
                Storage.Put(Storage.CurrentContext, keyTo, Helper.Serialize(toInfo));
            }
            //notify
            Transferred(from, to, value);
            return true;
        }


        public static Info getInfo(byte[] who)
        {
            var keyWho = new byte[] { 0x11 }.Concat(who);
            byte[] data = Storage.Get(Storage.CurrentContext, keyWho);
            Info info = new Info();
            if (data.Length > 0)
            {
                info = Helper.Deserialize(data) as Info;
            }
            else
            {
                info.balance = 0;
                info.block = 0;
                info.cancaim = 0;
            }
            return info;
        }


        /// <summary>
        /// 获取转账的这笔钱能分到的分红  [start,end)
        /// </summary>
        /// <param name="blockHeight">上次claim的高度</param>
        /// <param name="value">这次的交易值</param>
        /// <returns>可以领取的值</returns>
        private static BigInteger getCanClaim(BigInteger blockHeight,BigInteger value)
        {
            //获取上一个有完整记录的块的记录值
            CoinPoolInfo coinPoolInfo = Helper.Deserialize(Storage.Get(Storage.CurrentContext, "CoinPoolInfo")) as CoinPoolInfo;
            BigInteger endHeight = 0;
            if (coinPoolInfo != null)
                endHeight = coinPoolInfo.fullblock;
            var key_EndHeight = new byte[] { 0x12 }.Concat(endHeight.AsByteArray().Concat(quadZero).Range(0, 4));
            var totalMoney_end = Storage.Get(Storage.CurrentContext, key_EndHeight).AsBigInteger();
            //获取上一次领奖的块的总系统费 
            var key_StartHeight = new byte[] { 0x12 }.Concat(blockHeight.AsByteArray().Concat(quadZero).Range(0, 4));
            //start == end  不领取
            if (key_EndHeight == key_StartHeight) return 0;
            var totalMoney_start = Storage.Get(Storage.CurrentContext, key_StartHeight).AsBigInteger();
            //(totalMoneyB-totalMoneyA)*info.balance/发行量 就是这个块现在的余额可以领取的分红
            var canclaim = (totalMoney_end - totalMoney_start) * value / totalCoin;
            return canclaim;
        }

        private static bool useGas(byte[] txid)
        {
            TransferInfo transferInfo =  getTxInfo(txid);
            if (transferInfo == null) return false;
            if (transferInfo.value <= 0) return false;
            if (transferInfo.to.AsBigInteger() != ExecutionEngine.ExecutingScriptHash.AsBigInteger()) return false;

            //获取当前块的高度
            uint cur_height = Blockchain.GetHeight();
            //先获取当前块的值
            var bytes_CurHeight = ((BigInteger)cur_height).AsByteArray().Concat(quadZero).Range(0, 4);
            var key_CurHeight = new byte[] { 0x12 }.Concat(bytes_CurHeight);
            var curMoney = Storage.Get(Storage.CurrentContext, key_CurHeight).AsBigInteger();
            BigInteger totalMoney = 0;

            /*
            每一个块存储的是目前为止所有收到的系统费用    先判断当前高度有没有记录数据  如果没有则加上之前的所有费用记录下来
            如果有记录 就直接+=
            */
            CoinPoolInfo coinPoolInfo = Helper.Deserialize(Storage.Get(Storage.CurrentContext, "CoinPoolInfo")) as CoinPoolInfo;
            if (curMoney == 0)
            {
                //获取上一个有记录的块的高度  不一定每个块都有数据
                var preHeight = coinPoolInfo.fullblock;
                //获取该块的totalmoney
                var key_PreHeight = new byte[] { 0x12 }.Concat(preHeight.AsByteArray().Concat(quadZero).Range(0, 4));
                var preMoney = Storage.Get(Storage.CurrentContext, key_PreHeight).AsBigInteger();
                totalMoney = preMoney + transferInfo.value;
            }
            else
            {
                totalMoney = curMoney + transferInfo.value;
            }

            Storage.Put(Storage.CurrentContext, key_CurHeight, totalMoney);
            //标记这个txid 已经处理过了
            Storage.Put(Storage.CurrentContext, new byte[] { 0x13}.Concat(txid),1);

            if (coinPoolInfo.lastblock < cur_height)
            {
                coinPoolInfo.fullblock = coinPoolInfo.lastblock;
                coinPoolInfo.lastblock = cur_height;
                Storage.Put(Storage.CurrentContext, "CoinPoolInfo", Helper.Serialize(coinPoolInfo));
            }
            return true;
        }


        private static TransferInfo getTxInfo(byte[] txid)
        {
            var keytx = new byte[] { 0x13 }.Concat(txid);
            var done = Storage.Get(Storage.CurrentContext, keytx).AsBigInteger();
            if (done == 0)
            {
                var info = sgasCall("getTXInfo", new object[1] { txid });
                if (((object[])info).Length == 3)
                    return info as TransferInfo;
            }
            return null;
        }


        public static object getTotalMoney(byte[] height)
        {
            var key_Height = new byte[] { 0x12 }.Concat(height);
            var totalmoney = Storage.Get(Storage.CurrentContext, key_Height).AsBigInteger();
            return totalmoney;
        }

        private static bool claim(byte[] who)
        {
            var info = Helper.Deserialize(Storage.Get(Storage.CurrentContext, new byte[] { 0x11 }.Concat(who))) as Info;
            var canClaim = info.cancaim;
            object[] _param = new object[3];
            _param[0] = ExecutionEngine.ExecutingScriptHash; //from 
            _param[1] = who; //to
            _param[2] = canClaim.AsByteArray();//value
            info.cancaim = 0;
            Storage.Put(Storage.CurrentContext, new byte[] { 0x11 }.Concat(who),Helper.Serialize(info));
            return (bool)sgasCall("transfer_app", _param);
        }

        public static object Main(string method , object[] args)
        {
            if (Runtime.Trigger == TriggerType.Verification)//取钱才会涉及这里
            {
                return true;
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

                    Info info = new Info();
                    info.cancaim = 0;
                    info.balance = totalCoin;
                    info.block = Blockchain.GetHeight();

                    Storage.Put(Storage.CurrentContext, keySuperAdmin, Helper.Serialize(info));
                    Storage.Put(Storage.CurrentContext, "totalSupply", totalCoin);

                    Transferred(null, superAdmin, totalCoin);
                }
                if (method == "balanceOf")
                {
                    if (args.Length != 1) return 0;
                    byte[] who = (byte[])args[0];
                    return balanceOf(who);
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
                if (method == "useGas")
                {
                    var txid = (byte[])args[0];
                    useGas(txid);
                }
                if (method == "claim")
                {
                    if (args.Length != 1) return 0;
                    byte[] who = (byte[])args[0];
                    if (!Runtime.CheckWitness(who))
                        return false;
                    return claim(who);
                }
                if (method == "canClaimCount")
                {
                    byte[] who = (byte[])args[0];
                    return canClaimCount(who);
                }
                if (method == "getTotalMoney")
                {
                    var blockHeight = (byte[])args[0];
                    return getTotalMoney(blockHeight);
                }

                #region 升级合约,耗费490,仅限管理员
                if (method == "migrate")
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
                    bool need_storage = (bool)(object)01;
                    string name = "nnc";
                    string version = "1";
                    string author = "xx";
                    string email = "xx";
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
    }
}
