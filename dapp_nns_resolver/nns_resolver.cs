using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using System;
using System.ComponentModel;
using System.Numerics;

namespace DApp
{
    public class nns_resolver : SmartContract
    {
        //静态解析器实现
        //dict《domainhash+protocol,data>
        //dict<"调用nns查询权限 只有是"
        [Appcall("8e813d36b159400e4889ba0aed0c42b02dd58e9e")]
        static extern object rootCall(string method, object[] arr);

        static readonly byte[] superAdmin = Neo.SmartContract.Framework.Helper.ToScriptHash("ALjSnMZidJqd18iQaoCgFun6iqWRm2cVtj");

        //通知，设置解析操作
        public delegate void deleSetResolverData(byte[] namehash, string protocol, byte[] data);
        [DisplayName("setResolverData")]
        public static event deleSetResolverData onSetResolverData;

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
        public static byte[] setResolverData(byte[] owner,byte[] nnshash,string subdomain,string protocol,byte[] data)
        {
            if(Runtime.CheckWitness(owner)==false)
            {
                return new byte[] { 0x00 };
            }
            object[] args = new object[1];
            args[0] = nnshash;
            object[] info = (object[])rootCall("getOwnerInfo", args);
            byte[] nnsowner = (byte[])info[0];
            if(nnsowner.AsBigInteger()==owner.AsBigInteger())
            {
                byte[] fullhash = nameHashSub(nnshash, subdomain);
                byte[] key = fullhash.Concat(protocol.AsByteArray());
                key = new byte[] { 0x31 }.Concat(key);
                Storage.Put(Storage.CurrentContext, key, data);
                //通知解析设置动作
                onSetResolverData(fullhash, protocol, data);
                return new byte[] { 0x01 };
            }
            return new byte[] { 0x00 };
        }
        public static byte[] resolve(string protocol,byte[] domainhash)
        {
            byte[] key = domainhash.Concat(protocol.AsByteArray());
            key = new byte[] { 0x31 }.Concat(key);
            return Storage.Get(Storage.CurrentContext, key);
        }


        public static byte[] Main(string method, object[] args)
        {
            if (Runtime.Trigger == TriggerType.Verification)//取钱才会涉及这里
            {
                return new byte[] { 0x00 };
            }
            else if (Runtime.Trigger == TriggerType.VerificationR)
            {
                return new byte[] { 0x00 };
            }
            else if (Runtime.Trigger == TriggerType.Application)
            {
                //随便调用
                if (method == "resolve")
                    return resolve((string)args[0], (byte[])args[1]);
                //请求者调用
                if (method == "setResolverData")
                    return setResolverData((byte[])args[0], (byte[])args[1], (string)args[2], (string)args[3], (byte[])args[4]);



                #region 升级合约,耗费490,仅限管理员
                if (method == "upgrade")
                {
                    //不是管理员 不能操作
                    if (!Runtime.CheckWitness(superAdmin))
                        return new byte[] { 0 };

                    if (args.Length != 1 && args.Length != 9)
                        return new byte[] { 0 };

                    byte[] script = Blockchain.GetContract(ExecutionEngine.ExecutingScriptHash).Script;
                    byte[] new_script = (byte[])args[0];
                    //如果传入的脚本一样 不继续操作
                    if (script == new_script)
                        return new byte[] { 0 };

                    byte[] parameter_list = new byte[] { 0x07, 0x10 };
                    byte return_type = 0x05;
                    bool need_storage = (bool)(object)01;
                    string name = "resolver";
                    string version = "1";
                    string author = "NEL";
                    string email = "0";
                    string description = "解析器";

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
                    return new byte[] { 1 };
                }
                #endregion
            }
            return new byte[] { 0 };
        }
    }


}
