using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using System;
using System.Numerics;

namespace DApp
{
    public class nns_domaincenter : SmartContract
    {
        //域名中心跳板合约 
        //    域名中心开发过程中地址一直在改变，造成调试不变，故设置一个跳板
        //    跳板用法，_setTarget(目标脚本hash)
        //    然后就把跳板当成域名中心脚本用即可
        static readonly byte[] superAdmin = Helper.ToScriptHash("ALjSnMZidJqd18iQaoCgFun6iqWRm2cVtj");//初始管理員
        delegate object deleDyncall(string method, object[] arr);

        public static object Main(string method, object[] args)
        {
            string magicstr = "for nns test";
            if (method == "_setTarget")
            {
                if (Runtime.CheckWitness(superAdmin))
                {
                    Storage.Put(Storage.CurrentContext, "target", (byte[])args[0]);
                    return new byte[1];
                }
                return new byte[0];
            }

            byte[] target = Storage.Get(Storage.CurrentContext, "target");
            //必须在一开始取得callscript，调用脚本的函数，也会导致执行栈变化，再取callscript就晚了
            var callscript = ExecutionEngine.CallingScriptHash;

            deleDyncall dyncall = (deleDyncall)target.ToDelegate();
            #region 所有者接口 直接调用&智能合约
            if (method == "owner_SetOwner" ||
                method == "owner_SetRegister" ||
                method == "owner_SetResolver")
            {
                object[] newarg = new object[4];
                newarg[0] = callscript;
                newarg[1] = args[0];
                newarg[2] = args[1];
                newarg[3] = args[2];
                return dyncall(method, newarg);
            }
            #endregion
            #region 注册器接口 仅智能合约
            if (method == "register_SetSubdomainOwner")
            {
                object[] newarg = new object[5];
                newarg[0] = callscript;
                newarg[1] = args[0];
                newarg[2] = args[1];
                newarg[3] = args[2];
                newarg[4] = args[3];
                return dyncall(method, newarg);
            }
            #endregion
            return dyncall(method, args);
        }
    }


}
