using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using Helper = Neo.SmartContract.Framework.Helper;
using System;
using System.Numerics;
using System.ComponentModel;

namespace dapp_nns_domainTransaction
{
    public class DomainTransaction : SmartContract
    {
        //域名中心跳板合约地址
        [Appcall("77e193f1af44a61ed3613e6e3442a0fc809bb4b8")]
        static extern object rootCall(string method, object[] arr);

        // sgas合约地址
        // sgas转账
        [Appcall("f5630f4baba6a0333bfb10153e5f853125465b48")]
        static extern object sgasCall(string method, object[] arr);

        public class OwnerInfo
        {
            public byte[] owner;//如果长度=0 表示没有初始化
            public byte[] register;
            public byte[] resolver;
            public BigInteger TTL;
            public byte[] parentOwner;//当此域名注册时，他爹的所有者，记录这个，则可以检测域名的爹变了
            //nameinfo 整合到一起
            public string domain;//如果长度=0 表示没有初始化
            public byte[] parenthash;
            public BigInteger root;//是不是根合约
        }

        public class SgasTxInfo
        {
            public byte[] from;
            public byte[] to;
            public BigInteger value;
        }

        public class DomainSellingInfo
        {
            public byte[] fullHash;
            public byte[] seller;
            public BigInteger price;
        }

        public static object Main(string method,object[] args)
        {
            if (Runtime.Trigger == TriggerType.Verification)
            {
                return false;
            }
            else if (Runtime.Trigger == TriggerType.Application)
            {
                var callScript = ExecutionEngine.CallingScriptHash;
                if (method == "balanceOf")
                {
                    return BalanceOf((byte[])args[0]);
                }
                if (method == "getDomainSellingInfo")
                {
                    return GetDomainSellingInfo((byte[])args[0]);
                }
                //上架
                if (method == "putaway")
                {
                    return Putaway((byte[])args[0], (BigInteger)args[1]);
                }
                //取消上架
                if (method == "cancel")
                {
                    return Cancel((byte[])args[0]);
                }
                //购买
                if (method=="buy")
                {
                    return Buy((byte[])args[0],(byte[])args[1]);
                }
                //领取卖到的钱
                if (method == "getmoneyback")
                {
                    return GetMoneyBack((byte[])args[0]);
                }
                if (method == "setmoneyin")
                {
                    return SetMoneyIn((byte[])args[0]);
                }
            }
            return true;
        }

        public static BigInteger BalanceOf(byte[] who)
        {
            //查看用户的钱
            StorageMap assetMap = Storage.CurrentContext.CreateMap(nameof(assetMap));
            return assetMap.Get(who).AsBigInteger();
        }

        public static OwnerInfo GetOwnerInfo(byte[] fullHash)
        {
            var ownerInfo = rootCall("getOwnerInfo", new object[1] { fullHash }) as OwnerInfo;
            return ownerInfo;
        }

        public static SgasTxInfo GetSgasTxInfo(byte[] txid)
        {
            var txInfo = sgasCall("getTxInfo",new object[1] { txid}) as SgasTxInfo;
            return txInfo;
        }

        public static void SetDomainSellingInfo(byte[] fullHash,byte[] seller, BigInteger price)
        {
            DomainSellingInfo domainSellingInfo = new DomainSellingInfo();
            domainSellingInfo.fullHash = fullHash;
            domainSellingInfo.seller = seller;
            domainSellingInfo.price = price;

            StorageMap domainSellingInfoMap = Storage.CurrentContext.CreateMap(nameof(domainSellingInfoMap));
            domainSellingInfoMap.Put(fullHash, Helper.Serialize(domainSellingInfo));
        }

        public static DomainSellingInfo GetDomainSellingInfo(byte[] fullHash)
        {
            StorageMap domainSellingInfoMap = Storage.CurrentContext.CreateMap(nameof(domainSellingInfoMap));
            var bytes = domainSellingInfoMap.Get(fullHash);
            if (bytes.Length > 0)
                return Helper.Deserialize(bytes) as DomainSellingInfo;
            else
                return new DomainSellingInfo();
        }

