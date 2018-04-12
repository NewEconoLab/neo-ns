using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using System;
using System.Numerics;
using System.Text;

namespace NeoContractTest1
{
    public class Contract1 : SmartContract
    {
        //private static byte[] GetTrueByte()
        //{
        //    byte[] trueByte = { 2 };

        //    //Runtime.Notify(new object[] { "return", trueByte });
        //    return new byte[] { 1 };
        //}

        private static uint Main()
        {
            //一天的秒数
            const uint oneDay = 86400;

            return getLastBlockTimeStamp() + oneDay * 7;
            //byte[] namehash = Hash256(domain.AsByteArray().Concat(name.AsByteArray()).Concat(subname.AsByteArray()));

            //var owner = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0 };
            //byte[] zeroByte32 = new byte[32];
            ////bool isZero = a == zeroByte32;
            //if (owner == zeroByte32)
            //{
            //    return true;
            //}
            //else
            //{
            //    return false;
            //}
                

            //Runtime.Notify(new object[] { "namehash", namehash });
            //return a!=b;
        }

        //获取最新区块时间戳（unix格式，单位s）
        public static uint getLastBlockTimeStamp()
        {
            return Blockchain.GetBlock(Blockchain.GetHeight()).Timestamp;
        }

        ////VerifySignatureTest
        ////qingmingzi
        ////qingmingzi @hotmail.com
        ////Test VerifySignature, you well get Log and Notify from every step
        ////0006
        ////01

        ////V0.0.1
        ////TXID:0x38380ad9bd241d8a7fd0e4244a636312bc32a11bb6cac6155fe5a767767c7d76
        ////scripthash:0x76df57b9dbf9ec3adbb75b59483fca7c204620ab

        ////V0.0.2
        ////TXID:0xde117a2c15bf3bdfc7fdefe9079615cb2106f5f3e3fddcab5f33959c392e4beb
        ////scripthash:0x0dcf7bfdf63355b9a2681c3032416bcf36fac24e

        ////V0.0.3
        ////TXID:0x69e0a2a1feda9bed332b3dde9d7ee294d0296b8d61758e9ad915112bc0705a7f
        ////scripthash:0xca4743b1b0a4b80f19b5e66e8842835e4b55cc53

        ////V0.0.4
        ////TXID:0xca029d821a44aff653d6ad9fe3e9e8fe4e32fef94ef521fb49dbe3e381b32406
        ////scripthash:0xef5ad26efe26c20d2b1618597d69268062915243

        //public static bool Main(byte[] signature, byte[] pubkey)
        //{
        //    Runtime.Log("Signature:" + signature.AsString());
        //    Runtime.Notify(new object[] { "Signature", signature });

        //    Runtime.Log("Pubkey:" + pubkey.AsString());
        //    Runtime.Notify(new object[] { "Pubkey:", pubkey });

        //    bool vs = VerifySignature(signature, pubkey);
        //    if (vs)
        //    {
        //        Runtime.Log("VerifySignature:True");
        //        Runtime.Notify(new object[] { "VerifySignature", new byte[] { 1 } });
        //    }
        //    else
        //    {
        //        Runtime.Log("VerifySignature:False");
        //        Runtime.Notify(new object[] { "VerifySignature", new byte[] { 0 } });
        //    }

        //    return vs;
        //}

        //public static byte[] Main(string domain, string name, string subname)
        //{
        //    return NameHash(domain, name, subname);
        //}
        //private static byte[] NameHash(string domain, string name, string subname)
        //{
        //    return Hash256(domain.AsByteArray().Concat(name.AsByteArray()).Concat(subname.AsByteArray()));
        //}
        //public static bool Main(uint timestamp, byte[] pubkey, byte[] signature)
        //{
        //    Header header = Blockchain.GetHeader(Blockchain.GetHeight());
        //    if (header.Timestamp < timestamp)
        //        return false;
        //    return VerifySignature(signature,pubkey);
        //}
        //public static bool Main(byte[] signature)
        //{
        //    Header header = Blockchain.GetHeader(Blockchain.GetHeight());
        //    if (header.Timestamp < 1506758400) // 2017-9-30 16:00
        //        return false;
        //    // 这里粘贴上一步复制的公钥字节数组
        //    return VerifySignature(signature, new byte[] { 3, 96, 253, 14, 172, 248, 39, 81, 25, 168, 246, 175, 78, 180, 139, 87, 228, 231, 113, 91, 94, 169, 8, 56, 172, 18, 99, 109, 176, 203, 109, 234, 175 });
        //}
        //public static uint Main()
        //{
        //    Header header = Blockchain.GetHeader(Blockchain.GetHeight());
        //    return header.Timestamp;
        //}
        //public static bool Main(string key,string value) //0707
        //{
        //    //name：putStorage
        //    //TXID：0x0cef61f9f390b6e81ed2963143365d178f4d48afa7acf4cfe18f5341b0b89c35
        //    //scriptHash：a4e7140b0f3b28ec2aad2866503da04ff1760c58

