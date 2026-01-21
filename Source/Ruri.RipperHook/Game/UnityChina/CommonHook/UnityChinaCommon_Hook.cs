using Ruri.RipperHook.Attributes;
using Ruri.RipperHook.Crypto;

namespace Ruri.RipperHook.UnityChina;


public partial class UnityChinaCommon_Hook : RipperHook
{
    public static UnityChinaDecryptor unityChinaDecryptor { get; set; }

    public static void SetKey(string name, string key)
    {
        UnityChinaDecryptor.SetKey(new UnityChinaDecryptor.Entry(name, key));
    }
}