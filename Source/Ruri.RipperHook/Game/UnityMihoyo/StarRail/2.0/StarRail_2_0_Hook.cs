using AssetRipper.Primitives;
using Ruri.RipperHook.HookUtils.BundleFileBlockReaderHook;
using Ruri.RipperHook.HookUtils.GameBundleHook;
using Ruri.RipperHook.HookUtils.PlatformGameStructureHook_IsBundleHeader;
using Ruri.RipperHook.UnityMihoyo;

namespace Ruri.RipperHook.StarRail;

[GameHook("StarRail", "2.0", "2019.4.34f1")]
public partial class StarRail_2_0_Hook : StarRailCommon_Hook
{
    public static string customVersion = $"2019.4.200x{(int)CustomEngineType.StarRail}";
    public static UnityVersion starRailClassVersion = UnityVersion.Parse(customVersion);

    protected StarRail_2_0_Hook()
    {
        MihoyoCommon.Mr0kDecryptor = new Mr0kDecryptor(Mr0kKey.Mr0kExpansionKey, initVector: Mr0kKey.Mr0kInitVector, blockKey: Mr0kKey.Mr0kBlockKey);
    }
    protected override UnityVersion GetTargetVersion(GameHookAttribute attr)
    {
        return starRailClassVersion;
    }
    protected override void InitAttributeHook()
    {
        RegisterModule(new BundleFileBlockReaderHook(MihoyoCommon.CustomBlockCompression));
        RegisterModule(new PlatformGameStructureHook_IsBundleHeader(CustomAssetBundlesCheckMagicNum));
        RegisterModule(new GameBundleHook(CustomFilePreInitialize));
        base.InitAttributeHook();
    }
}