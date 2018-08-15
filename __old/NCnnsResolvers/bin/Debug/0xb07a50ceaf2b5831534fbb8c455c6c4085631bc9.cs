using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using System;
using System.ComponentModel;
using System.Numerics;

namespace NCnnsResolverAddr
{
    //nns解析器（地址型）

    //nnsResolver(addr)
    //qingmingzi
    //qingmingzi@hotmail.com
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

    //v0.0.3
    //解决new byte[]{ 1 }比较总是为真的bug
    //TXID:0xeeb4f79ea47b6b0b9d2263cf0a2d8fc755cdec647fa176c52783e2e90524d336
    //scripthash:0xda91c9cd8db25ea10112918f527046de1a651f56

    //v0.0.4
    //解决0地址输入Runtime.CheckWitness()造成合约失败的bug
    //TXID:0xddf0397d587a32fcc5512451714ed809e4de55056321abbb279ea9de4fdefa7f
    //scripthash:0xa5cc028d795322e10e88a2f62a248726d2552413

    //v0.0.5
    //解决所有权人无法维护解析的问题
    //TXID:0xd6e42e539fb70059247d72d35da2c393ca2a9c61ea20153af426f123d92b3b0e
    //scripthash:0x97922e240547e3ea9e3ed8e60785207dcff252b1

    //v0.0.6
    //解决所有权人无法维护解析的问题（仍未解决，似乎中间出错）
    //TXID:0x21f0d5c313eca2d8c2298f92f498d1dc1b8183ba78fa671b695af832c267f491
    //scripthash:0xf50b58d64e67f3cf4eb01041f919961947bafd56

    //v0.0.7
    //放弃自动删除，可以实现定义映射地址
    //TXID:0x5b3000c7f8b569f8f463a74b92aa86a5e01efc3ac7676027b61a0979de410412
    //scripthash:0xd8a79978453784c4f40ff3b0599b9d6584fecd28

    //v0.0.8
    //解决所有权人无法维护解析的问题
    //TXID:0xdf3163d928d2893e4e45f13d0ac4cb3a4723f8998eea56f0c46ae4e0443f40d9
    //scripthash:0x706c89208c5b6016a054a58cc83aeda0d70f0f95

    //v0.0.9
    //采用nep5类似模式，发出nns解析notify
    //TXID:0xa5ec2eea180f8bae75241e168ab07d70136c706d8e9df8e82f32961d9fa51ba3
    //scropthash:0x009e35c7b267b67b9ca89ea91e8fe71cdff470d1

    //v0.0.10
    //暂时去除CheckNnsOwner对未注册的判断
    //TXID:0x391d8ee93d8474a73254ff08980ad0bdbdf68c3bd89b19eaa9e2c4208a0942ab
    //scropthash:0xb07a50ceaf2b5831534fbb8c455c6c4085631bc9

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
        private static bool CheckNnsOwner(string domain, string name, string subname)
        {
            byte[] owner = NnsRegistry(new byte[32], "query", new object[] { domain, name, subname });
            Runtime.Notify(new object[] { "取到owner", owner });

            //如果域名没有注册，所有者验证返回假
            //byte[] zeroByte32 = new byte[32];
            //if (owner == zeroByte32)
            //{
            //    Runtime.Notify(new object[] { "CheckWitness验证（未注册）", new byte[] { 0 } });
            //    return false;
            //}

            if (Runtime.CheckWitness(owner)){
                byte[] trueByte = new byte[] { 1 };
                Runtime.Notify(new object[] { "CheckWitness验证", trueByte });
                return true;
            }
            else{
                Runtime.Notify(new object[] { "CheckWitness验证", new byte[] { 0 } });
                return false;
            }
        }

        private static byte[] Query(string domain, string name, string subname)
        {
            byte[] addr = Storage.Get(Storage.CurrentContext, NameHash(domain, name, subname));
            if (addr == null) { return GetZeroByte34("query查询"); }

            Runtime.Notify(new object[] { "addr地址", addr });
            return addr;
        }

        //nns resolver notify
        public delegate void deleAlertResolver(byte[] namehash, string addr);
        [DisplayName("alertResolver")]
        public static event deleAlertResolver AlertResolverNotify;

        private static byte[] Altert(string domain, string name, string subname, string addr)
        {
            if (CheckNnsOwner(domain, name, subname))
            {
                byte[] namehash = NameHash(domain, name, subname);

                //如果已有地址就先删除
                byte[] oldAddr = Storage.Get(Storage.CurrentContext, namehash);
                if (oldAddr.Length>0)
                {
                    Storage.Delete(Storage.CurrentContext, namehash);
                }

                //记录nns和地址映射
                Storage.Put(Storage.CurrentContext, namehash, addr);

                //发出域名地址解析映射通知
                AlertResolverNotify(namehash, addr);

                return GetTrueByte("altert修改");
            }
            else
            {
                return GetFalseByte("altert修改");
            }
        }

        private static byte[] Delete(string domain, string name, string subname)
        {
            if (CheckNnsOwner(domain, name, subname))
            {
                byte[] namehash = NameHash(domain, name, subname);

                byte[] oldAddr = Storage.Get(Storage.CurrentContext, namehash);

                if (oldAddr.Length>0){
                    Storage.Delete(Storage.CurrentContext, namehash);

                    //发出域名地址解析删除通知
                    AlertResolverNotify(namehash, "");

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
