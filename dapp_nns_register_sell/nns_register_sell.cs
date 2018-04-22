using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using Helper = Neo.SmartContract.Framework.Helper;
using System;
using System.Numerics;

namespace DApp
{
    public class nns_register_fifo : SmartContract
    {
        //注册器
        //    注册器合约，他的作用是分配某一个域名的二级域名
        //使用存储
        // dict<0x01+fullhash,sellingid> 记录域名最后一笔拍卖记录
        // dict<0x02+sellingid,sellingstate> 记录拍卖状态
        // dict<0x11+user,money> 暂存在拍卖合约的钱
        // dict<0x21+sellingid+user,money> 在拍卖中参与竞拍的数额
        // dict<0x12+id,1> 保存收据

        //粗略一天的秒数，为了测试需要，缩短时间为一分钟=一天，五分钟结束
        const int blockday = 5 * 60;//3600 * 24;

        //域名中心合约地址
        [Appcall("954f285a93eed7b4aed9396a7806a5812f1a5950")]
        static extern object rootCall(string method, object[] arr);

        //nnc合约地址
        [Appcall("bab964febd82c9629cc583596975f51811f25f47")]
        static extern object nncCall(string method, object[] arr);

        #region 域名转hash算法
        //域名转hash算法
        //aaa.bb.test =>{"test","bb","aa"}
        static byte[] nameHash(string domain)
        {
            return SmartContract.Sha256(domain.AsByteArray());
        }
        static byte[] nameHashSub(byte[] roothash, string subdomain)
        {
            var bs = subdomain.AsByteArray();
            if (bs.Length == 0)
                return roothash;

            var domain = SmartContract.Sha256(bs).Concat(roothash);
            return SmartContract.Sha256(domain);
        }
        static byte[] nameHashArray(string[] domainarray)
        {
            byte[] hash = nameHash(domainarray[0]);
            for (var i = 1; i < domainarray.Length; i++)
            {
                hash = nameHashSub(hash, domainarray[i]);
            }
            return hash;
        }

        #endregion
        private static byte[] byteLen(BigInteger n)
        {
            byte[] v = n.AsByteArray();
            if (v.Length > 2)
                throw new Exception("not support");
            if (v.Length < 2)
                v = v.Concat(new byte[1] { 0x00 });
            if (v.Length < 2)
                v = v.Concat(new byte[1] { 0x00 });
            return v;
        }
        public enum DomainUseState
        {
            Empty,//未注冊
            InUse,//正常使用
            TTLFail,//TTL已過期
        }

        public class OwnerInfo
        {
            public byte[] owner;//如果长度=0 表示没有初始化
            public byte[] register;
            public byte[] resolver;
            public BigInteger TTL;
            public byte[] parentOwner;//当此域名注册时，他爹的所有者，记录这个，则可以检测域名的爹变了
        }
        private static OwnerInfo getOwnerInfo(byte[] fullhash)
        {
            object[] _param = new object[1];
            _param[0] = fullhash;
            var info = rootCall("getOwnerInfo", _param) as OwnerInfo;
            return info;
        }
        public static DomainUseState getDomainUseState(byte[] fullhash)
        {
            var info = getOwnerInfo(fullhash);
            if (info.owner.Length == 0)
                return DomainUseState.Empty;
            var time = Blockchain.GetHeader(Blockchain.GetHeight()).Timestamp;
            if (time <= info.TTL || info.TTL == 0)//ttl=0 是根域名
            {
                return DomainUseState.InUse;
            }
            else
            {
                return DomainUseState.TTLFail;
            }
        }

        public enum SellingStep
        {
            NotSelling,
            SellingStepFix01,//第一阶段0~2天
            SellingStepFix02,//第二阶段第三天
            SellingStepRan,//随机阶段
            EndSelling,//结束销售
        }

        //dict<domainhash,lastsellid> //查看域名最终的拍卖id
        public class SellingState
        {
            public byte[] id; //拍卖id，就是拍卖生成的txid

