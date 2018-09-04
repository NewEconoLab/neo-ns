using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using Helper = Neo.SmartContract.Framework.Helper;
using System;
using System.Numerics;
using System.ComponentModel;

namespace DApp
{
    public class nns_register_sell : SmartContract
    {
        //注册器
        //    注册器合约,他的作用是分配某一个域名的二级域名
        //使用存储
        // dict<0x01+fullhash,sellingid> 记录域名最后一笔拍卖记录
        // dict<0x02+sellingid,sellingstate> 记录拍卖状态
        // dict<0x11+user,money> 暂存在拍卖合约的钱
        // dict<0x21+sellingid+user,money> 在拍卖中参与竞拍的数额
        // dict<0x12+id,1> 保存收据

        //注册器通知
        // （基础）域名信息变更 OwnerInfo 域名中心实现了
        //拍卖注册器通知
        // 拍卖信息变更通知  sellingstate
        // 资金变更  moneystate

        public delegate void deleChangeAuctionState(AuctionState auctionState);
        [DisplayName("changeAuctionState")]
        public static event deleChangeAuctionState onChangeAuctionState;

        public delegate void deleAssetManagement(byte[] from, byte[] to, BigInteger value);
        [DisplayName("assetManagement")]
        public static event deleAssetManagement onAssetManagement;

        public delegate void deleCollectDomain(byte[] who,byte[] auctionId,byte[] parentHash,string domain);
        [DisplayName("collectDomain")]
        public static event deleCollectDomain onCollectDomain;

        public delegate void deleStartAuction(byte[] who, byte[] auctionId, byte[] parentHash, string domain);
        [DisplayName("startAuction")]
        public static event deleStartAuction onStartAuction;

        public delegate void deleRaiseEndsAuction(byte[] who, byte[] auctionId);
        [DisplayName("raiseEndsAuction")]
        public static event deleRaiseEndsAuction onRaiseEndsAuction;

        //粗略一天的秒数,为了测试需要,缩短时间为五分钟=一天,五分钟结束
        //const int blockhour = 10;//加速版,每10块检测一次随机是否要结束
        //const int secondday = 5 * 60;//加速版,300秒当一天

        const int blockhour = 240;///一个小时约等于的块数,随机结束间隔,每240块检查一次
        const int secondday = 3600 * 24;///一天是多少秒,用来判断拍卖进程用
        const int secondyear = secondday * 365;//一租域名是365天
        const int secondmonth = secondday * 30 * 3;//90天可以续约
        //starttime + secondday*2  为拍卖阶段1
        //~starttime + secondday*3  为拍卖阶段2
        //~strttime +secondy*5

        //系统管理员
        static readonly byte[] superAdmin = Helper.ToScriptHash("ALjSnMZidJqd18iQaoCgFun6iqWRm2cVtj");
        //系统费暂存账户
        static readonly byte[] centralAccount = Helper.ToScriptHash("AVMm9kArWd9zfu8Aof7pcMCyQDDXdh8Tb8");

        //域名中心跳板合约地址
        [Appcall("8e813d36b159400e4889ba0aed0c42b02dd58e9e")]
        static extern object rootCall(string method, object[] arr);

        // sgas合约地址
        // sgas转账
        [Appcall("9121e89e8a0849857262d67c8408601b5e8e0524")]
        static extern object sgasCall(string method, object[] arr);

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
            public byte[] parentOwner;//当此域名注册时,他爹的所有者,记录这个,则可以检测域名的爹变了
        }

        private static OwnerInfo getOwnerInfo(byte[] fullhash)
        {
            object[] _param = new object[1];
            _param[0] = fullhash;
            var info = rootCall("getOwnerInfo", _param) as OwnerInfo;
            return info;
        }

