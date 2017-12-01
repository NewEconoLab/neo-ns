using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using System;
using System.Numerics;

namespace NCnnsResolvers
{
    //nns解析器（地址型）

    public class nnsResolvers : SmartContract
    {
        //以32位byte[]代表空地址
        private static byte[] GetZeroByte34()
        {
            byte[] zeroByte34 = new byte[34];

            Runtime.Notify(new object[] { "return", zeroByte34 });
            return zeroByte34;
        }

        private static byte[] GetFalseByte()
        {
            byte[] falseByte = new byte[] { 0 };

            Runtime.Notify(new object[] { "return", falseByte });
            return falseByte;
        }

        private static byte[] GetTrueByte()
        {
            byte[] trueByte = new byte[] { 1 };

            Runtime.Notify(new object[] { "return", trueByte });
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
                case "alter"://string domain, string name, string subname, byte[] publickey
                    return Altert((string)args[0], (string)args[1], (string)args[2], (string)args[3]);
                case "delete"://string domain, string name, string subname, byte[] publickey
                    return Delete((string)args[0], (string)args[1], (string)args[2], (string)args[3]);
                default:
                    return GetFalseByte();
            }
        }

        [Appcall("c191b3e4030b9105e59c6bb56ec0d1273cd43284")]//nns注册器 ScriptHash
        public static extern byte[] NnsRegistry(byte[] signature, string operation, object[] args);

        private static byte[] NameHash(string domain, string name, string subname)
        {
            byte[] namehash = NnsRegistry(new byte[32], "namehash", new object[]{ domain, name, subname });

            Runtime.Notify(new object[] { "namehash", namehash });
            return namehash;
        }

        //判断当前合约调用者是否为域名所有者
        private static byte[] CheckNnsOwner(string domain, string name, string subname)
        {
            byte[] owner = NnsRegistry(new byte[32], "query", new object[] { domain, name, subname });

            if (Runtime.CheckWitness(owner))
            {
                return GetTrueByte();
            }
            else{
                return GetFalseByte();
            }
        }

        private static byte[] Query(string domain, string name, string subname)
        {
            byte[] addr = Storage.Get(Storage.CurrentContext, NameHash(domain, name, subname));
            if (addr == null) { return GetZeroByte34(); }

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
                if (oldAddr != null) { Storage.Delete(Storage.CurrentContext, namehash); }

                //记录nns和地址映射
                Storage.Put(Storage.CurrentContext, namehash, addr);
                return GetTrueByte();
            }
            else
            {
                return GetFalseByte();
            }
        }
    }
}
