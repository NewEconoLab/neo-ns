using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using Helper = Neo.SmartContract.Framework.Helper;

using System;
using System.Numerics;
using System.ComponentModel;

namespace DApp
{
    public class nns_domaincenter : SmartContract
    {
        //域名中心 
        //    域名中心是一个不会改变地址的合约，他的作用是管理某一个域名的数据
        //使用存储
        //DomainInfo 这两个是不变的
        //{
        //subname
        //parenthash
        //}
        // dict<hash+0x11,NNSINFO> 

        //这些玩意会变的
        //dict<hash+0x04,parentowner> //当初这个域名的爹是谁，如果对不上了就是坑爹了
        // dict<hash+0x00,owner> 记录域名拥有者数据 
        // dict<hash+0x01,register> 域名注册器
        // dict<hash+0x02,resolver> 域名解析器
        // dict<hash+0x03,ttl>   记录域名过期数据
        //SuperAdmin 超级管理员，超级管理员改成可以管理多个根的注册器的
        //使用Nep4
        //所有者可以设置自己域名的控制器
        //以后还应该设置联合所有权的机制，多个所有者，几人以上签名才可用

        //const int blockday = 4096;//粗略一天的块数

        static readonly byte[] superAdmin = Helper.ToScriptHash("ALjSnMZidJqd18iQaoCgFun6iqWRm2cVtj");//初始管理員
        static readonly byte[] jumpContract = Helper.ToScriptHash("AWEFWzvXM9QFUQsppB6BNdoraT3oZtsUo8");//注意 script_hash 是反序的
                                                                                                        //跳板合约0x8e813d36b159400e4889ba0aed0c42b02dd58e9e
                                                                                                        //地址AWEFWzvXM9QFUQsppB6BNdoraT3oZtsUo8

        //改爲結構化方法
        //public static object[] getInfo(byte[] nnshash)
        //{
        //    object[] ret = new object[4];
        //    ret[0] = Storage.Get(Storage.CurrentContext, nnshash.Concat(new byte[] { 0x00 }));
        //    ret[1] = Storage.Get(Storage.CurrentContext, nnshash.Concat(new byte[] { 0x01 }));
        //    ret[2] = Storage.Get(Storage.CurrentContext, nnshash.Concat(new byte[] { 0x02 }));
        //    ret[3] = Storage.Get(Storage.CurrentContext, nnshash.Concat(new byte[] { 0x03 }));
        //    return ret;
        //}
        //通知 域名信息變更
        public delegate void deleOwnerInfoChange(byte[] namehash, OwnerInfo addr, bool newdomain);
        [DisplayName("changeOwnerInfo")]
        public static event deleOwnerInfoChange onChangeOwnerInfo;
        //通知 新的域名
        //public delegate void deleInitDomain(byte[] namehash, string domain, int root);
        //[DisplayName("initDomain")]
        //public static event deleInitDomain onInitDomain;

        delegate byte[] deleDyncall(string method, object[] arr);

        static string getDomain(byte[] hash)
        {
            string outstr = "";
            byte[] phash = hash;
            for (var i = 0; i < 10; i++)
            {
                var info = getOwnerInfo(phash);
                phash = info.parenthash;
                //if (info.root == 0)
                {
                    if (i == 0)
                        outstr = info.domain;
                    else
                        outstr = info.domain + "." + outstr;
                }
            }
            return outstr;
        }
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
            var nowtime = Blockchain.GetHeader(Blockchain.GetHeight()).Timestamp;

