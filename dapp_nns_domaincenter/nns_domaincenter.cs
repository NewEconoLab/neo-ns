using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using System;
using System.Numerics;

namespace DApp
{
    public class nns_domaincenter : SmartContract
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
        static readonly byte[] initSuperAdmin = Helper.ToScriptHash("ALjSnMZidJqd18iQaoCgFun6iqWRm2cVtj");//初始管理員
        static readonly byte[] jumpContract = Helper.HexToBytes("62134ef8f4aadfa9cb5cba564cdd414a53ddfbdf");//注意 script_hash 是反序的
        //跳板合约为0xdffbdd534a41dd4c56ba5ccba9dfaaf4f84e1362
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
                byte[] resolver = Storage.Get(Storage.CurrentContext, hash.Concat(new byte[] { 0x02 }));
                if (register.Length == 0)
                {
                    return new byte[] { 0x00 };
                }
                var subhash = nameHashSub(hash, domainarray[i]);

                var regcall = (deleDyncall)register.ToDelegate();
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
        static byte[] _doresolve(byte[] resolver, byte[] ttlhash, string protocol, byte[] nnshash)
        {
            if (resolver.Length == 0)
            {
                return new byte[] { 0x00 };
            }
            var ttl = Storage.Get(Storage.CurrentContext, ttlhash.Concat(new byte[] { 0x03 })).AsBigInteger();
            if (ttl < Blockchain.GetHeight())
            {
                return new byte[] { 0x00 };
            }
            var resolveCall = (deleDyncall)resolver.ToDelegate();
            return resolveCall("resolve", new object[] { protocol, nnshash });//解析
        }
        static byte[] resolve(string protocol, byte[] nnshash, string subdomain)
        {
            //先查完整hash是否对应解析器
            var fullhash = nameHashSub(nnshash, subdomain);
            if (fullhash.AsBigInteger() == nnshash.AsBigInteger())//是一个根查询
            {
                var resolverFull = Storage.Get(Storage.CurrentContext, fullhash.Concat(new byte[] { 0x02 }));
                return _doresolve(resolverFull, nnshash, protocol, nnshash);
            }

            var resolverSub = Storage.Get(Storage.CurrentContext, fullhash.Concat(new byte[] { 0x02 }));
            if (resolverSub.Length != 0)//如果他有一个子解析器,调用子解析器
            {
                return _doresolve(resolverSub, fullhash, protocol, fullhash);
            }

            //然后查根域名是否对应解析器
            var resolver = Storage.Get(Storage.CurrentContext, nnshash.Concat(new byte[] { 0x02 }));
            return _doresolve(resolver, nnshash, protocol, fullhash);
        }
        //快速解析
        static byte[] init(byte[] newowner)
        {
            var callhash = ExecutionEngine.CallingScriptHash;
            var nnshash = rootNameHash();
            var o = Storage.Get(Storage.CurrentContext, nnshash.Concat(new byte[] { 0x00 }));
            if (o.Length == 0)
            {
                //初始管理員衹有一個功能,就是轉讓根域名管理權，而且是一次性的，一旦轉讓出去，初始管理員就沒用了
                Storage.Put(Storage.CurrentContext, nnshash.Concat(new byte[] { 0x00 }), newowner);
                return new byte[] { 0x01 };
            }
            return new byte[] { 0x00 };
        }
        #region 所有者功能
        //設置新的所有者(域名轉讓)
        static byte[] owner_SetOwner(byte[] nnshash, byte[] newowner)
        {
            Storage.Put(Storage.CurrentContext, nnshash.Concat(new byte[] { 0x00 }), newowner);
            return new byte[] { 0x01 };
        }
        //所有者设置注册器
        static byte[] owner_SetRegister(byte[] nnshash, byte[] register)
        {
            Storage.Put(Storage.CurrentContext, nnshash.Concat(new byte[] { 0x01 }), register);
            return new byte[] { 0x01 };
        }
        //所有者设置解析器
        static byte[] owner_SetResolver(byte[] nnshash, byte[] resolver)
        {
            Storage.Put(Storage.CurrentContext, nnshash.Concat(new byte[] { 0x02 }), resolver);
            return new byte[] { 0x01 };
        }
        #endregion
        #region 注册器功能
        /// <summary>
        /// 注册器功能组
        /// </summary>
        //更改子域名所有者
        static byte[] register_SetSubdomainOwner(byte[] nnshash, byte[] subhash, byte[] owner, BigInteger ttl)
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
            Storage.Put(Storage.CurrentContext, subhash.Concat(new byte[] { 0x00 }), owner);
            Storage.Put(Storage.CurrentContext, subhash.Concat(new byte[] { 0x03 }), ttl);
            return new byte[] { 0x01 };
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

