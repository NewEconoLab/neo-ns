using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using Helper = Neo.SmartContract.Framework.Helper;
using System;
using System.ComponentModel;
using System.Numerics;

namespace Nep5_Contract
{
    public class ContractNNC : SmartContract
    {
        //一个完整的块天应该 4块每分钟*60*24=5760，但15秒出块只是个理论值，肯定会慢很多
        public const ulong blockday = 2;//建议用4096 这里是为了测试
        //取个整数吧，4096，
        public const ulong bonusInterval = blockday * 1;//发奖间隔
        public const int bonusCount = 7;
        //首先在nep5基础上将账户分为两个部分
        //一个部分是saving
        //saving 有一个产生块的标记
        //一个部分是cash
        //当你收到一笔转账，他算在cash里面
        //当你花费一笔钱，先扣cash，扣完再扣saving

        //余额是saving 和 cash 的总和

        //cash 如何转换为为saving
        //通过claim   指令
        //claim指令 将领奖并且把所有的余额转换为saving，产生块标记为当前块

        //*claim指令
        //claim指令取得已经公布的奖励，仅有自己的saving的block <奖励的startblock 才可以领奖
        //领奖后全部资产+领到的资产变成saving状态

        //消耗池
        //所有花费掉的资产进入消耗池，等候公布奖励
        //公布奖励
        //将现有奖励从消耗池中取出，变成一个奖励，奖励规定只有savingblock <startblock 才可以领取。
        //根据 消耗池数量/总发行数量 确定一个领奖比例。
        //将同时保持五个公布奖励。
        //当公布第六个奖励时，第一个奖励被删除，并将他的余额丢入消耗池

        //*检查奖励指令
        //程序约定好公布奖励的block间隔，当有人检查奖励并且奖励的block间隔已经大于等于程序设置，则公布奖励。
        //用户可以用检查奖励指令获知最近的五个奖励，以此可以推算，如果领奖自己可以获取多少收益。

        //增加两条指令
        //checkpool*检查奖励
        //claim*领取奖励

        //可循环分配资产
        //最终确定加四个接口（暂定名Nep5.1）
        //检查奖励，只读（everyone）
        public static object[] checkBonus()
        {
            byte[] data = Storage.Get(Storage.CurrentContext, "!bonus:L");
            if (data.Length == 0)
                return null;




            BigInteger lastBounsBlock = Helper.AsBigInteger(data);
            object[] retarray = new object[bonusCount];

            for (var i = 0; i < bonusCount; i++)
            {
                byte[] bIndex = Helper.AsByteArray("bonus").Concat(Helper.AsByteArray(lastBounsBlock));
                byte[] bStartBlock = bIndex.Concat(Helper.AsByteArray(":S"));
                byte[] bBonusValue = bIndex.Concat(Helper.AsByteArray(":V"));
                byte[] bBonusCount = bIndex.Concat(Helper.AsByteArray(":C"));
                byte[] bLastIndex = bIndex.Concat(Helper.AsByteArray(":L"));
                byte[] StartBlock = Storage.Get(Storage.CurrentContext, bStartBlock);
                byte[] BonusValue = Storage.Get(Storage.CurrentContext, bBonusValue);
                byte[] BonusCount = Storage.Get(Storage.CurrentContext, bBonusCount);
                byte[] LastIndex = Storage.Get(Storage.CurrentContext, bLastIndex);
                object[] bonusItem = new object[4];
                bonusItem[0] = StartBlock;
                bonusItem[1] = BonusCount;
                bonusItem[2] = BonusValue;
                bonusItem[3] = LastIndex;
                retarray[i] = bonusItem;
                if (LastIndex.Length == 0)
                    break;
                lastBounsBlock = Helper.AsBigInteger(LastIndex);
                if (lastBounsBlock == 0)
                    break;
            }
            return retarray;
        }
        //消耗资产（个人）