            //{ test.aaa.second //一層層上}
            //{test.aaa.second } for(i =1;i<2;i++)
            for (var i = 1; i < domainarray.Length - 1; i++)
            {
                var info = getOwnerInfo(hash);
                byte[] register = info.register;
                // Storage.Get(Storage.CurrentContext, hash.Concat(new byte[] { 0x01 }));
                byte[] resolver = info.resolver;
                // Storage.Get(Storage.CurrentContext, hash.Concat(new byte[] { 0x02 }));
                if (register.Length == 0)//這個域名沒有注冊其
                {
                    return new byte[] { 0x00 };
                }
                var subname = domainarray[i];
                var subhash = nameHashSub(hash, domainarray[i]);

                var subinfo = getOwnerInfo(subhash);
                if (subinfo.parentOwner.AsBigInteger() != info.owner.AsBigInteger())//所有者对不上，断链
                {
                    return new byte[] { 0x00 };
                }


                hash = subhash;
                //var ttl = info.TTL;// Storage.Get(Storage.CurrentContext, hash.Concat(new byte[] { 0x03 })).AsBigInteger();
                if (info.TTL < nowtime)//過期了
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
            var nowtime = Blockchain.GetHeader(Blockchain.GetHeight()).Timestamp;
            var ttl = getOwnerInfo(ttlhash).TTL;
            //Storage.Get(Storage.CurrentContext, ttlhash.Concat(new byte[] { 0x03 })).AsBigInteger();
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
                var resolverFull = getOwnerInfo(fullhash).resolver;
                // Storage.Get(Storage.CurrentContext, fullhash.Concat(new byte[] { 0x02 }));
                return _doresolve(resolverFull, nnshash, protocol, nnshash);
            }

            var resolverSub = getOwnerInfo(fullhash).resolver;
            // Storage.Get(Storage.CurrentContext, fullhash.Concat(new byte[] { 0x02 }));
            if (resolverSub.Length != 0)//如果他有一个子解析器,调用子解析器
            {
                return _doresolve(resolverSub, fullhash, protocol, fullhash);
            }

            //然后查根域名是否对应解析器
            var resolver = getOwnerInfo(nnshash).resolver;
            // Storage.Get(Storage.CurrentContext, nnshash.Concat(new byte[] { 0x02 }));
            return _doresolve(resolver, nnshash, protocol, fullhash);
        }
        //快速解析
        static byte[] initRoot(string rootname, byte[] newregister)
        {
            var nnshash = nameHash(rootname);

            //var oldinfo = GetNNSInfo(nnshash);
            //if (oldinfo.domain.Length > 0)
            //{
            //    return new byte[] { 0x00, 0x01 };//已经存在根域名记录了，可以允许修改的吧
            //}

            bool newdomain = false;
            var ninfo = getOwnerInfo(nnshash);
            if (ninfo.domain.Length == 0)
            {
                newdomain = true;
            }

            ninfo = new OwnerInfo();
            ninfo.owner = superAdmin;
            ninfo.register = newregister;
            ninfo.resolver = new byte[0];
            ninfo.TTL = 0;
            ninfo.parentOwner = new byte[0];

            ninfo.parenthash = new byte[0];
            ninfo.root = 1;
            ninfo.domain = rootname;
            saveOwnerInfo(nnshash, ninfo, newdomain);
            //OwnerInfo oinfo = new OwnerInfo();
            //oinfo.owner = superAdmin;
            //oinfo.register = newregister;
            //oinfo.resolver = new byte[0];
            //oinfo.TTL = 0;
            //oinfo.parentOwner = new byte[0];
            //saveOwnerInfo(nnshash, oinfo);
            //var oinfo = getInfo(nnshash);
            ////var o = Storage.Get(Storage.CurrentContext, nnshash.Concat(new byte[] { 0x00 }));
            //if (oinfo.owner.Length == 0)
            //{
            //    //初始管理員衹有一個功能,就是轉讓根域名管理權，而且是一次性的，一旦轉讓出去，初始管理員就沒用了
            //    Storage.Put(Storage.CurrentContext, nnshash.Concat(new byte[] { 0x00 }), superAdmin);
            //    Storage.Put(Storage.CurrentContext, nnshash.Concat(new byte[] { 0x01 }), superAdmin);
            //    return new byte[] { 0x01 };
            //}
            return new byte[] { 0x01 };
        }
        #region 所有者功能
        //設置新的所有者(域名轉讓)
        static byte[] owner_SetOwner(byte[] nnshash, byte[] newowner)
        {
            var info = getOwnerInfo(nnshash);

            var nowtime = Blockchain.GetHeader(Blockchain.GetHeight()).Timestamp;
            //判断有效期内才能转让
            if (info.TTL < nowtime)
                return new byte[] { 0x00 };

            info.owner = newowner;
            saveOwnerInfo(nnshash, info, false);
            //Storage.Put(Storage.CurrentContext, nnshash.Concat(new byte[] { 0x00 }), newowner);
            return new byte[] { 0x01 };
        }
        //所有者设置注册器
        static byte[] owner_SetRegister(byte[] nnshash, byte[] register)
        {
            var info = getOwnerInfo(nnshash);
            info.register = register;
            saveOwnerInfo(nnshash, info, false);
            //Storage.Put(Storage.CurrentContext, nnshash.Concat(new byte[] { 0x01 }), register);
            return new byte[] { 0x01 };
        }
        //所有者设置解析器
        static byte[] owner_SetResolver(byte[] nnshash, byte[] resolver)
        {
            var info = getOwnerInfo(nnshash);
            info.resolver = resolver;
            saveOwnerInfo(nnshash, info, false);
            //Storage.Put(Storage.CurrentContext, nnshash.Concat(new byte[] { 0x02 }), resolver);
            return new byte[] { 0x01 };
        }
        #endregion
        #region 注册器功能
        /// <summary>
        /// 注册器功能组
        /// </summary>
        //更改子域名所有者
        //dict<hash+0x04,parentowner> //当初这个域名的爹是谁，如果对不上了就是坑爹了
        // dict<hash+0x00,owner> 记录域名拥有者数据 
        // dict<hash+0x01,register> 域名注册器
        // dict<hash+0x02,resolver> 域名解析器
        // dict<hash+0x03,ttl>   记录域名过期数据