        //0 not match
        //1 address
        //2 contract
        //3 people jump
        //4 contract jump
        static int CheckOwner(byte[] callscript, byte[] p0, byte[] p1, byte[] p2)
        {
            if (callscript.AsBigInteger() == jumpContract.AsBigInteger())
            {//如果是跳板合约调用
                byte[] _callscript = p0;
                byte[] owner = p1;
                byte[] nnshash = p2;
                var o = Storage.Get(Storage.CurrentContext, nnshash.Concat(new byte[] { 0x00 }));
                if (_callscript.AsBigInteger() == o.AsBigInteger())//智能合約所有者
                {
                    return 4;
                }
                if (Runtime.CheckWitness(owner))//账户所有者
                {
                    if (o.AsBigInteger() == owner.AsBigInteger())
                    {
                        return 3;
                    }
                }
            }
            else
            {
                byte[] owner = p0;
                byte[] nnshash = p1;
                var o = Storage.Get(Storage.CurrentContext, nnshash.Concat(new byte[] { 0x00 }));
                if (callscript.AsBigInteger() == o.AsBigInteger())//智能合約所有者
                {
                    return 2;
                }
                if (Runtime.CheckWitness(owner))//账户所有者
                {
                    if (o.AsBigInteger() == owner.AsBigInteger())
                    {
                        return 1;
                    }
                }
            }
            return 0;
        }
        static int CheckRegister(byte[] callscript, byte[] p0, byte[] p1)
        {
            //先不去考虑跳板的问题
            if (callscript.AsBigInteger() == jumpContract.AsBigInteger())
            {//如果是跳板合约调用
                byte[] _callscript = p0;
                byte[] nnshash = p1;
                var register = Storage.Get(Storage.CurrentContext, nnshash.Concat(new byte[] { 0x01 }));
                if (_callscript.AsBigInteger() == register.AsBigInteger())
                {
                    return 2;
                }
            }
            else
            {
                byte[] nnshash = p0;
                var register = Storage.Get(Storage.CurrentContext, nnshash.Concat(new byte[] { 0x01 }));
                if (callscript.AsBigInteger() == register.AsBigInteger())
                {
                    return 1;
                }
            }
            return 0;
        }
        public static object Main(string method, object[] args)
        {
            string magic = "20180114";
            //必须在入口函数取得callscript，调用脚本的函数，也会导致执行栈变化，再取callscript就晚了
            var callscript = ExecutionEngine.CallingScriptHash;


            #region 通用功能,不需要权限验证
            if (method == "rootName")
                return rootName();
            if (method == "rootNameHash")
                return rootNameHash();
            if (method == "getInfo")
                return getInfo((byte[])args[0]);
            if (method == "nameHash")
                return nameHash((string)args[0]);
            if (method == "nameHashSub")
                return nameHashSub((byte[])args[0], (string)args[1]);
            if (method == "nameHashArray")
                return nameHashArray((string[])args[0]);
            if (method == "resolve")
                return resolve((string)args[0], (byte[])args[1], (string)args[2]);
            if (method == "resolveFull")
                return resolveFull((string)args[0], (string[])args[1]);
            #endregion
            #region 初始化功能,仅限初始管理员
            if (method == "init")
            {
                if (Runtime.CheckWitness(initSuperAdmin))
                {
                    return init((byte[])args[0]);
                }
                return new byte[] { 0x00 };
            }
            #endregion
            #region 所有者接口 直接调用&智能合约
            if (method == "owner_SetOwner")
            {
                int n = CheckOwner(callscript, (byte[])args[0], (byte[])args[1], (byte[])args[2]);
                if (n == 1 || n == 2)
                {
                    return owner_SetOwner((byte[])args[1], (byte[])args[2]);
                }
                if (n == 3 || n == 4)
                {
                    return owner_SetOwner((byte[])args[2], (byte[])args[3]);
                }
                return new byte[] { 0x00 };

            }
            if (method == "owner_SetRegister")
            {
                int n = CheckOwner(callscript, (byte[])args[0], (byte[])args[1], (byte[])args[2]);
                if (n == 1 || n == 2)
                {
                    return owner_SetRegister((byte[])args[1], (byte[])args[2]);
                }
                if (n == 3 || n == 4)
                {
                    return owner_SetRegister((byte[])args[2], (byte[])args[3]);
                }
                return new byte[] { 0x00 };
            }
            if (method == "owner_SetResolver")
            {
                int n = CheckOwner(callscript, (byte[])args[0], (byte[])args[1], (byte[])args[2]);
                if (n == 1 || n == 2)
                {
                    return owner_SetResolver((byte[])args[1], (byte[])args[2]);
                }
                if (n == 3 || n == 4)
                {
                    return owner_SetResolver((byte[])args[2], (byte[])args[3]);
                }
                return new byte[] { 0x00 };
            }
            #endregion
            #region 注册器接口 仅智能合约
            if (method == "register_SetSubdomainOwner")
            {
                int n = CheckRegister(callscript, (byte[])args[0], (byte[])args[1]);
                if (n == 1)
                {
                    return register_SetSubdomainOwner((byte[])args[0], (byte[])args[1], (byte[])args[2], ((byte[])args[3]).AsBigInteger());
                }
                if (n == 2)
                {
                    return register_SetSubdomainOwner((byte[])args[1], (byte[])args[2], (byte[])args[3], ((byte[])args[4]).AsBigInteger());
                }
                return new byte[] { 0x00 };
            }
            #endregion
            return new byte[] { 0 };
        }
    }


}