        public static bool use(byte[] from, BigInteger value)
        {
            if (value <= 0) return false;

            var detailfrom = GetAccountDetail(from);
            //var indexcash = from.Concat(new byte[] { 0 });
            //var indexsaving = from.Concat(new byte[] { 1 });
            //BigInteger from_value_cash = Storage.Get(Storage.CurrentContext, indexcash).AsBigInteger();
            //BigInteger from_value_saving = Storage.Get(Storage.CurrentContext, indexsaving).AsBigInteger();
            var balance = detailfrom.cash + detailfrom.saving * factor;

            if (balance < value) return false;
            if (detailfrom.cash >= value)//零钱就够扣了
            {
                detailfrom.cash = detailfrom.cash - value;
                SetAccountDetail(from, detailfrom);
                //Storage.Put(Storage.CurrentContext, indexcash, from_value_cash - value);
            }
            else//零钱不够扣
            {
                var lastv = balance - value;
                var bigN = lastv / (factor);
                var smallN = lastv % (factor);
                detailfrom.cash = smallN;
                detailfrom.saving = bigN;
                SetAccountDetail(from, detailfrom);
                //Storage.Put(Storage.CurrentContext, indexcash, smallN);
                //Storage.Put(Storage.CurrentContext, indexsaving, bigN);
            }


            //var indexcash = from.Concat(new byte[] { 0 });
            //var indexsaving = from.Concat(new byte[] { 1 });



            //BigInteger from_value_cash = Storage.Get(Storage.CurrentContext, indexcash).AsBigInteger();
            //BigInteger from_value_saving = Storage.Get(Storage.CurrentContext, indexsaving).AsBigInteger();
            //var balance = from_value_cash + from_value_saving * factor;
            //if (balance < value) return false;

            //if (from_value_cash >= value)//零钱就够扣了
            //{
            //    Storage.Put(Storage.CurrentContext, indexcash, from_value_cash - value);
            //}
            //else//零钱不够扣
            //{
            //    var lastv = balance - value;
            //    var bigN = lastv / (factor);
            //    var smallN = lastv % (factor);
            //    Storage.Put(Storage.CurrentContext, indexcash, smallN);
            //    Storage.Put(Storage.CurrentContext, indexsaving, bigN);
            //}

            byte[] data = Storage.Get(Storage.CurrentContext, "!pool");
            BigInteger v = Helper.AsBigInteger(data);
            v += value;
            Storage.Put(Storage.CurrentContext, "!pool", v);

            return true;
        }


