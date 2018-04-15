using Neo.SmartContract.Framework;
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

        const int blockday = 3600*24;//粗略一天的块数
        const int domaindays = 1;//租一次给几天

        [Appcall("dffbdd534a41dd4c56ba5ccba9dfaaf4f84e1362")]
        static extern object rootCall(string method, object[] arr);


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
        //不用在其他阶段保密
        public static byte[] requestSubDomain(byte[] who, byte[] nnshash, string subdomain)
        {
            if (subdomain.AsByteArray().Length == 0)
            {
                return new byte[] { 0x00 };
            }
            if (rootDomainHash.AsBigInteger() != nnshash.AsBigInteger())//只能用来分配固定的域
            {
                return new byte[] { 0x00 };
            }
            if (Runtime.CheckWitness(who) == false)
            {
                return new byte[] { 0x00 };
            }
            var subhash = nameHashSub(nnshash, subdomain);
            var owner = Storage.Get(Storage.CurrentContext, subhash);
            var ttl = Blockchain.GetHeight(); ;
            if (owner.Length == 0)//无人认领，直接分配
            {
                ttl += blockday * domaindays;
                return setSubOwner(nnshash, subdomain, who, ttl);
            }
            else
            { //bi
                object[] obj = new object[1];
                var callback = (object[])rootCall("getInfo", obj);
                var ttltarget = (BigInteger)callback[3];
                if (ttltarget < ttl || owner.AsBigInteger() == who.AsBigInteger())//过期域名
                {
                    ttl += blockday * domaindays;
                    return setSubOwner(nnshash, subdomain, who, ttl);
                }
            }
            return new byte[] { 0x00 };
        }
        public enum DomainUseStatus
        {
            Empty,//未注冊
            InUse,//正常使用
            TTLFall,//TTL已過期
        }
        public class DomainStatus
        {
            public DomainUseStatus status;
            public byte[] lastsellingID;//上一次拍賣的拍賣ID
        }
        public static object Main(string method, object[] args)
        {
            //随便调用，subowner 這個機制可否直接保存
            if (method == "getSubOwner")
                return getSubOwner((byte[])args[0], (string)args[1]);
            //请求者调用
            //不能這樣暴力開了
            //if (method == "requestSubDomain")
            //    return requestSubDomain((byte[])args[0], (byte[])args[1], (string)args[2]);
            if (method == "getdomainRegisterStatus")
            {//看域名狀態
                //0x00未登記 可以申請開標
                //0x01使用中
                //0x02已過期 可以申請開標
                //0x10開標階段01 ，自由競價，固定時間
                //0x11開標階段02 ，自由競價，固定時間如果這個階段無人出價直接階段
                //0x12開標階段03 ，自由競價，時間不確定隨時結束
                //0x20投標結束，有人中標則可將狀態改回01，無人中標則可直接申請開標，轉爲10
                byte[] nnshash = (byte[])args[0];
                string domain = (string)args[1];
            }

            if (method == "getdomainstate")//查看域名狀態
            {
                //0x00 為登記
                //0x01
                //0x02 
                //0x03
            }
            if (method == "getsellingstate")//查看拍賣狀態
            {//0x10 0x11 0x12 0x20

            }
            if (method == "wantbuy")//申請開標 (00,02,20)=>(10)
            {
                byte[] who = (byte[])args[0];

                byte[] nnshash = (byte[])args[1];
                string domain = (string)args[2];
            }
            if (method == "addprice")//出價&加價 (10,11,12)=>不改變狀態
            {
                byte[] who = (byte[])args[0];
                byte[] nnshash = (byte[])args[1];
                string domain = (string)args[2];
                BigInteger myprice = (BigInteger)args[2];
                byte[] txid = (byte[])args[3];//提供一個txid，查這筆txid 的nep5入賬證明
                //如果有就充值到我的戶頭
                //如果戶頭的錢夠扣，就參與投標
            }
            if (method == "endpaimai")//限制狀態20
            {
                byte[] txid = (byte[])args[0];//拍賣id
                //結束拍賣就會把我存進去的拍賣金退回90%（我沒中標）
                //如果中標，拍賣金全扣，給我域名所有權
            }
            if (method == "balanceOf")
            {
                byte[] who = (byte[])args[0];
            }
            if (method == "getmoneyback")//把多餘的錢取回
            {
                byte[] who = (byte[])args[0];
                BigInteger myprice = (BigInteger)args[2];
            }
            if (method == "setmoneyin")//如果用普通方式轉了nep5進來，也不要緊
            {
                byte[] txid = (byte[])args[3];//提供一個txid，查這筆txid 的nep5入賬證明
            }
            return new byte[] { 0 };
        }
    }


}
