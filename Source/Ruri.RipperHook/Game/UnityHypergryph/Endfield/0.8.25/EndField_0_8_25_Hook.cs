using Ruri.RipperHook.Attributes;
using AssetRipper.Primitives;
using AssetRipper.SourceGenerated;
using Ruri.RipperHook.Crypto;
using Ruri.RipperHook.Endfield;
using Ruri.RipperHook.HookUtils.BundleFileBlockReaderHook;
using Ruri.RipperHook.HookUtils.GameBundleHook;
using Ruri.RipperHook.HookUtils.PlatformGameStructureHook_CollectAssetBundles;
using Ruri.RipperHook.HookUtils.PlatformGameStructureHook_IsBundleHeader;

namespace Ruri.RipperHook.Endfield;

[GameHook("EndField", "0.8.25", "2021.3.34f1")]
public partial class EndField_0_8_25_Hook : EndFieldCommon_Hook
{
    public static UnityVersion EndFieldClassVersion = UnityVersion.Parse($"2021.3.825x{(int)CustomEngineType.EndField}");
    public const string ClassHookVersion = "2021.3.34f1";
    private static VFSDecryptor vfsDecryptor;

    protected EndField_0_8_25_Hook()
    {
        vfsDecryptor = new VFSDecryptor();
    }

    protected override UnityVersion GetTargetVersion(GameHookAttribute attr)
    {
        return EndFieldClassVersion;
    }

    protected override void InitAttributeHook()
    {
AddMethodHook(typeof(EndField_0_5_27_Hook), nameof(EndField_0_5_27_Hook.Mesh_ReadRelease));

        RegisterModule(new GameBundleHook(CustomFilePreInitialize));
        RegisterModule(new PlatformGameStructureHook_CollectAssetBundles(CustomAssetBundlesCheck));
        RegisterModule(new PlatformGameStructureHook_IsBundleHeader(CustomAssetBundlesCheckMagicNum));

        RegisterModule(new BundleFileBlockReaderHook(CustomBlockCompression));

        base.InitAttributeHook();
    }
}