        //新奖励，（everyone）随便调用，不符合规则就不会创建奖励，谁都可以调用这个，催促发奖励。
        public static BigInteger newBonus()
        {
            byte[] data = Storage.Get(Storage.CurrentContext, "!bonus:L");
            //if (data.Length == 0)
            //    ;

            BigInteger index = Blockchain.GetHeight() / bonusInterval;
            if (index < 1)
                return 0;

            BigInteger bounsheight = (index - 1) * bonusInterval;

            BigInteger lastBounsBlock = Helper.AsBigInteger(data);
            if (bounsheight == lastBounsBlock)
                return 0;

            //清掉奖池
            var poolv = Storage.Get(Storage.CurrentContext, "!pool").AsBigInteger();
            Storage.Delete(Storage.CurrentContext, "!pool");
            byte[] bIndex = Helper.AsByteArray("bonus").Concat(Helper.AsByteArray(bounsheight));

            //找到第一个奖池
            byte[] firstIndex = bIndex;
            byte[] secondIndex = bIndex;
            bool skipdel = false;
            for (var i = 0; i < bonusCount; i++)
            {
                secondIndex = firstIndex;
                byte[] _bLastIndex = firstIndex.Concat(Helper.AsByteArray(":L"));
                var _index = Storage.Get(Storage.CurrentContext, _bLastIndex);//+1+2+3
                if (_index.Length == 0)
                {
                    Runtime.Log("skipdel");
                    skipdel = true;
                    break;
                }
                firstIndex = Helper.AsByteArray("bonus").Concat(_index);
            }
            if (skipdel == false)//删除最后一个的连接，并把他的钱也放进新奖池里
            {
                byte[] secondLastIndex = secondIndex.Concat(Helper.AsByteArray(":L"));
                Storage.Delete(Storage.CurrentContext, secondLastIndex);
                byte[] firstBonusCount = firstIndex.Concat(Helper.AsByteArray(":C"));
                var count = Storage.Get(Storage.CurrentContext, firstBonusCount).AsBigInteger();
                poolv += count;
            }


            byte[] bStartBlock = bIndex.Concat(Helper.AsByteArray(":S"));
            byte[] bBonusValue = bIndex.Concat(Helper.AsByteArray(":V"));
            byte[] bBonusCount = bIndex.Concat(Helper.AsByteArray(":C"));
            byte[] bLastIndex = bIndex.Concat(Helper.AsByteArray(":L"));
            Storage.Put(Storage.CurrentContext, bStartBlock, bounsheight.AsByteArray());
            Storage.Put(Storage.CurrentContext, bBonusCount, poolv.AsByteArray());
            BigInteger bonusV = poolv / (totalSupply() / factor);//整数部分一发几
            Storage.Put(Storage.CurrentContext, bBonusValue, bonusV.AsByteArray());
            Storage.Put(Storage.CurrentContext, bLastIndex, lastBounsBlock.AsByteArray());

            //写入lastblock
            Storage.Put(Storage.CurrentContext, "!bonus:L", bounsheight.AsByteArray());


            return bounsheight;
        }
        //领取奖励（个人）
        public static BigInteger getBonus(byte[] to)
        {
            if (!Runtime.CheckWitness(to)) return 0;

            byte[] data = Storage.Get(Storage.CurrentContext, "!bonus:L");
            if (data.Length == 0)
                return 0;

            var toinfo = GetAccountDetail(to);
            //var indexcashto = to.Concat(new byte[] { 0 });
            //var indexsavingto = to.Concat(new byte[] { 1 });
            //var indexsavingblockto = to.Concat(new byte[] { 2 });
            //BigInteger to_value_cash = Storage.Get(Storage.CurrentContext, indexcashto).AsBigInteger();
            //BigInteger to_value_saving = Storage.Get(Storage.CurrentContext, indexsavingto).AsBigInteger();
            //BigInteger to_value_savingblock = Storage.Get(Storage.CurrentContext, indexsavingblockto).AsBigInteger();

            BigInteger lastBonusBlock = Helper.AsBigInteger(data);
            BigInteger addValue = 0;
            for (var i = 0; i < bonusCount; i++)
            {
                byte[] bIndex = Helper.AsByteArray("bonus").Concat(Helper.AsByteArray(lastBonusBlock));
                byte[] bStartBlock = bIndex.Concat(Helper.AsByteArray(":S"));
                byte[] bBonusValue = bIndex.Concat(Helper.AsByteArray(":V"));
                byte[] bBonusCount = bIndex.Concat(Helper.AsByteArray(":C"));
                byte[] bLastIndex = bIndex.Concat(Helper.AsByteArray(":L"));
                var StartBlock = Storage.Get(Storage.CurrentContext, bStartBlock).AsBigInteger();
                var BonusValue = Storage.Get(Storage.CurrentContext, bBonusValue).AsBigInteger();
                var BonusCount = Storage.Get(Storage.CurrentContext, bBonusCount).AsBigInteger();
                if (toinfo.savingblock < StartBlock)//有领奖资格
                {
                    var cangot = toinfo.saving * BonusValue;//要领走多少
                    addValue += cangot;
                    Storage.Put(Storage.CurrentContext, bBonusCount, BonusCount - cangot);
                }
                byte[] LastIndex = Storage.Get(Storage.CurrentContext, bLastIndex);
                if (LastIndex.Length == 0)
                    break;
                lastBonusBlock = Helper.AsBigInteger(LastIndex);
                if (lastBonusBlock == 0)
                    break;
            }
            //领奖写入
            BigInteger balanceto = toinfo.saving * factor + toinfo.cash;
            {
                var lastv = balanceto + addValue;
                var bigN = lastv / (factor);
                var smallN = lastv % (factor);
                toinfo.cash = smallN;
                toinfo.saving = bigN;
                toinfo.savingblock = Blockchain.GetHeight();
                SetAccountDetail(to, toinfo);
                //Storage.Put(Storage.CurrentContext, indexcashto, smallN);
                //Storage.Put(Storage.CurrentContext, indexsavingto, bigN);
                //BigInteger block = (Blockchain.GetHeight());
                //Storage.Put(Storage.CurrentContext, indexsavingblockto, block);
            }
            return 0;
        }

