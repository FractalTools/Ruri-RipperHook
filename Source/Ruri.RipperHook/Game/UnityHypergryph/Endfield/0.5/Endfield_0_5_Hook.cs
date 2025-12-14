using Ruri.RipperHook.EndFieldCommon;
using Ruri.RipperHook.Crypto;
using Ruri.RipperHook.HookUtils.BundleFileBlockReaderHook;
using Ruri.RipperHook.HookUtils.GameBundleHook;
using Ruri.RipperHook.HookUtils.PlatformGameStructureHook_CollectAssetBundles;
using Ruri.RipperHook.HookUtils.PlatformGameStructureHook_IsBundleHeader;

namespace Ruri.RipperHook.EndField_0_5;

public partial class EndField_0_5_Hook : RipperHook
{
    private static LZ4_EndField_0_5 customLZ4;
    private static FairGuardDecryptor_EndField_0_5 fairGuardDecryptor;

    protected EndField_0_5_Hook()
    {
        customLZ4 = new LZ4_EndField_0_5();
        fairGuardDecryptor = new FairGuardDecryptor_EndField_0_5();
    }

    protected override void InitAttributeHook()
    {
        additionalNamespaces.Add(typeof(EndFieldCommon_Hook).Namespace);
        
        AddExtraHook(typeof(GameBundleHook).Namespace, () => { GameBundleHook.CustomFilePreInitialize = CustomFilePreInitialize; });
        AddExtraHook(typeof(PlatformGameStructureHook_CollectAssetBundles).Namespace, () => { PlatformGameStructureHook_CollectAssetBundles.CustomAssetBundlesCheck = CustomAssetBundlesCheck; });
        AddExtraHook(typeof(PlatformGameStructureHook_IsBundleHeader).Namespace, () => { PlatformGameStructureHook_IsBundleHeader.CustomAssetBundlesCheckMagicNum = CustomAssetBundlesCheckMagicNum; });
        
        AddExtraHook(typeof(BundleFileBlockReaderHook).Namespace, () => { BundleFileBlockReaderHook.CustomBlockCompression = CustomBlockCompression; });
        
        base.InitAttributeHook();
    }
}