        //private static byte[] byteLen(BigInteger n)
        //{
        //    byte[] v = n.AsByteArray();
        //    if (v.Length > 2)
        //        throw new Exception("not support");
        //    if (v.Length < 2)
        //        v = v.Concat(new byte[1] { 0x00 });
        //    if (v.Length < 2)
        //        v = v.Concat(new byte[1] { 0x00 });
        //    return v;
        //}

        public class OwnerInfo
        {
            public byte[] owner;//如果长度=0 表示没有初始化
            public byte[] register;
            public byte[] resolver;
            public BigInteger TTL;
            public byte[] parentOwner;//当此域名注册时，他爹的所有者，记录这个，则可以检测域名的爹变了
            //nameinfo 整合到一起
            public string domain;//如果长度=0 表示没有初始化
            public byte[] parenthash;
            public BigInteger root;//是不是根合约
        }
        //public class NameInfo
        //{
        //    public string domain;//如果长度=0 表示没有初始化
        //    public byte[] parenthash;
        //    public BigInteger root;//是不是根合约
        //}
        public static OwnerInfo getOwnerInfo(byte[] hash)
        {
            var key = new byte[] { 0x12 }.Concat(hash);
            var data = Storage.Get(Storage.CurrentContext, key);
            if (data.Length == 0)
            {
                OwnerInfo state = new OwnerInfo();
                state.owner = new byte[0];
                state.domain = "";
                return state;
            }

            //老式实现方法
            OwnerInfo info = new OwnerInfo();
            int seek = 0;
            int len = 0;

            len = (int)data.Range(seek, 2).AsBigInteger();
            seek += 2;
            info.owner = data.Range(seek, len);
            seek += len;

            len = (int)data.Range(seek, 2).AsBigInteger();
            seek += 2;
            info.register = data.Range(seek, len);
            seek += len;

            len = (int)data.Range(seek, 2).AsBigInteger();
            seek += 2;
            info.resolver = data.Range(seek, len);
            seek += len;

            len = (int)data.Range(seek, 2).AsBigInteger();
            seek += 2;
            info.TTL = data.Range(seek, len).AsBigInteger();
            seek += len;

            len = (int)data.Range(seek, 2).AsBigInteger();
            seek += 2;
            info.parentOwner = data.Range(seek, len);
            seek += len;

            //整合nameinfo
            len = (int)data.Range(seek, 2).AsBigInteger();
            seek += 2;
            info.domain = data.Range(seek, len).AsString();
            seek += len;

            len = (int)data.Range(seek, 2).AsBigInteger();
            seek += 2;
            info.parenthash = data.Range(seek, len);
            seek += len;

            len = (int)data.Range(seek, 2).AsBigInteger();
            seek += 2;
            info.root = data.Range(seek, len).AsBigInteger();

            //新式实现方法
            //var info = Helper.Deserialize(data) as OwnerInfo;
            return info;
        }
        static void saveOwnerInfo(byte[] hash, OwnerInfo info, bool newdomain)
        {
            var hash2 = info.root == 1 ? nameHash(info.domain) : nameHashSub(info.parenthash, info.domain);
            if (hash2.AsBigInteger() != hash.AsBigInteger())
                throw new Exception("error hash.");


            var key = new byte[] { 0x12 }.Concat(hash);
            var doublezero = new byte[] { 0, 0 };

            var data = info.owner;
            var lendata = ((BigInteger)data.Length).ToByteArray().Concat(doublezero).Range(0, 2);
            byte[] value = lendata.Concat(data);

            data = info.register;
            lendata = ((BigInteger)data.Length).ToByteArray().Concat(doublezero).Range(0, 2);
            value = value.Concat(lendata).Concat(data);

            data = info.resolver;
            lendata = ((BigInteger)data.Length).ToByteArray().Concat(doublezero).Range(0, 2);
            value = value.Concat(lendata).Concat(data);

            data = info.TTL.AsByteArray();
            lendata = ((BigInteger)data.Length).ToByteArray().Concat(doublezero).Range(0, 2);
            value = value.Concat(lendata).Concat(data);

            data = info.parentOwner;
            lendata = ((BigInteger)data.Length).ToByteArray().Concat(doublezero).Range(0, 2);
            value = value.Concat(lendata).Concat(data);
            //整合nameinfo
            data = info.domain.AsByteArray();
            lendata = ((BigInteger)data.Length).ToByteArray().Concat(doublezero).Range(0, 2);
            value = value.Concat(lendata).Concat(data);

            data = info.parenthash;
            lendata = ((BigInteger)data.Length).ToByteArray().Concat(doublezero).Range(0, 2);
            value = value.Concat(lendata).Concat(data);

            data = info.root.AsByteArray();
            lendata = ((BigInteger)data.Length).ToByteArray().Concat(doublezero).Range(0, 2);
            value = value.Concat(lendata).Concat(data);

            //var value = Helper.Serialize(state);
            Storage.Put(Storage.CurrentContext, key, value);

            onChangeOwnerInfo(hash, info, newdomain);
        }

