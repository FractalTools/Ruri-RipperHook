using Ruri.RipperHook.Attributes;
using AssetRipper.Primitives;
using AssetRipper.SourceGenerated;
using Ruri.RipperHook.Endfield;
using Ruri.RipperHook.Crypto;
using Ruri.RipperHook.HookUtils.BundleFileBlockReaderHook;
using Ruri.RipperHook.HookUtils.GameBundleHook;
using Ruri.RipperHook.HookUtils.PlatformGameStructureHook_CollectAssetBundles;
using Ruri.RipperHook.HookUtils.PlatformGameStructureHook_IsBundleHeader;

namespace Ruri.RipperHook.Endfield;

[GameHook("Endfield", "0.5.27", "2021.3.34f1")]
public partial class EndField_0_5_27_Hook : EndFieldCommon_Hook
{
    public static UnityVersion EndFieldClassVersion = UnityVersion.Parse($"2021.3.527x{(int)CustomEngineType.EndField}");
    public const string ClassHookVersion = "2021.3.34f1";

    private static EndField_0_5_27_FairGuardDecryptor fairGuardDecryptor;

    protected EndField_0_5_27_Hook()
    {
        fairGuardDecryptor = new EndField_0_5_27_FairGuardDecryptor();
    }

    protected override UnityVersion GetTargetVersion(GameHookAttribute attr)
    {
        return EndFieldClassVersion;
    }

    protected override void InitAttributeHook()
    {
RegisterModule(new GameBundleHook(CustomFilePreInitialize));
        RegisterModule(new PlatformGameStructureHook_CollectAssetBundles(CustomAssetBundlesCheck));
        RegisterModule(new PlatformGameStructureHook_IsBundleHeader(CustomAssetBundlesCheckMagicNum));

        RegisterModule(new BundleFileBlockReaderHook(CustomBlockCompression));

        base.InitAttributeHook();
    }
}