using AssetRipper.Primitives;
using Ruri.RipperHook.Crypto;
using Ruri.RipperHook.EndFieldCommon;
using Ruri.RipperHook.HookUtils.BundleFileBlockReaderHook;

namespace Ruri.RipperHook.EndField_0_8_25;

public partial class EndField_0_8_25_Hook : RipperHook
{
    public static UnityVersion EndFieldClassVersion = UnityVersion.Parse("2021.3.825x5" + (int)CustomEngineType.EndField);

    // 用于 Attribute 匹配的目标原始 Unity 版本
    public const string ClassHookVersion = "2021.3.34f5";

    protected static LZ4_EndField_0_5 customLZ4;
    protected EndField_0_8_25_Hook()
    {
        customLZ4 = new LZ4_EndField_0_5();
        RuriRuntimeHook.commonDecryptor = new FairGuardDecryptor_EndField_0_5();
    }

    protected override void InitAttributeHook()
    {
        additionalNamespaces.Add(typeof(EndFieldCommon_Hook).Namespace);
        AddExtraHook(typeof(BundleFileBlockReaderHook).Namespace, () => { BundleFileBlockReaderHook.CustomBlockCompression = CustomBlockCompression; });
        base.InitAttributeHook();
    }
}