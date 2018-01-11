using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using System;
using System.Numerics;

namespace DApp
{
    public class nns_reslover : SmartContract
    {
        //域名中心 
        //    域名中心是一个不会改变地址的合约，他的作用是管理某一个域名的数据
        //使用存储
        // dict<hash+0x00,owner> 记录域名拥有者数据
        // dict<hash+0x01,register> 域名注册器
        // dict<hash+0x02,resolver> 域名解析器
        // dict<hash+0x03,ttl>   记录域名过期数据
        //InitSuperAdmin 一次性超级管理员，超级管理员只能转让根域名的所有权
        //使用Nep4
        //所有者可以设置自己域名的控制器
        //以后还应该设置联合所有权的机制，多个所有者，几人以上签名才可用

        const int blockday = 4096;//粗略一天的块数

        const string rootDomain = "test";
        static readonly byte[] InitSuperAdmin = Helper.ToScriptHash("ALjSnMZidJqd18iQaoCgFun6iqWRm2cVtj");//初始管理員
        public static byte[] rootNameHash()
        {
            return nameHash(rootDomain);
        }
        public static string rootName()
        {
            return rootDomain;
        }
        public static object[] getInfo(byte[] nnshash)
        {
            object[] ret = new object[4];
            ret[0] = Storage.Get(Storage.CurrentContext, nnshash.Concat(new byte[] { 0x00 }));
            ret[1] = Storage.Get(Storage.CurrentContext, nnshash.Concat(new byte[] { 0x01 }));
            ret[2] = Storage.Get(Storage.CurrentContext, nnshash.Concat(new byte[] { 0x02 }));
            ret[3] = Storage.Get(Storage.CurrentContext, nnshash.Concat(new byte[] { 0x03 }));
            return ret;
        }

