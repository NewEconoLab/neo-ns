using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using System;
using System.Numerics;

namespace DApp
{
    public class nns_domaincenter : SmartContract
    {
        //域名中心 
        //    域名中心是一个不会改变地址的合约，他的作用是管理某一个域名的数据
        //使用存储
        //dict<nnshash,owner> 记录域名拥有者数据
        //dict<nnshash,controller> 域名控制器
        //dict<nnshash,resolver> 域名解析器
        //dict<nnshash,ttl>   记录域名过期数据
        //dict<nnshash,resolvedata> 记录域名解析数据
        //control 记录当前域的管理器
        
        //域名解析器
        //解析器是否有必要存在，如果控制器已經能直接設置域名解析数据，那么解析器功能有些多余。
        //除了动态解析，而且是不是每层都必须配置解析器，如果不配置解析器，能不能解析？
            
        //使用Nep4
        //
        //根域名的所有者是超级管理员
        //所有者可以设置自己域名的控制器
        //以后还应该设置联合所有权的机制，多个所有者，几人以上签名才可用

        const string rootDomain = "test";
        static readonly byte[] InitSuperAdmin = Helper.ToScriptHash("");//初始管理員
        public static byte[] rootNameHash()
        {
            return NameHash(rootDomain);
        }
        public static string rootName()
        {
            return rootDomain;
        }

        delegate object deleResolve(string method, object[] arr);
        //域名解析
        //完整解析，可以处理各种域名到期，权限变化问题，也可以处理动态解析
        static object resolveFull(string protocol, string[] domainarray)
        {
            byte[] hash = NameHash(domainarray[0]);
            byte[] resolver = Storage.Get(Storage.CurrentContext, "mapresolver".AsByteArray().Concat(hash));
            //{ test.aaa.second //一層層上}
            for (var i = 1; i < domainarray.Length; i++)
            {
                hash = NameHashSub(hash, domainarray[i]);
                var ttl = Storage.Get(Storage.CurrentContext, "mapttl".AsByteArray().Concat(hash)).AsBigInteger();
                if (ttl < Blockchain.GetHeight()) //調用鏈條上面有一個解析器過期了
                {
                    return null;
                }

                if (i == domainarray.Length - 1)
                {
                    var resolveCall = (deleResolve)resolver.ToDelegate();
                    return resolveCall("resolve", new object[] {protocol, hash });//解析
                }
                else
                {
                    var resolveCall = (deleResolve)resolver.ToDelegate();
                    if ((int)resolveCall("active", new object[] { hash }) == 1)
                    {
                        resolver = Storage.Get(Storage.CurrentContext, "mapresolver".AsByteArray().Concat(hash));//得到子解析器
                    }
                    else//如果解析鏈上有一個解析器掉鏈子了，那就不解析了
                    {
                        return null;
                    }
                }
            }
            return null;
            //dict<nnshash,resolver> //查到一层解析器
        }
        //快速解析，查表，返回快，无需nep4
        static object resolveQuick(string protocol, byte[] nnshash)
        {
            var ttl = Storage.Get(Storage.CurrentContext, "mapttl".AsByteArray().Concat(nnshash)).AsBigInteger();
            if (ttl < Blockchain.GetHeight()) //過期了
            {
                return null;
            }
            //dict<nnshash,resolvedata> 记录域名解析数据
            var o = Storage.Get(Storage.CurrentContext, "mapresolvedata".AsByteArray().Concat(nnshash).Concat(protocol.AsByteArray()));
            return o;
        }

