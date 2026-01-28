using AssetRipper.Primitives;
using Ruri.RipperHook.HookUtils.BundleFileBlockReaderHook;
using Ruri.RipperHook.HookUtils.GameBundleHook;
using Ruri.RipperHook.HookUtils.PlatformGameStructureHook_IsBundleHeader;
using Ruri.RipperHook.UnityMihoyo;

namespace Ruri.RipperHook.StarRail;

[GameHook("StarRail", "3.2", "2019.4.34f1")]
public partial class StarRail_3_2_Hook : StarRailCommon_Hook
{
    public static string customVersion = $"2019.4.320x{(int)CustomEngineType.StarRail}";
    public static UnityVersion starRailClassVersion = UnityVersion.Parse(customVersion);
    protected StarRail_3_2_Hook()
    {
        MihoyoCommon.Mr0kDecryptor = new Mr0kDecryptor(Mr0kKey.Mr0kExpansionKey, initVector: Mr0kKey.Mr0kInitVector, blockKey: Mr0kKey.Mr0kBlockKey);
    }
    protected override UnityVersion GetTargetVersion(GameHookAttribute attr)
    {
        return starRailClassVersion;
    }
    protected override void InitAttributeHook()
    {
        RegisterModule(new GameBundleHook(CustomFilePreInitialize));
        RegisterModule(new BundleFileBlockReaderHook(MihoyoCommon.CustomBlockCompression));
        RegisterModule(new PlatformGameStructureHook_IsBundleHeader(CustomAssetBundlesCheckMagicNum));
        
        base.InitAttributeHook(); 
    }
}