        delegate byte[] deleDyncall(string method, object[] arr);
        //域名解析
        //完整解析，可以处理各种域名到期，权限变化问题，也可以处理动态解析
        static byte[] resolveFull(string protocol, string[] domainarray)
        {
            byte[] hash = nameHash(domainarray[0]);
            //根域名不用管ttl
            //var ttl = Storage.Get(Storage.CurrentContext, hash.Concat(new byte[] { 0x03 })).AsBigInteger();
            //if (ttl < height)
            //{
            //    return null;
            //}
            var height = Blockchain.GetHeight();

            //{ test.aaa.second //一層層上}
            //{test.aaa.second } for(i =1;i<2;i++)
            for (var i = 1; i < domainarray.Length - 1; i++)
            {
                byte[] register = Storage.Get(Storage.CurrentContext, hash.Concat(new byte[] { 0x01 }));
                if (register.Length == 0)
                {
                    return new byte[] { 0x00 };
                }
                var regcall = (deleDyncall)register.ToDelegate();

                var subhash = nameHashSub(hash, domainarray[i]);

                byte[] data = (byte[])regcall("getSubOwner", new object[] { hash, subhash });
                if (data.Length == 0)//没有子域名，断链
                {
                    return new byte[] { 0x00 };
                }

                hash = subhash;
                var ttl = Storage.Get(Storage.CurrentContext, hash.Concat(new byte[] { 0x03 })).AsBigInteger();
                if (ttl < height)
                {
                    return new byte[] { 0x00 };
                }
            }
            string lastname = domainarray[domainarray.Length - 1];
            return resolve(protocol, hash, lastname);
        }
        //一般解析
        static byte[] resolve(string protocol, byte[] nnshash, string subdomain)
        {
            //先查完整hash是否对应解析器
            var fullhash = nameHashSub(nnshash, subdomain);

            var resolver = Storage.Get(Storage.CurrentContext, fullhash.Concat(new byte[] { 0x02 }));
            if (resolver.Length != 0)
            {
                var ttl = Storage.Get(Storage.CurrentContext, nnshash.Concat(new byte[] { 0x03 })).AsBigInteger();
                if (ttl < Blockchain.GetHeight())
                {
                    return new byte[] { 0x00 };
                }
                //还是要确认一下这个玩意是不是合法的
                byte[] register = Storage.Get(Storage.CurrentContext, nnshash.Concat(new byte[] { 0x01 }));
                if (register.Length == 0)
                {
                    return new byte[] { 0x00 };
                }
                var regcall = (deleDyncall)register.ToDelegate();

                var subhash = nameHashSub(nnshash, subdomain);
                byte[] data = (byte[])regcall("getSubOwner", new object[] { nnshash, subhash });
                if (data.Length == 0)//没有子域名，断链
                {
                    return new byte[] { 0x00 };
                }

                var resolveCall = (deleDyncall)resolver.ToDelegate();
                return resolveCall("resolve", new object[] { protocol, fullhash });//解析
            }

            //然后查根域名是否对应解析器
            resolver = Storage.Get(Storage.CurrentContext, nnshash.Concat(new byte[] { 0x02 }));
            if (resolver.Length != 0)
            {
                var ttl = Storage.Get(Storage.CurrentContext, nnshash.Concat(new byte[] { 0x03 })).AsBigInteger();
                if (ttl < Blockchain.GetHeight())
                {
                    return new byte[] { 0x00 };
                }
                var resolveCall = (deleDyncall)resolver.ToDelegate();
                return resolveCall("resolve", new object[] { protocol, nnshash });//解析
            }
            return new byte[] { 0x00 };
        }
        //快速解析
        #region 所有者功能
        //設置新的所有者(域名轉讓)
        static byte[] owner_SetOwner(byte[] owner, byte[] nnshash, byte[] newowner)
        {
            var callhash = ExecutionEngine.CallingScriptHash;
            var o = Storage.Get(Storage.CurrentContext, nnshash.Concat(new byte[] { 0x00 }));
            if (o.Length == 0 && //一個域名沒有所有者
                InitSuperAdmin.AsBigInteger() == owner.AsBigInteger() && //并且owner 是 初始管理員
                rootNameHash().AsBigInteger() == nnshash.AsBigInteger() //并且設置的是 rootHash
                )
            {
                if (Runtime.CheckWitness(owner))
                {
                    //初始管理員衹有一個功能,就是轉讓根域名管理權，而且是一次性的，一旦轉讓出去，初始管理員就沒用了
                    Storage.Put(Storage.CurrentContext, nnshash.Concat(new byte[] { 0x00 }), newowner);
                    return new byte[] { 0x01 };
                }
                return new byte[] { 0x00 };
            }
            if (callhash.AsBigInteger() == o.AsBigInteger())//智能合約所有者
            {
                Storage.Put(Storage.CurrentContext, nnshash.Concat(new byte[] { 0x00 }), newowner);
                return new byte[] { 0x01 };
            }
            if (Runtime.CheckWitness(owner) == true && o.AsBigInteger() == owner.AsBigInteger())//账户所有者
            {
                Storage.Put(Storage.CurrentContext, nnshash.Concat(new byte[] { 0x00 }), newowner);
                return new byte[] { 0x01 };
            }
            return new byte[] { 0x00 };
        }
        //所有者设置注册器
        static byte[] owner_SetRegister(byte[] owner, byte[] nnshash, byte[] controller)
        {
            var callhash = Neo.SmartContract.Framework.Services.System.ExecutionEngine.CallingScriptHash;
            var o = Storage.Get(Storage.CurrentContext, nnshash.Concat(new byte[] { 0x00 }));
            if (
                callhash.AsBigInteger() == o.AsBigInteger()//智能合約所有者
                ||
                (Runtime.CheckWitness(owner) && o.AsBigInteger() == owner.AsBigInteger())//個人所有者
                )
            {
                Storage.Put(Storage.CurrentContext, nnshash.Concat(new byte[] { 0x01 }), controller);
                return new byte[] { 0x01 };
            }
            return new byte[] { 0x00 };
        }
        //所有者设置解析器
        static byte[] owner_SetResolver(byte[] owner, byte[] nnshash, byte[] resolver)
        {
            var callhash = Neo.SmartContract.Framework.Services.System.ExecutionEngine.CallingScriptHash;
            var o = Storage.Get(Storage.CurrentContext, nnshash.Concat(new byte[] { 0x00 }));
            if (
                callhash.AsBigInteger() == o.AsBigInteger()//智能合約所有者
                ||
                (Runtime.CheckWitness(owner) && o.AsBigInteger() == owner.AsBigInteger())//個人所有者
                )
            {
                Storage.Put(Storage.CurrentContext, nnshash.Concat(new byte[] { 0x02 }), resolver);
                return new byte[] { 0x01 };
            }
            return new byte[] { 0x00 };
        }
        #endregion
        #region 注册器功能
        /// <summary>
        /// 注册器功能组
        /// </summary>
        //更改子域名所有者
        static byte[] register_SetSubdomainOwner(byte[] nnshash, string subdomain, byte[] owner, BigInteger ttl)
        {

            var ttlself = Storage.Get(Storage.CurrentContext, nnshash.Concat(new byte[] { 0x03 })).AsBigInteger();
            if (
                (nnshash.AsBigInteger() != rootNameHash().AsBigInteger())//一级域名不检查ttl
                &&
                ttl > ttlself
                )
            {
                return new byte[] { 0x00 };
            }
            var register = Storage.Get(Storage.CurrentContext, nnshash.Concat(new byte[] { 0x01 }));
            if (Helper.AsBigInteger(register) == Helper.AsBigInteger(ExecutionEngine.CallingScriptHash))
            {
                //衹有控制器允許改
                byte[] namehashsub = nameHashSub(nnshash, subdomain);
                Storage.Put(Storage.CurrentContext, namehashsub.Concat(new byte[] { 0x00 }), owner);
                Storage.Put(Storage.CurrentContext, namehashsub.Concat(new byte[] { 0x03 }), ttl);
                return new byte[] { 0x01 };
            }
            return new byte[] { 0x00 };
        }
        #endregion
        #region 域名转hash算法
        //域名转hash算法
        //aaa.bb.test =>{"test","bb","aa"}
        static byte[] nameHash(string domain)
        {
            return SmartContract.Sha256(domain.AsByteArray());
        }
        static byte[] nameHashSub(byte[] roothash, string subdomain)
        {
            var domain = SmartContract.Sha256(subdomain.AsByteArray()).Concat(roothash);
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


        public static object Main(string method, object[] args)
        {
            #region 通用功能
            if (method == "rootName")
                return rootName();
            if (method == "rootNameHash")
                return rootNameHash();
            if (method == "getInfo")
                return getInfo(args[0] as byte[]);
            if (method == "nameHash")
                return nameHash(args[0] as string);
            if (method == "nameHashSub")
                return nameHashSub(args[0] as byte[], args[1] as string);
            if (method == "nameHashArray")
                return nameHashArray(args[0] as string[]);
            if (method == "resolve")
                return resolve(args[0] as string, args[1] as byte[], args[2] as string);
            if (method == "resolveFull")
                return resolveFull(args[0] as string, args[1] as string[]);
            #endregion
            #region 所有者接口 直接调用&智能合约
            if (method == "owner_SetOwner")
                return owner_SetOwner(args[0] as byte[], args[1] as byte[], args[2] as byte[]);
            if (method == "owner_SetRegister")
                return owner_SetRegister(args[0] as byte[], args[1] as byte[], args[2] as byte[]);
            if (method == "owner_SetResolver")
                return owner_SetResolver(args[0] as byte[], args[1] as byte[], args[2] as byte[]);
            #endregion
            #region 注册器接口 仅智能合约
            if (method == "register_SetSubdomainOwner")
                return register_SetSubdomainOwner(args[0] as byte[], args[1] as string, args[2] as byte[], (args[3] as byte[]).AsBigInteger());
            #endregion
            return new byte[] { 0 };
        }
    }


}
