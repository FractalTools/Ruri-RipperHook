using Ruri.RipperHook.EndFieldCommon;
using Ruri.RipperHook.Crypto;
using Ruri.RipperHook.HookUtils.BundleFileBlockReaderHook;
using Ruri.RipperHook.HookUtils.PlatformGameStructureHook_CollectAssetBundles;

namespace Ruri.RipperHook.EndField_0_5;

public partial class EndField_0_5_Hook : RipperHook
{
    protected static LZ4_EndField_0_5 customLZ4;
    protected EndField_0_5_Hook()
    {
        customLZ4 = new LZ4_EndField_0_5();
        RuriRuntimeHook.CurrentDecryptor = new FairGuardDecryptor_EndField_0_5();
    }

    protected override void InitAttributeHook()
    {
        additionalNamespaces.Add(typeof(EndFieldCommon_Hook).Namespace);
        AddExtraHook(typeof(BundleFileBlockReaderHook).Namespace, () => { BundleFileBlockReaderHook.CustomBlockCompression = CustomBlockCompression; });
        base.InitAttributeHook();
    }
}