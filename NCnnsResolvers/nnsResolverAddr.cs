using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using System;
using System.Numerics;

namespace NCnnsResolverAddr
{
    //nns解析器（地址型）

    //nnsResolver(addr)
    //qingmingzi
    //matrix3345@hotmail.com
    //NEO Name Service Resolver(address)
    //0710
    //05

    //部署
    //v0.0.1
    //TXID:0x8615e23ea07f4d029ed34fa58bf6ac1cdd3642bc909cf0087d2057f47d7d3abf
    //scripthash:0x7244f292382c4db2fcc391cc565d51806dcdcdc8

    //v0.0.2
    //加强通知辨识
    //TXID:0x3418250eab0938e90322787acafecdc3a2fb9674501df29ec7e28f72f0a46827
    //scripthash:0x171ca20b36c73cb20b10d2804286eb82f6b93069

    public class nnsResolverAddr : SmartContract
    {
        string miagic = "qingmingzi";//魔法代码

        //以34位byte[]代表空地址
        private static byte[] GetZeroByte34(string label)
        {
            byte[] zeroByte34 = new byte[34];

            Runtime.Notify(new object[] { label, zeroByte34 });
            return zeroByte34;
        }

        private static byte[] GetFalseByte(string label)
        {
            byte[] falseByte = new byte[] { 0 };

            Runtime.Notify(new object[] { label, falseByte });
            return falseByte;
        }

        private static byte[] GetTrueByte(string label)
        {
            byte[] trueByte = new byte[] { 1 };

            Runtime.Notify(new object[] { label, trueByte });
            return trueByte;
        }

        public static byte[] Main(string operation, object[] args)
        {
            switch (operation)
            {
                case "namehash"://string domain, string name, string subname
                    return NameHash((string)args[0], (string)args[1], (string)args[2]);
                case "query"://string domain, string name, string subname
                    return Query((string)args[0], (string)args[1], (string)args[2]);
                case "alter"://string domain, string name, string subname, string addr
                    return Altert((string)args[0], (string)args[1], (string)args[2], (string)args[3]);
                case "delete"://string domain, string name, string subname
                    return Delete((string)args[0], (string)args[1], (string)args[2]);
                default:
                    return GetFalseByte("main方法");
            }
        }

        [Appcall("c191b3e4030b9105e59c6bb56ec0d1273cd43284")]//nns注册器 ScriptHash
        public static extern byte[] NnsRegistry(byte[] signature, string operation, object[] args);

        private static byte[] NameHash(string domain, string name, string subname)
        {
            byte[] namehash = NnsRegistry(new byte[32], "namehash", new object[]{ domain, name, subname });

            Runtime.Notify(new object[] { "取到namehash", namehash });
            return namehash;
        }

        //判断当前合约调用者是否为域名所有者
        private static byte[] CheckNnsOwner(string domain, string name, string subname)
        {
            byte[] owner = NnsRegistry(new byte[32], "query", new object[] { domain, name, subname });
            Runtime.Notify(new object[] { "取到owner", owner });

            if (Runtime.CheckWitness(owner))
            {
                return GetTrueByte("CheckWitness验证");
            }
            else{
                return GetFalseByte("CheckWitness验证");
            }
        }

        private static byte[] Query(string domain, string name, string subname)
        {
            byte[] addr = Storage.Get(Storage.CurrentContext, NameHash(domain, name, subname));
            if (addr == null) { return GetZeroByte34("query查询"); }

            Runtime.Notify(new object[] { "addr", addr });
            return addr;
        }


        private static byte[] Altert(string domain, string name, string subname, string addr)
        {
            if (CheckNnsOwner(domain, name, subname) == new byte[] { 1 })
            {
                byte[] namehash = NameHash(domain, name, subname);

                //如果已有地址就先删除
                byte[] oldAddr = Storage.Get(Storage.CurrentContext, namehash);
                if (oldAddr != null) {
                    Storage.Delete(Storage.CurrentContext, namehash);
                }

                //记录nns和地址映射
                Storage.Put(Storage.CurrentContext, namehash, addr);
                return GetTrueByte("altert修改");
            }
            else
            {
                return GetFalseByte("altert修改");
            }
        }

        private static byte[] Delete(string domain, string name, string subname)
        {
            if (CheckNnsOwner(domain, name, subname) == new byte[] { 1 })
            {
                byte[] namehash = NameHash(domain, name, subname);

                byte[] oldAddr = Storage.Get(Storage.CurrentContext, namehash);

                if (oldAddr != null){
                    Storage.Delete(Storage.CurrentContext, namehash);
                    return GetTrueByte("delete删除地址");
                }
                else
                {
                    return GetFalseByte("delete删除地址");
                }
            }
            else
            {
                return GetFalseByte("delete删除鉴权");
            }
        }
    }
}