        //nep5 notify
        public delegate void deleTransfer(byte[] from, byte[] to, BigInteger value);
        [DisplayName("transfer")]
        public static event deleTransfer Transferred;

        public static readonly byte[] SuperAdmin = Helper.ToScriptHash("ALjSnMZidJqd18iQaoCgFun6iqWRm2cVtj");

        //nep5 func
        public static BigInteger totalSupply()
        {
            return Storage.Get(Storage.CurrentContext, "totalSupply").AsBigInteger();
        }
        public static string name()
        {
            return "NNS Coin";
        }
        public static string symbol()
        {
            return "NNC";
        }
        private const ulong factor = 100000000;
        private const ulong totalCoin = 100000000 * factor;
        public static byte decimals()
        {
            return 8;
        }
        public class AccountDetail
        {
            public BigInteger cash;//现金不能领奖
            public BigInteger saving;//存款可以另据
            public BigInteger savingblock;//存款时间
        }
        private static AccountDetail GetAccountDetail(byte[] address)
        {
            var key = new byte[] { 0x11 }.Concat(address);
            var data = Storage.Get(Storage.CurrentContext, key);

            AccountDetail detail = new AccountDetail();
            if (data.Length == 0)
            {
                detail.cash = 0;
                detail.saving = 0;
                detail.savingblock = 0;
                return detail;
            }

            int seek = 0;
            int len = 0;
            len = (int)data.Range(seek, 2).AsBigInteger();
            seek += 2;
            detail.cash = data.Range(seek, len).AsBigInteger();
            seek += len;

            len = (int)data.Range(seek, 2).AsBigInteger();
            seek += 2;
            detail.saving = data.Range(seek, len).AsBigInteger();
            seek += len;

            len = (int)data.Range(seek, 2).AsBigInteger();
            seek += 2;
            detail.savingblock = data.Range(seek, len).AsBigInteger();
            seek += len;
            return detail;

        }
        private static void SetAccountDetail(byte[] address, AccountDetail detail)
        {
            var key = new byte[] { 0x11 }.Concat(address);
            byte[] doublezero = new byte[] { 0, 0 };

            byte[] data = detail.cash.AsByteArray();
            byte[] datalen = ((BigInteger)data.Length).AsByteArray().Concat(doublezero).Range(0, 2);
            byte[] outinfo = datalen.Concat(data);

            data = detail.saving.AsByteArray();
            datalen = ((BigInteger)data.Length).AsByteArray().Concat(doublezero).Range(0, 2);
            outinfo = outinfo.Concat(datalen).Concat(data);

            data = detail.savingblock.AsByteArray();
            datalen = ((BigInteger)data.Length).AsByteArray().Concat(doublezero).Range(0, 2);
            outinfo = outinfo.Concat(datalen).Concat(data);

            Storage.Put(Storage.CurrentContext, key, outinfo);
        }

