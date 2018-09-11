using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using System;
using System.Numerics;

namespace adpp_nns_register_common
{
    public class nns_register_common : SmartContract
    {
        //最普通的注册器
        //域名所有者分配子域名的归属权

        //域名中心跳板合约地址
        [Appcall("348387116c4a75e420663277d9c02049907128c7")]
        static extern object rootCall(string method, object[] arr);

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

        public static bool SetDomain(byte[] who,byte[] hash,string domainname)
        {
            //从域名中心获取根域名的信息
            var ownerinfo = getOwnerInfo(hash);
            //检查根域名的所有者和注册器
            if (!Runtime.CheckWitness(ownerinfo.owner) || ownerinfo.register.AsBigInteger()!= ExecutionEngine.ExecutingScriptHash.AsBigInteger())
                return false;
            object[] obj = new object[4];
            obj[0] = hash;
            obj[1] = domainname;
            obj[2] = who;
            var starttime = Blockchain.GetHeader(Blockchain.GetHeight()).Timestamp;
            obj[3] = ownerinfo.TTL; //到期时间怎们也不能比根域名到期时间长吧
            var r = (byte[])rootCall("register_SetSubdomainOwner", obj);
            return true;
        }

        public static object Main(string method,object[] args)
        {
            if (Runtime.Trigger == TriggerType.Verification)
            {
                return false;
            }
            else if (Runtime.Trigger == TriggerType.VerificationR)
            {
                return false;
            }
            else if (Runtime.Trigger == TriggerType.Application)
            {
                if (method == "SetDomainOwner")  //设置子域名的所有者
                {
                    if (args.Length != 3)
                        return false;
                    byte[] who = (byte[])args[0];
                    byte[] hash = (byte[])args[1];
                    string domain = (string)args[2];
                    SetDomain(who,hash,domain);
                }
            }
            else if (Runtime.Trigger == TriggerType.ApplicationR)
            {
                return false;
            }

            return false;
        }
    }
}