        public static DomainUseState getDomainUseState(OwnerInfo info)
        {
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
        public class AuctionState
        {
            public byte[] id; //拍卖id,就是拍卖生成的auctionid

            public byte[] parenthash;//拍卖内容
            public string domain;//拍卖内容
            public BigInteger domainTTL;//域名的TTL,用这个信息来判断域名是否发生了变化

            public BigInteger startBlockSelling;//开始销售块
            //public int StartTime 算出
            //step2time //算出
            //rantime //算出
            //endtime //算出
            //最终领取时间 算出,如果超出最终领取时间没有领域名,就不让领了
            //public BigInteger startBlockRan;//当第一个在rantime~endtime之后出价的人,记录他出价的块
            //这个变量移除,改为运算更少的随机块决定方式
            //从这个块开始,往后的每一个块出价都有一定几率直接结束
            public BigInteger endBlock;//结束块

            public BigInteger maxPrice;//最高出价
            public byte[] maxBuyer;//最大出价者
            public BigInteger lastBlock;//最后出价块
        }

        public static AuctionState getAuctionStateByAuctionID(byte[] auctionID)
        {
            var data = Storage.Get(Storage.CurrentContext, new byte[] { 0x02 }.Concat(auctionID));

            AuctionState state = new AuctionState();
            state.id = auctionID;
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

            //len = (int)data.Range(seek, 2).AsBigInteger();
            //seek += 2;
            //state.startBlockRan = data.Range(seek, len).AsBigInteger();
            //seek += len;

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
        public static AuctionState getAuctionStateByFullhash(byte[] fullhash)
        {
            //需要保存每一笔拍卖记录,因为过去拍卖者的资金都要锁定在这里
            byte[] id = Storage.Get(Storage.CurrentContext, new byte[] { 0x01 }.Concat(fullhash));
            if (id.Length == 0)//没在销售中
            {
                AuctionState _state = new AuctionState();
                _state.id = new byte[0];
                _state.startBlockSelling = 0;
                return _state;
            }
            return getAuctionStateByAuctionID(id);
        }
        private static void saveAuctionState(AuctionState state)
        {
            var fullhash = nameHashSub(state.parenthash, state.domain);
            byte[] _id = Storage.Get(Storage.CurrentContext, new byte[] { 0x01 }.Concat(fullhash));
            if (_id.AsBigInteger() != state.id.AsBigInteger())//没存过ID
            {
                Storage.Put(Storage.CurrentContext, new byte[] { 0x01 }.Concat(fullhash), state.id);
            }

            var key = new byte[] { 0x02 }.Concat(state.id);

            var doublezero = new byte[] { 0, 0 };

            var data = state.parenthash;
            var datalen = ((BigInteger)data.Length).AsByteArray().Concat(doublezero).Range(0, 2);
            var value = datalen.Concat(data);

            data = state.domain.AsByteArray();
            datalen = ((BigInteger)data.Length).AsByteArray().Concat(doublezero).Range(0, 2);
            value = value.Concat(datalen).Concat(data);

            data = state.domainTTL.AsByteArray();
            datalen = ((BigInteger)data.Length).AsByteArray().Concat(doublezero).Range(0, 2);
            value = value.Concat(datalen).Concat(data);

            data = state.startBlockSelling.AsByteArray();
            datalen = ((BigInteger)data.Length).AsByteArray().Concat(doublezero).Range(0, 2);
            value = value.Concat(datalen).Concat(data);

            //data = state.startBlockRan.AsByteArray();
            //datalen = ((BigInteger)data.Length).AsByteArray().Concat(doublezero).Range(0, 2);
            //value = value.Concat(datalen).Concat(data);

            data = state.endBlock.AsByteArray();
            datalen = ((BigInteger)data.Length).AsByteArray().Concat(doublezero).Range(0, 2);
            value = value.Concat(datalen).Concat(data);

            data = state.maxPrice.AsByteArray();
            datalen = ((BigInteger)data.Length).AsByteArray().Concat(doublezero).Range(0, 2);
            value = value.Concat(datalen).Concat(data);

            data = state.maxBuyer;
            datalen = ((BigInteger)data.Length).AsByteArray().Concat(doublezero).Range(0, 2);
            value = value.Concat(datalen).Concat(data);

            data = state.lastBlock.AsByteArray();
            datalen = ((BigInteger)data.Length).AsByteArray().Concat(doublezero).Range(0, 2);
            value = value.Concat(datalen).Concat(data);

            onChangeAuctionState(state);
            Storage.Put(Storage.CurrentContext, key, value);
        }

        public static bool startAuction(byte[] who,byte[] hash, string domainname)
        {
            //判断域名的合法性
            //域名的有效性  只能是a~z 0~9 2~32长
            if (domainname.Length < 2 || domainname.Length > 32)
                return false;
            foreach (var c in domainname)
            {
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')))
                {
                    return false;
                }
            }

            var domaininfo = getOwnerInfo(hash);
            //先看这个域名归我管不
            if (domaininfo.register.AsBigInteger() != ExecutionEngine.ExecutingScriptHash.AsBigInteger())
                return false;

            //再看看域名能不能拍卖
            var fullhash = nameHashSub(hash, domainname);
            var info = getOwnerInfo(fullhash);
            var inuse = getDomainUseState(info);
            if (inuse == DomainUseState.InUse)
            {
                return false;
            }

            //再看看有没有在拍卖
            var selling = getAuctionStateByFullhash(fullhash);
            if (selling.startBlockSelling > 0)//已经在拍卖中了
            {
                if (testEnd(selling) == false)//拍卖未结束不准
                    return false;

                if (selling.maxBuyer.Length > 0)//未流拍的拍卖,一年内不得再拍
                {
                    var nowtime = Blockchain.GetHeader(Blockchain.GetHeight()).Timestamp;
                    var starttime = Blockchain.GetHeader((uint)selling.startBlockSelling).Timestamp;
                    if ((nowtime - starttime) < secondyear)
                    {
                        return false;
                    }
                }
            }

            AuctionState sell = new AuctionState();
            sell.parenthash = hash;
            sell.domain = domainname;
            sell.domainTTL = info.TTL;

            sell.startBlockSelling = Blockchain.GetHeight();//开始拍卖了
            //sell.startBlockRan = 0;//随机块现在还不能确定
            sell.endBlock = 0;
            sell.maxPrice = 0;
            sell.maxBuyer = new byte[0];
            sell.lastBlock = 0;

            var txid = (ExecutionEngine.ScriptContainer as Transaction).Hash;
            sell.id = txid;
            saveAuctionState(sell);
            onStartAuction(who, txid, hash, domainname);
            return true;
        }
        public static BigInteger balanceOfBid(byte[] who, byte[] auctionID)
        {
            var pricekey = new byte[] { 0x21 }.Concat(auctionID).Concat(who);
            return Storage.Get(Storage.CurrentContext, pricekey).AsBigInteger();
        }

        /// <summary>
        /// 域名拍卖 加价
        /// </summary>
        /// <param name="who">加价人</param>
        /// <param name="txid">拍卖id</param>
        /// <param name="value">增加的出价</param>
        /// <returns>加价成功？</returns>
        public static bool raise(byte[] who, byte[] auctionID, BigInteger value)
        {
            var money = balanceOf(who);
            if (money < value)//钱不够
                return false;

            var selling = getAuctionStateByAuctionID(auctionID);
            if (selling.startBlockSelling == 0 || selling.endBlock > 0)//没拍卖中了
            {
                return false;
            }
            var nowtime = Blockchain.GetHeader(Blockchain.GetHeight()).Timestamp;
            var starttime = Blockchain.GetHeader((uint)selling.startBlockSelling).Timestamp;
            var step2time = starttime + secondday * 2;
            var steprantime = starttime + secondday * 3;
            var endtime = starttime + secondday * 5;
            if (nowtime > endtime)//太久了,不能出价
            {
                return false;
            }
            if (selling.endBlock > 0)//拍卖已经结束,不能出价
            {
                return false;
            }
            if (nowtime < steprantime)//随机期以前,随便出价
            {
            }
            else //随机期怎么办,有可能这里就直接被结束了
            {
                var b = testEnd(selling,who);//测试能不能结束
                if (b)
                    return false;
                //没结束就可以出价
            }


            //转移资金
            money -= value;
            var key = new byte[] { 0x11 }.Concat(who);
            Storage.Put(Storage.CurrentContext, key, money);

            var pricekey = new byte[] { 0x21 }.Concat(auctionID).Concat(who);
            var moneyfordomain = Storage.Get(Storage.CurrentContext, pricekey).AsBigInteger();
            moneyfordomain += value;
            Storage.Put(Storage.CurrentContext, pricekey, moneyfordomain);
            onAssetManagement(who, auctionID, value);
            if (moneyfordomain > selling.maxPrice)
            {
                // 高于最高出价了,更新我为最高出价者
                selling.maxPrice = moneyfordomain;
                selling.maxBuyer = who;
            }
            selling.lastBlock = Blockchain.GetHeight();
            saveAuctionState(selling);
            return true;
        }

        private static bool testEnd(AuctionState state,byte[] who = null)
        {
            if (state.startBlockSelling == 0)//就没开始过
                return false;
            if (state.endBlock > 0)//已经结束了
                return true;


            var nowtime = Blockchain.GetHeader(Blockchain.GetHeight()).Timestamp;
            var starttime = Blockchain.GetHeader((uint)state.startBlockSelling).Timestamp;
            var step2time = starttime + secondday * 2;
            var steprantime = starttime + secondday * 3;
            var endtime = starttime + secondday * 5;

            if (nowtime < steprantime)//随机期都没到,肯定没结束
                return false;

            if (nowtime > endtime)//毫无悬念结束了
            {
                state.endBlock = Blockchain.GetHeight();
                saveAuctionState(state);
                return true;
            }

            var lasttime = Blockchain.GetHeader((uint)state.lastBlock).Timestamp;
            if (lasttime < step2time)//阶段2没出过价
            {
                state.endBlock = Blockchain.GetHeight();
                saveAuctionState(state);
                return true;
            }

            //随机期
            var nowheader = Blockchain.GetHeader(Blockchain.GetHeight());
            //得到当前块在整个随机期所处的位置
            var persenttime = (nowheader.Timestamp - steprantime) * 1000 / (endtime - steprantime);
            //当处于10%位置的时候,只有10%的几率结束
            if ((nowheader.ConsensusData % 1000) < persenttime)//随机数小于块位置
            {
                state.endBlock = nowheader.Index; ;//突然死亡,无法出价了
                saveAuctionState(state);
                onRaiseEndsAuction(who, state.id);
                return true;
            }

            //走到这里都没死,那就允许你出价,这里是随机期
            return false;
        }

        /// <summary>
        /// 结束竞拍
        /// </summary>
        /// <param name="who">账户地址</param>
        /// <param name="txid">竞拍id</param>
        /// <returns></returns>
        public static bool bidSettlement(byte[] who, byte[] auctionID)
        {
            var selling = getAuctionStateByAuctionID(auctionID);
            bool b = testEnd(selling);

            if (b == false)
                return false;

            BigInteger use = 0;
            var pricekey = new byte[] { 0x21 }.Concat(auctionID).Concat(who);
            var moneyfordomain = Storage.Get(Storage.CurrentContext, pricekey).AsBigInteger();
            if (moneyfordomain == 0)
                return true;
            Storage.Delete(Storage.CurrentContext, pricekey);

            if (selling.maxBuyer.AsBigInteger() != who.AsBigInteger())
            {
                var money = balanceOf(who);

                use = moneyfordomain / 10;

                money += (moneyfordomain - use);//退9折
                var key = new byte[] { 0x11 }.Concat(who);
                Storage.Put(Storage.CurrentContext, key, money);
            }
            else
            {

                use = moneyfordomain;
            }
            onAssetManagement(auctionID, who,(moneyfordomain - use));
            if (use > 0)
            {
                // 把扣的钱丢进管理员账户
                object[] _param = new object[3];
                _param[0] = ExecutionEngine.ExecutingScriptHash; //from 
                _param[1] = superAdmin; //to; //to
                _param[2] = use.ToByteArray();//value

                object[] id = new object[1];
                id[0] = (ExecutionEngine.ScriptContainer as Transaction).Hash;

                sgasCall("transferAPP", _param);
                onAssetManagement(auctionID, superAdmin, use);
            }

            return true;


        }
        public static bool collectDomain(byte[] who, byte[] auctionID)
        {
            var selling = getAuctionStateByAuctionID(auctionID);
            var fullhash = nameHashSub(selling.parenthash, selling.domain);
            var info = getOwnerInfo(fullhash);
            if (selling.maxBuyer.AsBigInteger() == who.AsBigInteger())
            {
                //还要判断 
                var pricekey = new byte[] { 0x21 }.Concat(auctionID).Concat(who);
                var moneyfordomain = Storage.Get(Storage.CurrentContext, pricekey).AsBigInteger();
                if (moneyfordomain > 0)//没有endselling 付钱呢
                {
                    return false;
                }

                if (selling.domainTTL == info.TTL)//只要拿过这个数据会变化,所以可以用ttl比较
                {//域名我可以拿走了
                    object[] obj = new object[4];
                    obj[0] = selling.parenthash;
                    obj[1] = selling.domain;
                    obj[2] = who;
                    var starttime = Blockchain.GetHeader((uint)selling.startBlockSelling).Timestamp;
                    obj[3] = starttime + secondyear;
                    var r = (byte[])rootCall("register_SetSubdomainOwner", obj);
                    if (r.AsBigInteger() == 1)
                    {
                        onCollectDomain(who, auctionID, selling.parenthash, selling.domain);
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
            if (info.TTL < nowtime)
                return false;
            if ((info.TTL-nowtime) < secondmonth)//90天内 可以续约
            {
                object[] obj = new object[4];
                obj[0] = parenthash;
                obj[1] = domain;
                obj[2] = who;
                obj[3] = info.TTL + secondyear;
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
            if (v == 0)//如果這個交易已經處理過,就當get不到
            {
                object[] _p = new object[1];
                _p[0] = txid;
                var info = sgasCall("getTxInfo", _p);
                if (((object[])info).Length == 3)
                    return info as TransferInfo;
            }
            var tInfo = new TransferInfo();
            tInfo.from = new byte[0];
            return tInfo;
        }

        /// <summary>
        /// 获取存在注册器的sgas余额
        /// </summary>
        /// <param name="who">地址</param>
        /// <returns>余额</returns>
        public static BigInteger balanceOf(byte[] who)
        {
            var key = new byte[] { 0x11 }.Concat(who);
            return Storage.Get(Storage.CurrentContext, key).AsBigInteger();
        }

        /// <summary>
        /// 向注册器转账
        /// </summary>
        /// <param name="txid">交易id</param>
        /// <returns>转账成功？</returns>
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
                //記錄這個txid處理過了,只處理一次
                Storage.Put(Storage.CurrentContext, keytx, 1);
            }
            return false;
        }

        /// <summary>
        /// 从注册器提取sgas
        /// </summary>
        /// <param name="who">地址</param>
        /// <param name="count">提取金额</param>
        /// <returns></returns>
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

            bool succ = (bool)sgasCall("transferAPP", trans);
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
            if (Runtime.Trigger == TriggerType.Verification)//取钱才会涉及这里
            {
                return false;
            }
            else if (Runtime.Trigger == TriggerType.VerificationR)
            {
                return true;
            }
            ////请求者调用
            ////不能這樣暴力開了
            ////if (method == "requestSubDomain")
            ////    return requestSubDomain((byte[])args[0], (byte[])args[1], (string)args[2]);
            //if (method == "getdomainRegisterStatus")
            //{//看域名狀態
            //    //0x00未登記 可以申請開標
            //    //0x01使用中
            //    //0x02已過期 可以申請開標
            //    //0x10開標階段01 ,自由競價,固定時間
            //    //0x11開標階段02 ,自由競價,固定時間如果這個階段無人出價直接階段
            //    //0x12開標階段03 ,自由競價,時間不確定隨時結束
            //    //0x20投標結束,有人中標則可將狀態改回01,無人中標則可直接申請開標,轉爲10
            //    byte[] nnshash = (byte[])args[0];
            //    string domain = (string)args[1];
            //}
            else if (Runtime.Trigger == TriggerType.Application)
            {
                if (method == "getDomainUseState")//查看域名狀態
                {
                    var info = getOwnerInfo((byte[])args[0]);
                    return getDomainUseState(info);
                }
                if (method == "getAuctionStateByFullhash")//查看域名狀態
                {
                    return getAuctionStateByFullhash((byte[])args[0]);
                }
                if (method == "getAuctionStateByAuctionID")//查看域名狀態
                {
                    return getAuctionStateByAuctionID((byte[])args[0]);
                }
                if (method == "startAuction")//申請开始拍卖 (00,02,20)=>(10) //openbid
                {
                    byte[] who = (byte[])args[0];
                    if (Runtime.CheckWitness(who) == false)
                        return false;
                    byte[] nnshash = (byte[])args[1];
                    string domain = (string)args[2];
                    return startAuction(who, nnshash, domain);
                }
                if (method == "raise")//出價&加價 (10,11,12)=>不改變狀態  //raise
                {
                    byte[] who = (byte[])args[0];
                    if (Runtime.CheckWitness(who) == false)
                        return false;

                    byte[] auctionID = (byte[])args[1];
                    BigInteger myprice = (BigInteger)args[2];
                    //如果有就充值到我的戶頭
                    //如果戶頭的錢夠扣,就參與投標
                    return raise(who, auctionID, myprice);
                }
                if (method == "balanceOfBid")// 看我投标的数额  balanceOfBid
                {
                    byte[] who = (byte[])args[0];
                    byte[] auctionID = (byte[])args[1];//拍賣id
                    return balanceOfBid(who, auctionID);
                }
                if (method == "bidSettlement")// 限制狀態20 bidSettlement
                {
                    byte[] who = (byte[])args[0];
                    if (Runtime.CheckWitness(who) == false)
                        return false;

                    byte[] auctionID = (byte[])args[1];//拍賣id
                                                  //結束拍賣就會把我存進去的拍賣金退回90%（我沒中標）
                                                  //如果中標,拍賣金全扣,給我域名所有權
                    return bidSettlement(who, auctionID);
                }
                if (method == "collectDomain")//拿走我拍到的域名  collectdomain
                {
                    byte[] who = (byte[])args[0];
                    if (Runtime.CheckWitness(who) == false)
                        return false;
                    byte[] auctionID = (byte[])args[1];//拍賣id

                    return collectDomain(who, auctionID);
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
                if (method == "getmoneyback")// 把多餘的錢取回
                {
                    byte[] who = (byte[])args[0];
                    BigInteger myprice = (BigInteger)args[1];
                    return getMoneyBack(who, myprice);
                }
                if (method == "setmoneyin")//如果用普通方式轉了nep5進來,也不要緊
                {
                    byte[] txid = (byte[])args[0];// 提供一個txid,查這筆txid 的nep5入賬證明
                    return setMoneyIn(txid);
                }
                #endregion

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
                    bool need_storage = (bool)(object)01;
                    string name = "register_sell";
                    string version = "1";
                    string author = "NEL";
                    string email = "0";
                    string description = "拍卖注册器";

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
            return new byte[] { 0 };
        }
    }


}