        public static object Putaway(byte[] fullHash,BigInteger price)
        {
            //先获取这个域名的拥有者
            OwnerInfo ownerInfo = GetOwnerInfo(fullHash);
            var seller = ownerInfo.owner;
            var ttl = ownerInfo.TTL;
            //验证所有者签名
            if (!Runtime.CheckWitness(seller))
            {
                return false;
            }
            //域名已经到期了不能上架
            var curTime =(BigInteger)Blockchain.GetHeader(Blockchain.GetHeight()).Timestamp;
            if (ttl <= curTime)
                return false;
            //将域名抵押给本合约
            var result = (byte[])rootCall("owner_SetOwner", new object[3] { seller, fullHash, ExecutionEngine.ExecutingScriptHash });
            if (result.AsBigInteger() != 1)
                return false;
            //更新售卖域名的信息
            SetDomainSellingInfo(fullHash, seller, price);
            return true;
        }

        public static object Cancel(byte[] fullHash)
        {
            StorageMap domainSellingInfoMap = Storage.CurrentContext.CreateMap(nameof(domainSellingInfoMap));
            var price = GetDomainSellingInfo(fullHash).price;
            var seller = GetDomainSellingInfo(fullHash).seller;
            if (price == 0)
                return false;
            var result = (byte[])rootCall("owner_SetOwner", new object[3] { ExecutionEngine.ExecutingScriptHash, fullHash, seller });
            if (result.AsBigInteger() != 1)
                return false;
            domainSellingInfoMap.Delete(fullHash);
            return true;
        }

        public static object Buy(byte[] who,byte[] fullHash)
        {
            if (!Runtime.CheckWitness(who))
                return false;
            StorageMap assetMap = Storage.CurrentContext.CreateMap(nameof(assetMap));
            var domainSellingInfo =GetDomainSellingInfo(fullHash);
            var price = domainSellingInfo.price;
            var seller = domainSellingInfo.seller;
            var balanceOfBuyer = assetMap.Get(who).AsBigInteger();
            if (balanceOfBuyer < price)
                return false;
            //进行域名的转让操作
            var resut = (byte[])rootCall("owner_SetOwner",new object[3] { ExecutionEngine.ExecutingScriptHash, domainSellingInfo.fullHash, who });
            if (resut.AsBigInteger() == 0) //如果域名转账操作gg 返回
                return false;
            /*
             * 域名转让成功 开始算钱
             */
            var balanceOfSeller = assetMap.Get(seller).AsBigInteger();
            assetMap.Put(seller, balanceOfSeller+ price);
            if (balanceOfBuyer == price)
                assetMap.Delete(who);
            else
                assetMap.Put(who, balanceOfBuyer - price);
            //完成 把售卖信息删除
            StorageMap domainSellingInfoMap = Storage.CurrentContext.CreateMap(nameof(domainSellingInfoMap));
            domainSellingInfoMap.Delete(fullHash);
            return true;
        }

        public static bool SetMoneyIn(byte[] txid)
        {
            SgasTxInfo sgasTxInfo = GetSgasTxInfo(txid);
            if (sgasTxInfo.Equals(null))
                return false;
            var from = sgasTxInfo.from;
            var to = sgasTxInfo.to;
            var value = sgasTxInfo.value;
            //查看这个交易是不是转给本合约的钱
            if (to.AsBigInteger() != ExecutionEngine.ExecutingScriptHash.AsBigInteger())
                return false;

            //查看这个交易有没有被使用过 0.1
            StorageMap sgasTxUsed = Storage.CurrentContext.CreateMap(nameof(sgasTxUsed));
            var used = sgasTxUsed.Get(txid).AsBigInteger();
            if (used == 1)
                return false;

            StorageMap assetMap = Storage.CurrentContext.CreateMap(nameof(assetMap));
            var balance = assetMap.Get(from).AsBigInteger();
            balance += value;
            assetMap.Put(from,balance);
            //标记一下已经使用 0.1
            sgasTxUsed.Put(txid, 1);
            return true;
        }

        public static bool GetMoneyBack(byte[] who)
        {
            if (!Runtime.CheckWitness(who))
                return false;
            //查看用户的钱
            StorageMap assetMap = Storage.CurrentContext.CreateMap(nameof(assetMap));
            var balance = assetMap.Get(who).AsBigInteger();
            if (balance == 0)
                return false;
            //转钱
            var result = (bool)sgasCall("transferAPP",new object[3] {ExecutionEngine.ExecutingScriptHash,who,balance });
            if (!result)
                return false;
            assetMap.Delete(who);
            return true;
        }
    }
    
}