        public static BigInteger balanceOf(byte[] address)
        {
            AccountDetail detail = GetAccountDetail(address);
            var balance = detail.cash + detail.saving * factor;
            return balance;
            //from_value_cash + from_value_saving * factor;

            //var indexcash = address.Concat(new byte[] { 0 });
            //var indexsaving = address.Concat(new byte[] { 1 });
            //BigInteger from_value_cash = Storage.Get(Storage.CurrentContext, indexcash).AsBigInteger();
            //BigInteger from_value_saving = Storage.Get(Storage.CurrentContext, indexsaving).AsBigInteger();
            //var balance = from_value_cash + from_value_saving * factor;
            //return balance;
        }
        public static BigInteger[] balanceOfDetail(byte[] address)
        {
            AccountDetail detail = GetAccountDetail(address);
            var balance = detail.cash + detail.saving * factor;

            //var indexcash = address.Concat(new byte[] { 0 });
            //var indexsaving = address.Concat(new byte[] { 1 });
            //var indexsavingblock = address.Concat(new byte[] { 2 });
            //BigInteger from_value_cash = Storage.Get(Storage.CurrentContext, indexcash).AsBigInteger();
            //BigInteger from_value_saving = Storage.Get(Storage.CurrentContext, indexsaving).AsBigInteger();
            //BigInteger from_value_savingblock = Storage.Get(Storage.CurrentContext, indexsavingblock).AsBigInteger();
            //var balance = from_value_cash + from_value_saving * factor;
            BigInteger[] ret = new BigInteger[4];
            ret[0] = detail.cash;
            ret[1] = detail.saving;
            ret[2] = detail.savingblock;
            ret[3] = balance;
            return ret;
        }
        public static bool transfer(byte[] from, byte[] to, BigInteger value)
        {
            if (value <= 0) return false;
            if (from == to) return true;

            var detailfrom = GetAccountDetail(from);
            //var indexcash = from.Concat(new byte[] { 0 });
            //var indexsaving = from.Concat(new byte[] { 1 });
            //BigInteger from_value_cash = Storage.Get(Storage.CurrentContext, indexcash).AsBigInteger();
            //BigInteger from_value_saving = Storage.Get(Storage.CurrentContext, indexsaving).AsBigInteger();
            var balance = detailfrom.cash + detailfrom.saving * factor;

            if (balance < value) return false;
            if (detailfrom.cash >= value)//零钱就够扣了
            {
                detailfrom.cash = detailfrom.cash - value;
                SetAccountDetail(from, detailfrom);
                //Storage.Put(Storage.CurrentContext, indexcash, from_value_cash - value);
            }
            else//零钱不够扣
            {
                var lastv = balance - value;
                var bigN = lastv / (factor);
                var smallN = lastv % (factor);
                detailfrom.cash = smallN;
                detailfrom.saving = bigN;
                SetAccountDetail(from, detailfrom);
                //Storage.Put(Storage.CurrentContext, indexcash, smallN);
                //Storage.Put(Storage.CurrentContext, indexsaving, bigN);
            }


            var detailto = GetAccountDetail(to);
            //var indexcashto = to.Concat(new byte[] { 0 });
            //var indexsavingto = to.Concat(new byte[] { 1 });
            //var indexsavingblockto = to.Concat(new byte[] { 2 });
            //BigInteger to_value_cash = Storage.Get(Storage.CurrentContext, indexcashto).AsBigInteger();
            //BigInteger to_value_saving = Storage.Get(Storage.CurrentContext, indexsavingto).AsBigInteger();
            var balanceto = detailto.cash + detailto.saving * factor;
            if (detailto.saving == 0)//无存款账户，帮他存了
            {
                var lastv = balanceto + value;
                var bigN = lastv / (factor);
                var smallN = lastv % (factor);
                detailto.saving = bigN;
                detailto.cash = smallN;
                detailto.savingblock = Blockchain.GetHeight();
                //Storage.Put(Storage.CurrentContext, indexcashto, smallN);
                //Storage.Put(Storage.CurrentContext, indexsavingto, bigN);
                //BigInteger block = (Blockchain.GetHeight());
                //Storage.Put(Storage.CurrentContext, indexsavingblockto, block);
                SetAccountDetail(to, detailto);
            }
            else
            {
                detailto.cash = detailto.cash + value;
                SetAccountDetail(to, detailto);
                //Storage.Put(Storage.CurrentContext, indexcashto, to_value_cash + value);
            }

            //记录交易信息
            setTxInfo(from, to, value);

            //交易notify
            Transferred(from, to, value);
            return true;
        }
        public class TransferInfo
        {
            public byte[] from;
            public byte[] to;
            public BigInteger value;
        }
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
            {
                TransferInfo einfo = new TransferInfo();
                einfo.from = new byte[0];
                return einfo;
            }
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
        private static void setTxInfo(byte[] from, byte[] to, BigInteger value)
        {
            //因为testnet 还在2.6，限制

            TransferInfo info = new TransferInfo();
            info.from = from;
            info.to = to;
            info.value = value;

            //用一个老式实现法
            byte[] doublezero = new byte[] { 0, 0 };

            byte[] data = info.from;
            byte[] datalen = ((BigInteger)data.Length).AsByteArray().Concat(doublezero).Range(0, 2);
            byte[] txinfo = datalen.Concat(data);

            data = info.to;
            datalen = ((BigInteger)data.Length).AsByteArray().Concat(doublezero).Range(0, 2);
            txinfo = txinfo.Concat(datalen).Concat(data);

            data = value.AsByteArray();
            datalen = ((BigInteger)data.Length).AsByteArray().Concat(doublezero).Range(0, 2);
            txinfo = txinfo.Concat(datalen).Concat(data);

            //新式实现方法只要一行
            //byte[] txinfo = Helper.Serialize(info);

            var txid = (ExecutionEngine.ScriptContainer as Transaction).Hash;
            Storage.Put(Storage.CurrentContext, txid, txinfo);
        }
        public static object Main(string method, object[] args)
        {
            var magicstr = "2018-04-11";

