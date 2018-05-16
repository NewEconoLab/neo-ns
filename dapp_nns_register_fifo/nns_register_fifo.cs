﻿using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using Helper = Neo.SmartContract.Framework.Helper;
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

        const int secondperday = 24 * 3600;//一天
        const int domaindays = 7;//租一次给几天

        [Appcall("954f285a93eed7b4aed9396a7806a5812f1a5950")]
        static extern object rootCall(string method, object[] arr);

        //static readonly byte[] rootDomainHash = Helper.HexToBytes("9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08");

        public class OwnerInfo
        {
            public byte[] owner;//如果长度=0 表示没有初始化
            public byte[] register;
            public byte[] resolver;
            public BigInteger TTL;
            public byte[] parentOwner;//当此域名注册时，他爹的所有者，记录这个，则可以检测域名的爹变了
        }
        static OwnerInfo getOwnerInfo(byte[] hash)
        {
            var _param = new object[1];
            _param[0] = hash;
            var info = rootCall("getOwnerInfo", new object[] { hash }) as OwnerInfo;
            return info;
        }

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
        //public static byte[] getSubOwner(byte[] nnshash, string subdomain)
        //{
        //    if (rootDomainHash.AsBigInteger() != nnshash.AsBigInteger())//只能用来分配固定的域
        //    {
        //        return new byte[] { 0x00 };
        //    }
        //    var subhash = nameHashSub(nnshash, subdomain);
        //    var owner = Storage.Get(Storage.CurrentContext, subhash);
        //    if (owner.Length > 0)
        //    {
        //        return owner;
        //    }
        //    return new byte[] { 0x00 };
        //}
        static byte[] setSubOwner(byte[] nnshash, string subdomain, byte[] owner, BigInteger ttl)
        {
            object[] obj = new object[4];
            obj[0] = nnshash;
            obj[1] = subdomain;
            obj[2] = owner;
            obj[3] = ttl;
            var r = (byte[])rootCall("register_SetSubdomainOwner", obj);
            if (r.AsBigInteger() == 1)
            {
                //var subhash = nameHashSub(nnshash, subdomain);
                //Storage.Put(Storage.CurrentContext, subhash, owner);
                return new byte[] { 0x01 };
            }
            else
            {
                return new byte[] { 0x00 };
            }
        }
        //保密机制由register 确定
        //不用在其他阶段保密
        public static byte[] requestSubDomain(byte[] who, byte[] nnshash, string subdomain)
        {
            //var info = getOwnerInfo(nnshash);
            //if(info.register!=ExecutionEngine.ExecutingScriptHash )
            //{
            //throw  這個監測不用這邊做，domaincenter已經做過了
            //}
            if (subdomain.AsByteArray().Length == 0)
            {
                return new byte[] { 0x00 };
            }
            //if (rootDomainHash.AsBigInteger() != nnshash.AsBigInteger())//只能用来分配固定的域
            //{
            //    return new byte[] { 0x00 };
            //}
            if (Runtime.CheckWitness(who) == false)
            {
                return new byte[] { 0x00 };
            }
            var subhash = nameHashSub(nnshash, subdomain);
            var info = getOwnerInfo(subhash);
            var ttl = Blockchain.GetHeader(Blockchain.GetHeight()).Timestamp;

            if (info.owner.Length == 0)//无人认领，直接分配
            {
                ttl += secondperday * domaindays;
                return setSubOwner(nnshash, subdomain, who, ttl);
            }
            else
            {
                if(info.TTL<ttl)//過期域名
                {
                    ttl += secondperday * domaindays;
                    return setSubOwner(nnshash, subdomain, who, ttl);
                }
                else //沒過期
                {
                    if(info.owner.AsBigInteger()!=who.AsBigInteger())
                    {
                        return new byte[] { 0x00 };//別人的域名
                    }
                    //自己的域名，續期
                    ttl += secondperday * domaindays;
                    return setSubOwner(nnshash, subdomain, who, ttl);

                }

                //    var r = (byte[])rootCall("register_SetSubdomainOwner", obj);
            }
            //var owner = Storage.Get(Storage.CurrentContext, subhash);
            //var ttl = Blockchain.GetHeight(); ;
            //if (owner.Length == 0)//无人认领，直接分配
            //{
            //    ttl += blockday * domaindays;
            //    return setSubOwner(nnshash, subdomain, who, ttl);
            //}
            //else
            //{ //bi
            //    object[] obj = new object[1];
            //    var callback = (object[])rootCall("getInfo", obj);
            //    var ttltarget = (BigInteger)callback[3];
            //    if (ttltarget < ttl || owner.AsBigInteger() == who.AsBigInteger())//过期域名
            //    {
            //        ttl += blockday * domaindays;
            //        return setSubOwner(nnshash, subdomain, who, ttl);
            //    }
            //}
            //return new byte[] { 0x00 };
        }


        public static object Main(string method, object[] args)
        {

            ////随便调用
            //if (method == "getSubOwner")
            //    return getSubOwner((byte[])args[0], (string)args[1]);
            //请求者调用
            if (method == "requestSubDomain")
                return requestSubDomain((byte[])args[0], (byte[])args[1], (string)args[2]);
            return new byte[] { 0 };
        }
    }


}
