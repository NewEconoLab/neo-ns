using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using System;
using System.Numerics;

namespace DApp
{
    public class nns_register_fifo : SmartContract
    {
        //注册器
        //    注册器合约，他的作用是分配某一个域名的二级域名
        //使用存储
        // dict<subhash+0x01,owner > 记录域名拥有者数据
        // dict<subhash+0x02,ttl > 记录域名拥有者数据

        const int blockday = 4096;//粗略一天的块数
        const int domaindays = 1;//租一次给几天

        [Appcall("bf67d51518852e715fd7849504bb07a934e0b98f")]
        static extern object rootCall(string method, object[] arr);

        static readonly byte[] rootDomainHash = Helper.HexToBytes("9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08");



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
        //根合约
        public static byte[] getSubOwner(byte[] nnshash, byte[] subhash)
        {
            if (rootDomainHash.AsBigInteger() != nnshash.AsBigInteger())//只能用来分配固定的域
            {
                return new byte[] { 0x00 };
            }
            var owner = Storage.Get(Storage.CurrentContext, subhash);
            if (owner.Length > 0)
            {
                return owner;
            }
            return new byte[] { 0x00 };
        }
        static byte[] setSubOwner(byte[] nnshash, byte[] subhash, byte[] owner, BigInteger ttl)
        {
            object[] obj = new object[4];
            obj[0] = nnshash;
            obj[1] = subhash;
            obj[2] = owner;
            obj[3] = ttl;
            var r = (byte[])rootCall("register_SetSubdomainOwner", obj);
            if (r.AsBigInteger() == 1)
            {
                //var subhash = nameHashSub(nnshash, subdomain);
                Storage.Put(Storage.CurrentContext, subhash, owner);
                return new byte[] { 0x01 };
            }
            else
            {
                return new byte[] { 0x00 };
            }
        }
        public static byte[] requestSubDomain(byte[] who, byte[] nnshash, byte[] subhash)
        {
            if (rootDomainHash.AsBigInteger() != nnshash.AsBigInteger())//只能用来分配固定的域
            {
                return new byte[] { 0x00 };
            }
            if (Runtime.CheckWitness(who) == false)
            {
                return new byte[] { 0x00 };
            }
            //var subhash = nameHashSub(nnshash, subdomain);
            var owner = Storage.Get(Storage.CurrentContext, subhash);
            var ttl = Blockchain.GetHeight(); ;
            if (owner.Length == 0)//无人认领，直接分配
            {
                ttl += blockday * domaindays;
                return setSubOwner(nnshash, subhash, who, ttl);
            }
            else
            { //bi
                object[] obj = new object[1];
                var callback = (object[])rootCall("getInfo", obj);
                var ttltarget = (BigInteger)callback[3];
                if (ttltarget < ttl)//过期域名
                {
                    ttl += blockday * domaindays;
                    return setSubOwner(nnshash, subhash, who, ttl);
                }
            }
            return new byte[] { 0x00 };
        }


        public static object Main(string method, object[] args)
        {
            //随便调用
            if (method == "getSubOwner")
                return getSubOwner(args[0] as byte[], args[1] as byte[]);
            //请求者调用
            if (method == "requestSubDomain")
                return requestSubDomain(args[0] as byte[], args[1] as byte[], args[2] as byte[]);
            return new byte[] { 0 };
        }
    }


}
