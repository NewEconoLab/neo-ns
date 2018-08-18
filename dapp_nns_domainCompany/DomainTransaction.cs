using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using System;
using System.Numerics;

namespace dapp_nns_domainTransaction
{
    public class DomainCompany : SmartContract
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

        public class SgasTransferInfo
        {
            public byte[] from;
            public byte[] to;
            public BigInteger value;
        }

        public class DomainTransferInfo
        {
            public byte[] fullHash;
            public BigInteger value;
        }

        public static object Main(string method,object[] args)
        {
            if (Runtime.Trigger == TriggerType.Application)
            {
                return false;
            }
            else if (Runtime.Trigger == TriggerType.Verification)
            {
                var callScript = ExecutionEngine.CallingScriptHash;
                //上架
                if (method == "putaway")
                {
                    return Putaway((byte[])args[0], (BigInteger)args[1]);
                }
                //购买
                if (method=="buy")
                {
                }
            }
            return true;
        }

        public static OwnerInfo GetOwnerInfo(byte[] fullHash)
        {
            var ownerInfo = rootCall("getOwnerInfo", new object[1] { fullHash }) as OwnerInfo;
            return ownerInfo;
        }

        public static SgasTransferInfo GetTxInfo(byte[] txid)
        {
            var transferInfo = sgasCall("getTxInfo",new object[1] { txid}) as SgasTransferInfo;
            return transferInfo;
        }


        public static object Putaway(byte[] fullHash,BigInteger value)
        {
            //先获取这个域名的拥有者
            OwnerInfo ownerInfo = GetOwnerInfo(fullHash);
            if (!Runtime.CheckWitness(ownerInfo.owner))
            {
                return false;
            }

            return true;
        }
    }
    
}