        //    Storage.Put(Storage.CurrentContext, key, value);
        //    return true; //01

        //    //调用“Hello”“QingmingZi” TXID：0xe9f385112e8c6b320c4a95640be52d2e9d5ac64dafa22e86dbff46a0401b9ea2
        //}

        ////**************************
        ////域名解析namehash v
        ////**************************
        //public static byte[] Main(string nns)
        //{
        //    //name.AsByteArray();
        //    //return GetZeroByteArray(0, 32);
        //    //return Sha256(name.AsByteArray());
        //    return NameHash(nns);
        //}

        //private static byte[] NameHash(string name)
        //{
        //    if (name == "")
        //    {
        //        return GetZeroByte32();
        //    }
        //    else
        //    { 
        //        string[] labels = Split(name, ".");
        //        if (labels.Length == 1)
        //        {
        //            byte[] a = GetZeroByte32();
        //            byte[] c = Sha256(labels[0].AsByteArray());

        //            byte[] node = a;
        //            node = Sha256(node.Concat(c));
        //            return node;
        //        }
        //        else
        //        {
        //            byte[] a = GetZeroByte32();
        //            byte[] b = Sha256(labels[1].AsByteArray());
        //            byte[] c = Sha256(labels[0].AsByteArray());

        //            byte[] node = a;
        //            node = Sha256(node.Concat(b));
        //            node = Sha256(node.Concat(c));
        //            return node;
        //        }
        //    }
        //}
        ////**************************
        ////域名解析namehash ^
        ////**************************

        //private static byte[] GetZeroByte32()
        //{
        //    byte[] ba = new byte[32];

        //    //byte[] ba = { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

        //    //byte[] bb = (new byte[] { 4, 5, 6 }).Concat(ba).Concat(new byte[] { 1,2,3});

        //    //byte[] ba = new byte[n];
        //    //for (int i = 0; i < n; i++)
        //    //{
        //    //    ba[i]=b;
        //    //}
        //    return ba;
        //}

        ////循环大量消耗gas!!!
        //private static string[] Split(string str, string c)
        //{
        //    int splitIndex = 0;
        //    for (int i = 0; i < str.Length; i++)
        //    {
        //        if (str.Substring(i, 1) == c)
        //        {
        //            splitIndex = i;
        //            break;
        //        }
        //    }
        //    if (splitIndex == 0)
        //    { return new string[] { str }; }
        //    else
        //    {
        //        string[] result = { str.Substring(0, splitIndex), str.Substring(splitIndex + 1, str.Length - splitIndex - 1) };
        //        return result;
        //    }
        //}

    }

    //namespace Neo.SmartContract
    //{
    //    /// <summary>
    //    /// 表示智能合约的参数类型
    //    /// </summary>
    //    public enum ContractParameterType : byte
    //    {
    //        /// <summary>
    //        /// 签名
    //        /// </summary>
    //        Signature = 0x00,
    //        Boolean = 0x01,
    //        /// <summary>
    //        /// 整数
    //        /// </summary>
    //        Integer = 0x02,
    //        /// <summary>
    //        /// 160位散列值
    //        /// </summary>
    //        Hash160 = 0x03,
    //        /// <summary>
    //        /// 256位散列值
    //        /// </summary>
    //        Sha256 = 0x04,
    //        /// <summary>
    //        /// 字节数组
    //        /// </summary>
    //        ByteArray = 0x05,
    //        PublicKey = 0x06,
    //        String = 0x07,

    //        Array = 0x10,

    //        InteropInterface = 0xf0,

    //        Void = 0xff
    //    }
    //}
}