        //public static NameInfo getNameInfo(byte[] hash)
        //{
        //    var key = new byte[] { 0x11 }.Concat(hash);

        //    var data = Storage.Get(Storage.CurrentContext, key);
        //    if (data.Length == 0)
        //    {
        //        NameInfo einfo = new NameInfo();
        //        einfo.domain = "";
        //        return einfo;
        //    }

        //    //老式实现方法
        //    NameInfo info = new NameInfo();
        //    int seek = 0;
        //    var domainlen = (int)data.Range(seek, 2).AsBigInteger();
        //    seek += 2;
        //    info.domain = data.Range(seek, domainlen).AsString();
        //    seek += domainlen;

        //    var parenthashlen = (int)data.Range(seek, 2).AsBigInteger();
        //    seek += 2;
        //    info.parenthash = data.Range(seek, parenthashlen);
        //    seek += parenthashlen;

        //    var rootlen = (int)data.Range(seek, 2).AsBigInteger();
        //    seek += 2;
        //    info.root = data.Range(seek, rootlen).AsBigInteger();
        //    return info;


        //    //var nnsInfo = Helper.Deserialize(data) as NameInfo;
        //    //return nnsInfo;
        //}

        //static void saveNameInfo(byte[] hash, NameInfo info)
        //{


        //    var key = new byte[] { 0x11 }.Concat(hash);

        //    //老实实现法

        //    byte[] value = byteLen(info.domain.Length).Concat(info.domain.AsByteArray());
        //    value = value.Concat(byteLen(info.parenthash.Length)).Concat(info.parenthash);
        //    value = value.Concat(byteLen(info.root.AsByteArray().Length)).Concat(info.root.AsByteArray());
        //    //新式实现法
        //    //var value = Helper.Serialize(info);

