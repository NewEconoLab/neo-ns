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
        [Appcall("77e193f1af44a61ed3613e6e3442a0fc809bb4b8")]
        static extern object rootCall(string method, object[] arr);

        //通知 认证域名
        public delegate void deleAuthenticateNNS(NNScredit creditData);
        [DisplayName("authenticateNNS")]
        public static event deleAuthenticateNNS onAuthenticateNNS;

        public class NNScredit
        {
            public byte[] namehash;
            public string fullName;
            public BigInteger TTL;
            //public byte[] witness;
        }
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

        static byte[] authenticate(byte[] addr, string rootDomain, string subDomain)
        {
            byte[] roothash = rootCall("nameHash", new object[] { rootDomain }) as byte[];
            byte[] namehash = rootCall("nameHashSub", new object[] { roothash,subDomain }) as byte[];
            OwnerInfo ownerInfo = getOwnerInfo(namehash);
            if (ownerInfo.owner == addr) {
                NNScredit creditData = new NNScredit();
                creditData.namehash = namehash;
                creditData.fullName = subDomain + "." + rootDomain;
                creditData.TTL = ownerInfo.TTL;
                //creditData.witness = "77e193f1af44a61ed3613e6e3442a0fc809bb4b8".AsByteArray();

                //存储
                Storage.Put(Storage.CurrentContext, new byte[] { 0x01 }.Concat(addr), creditData.Serialize());
                //通知
                onAuthenticateNNS(creditData);

                return new byte[] { 1 };
            }
            return new byte[] { 0 };
        }

        static byte[] getCreditDataForAddr(byte[] addr,byte[] flag) {
            //读取
            NNScredit creditData = (NNScredit)Storage.Get(Storage.CurrentContext, new byte[] { 0x01 }.Concat(addr)).Deserialize();

            if (creditData.namehash.Length > 0)
            {
                if (flag == new byte[] { 0 })
                    return creditData.fullName.AsByteArray();
                else if (flag == new byte[] { 0 })
                    return creditData.TTL.AsByteArray();
                else
                    return creditData.namehash;
            }
            else
            {
                return new byte[] { 0 };
            }
        }

        public static byte[] Main(string method, object[] args)
        {
            if (method == "authenticate")
                return authenticate((byte[])args[0], (string)args[1], (string)args[2]);
            else if (method == "getCreditDataForAddr")
                return getCreditDataForAddr((byte[])args[0], (byte[])args[1]);
            else
                return new byte[] { 0 };
        }



    }
}
