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
        [Appcall("954f285a93eed7b4aed9396a7806a5812f1a5950")]
        static extern object rootCall(string method, object[] arr);

        //通知，设置解析操作
        public delegate void deleSetResolveData(byte[] namehash, string protocol, byte[] data);
        [DisplayName("setResolveData")]
        public static event deleSetResolveData onSetResolveData;

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
        public static byte[] setResolveData(byte[] owner,byte[] nnshash,string subdomain,string protocol,byte[] data)
        {
            if(Runtime.CheckWitness(owner)==false)
            {
                return new byte[] { 0x00 };
            }
            object[] args = new object[1];
            args[0] = nnshash;
            object[] info = (object[])rootCall("getInfo", args);
            byte[] nnsowner = (byte[])info[0];
            if(nnsowner.AsBigInteger()==owner.AsBigInteger())
            {
                byte[] fullhash = nameHashSub(nnshash, subdomain);
                byte[] key = fullhash.Concat(protocol.AsByteArray());
                Storage.Put(Storage.CurrentContext, key, data);
                //通知解析设置动作
                onSetResolveData(fullhash, protocol, data);
                return new byte[] { 0x01 };
            }
            return new byte[] { 0x00 };
        }
        public static byte[] resolve(string protocol,byte[] domainhash)
        {
            byte[] key = domainhash.Concat(protocol.AsByteArray());
            return Storage.Get(Storage.CurrentContext, key);
        }


        public static byte[] Main(string method, object[] args)
        {
            //随便调用
            if (method == "resolve")
                return resolve((string)args[0], (byte[])args[1]);
            //请求者调用
            if (method == "setResolveData")
                return setResolveData((byte[])args[0], (byte[])args[1], (string)args[2], (string)args[3], (byte[])args[4]);
            return new byte[] { 0 };
        }
    }


}