            public byte[] parenthash;//拍卖内容
            public string domain;//拍卖内容
            public BigInteger domainTTL;//域名的TTL，用这个信息来判断域名是否发生了变化

            public BigInteger startBlockSelling;//开始销售块
            //public int StartTime 算出
            //step2time //算出
            //rantime //算出
            //endtime //算出
            //最终领取时间 算出，如果超出最终领取时间没有领域名，就不让领了
            public BigInteger startBlockRan;//当第一个在rantime~endtime之后出价的人，记录他出价的块
            //从这个块开始，往后的每一个块出价都有一定几率直接结束
            public BigInteger endBlock;//结束块

            public BigInteger maxPrice;//最高出价
            public byte[] maxBuyer;//最大出价者
            public BigInteger lastBlock;//最后出价块
        }
        public static SellingState getSellingStateByTXID(byte[] txid)
        {
            var data = Storage.Get(Storage.CurrentContext, new byte[] { 0x02 }.Concat(txid));
            SellingState state = new SellingState();
            state.id = txid;
            int seek = 0;
            int len = 0;
            len = (int)data.Range(seek, 2).AsBigInteger();
            seek += 2;
            state.parenthash = data.Range(seek, len);
            seek += len;

            len = (int)data.Range(seek, 2).AsBigInteger();
            seek += 2;
            state.domain = data.Range(seek, len).AsString();
            seek += len;

            len = (int)data.Range(seek, 2).AsBigInteger();
            seek += 2;
            state.domainTTL = data.Range(seek, len).AsBigInteger();
            seek += len;


            len = (int)data.Range(seek, 2).AsBigInteger();
            seek += 2;
            state.startBlockSelling = data.Range(seek, len).AsBigInteger();
            seek += len;

            len = (int)data.Range(seek, 2).AsBigInteger();
            seek += 2;
            state.startBlockRan = data.Range(seek, len).AsBigInteger();
            seek += len;

            len = (int)data.Range(seek, 2).AsBigInteger();
            seek += 2;
            state.endBlock = data.Range(seek, len).AsBigInteger();
            seek += len;

            len = (int)data.Range(seek, 2).AsBigInteger();
            seek += 2;
            state.maxPrice = data.Range(seek, len).AsBigInteger();
            seek += len;

            len = (int)data.Range(seek, 2).AsBigInteger();
            seek += 2;
            state.maxBuyer = data.Range(seek, len);
            seek += len;

            len = (int)data.Range(seek, 2).AsBigInteger();
            seek += 2;
            state.lastBlock = data.Range(seek, len).AsBigInteger();
            seek += len;

            return state;
        }
        public static SellingState getSellingStateByFullhash(byte[] fullhash)
        {
            //需要保存每一笔拍卖记录,因为过去拍卖者的资金都要锁定在这里
            byte[] id = Storage.Get(Storage.CurrentContext, new byte[] { 0x01 }.Concat(fullhash));
            if (id.Length == 0)//没在销售中
            {
                SellingState _state = new SellingState();
                _state.id = new byte[0];
                _state.startBlockSelling = 0;
                return _state;
            }
            return getSellingStateByTXID(id);
        }
        private static void saveSellingState(SellingState state)
        {
            var fullhash = nameHashSub(state.parenthash, state.domain);
            byte[] _id = Storage.Get(Storage.CurrentContext, new byte[] { 0x01 }.Concat(fullhash));
            if (_id.AsBigInteger() != state.id.AsBigInteger())//没存过ID
            {
                Storage.Put(Storage.CurrentContext, new byte[] { 0x01 }.Concat(fullhash), state.id);
            }

            var key = new byte[] { 0x02 }.Concat(state.id);
            var value = byteLen(state.parenthash.Length).Concat(state.parenthash);
            value = value.Concat(byteLen(state.domain.AsByteArray().Length)).Concat(state.domain.AsByteArray());
            value = value.Concat(byteLen(state.domainTTL.AsByteArray().Length)).Concat(state.domainTTL.AsByteArray());

            value = value.Concat(byteLen(state.startBlockSelling.AsByteArray().Length)).Concat(state.startBlockSelling.AsByteArray());
            value = value.Concat(byteLen(state.startBlockRan.AsByteArray().Length)).Concat(state.startBlockRan.AsByteArray());
            value = value.Concat(byteLen(state.endBlock.AsByteArray().Length)).Concat(state.endBlock.AsByteArray());
            value = value.Concat(byteLen(state.maxPrice.AsByteArray().Length)).Concat(state.maxPrice.AsByteArray());
            value = value.Concat(byteLen(state.maxBuyer.Length)).Concat(state.maxBuyer);
            value = value.Concat(byteLen(state.lastBlock.AsByteArray().Length)).Concat(state.lastBlock.AsByteArray());

            Storage.Put(Storage.CurrentContext, key, value);

        }