        //設置新的所有者(域名轉讓)
        static object owner_SetOwner(byte[] owner, byte[] nnshash, byte[] newowner)
        {
            var callhash = Neo.SmartContract.Framework.Services.System.ExecutionEngine.CallingScriptHash;
            var o = Storage.Get(Storage.CurrentContext, "mapowner".AsByteArray().Concat(nnshash));
            if (o.Length == 0 && //一個域名沒有所有者
                InitSuperAdmin.AsBigInteger() == owner.AsBigInteger() && //并且owner 是 初始管理員
                rootNameHash().AsBigInteger() == nnshash.AsBigInteger() //并且設置的是 rootHash
                )
            {
                //初始管理員衹有一個功能,就是轉讓根域名管理權，而且是一次性的，一旦轉讓出去，初始管理員就沒用了
                Storage.Put(Storage.CurrentContext, "mapowner".AsByteArray().Concat(nnshash), newowner);
            }
            if (
                callhash.AsBigInteger() == o.AsBigInteger()//智能合約所有者
                ||
                (Runtime.CheckWitness(owner) && o.AsBigInteger() == owner.AsBigInteger())//個人所有者
                )
            {
                Storage.Put(Storage.CurrentContext, "mapowner".AsByteArray().Concat(nnshash), newowner);
                return true;
            }
            return false;
        }
        //所有者设置控制器
        static object owner_SetController(byte[] owner, byte[] nnshash, byte[] controller)
        {
            var callhash = Neo.SmartContract.Framework.Services.System.ExecutionEngine.CallingScriptHash;
            var o = Storage.Get(Storage.CurrentContext, "mapowner".AsByteArray().Concat(nnshash));
            if (
                callhash.AsBigInteger() == o.AsBigInteger()//智能合約所有者
                ||
                (Runtime.CheckWitness(owner) && o.AsBigInteger() == owner.AsBigInteger())//個人所有者
                )
            {
                Storage.Put(Storage.CurrentContext, "mapcontroller".AsByteArray().Concat(nnshash), controller);
                return true;
            }
            return false;
        }
        //控制器注册域名，就是给域名拥有者
        static object controller_SetSubdomainOwner(byte[] nnshash, string subdomain, byte[] owner)
        {
            var c = Storage.Get(Storage.CurrentContext, "mapcontroller".AsByteArray().Concat(nnshash));
            if (Helper.AsBigInteger(c) == Helper.AsBigInteger(Neo.SmartContract.Framework.Services.System.ExecutionEngine.CallingScriptHash))
            {
                //衹有控制器允許改
                byte[] namehashsub = NameHashSub(nnshash, subdomain);
                Storage.Put(Storage.CurrentContext, "mapowner".AsByteArray().Concat(namehashsub), owner);
                return true;
            }
            return false;
        }
        //控制器设置解析器
        static object controller_SetResolver(byte[] nnshash, byte[] resolver)
        {
            var c = Storage.Get(Storage.CurrentContext, "mapcontroller".AsByteArray().Concat(nnshash));
            if (Helper.AsBigInteger(c) == Helper.AsBigInteger(Neo.SmartContract.Framework.Services.System.ExecutionEngine.CallingScriptHash))
            {
                //衹有控制器允許改
                Storage.Put(Storage.CurrentContext, "mapresolver".AsByteArray().Concat(nnshash), resolver);
                return true;
            }
            return false;
        }
        static object controller_SetSubdomainResolver(byte[] nnshash, string subdomain, byte[] resolver)
        {
            var c = Storage.Get(Storage.CurrentContext, "mapcontroller".AsByteArray().Concat(nnshash));
            if (Helper.AsBigInteger(c) == Helper.AsBigInteger(Neo.SmartContract.Framework.Services.System.ExecutionEngine.CallingScriptHash))
            {
                //衹有控制器允許改
                byte[] namehashsub = NameHashSub(nnshash, subdomain);
                Storage.Put(Storage.CurrentContext, "mapresolver".AsByteArray().Concat(namehashsub), resolver);
                return true;
            }
            return false;
        }
        //控制器设置解析数据
        static object controller_SetResolveData(byte[] nnshash, string protocol, byte[] data)
        {
            var c = Storage.Get(Storage.CurrentContext, "mapcontroller".AsByteArray().Concat(nnshash));
            if (Helper.AsBigInteger(c) == Helper.AsBigInteger(Neo.SmartContract.Framework.Services.System.ExecutionEngine.CallingScriptHash))
            {
                //衹有控制器允許改
                Storage.Put(Storage.CurrentContext, "mapresolvedata".AsByteArray().Concat(nnshash).Concat(protocol.AsByteArray()), data);
                return true;
            }
            return false;
        }
        static object controller_SetSubdomainResolveData(byte[] nnshash, string subdomain, string protocol, byte[] data)
        {
            var c = Storage.Get(Storage.CurrentContext, "mapcontroller".AsByteArray().Concat(nnshash));
            if (Helper.AsBigInteger(c) == Helper.AsBigInteger(Neo.SmartContract.Framework.Services.System.ExecutionEngine.CallingScriptHash))
            {
                //衹有控制器允許改
                byte[] namehashsub = NameHashSub(nnshash, subdomain);
                Storage.Put(Storage.CurrentContext, "mapresolvedata".AsByteArray().Concat(namehashsub).Concat(protocol.AsByteArray()), data);
                return true;
            }
            return false;
        }
        //域名转hash算法
        //aaa.bb.test =>{"test","bb","aa"}
        static byte[] NameHash(string domain)
        {
            return SmartContract.Sha256(domain.AsByteArray());
        }
        static byte[] NameHashSub(byte[] roothash, string subdomain)
        {
            var domain = SmartContract.Sha256(subdomain.AsByteArray()).Concat(roothash);
            return SmartContract.Sha256(domain);
        }
        static byte[] NameHashArray(string[] domainarray)
        {
            byte[] hash = NameHash(domainarray[0]);
            for (var i = 1; i < domainarray.Length; i++)
            {
                hash = NameHashSub(hash, domainarray[i]);
            }
            return hash;
        }




        public static object Main(string method, object[] args)
        {
            if (method == "rootName")
                return rootName();
            if (method == "rootNameHash")
                return rootNameHash();


            return false;
        }
    }


}
