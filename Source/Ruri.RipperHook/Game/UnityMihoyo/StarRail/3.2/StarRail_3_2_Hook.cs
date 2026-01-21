using AssetRipper.Primitives;
using AssetRipper.SourceGenerated;
using Ruri.RipperHook.Attributes;
using Ruri.RipperHook.HookUtils.BundleFileBlockReaderHook;
using Ruri.RipperHook.HookUtils.GameBundleHook;
using Ruri.RipperHook.HookUtils.PlatformGameStructureHook_IsBundleHeader;
using Ruri.RipperHook.StarRail;
using Ruri.RipperHook.UnityMihoyo;

namespace Ruri.RipperHook.StarRail;

[GameHook("StarRail", "3.2", "2019.4.34f1")]
public partial class StarRail_3_2_Hook : StarRailCommon_Hook
{
    protected StarRail_3_2_Hook()
    {
        MihoyoCommon.Mr0kDecryptor = new Mr0kDecryptor(Mr0kKey.Mr0kExpansionKey, initVector: Mr0kKey.Mr0kInitVector, blockKey: Mr0kKey.Mr0kBlockKey);
    }

    protected override void InitAttributeHook()
    {
        // Restore implicit logic from Common Class
// New Distributed Modularity
        RegisterModule(new GameBundleHook(StarRailCommon_Hook.CustomFilePreInitialize));

        // Legacy / Other hooks not yet converted to Modules
        RegisterModule(new BundleFileBlockReaderHook(MihoyoCommon.CustomBlockCompression));
        RegisterModule(new PlatformGameStructureHook_IsBundleHeader(StarRailCommon_Hook.CustomAssetBundlesCheckMagicNum));
        
        base.InitAttributeHook(); 
    }
}