        public static bool wantBuy(byte[] hash, string domainname)
        {
            var domaininfo = getOwnerInfo(hash);
            //先看这个域名归我管不
            if (domaininfo.register.AsBigInteger() != ExecutionEngine.ExecutingScriptHash.AsBigInteger())
                return false;

            //再看看域名能不能拍卖
            var fullhash = nameHashSub(hash, domainname);
            var inuse = getDomainUseState(fullhash);
            if (inuse == DomainUseState.InUse)
            {
                return false;
            }

            //再看看有没有在拍卖
            var selling = getSellingStateByFullhash(fullhash);
            if (selling.startBlockSelling > 0)//已经在拍卖中了
            {
                if (testEnd(selling) == false)//拍卖未结束不准
                    return false;

                if (selling.maxBuyer.Length > 0)//未流拍的拍卖，一年内不得再拍
                {
                    var nowtime = Blockchain.GetHeader(Blockchain.GetHeight()).Timestamp;
                    var starttime = Blockchain.GetHeader((uint)selling.startBlockSelling).Timestamp;
                    if ((nowtime - starttime) < blockday * 365)//一个拍卖7天以内是不能再拍的
                    {
                        return false;
                    }
                }

            }

            SellingState sell = new SellingState();
            sell.parenthash = hash;
            sell.domain = domainname;
            sell.domainTTL = domaininfo.TTL;

            sell.startBlockSelling = Blockchain.GetHeight();//开始拍卖了
            sell.startBlockRan = 0;//随机块现在还不能确定
            sell.endBlock = 0;
            sell.maxPrice = 0;
            sell.maxBuyer = new byte[0];
            sell.lastBlock = 0;

            var txid = (ExecutionEngine.ScriptContainer as Transaction).Hash;
            sell.id = txid;
            saveSellingState(selling);

            return true;
        }
        public static BigInteger balanceOfSelling(byte[] who, byte[] txid)
        {
            var pricekey = new byte[] { 0x21 }.Concat(txid).Concat(who);
            return Storage.Get(Storage.CurrentContext, pricekey).AsBigInteger();
        }
        public static bool addPrice(byte[] who, byte[] txid, BigInteger value)
        {
            var money = balanceOf(who);
            if (money < value)//钱不够
                return false;

            var selling = getSellingStateByTXID(txid);
            if (selling.startBlockSelling == 0 || selling.endBlock > 0)//没拍卖中了
            {
                return false;
            }
            var nowtime = Blockchain.GetHeader(Blockchain.GetHeight()).Timestamp;
            var starttime = Blockchain.GetHeader((uint)selling.startBlockSelling).Timestamp;
            var step2time = starttime + blockday * 2;
            var steprantime = starttime + blockday * 3;
            var endtime = starttime + blockday * 5;
            if (nowtime > endtime)//太久了，不能出价
            {
                return false;
            }
            if (selling.endBlock > 0)//拍卖已经结束,不能出价
            {
                return false;
            }
            if (nowtime < steprantime)//随机期以前，随便出价
            {
            }
            else //随机期怎么办，有可能这里就直接被结束了
            {
                var b = testEnd(selling);//测试能不能结束
                if (b)
                    return false;
                //没结束就可以出价
            }


            //转移资金
            money -= value;
            var key = new byte[] { 0x11 }.Concat(who);
            Storage.Put(Storage.CurrentContext, key, money);

            var pricekey = new byte[] { 0x21 }.Concat(txid).Concat(who);
            var moneyfordomain = Storage.Get(Storage.CurrentContext, pricekey).AsBigInteger();
            moneyfordomain += value;
            Storage.Put(Storage.CurrentContext, pricekey, moneyfordomain);

            if (moneyfordomain > selling.maxPrice)
            {//高于最高出价了,更新我为最高出价者
                selling.maxPrice = moneyfordomain;
                selling.maxBuyer = who;
                selling.lastBlock = Blockchain.GetHeight();
                saveSellingState(selling);
            }
            return true;
        }
        private static bool testEnd(SellingState selling)
        {
            if (selling.startBlockSelling == 0)//就没开始过
                return false;
            if (selling.endBlock > 0)//已经结束了
                return false;


            var nowtime = Blockchain.GetHeader(Blockchain.GetHeight()).Timestamp;
            var starttime = Blockchain.GetHeader((uint)selling.startBlockSelling).Timestamp;
            var step2time = starttime + blockday * 2;
            var steprantime = starttime + blockday * 3;
            var endtime = starttime + blockday * 5;

            if (nowtime < steprantime)//随机期都没到，肯定没结束
                return false;

            if (nowtime > endtime)//毫无悬念结束了
            {
                selling.endBlock = Blockchain.GetHeight();
                saveSellingState(selling);
                return true;
            }

            var lasttime = Blockchain.GetHeader((uint)selling.lastBlock).Timestamp;
            if (lasttime < step2time)//阶段2没出过价
            {
                selling.endBlock = Blockchain.GetHeight();
                saveSellingState(selling);
                return true;
            }

            //如果发现随机期都没进，先进一下
            if (selling.startBlockRan == 0)
            {
                selling.startBlockRan = Blockchain.GetHeight();
                saveSellingState(selling);
            }

            ulong endv = 0;
            for (var i = selling.startBlockRan; i < Blockchain.GetHeight(); i += 240)//随机结束，那就用4800
            {
                var blockheader = Blockchain.GetHeader((uint)i);
                endv += (blockheader.ConsensusData % 4800);
                if (endv > 4800)
                {
                    selling.endBlock = i;//突然死亡，无法出价了
                    saveSellingState(selling);
                    return true;
                }
            }

            //走到这里都没死，那就允许你出价，这里是随机期
            return false;
        }
        public static bool endSelling(byte[] who, byte[] txid)
        {
            var selling = getSellingStateByTXID(txid);
            bool b = testEnd(selling);
            if (b == false)
                return false;
            if (selling.maxBuyer.AsBigInteger() != who.AsBigInteger())
            {//最大出价人不是我
                //结束了，把我的钱取回来
                var pricekey = new byte[] { 0x21 }.Concat(txid).Concat(who);
                var moneyfordomain = Storage.Get(Storage.CurrentContext, pricekey).AsBigInteger();
                Storage.Delete(Storage.CurrentContext, pricekey);

                var money = balanceOf(who);

                var use = moneyfordomain / 10;

                money += (moneyfordomain - use);//退9折
                var key = new byte[] { 0x11 }.Concat(who);
                Storage.Put(Storage.CurrentContext, key, money);


                //把扣的钱丢进nnc
                object[] _param = new object[2];
                _param[0] = who;
                _param[1] = use;
                nncCall("use_app", _param);

                return true;
            }
            else
            {
                //结束了，把我的钱扣了
                var pricekey = new byte[] { 0x21 }.Concat(txid).Concat(who);
                var moneyfordomain = Storage.Get(Storage.CurrentContext, pricekey).AsBigInteger();
                Storage.Delete(Storage.CurrentContext, pricekey);

                //把扣的钱丢进nnc
                object[] _param = new object[2];
                _param[0] = who;
                _param[1] = moneyfordomain;
                nncCall("use_app", _param);

                return true;
                //var money = balanceOf(who);
                //money += moneyfordomain;
                //var key = new byte[] { 0x11 }.Concat(who);
                //Storage.Put(Storage.CurrentContext, key, money);

            }

        }
        public static bool getSellingDomain(byte[] who, byte[] txid)
        {
            var selling = getSellingStateByTXID(txid);
            var fullhash = nameHashSub(selling.parenthash, selling.domain);
            var info = getOwnerInfo(fullhash);
            if (selling.maxBuyer.AsBigInteger() == who.AsBigInteger())
            {
                if (selling.domainTTL == info.TTL)//只要拿过这个数据会变化，所以可以用ttl比较
                {//域名我可以拿走了
                    object[] obj = new object[4];
                    obj[0] = selling.parenthash;
                    obj[1] = selling.domain;
                    obj[2] = who;
                    var starttime = Blockchain.GetHeader((uint)selling.startBlockSelling).Timestamp;
                    obj[3] = starttime + blockday * 365;
                    var r = (byte[])rootCall("register_SetSubdomainOwner", obj);
                    if (r.AsBigInteger() == 1)
                    {
                        return true;
                    }
                }
            }
            return false;

        }
        public static bool renewDomain(byte[] who, byte[] parenthash, string domain)
        {
            byte[] fullhash = nameHashSub(parenthash, domain);
            var info = getOwnerInfo(fullhash);
            var nowtime = Blockchain.GetHeader(Blockchain.GetHeight()).Timestamp;
            if (info.owner.AsBigInteger() != who.AsBigInteger())
                return false;
            if (info.TTL > nowtime)
                return false;
            if ((nowtime - info.TTL) < blockday * 30)//30天内 可以续约
            {
                object[] obj = new object[4];
                obj[0] = parenthash;
                obj[1] = domain;
                obj[2] = who;
                obj[3] = info.TTL + blockday * 365;
                var r = (byte[])rootCall("register_SetSubdomainOwner", obj);
                return r.AsBigInteger() == 1;
            }
            return false;
        }
        #region 資金管理
        //dict<0x11+who,bigint money> //money字典
        //dict<0x12+txid,0 or 1> //交易是否已充值字典
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
                var info = nncCall("getTXInfo", _p);
                if (((object[])info).Length == 3)
                    return info as TransferInfo;
            }
            var tInfo = new TransferInfo();
            tInfo.from = new byte[0];
            return tInfo;
        }
        //返回我在拍賣合約裏面存的nnc余額
        public static BigInteger balanceOf(byte[] who)
        {
            var key = new byte[] { 0x11 }.Concat(who);
            return Storage.Get(Storage.CurrentContext, key).AsBigInteger();
        }
        public static bool setMoneyIn(byte[] txid)
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
                //存錢
                var key = new byte[] { 0x11 }.Concat(tx.from);
                var money = Storage.Get(Storage.CurrentContext, key).AsBigInteger();
                money += tx.value;
                Storage.Put(Storage.CurrentContext, key, money);
                //記錄這個txid處理過了，只處理一次
                Storage.Put(Storage.CurrentContext, keytx, 1);
            }
            return false;
        }
        public static bool getMoneyBack(byte[] who, BigInteger count)
        {
            var key = new byte[] { 0x11 }.Concat(who);
            var money = Storage.Get(Storage.CurrentContext, key).AsBigInteger();
            if (money < count)
                return false;
            //存錢
            object[] trans = new object[3];
            trans[0] = ExecutionEngine.ExecutingScriptHash;
            trans[1] = who;
            trans[2] = count;
            bool succ = (bool)nncCall("transfer_app", trans);
            if (succ)
            {
                money -= count;
                Storage.Put(Storage.CurrentContext, key, money);
                return true;
            }

            return false;
        }
        #endregion
        public static object Main(string method, object[] args)
        {
            ////请求者调用
            ////不能這樣暴力開了
            ////if (method == "requestSubDomain")
            ////    return requestSubDomain((byte[])args[0], (byte[])args[1], (string)args[2]);
            //if (method == "getdomainRegisterStatus")
            //{//看域名狀態
            //    //0x00未登記 可以申請開標
            //    //0x01使用中
            //    //0x02已過期 可以申請開標
            //    //0x10開標階段01 ，自由競價，固定時間
            //    //0x11開標階段02 ，自由競價，固定時間如果這個階段無人出價直接階段
            //    //0x12開標階段03 ，自由競價，時間不確定隨時結束
            //    //0x20投標結束，有人中標則可將狀態改回01，無人中標則可直接申請開標，轉爲10
            //    byte[] nnshash = (byte[])args[0];
            //    string domain = (string)args[1];
            //}
            if (method == "getDomainUseState")//查看域名狀態
            {
                return getDomainUseState((byte[])args[0]);
            }
            if (method == "getSellingStateByFullhash")//查看域名狀態
            {
                return getSellingStateByFullhash((byte[])args[0]);
            }
            if (method == "getSellingStateByTXID")//查看域名狀態
            {
                return getSellingStateByTXID((byte[])args[0]);
            }
            if (method == "wantBuy")//申請開標 (00,02,20)=>(10)
            {
                byte[] who = (byte[])args[0];
                if (Runtime.CheckWitness(who) == false)
                    return false;
                byte[] nnshash = (byte[])args[1];
                string domain = (string)args[2];
                return wantBuy(nnshash, domain);
            }
            if (method == "addPrice")//出價&加價 (10,11,12)=>不改變狀態
            {
                byte[] who = (byte[])args[0];
                if (Runtime.CheckWitness(who) == false)
                    return false;

                byte[] txid = (byte[])args[1];
                BigInteger myprice = (BigInteger)args[2];
                //如果有就充值到我的戶頭
                //如果戶頭的錢夠扣，就參與投標
                return addPrice(who, txid, myprice);
            }
            if (method == "balanceOfSelling")//看我投标的数额
            {
                byte[] who = (byte[])args[0];
                byte[] txid = (byte[])args[1];//拍賣id
                return balanceOfSelling(who, txid);
            }
            if (method == "endSelling")//限制狀態20
            {
                byte[] who = (byte[])args[0];
                if (Runtime.CheckWitness(who) == false)
                    return false;

                byte[] txid = (byte[])args[1];//拍賣id
                //結束拍賣就會把我存進去的拍賣金退回90%（我沒中標）
                //如果中標，拍賣金全扣，給我域名所有權
                return endSelling(who, txid);
            }
            if (method == "getSellingDomain")//拿走我拍到的域名
            {
                byte[] who = (byte[])args[0];
                if (Runtime.CheckWitness(who) == false)
                    return false;
                byte[] txid = (byte[])args[1];//拍賣id

                return getSellingDomain(who, txid);
            }
            if (method == "renewDomain")//续约域名
            {
                byte[] who = (byte[])args[0];
                if (Runtime.CheckWitness(who) == false)
                    return false;
                byte[] nnshash = (byte[])args[1];
                string domain = (string)args[2];
                return renewDomain(who, nnshash, domain);
            }
            #region 资金管理
            if (method == "balanceOf")
            {
                byte[] who = (byte[])args[0];
                return balanceOf(who);
            }
            if (method == "getmoneyback")//把多餘的錢取回
            {
                byte[] who = (byte[])args[0];
                BigInteger myprice = (BigInteger)args[1];
                return getMoneyBack(who, myprice);
            }
            if (method == "setmoneyin")//如果用普通方式轉了nep5進來，也不要緊
            {
                byte[] txid = (byte[])args[0];//提供一個txid，查這筆txid 的nep5入賬證明
                return setMoneyIn(txid);
            }
            #endregion
            return new byte[] { 0 };
        }
    }


}
