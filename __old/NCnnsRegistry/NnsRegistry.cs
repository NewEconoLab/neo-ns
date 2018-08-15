using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using System;
using System.Numerics;

namespace NCnnsRegistry
{
    public class NnsRegistry : SmartContract
    {
        //nnsRegistry
        //qingmingzi
        //qingmingzi@hotmail.com
        //NEO Name Service Registry
        //000710
        //05

        //部署
        //v0.0.1
        //TXID:0x7cd01c3af34977285ed2875db442ddcdb485b4dce6d1d6994832f8d6c5540c72
        //scripthash:0x5bc467cfa8c5d3bea7d7c4c9077800112d0e4576

        //v0.0.2
        //TXID:0xad198ece0367ce93943cbf29f5afe5061bf5ff957ee2e85691d31020c514d9f6
        //scripthash:0x3f5484c34e1f6a15a815b7b1e37b3529fa7618f8

        //v0.0.3
        //TXID:0x7a7f808287e5f3e3782ac4c8fff0485e577412b425fccb86c44114ecac9f3d4a
        //scripthash:0xa386dd4f87e7757f117bb16ac533cc5c0378bfea

        //v0.0.3.1 暂时去除VerifySignature(signature, publickey)校验
        //TXID:0x8bf5fa6a4eb33023af8fd0f9f10c72c34515475fa4ec8f4d4db265ae6a57442f
        //scripthash:0x2e88caf10afe621e90142357236834e010b16df2

        //v0.0.4
        //暂时去除VerifySignature(signature, publickey)校验
        //所有返回前添加Runtime.Notify
        //TXID:0x01daf53e667ae32ba146779272a52bfcb5d9c058071785bc8141ee8f7ae6202c
        //scripthash:0xaffecff851475894a807d52b0554f63768855cdf

        //v0.0.5 hash未变，发布失败
        //暂时去除VerifySignature(signature, publickey)校验
        //所有返回前添加Runtime.Notify
        //修复bug
        //TXID:0x50f356af4d3958184f13038f4cd0a82d5178a5993da198897bffdd717b8ac4ac
        //scripthash:0xaffecff851475894a807d52b0554f63768855cdf

        //v0.0.6
        //暂时去除VerifySignature(signature, publickey)校验
        //所有返回前添加Runtime.Notify
        //修复bug
        //用0x00代表fasle，用0x0000代表true
        //TXID:0xae76ee147676ad236bb249625bc5df465cec60245dc9df0a8abf41e6c9fa892b
        //scripthash:0xc191b3e4030b9105e59c6bb56ec0d1273cd43284

        //qingmingzi.neo
        //9b87a694f0a282b2b5979e4138944b6805350c6fa3380132b21a2f12f9c2f4b6

        //以32位byte[]代表空地址
        private static byte[] GetZeroByte32()
        {
            byte[] zeroByte32 = new byte[32];

            Runtime.Notify(new object[] { "return", zeroByte32 });
            return zeroByte32;
        }

        private static byte[] GetFalseByte()
        {
            byte[] falseByte = new byte[1];

            Runtime.Notify(new object[] { "return", falseByte });
            return falseByte;
        }

        private static byte[] GetTrueByte()
        {
            byte[] trueByte = new byte[2];

            Runtime.Notify(new object[] { "return", trueByte });
            return trueByte;
        }



        public static byte[] Main(byte[] signature, string operation, object[] args)
        {
            switch (operation)
            {
                case "namehash"://string domain, string name, string subname
                    return NameHash((string)args[0], (string)args[1], (string)args[2]);
                case "query"://string domain, string name, string subname
                    return Query((string)args[0], (string)args[1], (string)args[2]);
                case "register"://string domain, string name, byte[] publickey, byte[] signature
                    return Register((string)args[0], (string)args[1], (byte[])args[2], signature);
                case "subregister"://string domain, string name, string subname, byte[] publickey,byte[] signature
                    return SubRegister((string)args[0], (string)args[1], (string)args[2], (byte[])args[3], signature);
                case "delete"://string domain, string name, string subname, byte[] signature
                    return Delete((string)args[0], (string)args[1], (string)args[2], (byte[])args[3], signature);
                //case "transfer":
                //    return Transfer((string)args[0], (byte[])args[1]);
                default:
                    return GetFalseByte();
            }
        }