        //    Storage.Put(Storage.CurrentContext, key, value);

        //    onInitDomain(info.parenthash, info.domain, info);
        //}
        static byte[] register_SetSubdomainOwner(byte[] nnshash, string subdomain, byte[] owner, BigInteger ttl, OwnerInfo pinfo)
        {
            if (subdomain.AsByteArray().Length == 0)
            {
                return new byte[] { 0x00 };
            }
            //var nameinfo = getNameInfo(nnshash);
            if (pinfo.domain.Length == 0)
            {
                throw new Exception("没找到根域名信息");
            }
            if (
                pinfo.root == 0//一级域名不检查ttl
                &&
                ttl > pinfo.TTL
                )
            {
                return new byte[] { 0x00 };
            }
            var hash = nameHashSub(nnshash, subdomain);

            //記錄所有者信息
            var info = getOwnerInfo(hash);
            bool newdomain = false;
            if (info.owner.Length == 0)
            {
                info = new OwnerInfo();
                newdomain = true;
            }
            info.owner = owner;
            info.TTL = ttl;
            info.parentOwner = pinfo.owner;//記錄注冊此域名時父域名的所有者，一旦父域名的所有者發生變化，子域名就可以檢查
            //info.register=
            //info.resolver
            // 记录域名信息
            info.parenthash = nnshash;
            info.domain = subdomain;
            info.root = 0;

            saveOwnerInfo(hash, info, newdomain);
            //Storage.Put(Storage.CurrentContext, hash.Concat(new byte[] { 0x00 }), owner);
            //Storage.Put(Storage.CurrentContext, hash.Concat(new byte[] { 0x03 }), ttl);

            //记录域名信息
            //var ninfo = getNameInfo(hash);
            //if (ninfo.domain.Length == 0)
            //{
            //    ninfo = new NameInfo();
            //    ninfo.parenthash = nnshash;
            //    ninfo.domain = subdomain;
            //    ninfo.root = 0;
            //    saveNameInfo(hash, ninfo);
            //}
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
        static byte[] nameHashWithSubHash(byte[] roothash, byte[] subhash)
        {
            var domain = subhash.Concat(roothash);
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
                var info = getOwnerInfo(nnshash);
                //var o = Storage.Get(Storage.CurrentContext, nnshash.Concat(new byte[] { 0x00 }));
                if (_callscript.AsBigInteger() == info.owner.AsBigInteger())//智能合約所有者
                {
                    return 4;
                }
                if (Runtime.CheckWitness(owner))//账户所有者
                {
                    if (info.owner.AsBigInteger() == owner.AsBigInteger())
                    {
                        return 3;
                    }
                }
            }
            else
            {
                byte[] owner = p0;
                byte[] nnshash = p1;
                var info = getOwnerInfo(nnshash);
                //var o = Storage.Get(Storage.CurrentContext, nnshash.Concat(new byte[] { 0x00 }));
                if (callscript.AsBigInteger() == info.owner.AsBigInteger())//智能合約所有者
                {
                    return 2;
                }
                if (Runtime.CheckWitness(owner))//账户所有者
                {
                    if (info.owner.AsBigInteger() == owner.AsBigInteger())
                    {
                        return 1;
                    }
                }
            }
            return 0;
        }
        //static int CheckRegister(byte[] callscript, byte[] p0, byte[] p1)
        //{

