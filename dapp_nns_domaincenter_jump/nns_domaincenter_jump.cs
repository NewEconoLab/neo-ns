using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using Helper = Neo.SmartContract.Framework.Helper;

using System;
using System.Numerics;

namespace DApp
{
    public class nns_domaincenter_jump : SmartContract
    {
        //域名中心跳板合约 
        //    域名中心开发过程中地址一直在改变，造成调试不变，故设置一个跳板
        //    跳板用法，_setTarget(目标脚本hash)
        //    然后就把跳板当成域名中心脚本用即可
        static readonly byte[] superAdmin = Helper.ToScriptHash("ALjSnMZidJqd18iQaoCgFun6iqWRm2cVtj");//初始管理員
        delegate object deleDyncall(string method, object[] arr);

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
                if (method == "_setTarget")
                {
                    if (Runtime.CheckWitness(superAdmin))
                    {
                        Storage.Put(Storage.CurrentContext, "target", (byte[])args[0]);
                        return new byte[] { 0x01 };
                    }
                    return new byte[] { 0x00 };
                }
                var callscript = ExecutionEngine.CallingScriptHash;
                byte[] target = Storage.Get(Storage.CurrentContext,"target");

                #region 所有者接口 直接调用&智能合约
                if (method == "owner_SetOwner")
                {
                    object[] newarg = new object[4];
                    newarg[0] = callscript;
                    newarg[1] = args[0];
                    newarg[2] = args[1];
                    newarg[3] = args[2];
                    deleDyncall dyncall = (deleDyncall)target.ToDelegate();
                    return dyncall(method, newarg);
                }
                if (method == "owner_SetRegister")
                {
                    object[] newarg = new object[4];
                    newarg[0] = callscript;
                    newarg[1] = args[0];
                    newarg[2] = args[1];
                    newarg[3] = args[2];
                    deleDyncall dyncall = (deleDyncall)target.ToDelegate();
                    return dyncall(method, newarg);
                }
                if (method == "owner_SetResolver")
                {
                    object[] newarg = new object[4];
                    newarg[0] = callscript;
                    newarg[1] = args[0];
                    newarg[2] = args[1];
                    newarg[3] = args[2];
                    deleDyncall dyncall = (deleDyncall)target.ToDelegate();
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
                    deleDyncall dyncall = (deleDyncall)target.ToDelegate();
                    return dyncall(method, newarg);
                }
                #endregion

                #region 升级合约,耗费990(有动态调用),仅限管理员
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
                    bool need_storage = (bool)(object)03;
                    string name = "domaincenter_jump";
                    string version = "1";
                    string author = "NEL";
                    string email = "0";
                    string description = "域名跳板合约";

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
                deleDyncall _dyncall = (deleDyncall)target.ToDelegate();
                return _dyncall(method, args);
            }
            return new byte[] { 0 };
        }
    }


}
