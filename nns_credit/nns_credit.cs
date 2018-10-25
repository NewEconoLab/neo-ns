using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using Helper = Neo.SmartContract.Framework.Helper;
using System;
using System.Numerics;
using System.ComponentModel;

namespace nns_credit
{
    public class nns_credit : SmartContract
    {
        string magic = "20181016";

        //跳板合约
        [Appcall("348387116c4a75e420663277d9c02049907128c7")]
        static extern object rootCall(string method, object[] arr);

        //通知 地址信誉信息注册
        public delegate void deleAddrCreditRegistered(byte[] addr,NNScredit creditData);
        [DisplayName("addrCreditRegistered")]
        public static event deleAddrCreditRegistered onAddrCreditRegistered;

        //通知 地址信誉信息注销
        public delegate void deleAddrCreditRevoke(byte[] addr);
        [DisplayName("addrCreditRevoke")]
        public static event deleAddrCreditRevoke onAddrCreditRevoke;

        public class NNScredit
        {
            public byte[] namehash;
            public string fullDomainName;
            public BigInteger TTL;
            //public byte[] witness;
        }

        //使用StorageMap，推荐的存储区使用方式
        static StorageMap addrCreditMap = Storage.CurrentContext.CreateMap("addrCreditMap");

        public class OwnerInfo
        {
            public byte[] owner;//如果长度=0 表示没有初始化
            public byte[] register;
            public byte[] resolver;
            public BigInteger TTL;
            public byte[] parentOwner;//当此域名注册时，他爹的所有者，记录这个，则可以检测域名的爹变了
        }
        //获取域名信息
        static OwnerInfo getOwnerInfo(byte[] nameHash)
        {
            var _param = new object[1];
            _param[0] = nameHash;
            var info = rootCall("getOwnerInfo", new object[] { nameHash }) as OwnerInfo;
            return info;
        }

        static byte[] authenticate(byte[] addr, string[] domainArray)
        {
            //只能操作自己的地址
            if (!Runtime.CheckWitness(addr)) return new byte[] { 0 };

            //使用域名中心计算namehash
            //byte[] roothash = rootCall("nameHash", new object[] { rootDomain }) as byte[];
            //byte[] namehash = rootCall("nameHashSub", new object[] { roothash,subDomain }) as byte[];
            byte[] namehash = rootCall("nameHashArray", new object[] { domainArray }) as byte[];

            //使用域名中心获取域名信息
            OwnerInfo ownerInfo = getOwnerInfo(namehash);
            //获取最新块时间
            var lastBlockTime = Blockchain.GetHeader(Blockchain.GetHeight()).Timestamp;
            if ((ownerInfo.owner == addr) && (lastBlockTime <= ownerInfo.TTL)) {
                NNScredit creditData = new NNScredit();
                creditData.namehash = namehash;
                //根域名
                string fullDomainStr = domainArray[0];
                //其它逐级拼接
                for (var i = 1; i < domainArray.Length; i++)
                {
                    fullDomainStr = string.Concat(domainArray[i], string.Concat(".",fullDomainStr));
                }
                creditData.fullDomainName = fullDomainStr;
                creditData.TTL = ownerInfo.TTL;
                //creditData.witness = "77e193f1af44a61ed3613e6e3442a0fc809bb4b8".AsByteArray();

                //存储
                addrCreditMap.Put(addr, creditData.Serialize());
                //通知注册
                onAddrCreditRegistered(addr,creditData);

                return new byte[] { 1 };
            }
            return new byte[] { 0 };
        }

        static byte[] revoke(byte[] addr)
        {
            //只能操作自己的地址
            if (!Runtime.CheckWitness(addr)) return new byte[] { 0 };

            //操作注销
            addrCreditMap.Delete(addr);
            //通知注销
            onAddrCreditRevoke(addr);

            return new byte[] { 1 };
        }

        static byte[] getCreditInfo(byte[] addr) {
            //读取
            NNScredit creditData = (NNScredit)addrCreditMap.Get(addr).Deserialize();

            //判断是否所有者变了或者域名是否过期了，变了或过期了则先清空信誉信息
            byte[] creditNamehash = creditData.namehash;
            OwnerInfo ownerInfo = getOwnerInfo(creditNamehash);
            //获取最新块时间
            var lastBlockTime = Blockchain.GetHeader(Blockchain.GetHeight()).Timestamp;

            if ((ownerInfo.owner != addr) || (lastBlockTime > ownerInfo.TTL)) {
                //操作注销
                addrCreditMap.Delete(addr);
                //通知注销
                onAddrCreditRevoke(addr);
                //返回查询失败
                return new byte[] { 0 };
            }
            
            if (creditData.namehash.Length > 0)
            {
                ////默认返回完整域名名称
                //if (flag == new byte[] { 0 })
                //    return creditData.fullDomainName.AsByteArray();
                //else if (flag == new byte[] { 1 })
                //    return creditData.namehash;
                //else if (flag == new byte[] { 2 })
                //    return creditData.TTL.AsByteArray();
                //else
                return creditData.Serialize();
            }
            else
            {
                return new byte[] { 0 };
            }
        }

        public static byte[] Main(string method, object[] args)
        {
            if (method == "authenticate")
                return authenticate((byte[])args[0], (string[])args[1]);
            else if (method == "revoke")
                return revoke((byte[])args[0]);
            else if (method == "getCreditInfo")
                return getCreditInfo((byte[])args[0]);
            else
                return new byte[] { 0 };
        }
    }
}