            if (Runtime.Trigger == TriggerType.Verification)//取钱才会涉及这里
            {
                return Runtime.CheckWitness(SuperAdmin);
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
                    BigInteger value = (BigInteger)args[2];

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

                    if (callscript.AsBigInteger() != from.AsBigInteger())
                        return false;


                    return transfer(from, to, value);
                }
                //this is add

                if (method == "deploy")//fix count
                {
                    //if (args.Length != 1) return false;
                    if (!Runtime.CheckWitness(SuperAdmin)) return false;
                    byte[] total_supply = Storage.Get(Storage.CurrentContext, "totalSupply");
                    if (total_supply.Length != 0) return false;

                    var info = new AccountDetail();
                    info.cash = totalCoin;
                    info.saving = 0;
                    info.savingblock = 0;
                    SetAccountDetail(SuperAdmin, info);
                    //var indexcashto = SuperAdmin.Concat(new byte[] { 0 });

                    //Storage.Put(Storage.CurrentContext, indexcashto, totalCoin);
                    Storage.Put(Storage.CurrentContext, "totalSupply", totalCoin);
                    Transferred(null, SuperAdmin, totalCoin);
                }
                if (method == "balanceOfDetail")
                {
                    if (args.Length != 1) return 0;
                    byte[] account = (byte[])args[0];
                    return balanceOfDetail(account);
                }
                if (method == "use")
                {
                    if (args.Length != 2) return false;
                    byte[] from = (byte[])args[0];
                    BigInteger value = (BigInteger)args[1];

                    if (!Runtime.CheckWitness(from)) return false;

                    //如果有跳板调用，不让转
                    if (ExecutionEngine.EntryScriptHash.AsBigInteger() != callscript.AsBigInteger())
                        return false;


                    return use(from, value);
                }
                if (method == "use_app")
                {
                    if (args.Length != 2) return false;
                    byte[] from = (byte[])args[0];
                    BigInteger value = (BigInteger)args[1];

                    if (callscript.AsBigInteger() != from.AsBigInteger())
                        return false;


                    return use(from, value);
                }
                if (method == "getTXInfo")
                {
                    if (args.Length != 1) return 0;
                    byte[] txid = (byte[])args[0];
                    return getTXInfo(txid);
                }
                if (method == "getBonus")
                {
                    if (args.Length != 1) return 0;
                    byte[] account = (byte[])args[0];
                    return getBonus(account);
                }
                if (method == "checkBonus")
                {
                    return checkBonus();
                }
                if (method == "newBonus")
                {
                    return newBonus();
                }
            }
            return false;
        }

    }
}