        //}
        public static object Main(string method, object[] args)
        {
            if (Runtime.Trigger == TriggerType.Verification)//取钱才会涉及这里
            {
                return false;
            }
            else if (Runtime.Trigger == TriggerType.VerificationR)
            {
                return false;
            }
            else if (Runtime.Trigger == TriggerType.Application)
            {
                string magic = "20180820";
                //必须在入口函数取得callscript，调用脚本的函数，也会导致执行栈变化，再取callscript就晚了
                var callscript = ExecutionEngine.CallingScriptHash;


                #region 通用功能,不需要权限验证
                if (method == "name")
                {
                    return "NNS DomainCenter";
                }
                if (method == "getDomain")
                {
                    var hash = (byte[])args[0];
                    return getDomain(hash);
                }
                if (method == "getOwnerInfo")
                {
                    return getOwnerInfo((byte[])args[0]);
                }
                //if (method == "getNameInfo")
                //{
                //    return getNameInfo((byte[])args[0]);
                //}
                if (method == "nameHash")
                {
                    var name = (string)args[0];
                    return nameHash(name);
                }
                if (method == "nameHashSub")
                {
                    var rootHash = (byte[])args[0];
                    var subdomain = (string)args[1];
                    return nameHashSub(rootHash, subdomain);
                }
                if (method == "nameHashArray")
                {
                    string[] list = (string[])args[0];
                    return nameHashArray(list);
                }
                if (method == "resolve")
                {
                    string protocol = (string)args[0];
                    var rootHash = (byte[])args[1];
                    var subdomain = (string)args[2];
                    return resolve(protocol, rootHash, subdomain);
                }
                if (method == "resolveFull")
                {
                    string protocol = (string)args[0];
                    string[] list = (string[])args[0];
                    return resolveFull(protocol, list);
                }
                #endregion
                #region 配置根合约注册器,仅限管理员
                if (method == "initRoot")
                {
                    if (Runtime.CheckWitness(superAdmin))
                    {
                        string rootdomain = (string)args[0];
                        byte[] register = (byte[])args[1];
                        return initRoot(rootdomain, register);
                    }
                    return new byte[] { 0x00 };
                }
                #endregion
                #region 升级合约,耗费490,仅限管理员
                if (method == "upgrade")
                {
                    //不是管理员 不能操作
                    if (!Runtime.CheckWitness(superAdmin))
                        return false;

                    if (args.Length != 1 && args.Length != 9)
                        return false;

                    byte[] script = Blockchain.GetContract(ExecutionEngine.ExecutingScriptHash).Script;
                    byte[] new_script = (byte[])args[0];
                    //如果传入的脚本一样 不继续操作
                    if (script == new_script)
                        return false;

                    byte[] parameter_list = new byte[] { 0x07, 0x10 };
                    byte return_type = 0x05;
                    bool need_storage = (bool)(object)01;
                    string name = "domaincenter";
                    string version = "1";
                    string author = "NEL";
                    string email = "0";
                    string description = "域名中心";

                    if (args.Length == 9)
                    {
                        parameter_list = (byte[])args[1];
                        return_type = (byte)args[2];
                        need_storage = (bool)args[3];
                        name = (string)args[4];
                        version = (string)args[5];
                        author = (string)args[6];
                        email = (string)args[7];
                        description = (string)args[8];
                    }
                    Contract.Migrate(new_script, parameter_list, return_type, need_storage, name, version, author, email, description);
                    return true;
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
                    if (callscript.AsBigInteger() == jumpContract.AsBigInteger())
                    {//如果是跳板合约调用
                        byte[] _callscript = (byte[])args[0];
                        byte[] nnshash = (byte[])args[1];
                        var pinfo = getOwnerInfo(nnshash);
                        //var register = Storage.Get(Storage.CurrentContext, nnshash.Concat(new byte[] { 0x01 }));
                        if (_callscript.AsBigInteger() == pinfo.register.AsBigInteger())
                        {
                            return register_SetSubdomainOwner((byte[])args[1], (string)args[2], (byte[])args[3], ((byte[])args[4]).AsBigInteger(), pinfo);
                        }
                    }
                    else
                    {
                        byte[] nnshash = (byte[])args[0];
                        var pinfo = getOwnerInfo(nnshash);
                        //var register = Storage.Get(Storage.CurrentContext, nnshash.Concat(new byte[] { 0x01 }));
                        if (callscript.AsBigInteger() == pinfo.register.AsBigInteger())
                        {
                            return register_SetSubdomainOwner((byte[])args[0], (string)args[1], (byte[])args[2], ((byte[])args[3]).AsBigInteger(), pinfo);
                        }
                    }
                    return new byte[] { 0x00 };
                }
                #endregion
            }
            return new byte[] { 0 };
        }
    }


}
