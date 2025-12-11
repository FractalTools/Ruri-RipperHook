using Ruri.RipperHook.EndFieldCommon;
using Ruri.RipperHook.Crypto;
using Ruri.RipperHook.HookUtils.BundleFileBlockReaderHook;

namespace Ruri.RipperHook.EndField_0_1;

public partial class EndField_0_1_Hook : RipperHook
{
    public static LZ4_EndField_0_1 customLZ4;
    protected EndField_0_1_Hook()
    {
        customLZ4 = new LZ4_EndField_0_1();
        RuriRuntimeHook.commonDecryptor = new FairGuardDecryptor();
    }

    protected override void InitAttributeHook()
    {
        additionalNamespaces.Add(typeof(EndFieldCommon_Hook).Namespace);
        AddExtraHook(typeof(BundleFileBlockReaderHook).Namespace, () => { BundleFileBlockReaderHook.CustomBlockCompression = EndField_0_1_Hook.CustomBlockCompression; });
        base.InitAttributeHook();
    }
}