        private static byte[] NameHash(string domain, string name, string subname)
        {
            byte[] namehash = Hash256(domain.AsByteArray().Concat(name.AsByteArray()).Concat(subname.AsByteArray()));

            Runtime.Notify(new object[] { "namehash", namehash });
            return namehash;
        }

        private static byte[] Query(string domain, string name, string subname)
        {
            byte[] owner = Storage.Get(Storage.CurrentContext, NameHash(domain, name, subname));
            if (owner == null) { return GetZeroByte32(); }

            Runtime.Notify(new object[] { "owner", owner });
            return owner;
        }

        //主域名注册者只能是自己
        private static byte[] Register(string domain, string name, byte[] publickey, byte[] signature)
        {
            //if (!Runtime.CheckWitness(publickey.AsByteArray())) return false;

            ////验证注册登记员是否是当前账户
            //bool vs = VerifySignature(signature, publickey);        
            //if (!vs)
            //{
            //    Runtime.Log("Register VerifySignature is false.");
            //    return getZeroByte32();
            //}              

            //验证主域名是否已被注册
            byte[] namehash = NameHash(domain, name,"");
            byte[] value = Storage.Get(Storage.CurrentContext, namehash);
            if (value != null) return GetFalseByte();

            //所有权映射写入区块链私有化存储区
            //Runtime.Log("Register namehash is " + namehash.AsString());
            Storage.Put(Storage.CurrentContext, namehash, publickey);

            return GetTrueByte();
        }

        //子域名可以由主域名登记员指定所有者
        private static byte[] SubRegister(string domain, string name, string subname, byte[] publickey,byte[] signature)
        {

            //if (!Runtime.CheckWitness(publickey.AsByteArray())) return false;

            //验证主域名所有者是否当前登记员
            byte[] namehash = NameHash(domain, name,"");
            byte[] namevalue = Storage.Get(Storage.CurrentContext, namehash);
            if (namevalue == null) return GetFalseByte();
            if (namevalue != publickey) return GetFalseByte();

            //bool vs = VerifySignature(signature, namevalue);  
            //if (!vs)
            //{
            //    Runtime.Log("SubRegister VerifySignature is false.");
            //    return getZeroByte32();
            //} 

            //验证子域名是否已被注册
            byte[] subnamehash = NameHash(domain, name, subname);
            byte[] subnamevalue = Storage.Get(Storage.CurrentContext, subnamehash);
            if (subnamevalue != null) return GetFalseByte();

            //所有权映射写入区块链私有化存储区
            //Runtime.Log("SubRegister subnamehash is " + subnamehash.AsString());
            Storage.Put(Storage.CurrentContext, subnamehash, publickey);

            return GetTrueByte();
        }

        private static byte[] Delete(string domain, string name, string subname, byte[] publickey, byte[] signature)
        {
            byte[] subnamehash = NameHash(domain, name, subname);

            //验证域名所有者是否为当前登记员
            byte[] subnamevalue = Storage.Get(Storage.CurrentContext, subnamehash);
            if (subnamevalue == null) return GetFalseByte();
            if (subnamevalue != publickey) return GetFalseByte();

            //bool vs = VerifySignature(signature, subnamevalue);          
            //if (!vs)
            //{
            //    Runtime.Log("Delete VerifySignature is false.");
            //    return getZeroByte32();
            //} 
            //if (!Runtime.CheckWitness(subnamevalue)) return false;

            //注销域名注册
            Storage.Delete(Storage.CurrentContext, subnamehash);

            //待加入注销解析器

            return GetTrueByte();
        }

        //private static bool Transfer(string domain, byte[] to)
        //{
        //    if (!Runtime.CheckWitness(to)) return false;
        //    byte[] from = Storage.Get(Storage.CurrentContext, domain);
        //    if (from == null) return false;
        //    if (!Runtime.CheckWitness(from)) return false;
        //    Storage.Put(Storage.CurrentContext, domain, to);
        //    return true;
        //}


    }
}
