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
        //魔法数字
        string magic = "20181025";

        //域名中心（跳板）
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

        //域名信誉数据结构
        public class NNScredit
        {
            public byte[] namehash;
            public string fullDomainName;
            public BigInteger TTL;
            //public byte[] witness;
        }

        //域名中心的所有者信息结构
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

        //登记认证
        //必须拥有NNS的所有权才能成功
        static byte[] authenticate(byte[] addr, string[] domainArray)
        {
            //只能操作自己的地址
            if (!Runtime.CheckWitness(addr)) return new byte[] { 0 };

            //使用StorageMap，推荐的存储区使用方式
            StorageMap addrCreditMap = Storage.CurrentContext.CreateMap("addrCreditMap");

            //使用域名中心计算namehash
            //byte[] roothash = rootCall("nameHash", new object[] { rootDomain }) as byte[];
            //byte[] namehash = rootCall("nameHashSub", new object[] { roothash,subDomain }) as byte[];
            byte[] namehash = rootCall("nameHashArray", new object[] { domainArray }) as byte[];

            //使用域名中心获取域名信息
            OwnerInfo ownerInfo = getOwnerInfo(namehash);
            //判断NNS有没有初始化，初始化了才进行
            if (ownerInfo.owner.Length > 0)
            {
                //获取最新块时间
                var lastBlockTime = Blockchain.GetHeader(Blockchain.GetHeight()).Timestamp;
                //如果addr是所有者，而且没有过期，才能登记
                if ((ownerInfo.owner == addr) && (lastBlockTime <= ownerInfo.TTL))
                {
                    NNScredit creditData = new NNScredit();
                    creditData.namehash = namehash;
                    //根域名
                    string fullDomainStr = domainArray[0];
                    //其它逐级拼接
                    for (var i = 1; i < domainArray.Length; i++)
                    {
                        fullDomainStr = string.Concat(domainArray[i], string.Concat(".", fullDomainStr));
                    }
                    creditData.fullDomainName = fullDomainStr;
                    creditData.TTL = ownerInfo.TTL;
                    //creditData.witness = "77e193f1af44a61ed3613e6e3442a0fc809bb4b8".AsByteArray();

                    //存储
                    addrCreditMap.Put(addr, creditData.Serialize());
                    //通知注册
                    onAddrCreditRegistered(addr, creditData);

                    return new byte[] { 1 };
                }
                else
                {
                    return new byte[] { 0 };
                }
            }
            else
            {
                return new byte[] { 0 };
            }          
        }

        static byte[] revoke(byte[] addr)
        {
            //只能操作自己的地址
            if (!Runtime.CheckWitness(addr)) return new byte[] { 0 };

            //使用StorageMap，推荐的存储区使用方式
            StorageMap addrCreditMap = Storage.CurrentContext.CreateMap("addrCreditMap");

            //读取并反序列化为类
            NNScredit creditData = (NNScredit)addrCreditMap.Get(addr).Deserialize();

            //判断是否有数据,有数据才执行
            if (creditData.namehash.Length > 0)
            {
                //操作注销
                addrCreditMap.Delete(addr);
                //通知注销
                onAddrCreditRevoke(addr);

                return new byte[] { 1 };
            }
            else
            {
                return new byte[] { 0 };
            }       
        }

        static NNScredit getCreditInfo(byte[] addr) {
            //addr不满足地址正常长度，返回失败
            if (addr.Length != 20) return new NNScredit();

            //使用StorageMap，推荐的存储区使用方式
            StorageMap addrCreditMap = Storage.CurrentContext.CreateMap("addrCreditMap");

            //读取并反序列化为类
            NNScredit creditData = (NNScredit)addrCreditMap.Get(addr).Deserialize();

            //判断是否有数据
            if (creditData.namehash.Length > 0)
            {
                //获取域名信息
                byte[] creditNamehash = creditData.namehash;
                OwnerInfo ownerInfo = getOwnerInfo(creditNamehash);
                //获取最新块时间
                var lastBlockTime = Blockchain.GetHeader(Blockchain.GetHeight()).Timestamp;

                //如果NNS所有者变了，或者NNS过期了则不返回数据（即使有）
                if ((ownerInfo.owner != addr) || (lastBlockTime > ownerInfo.TTL))
                {

                    //为了能够不用发送交易也能正常运行，这里不能做删除操作（修改性操作）
                    ////操作注销
                    //addrCreditMap.Delete(addr);
                    ////通知注销
                    //onAddrCreditRevoke(addr);

                    //返回空类
                    return new NNScredit();
                }
                else
                {
                    return creditData;
                }
            }
            else
            {
                //没数据返回空类
                return new NNScredit();
            }
            
            ////判断addr是否做过NNS登记
            //if (creditData.namehash.Length > 0)
            //{
            //    ////默认返回完整域名名称
            //    //if (flag == new byte[] { 0 })
            //    //    return creditData.fullDomainName.AsByteArray();
            //    //else if (flag == new byte[] { 1 })
            //    //    return creditData.namehash;
            //    //else if (flag == new byte[] { 2 })
            //    //    return creditData.TTL.AsByteArray();
            //    //else
            //    return creditData;
            //}
            //else
            //{
            //    return new NNScredit();
            //}
        }

        public static object Main(string method, object[] args)
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
