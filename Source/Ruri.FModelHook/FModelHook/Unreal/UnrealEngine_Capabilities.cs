using AssetRipper.Assets.Bundles;
using AssetRipper.IO.Files;
using Ruri.RipperHook.Core.Capabilities;
using Ruri.RipperHook.HookUtils.GameBundleHook;

namespace Ruri.FModelHook.Unreal;

[GameCapabilities(Ruri.RipperHook.GameType.UnrealEngine)]
public static class UnrealEngine_Capabilities
{
    /// <summary>
    /// AssetRipper's file loader, on an Unreal install: the paths it was handed are archive
    /// containers, the packages inside them are read through CUE4Parse and rebuilt as Unity
    /// assets straight into the game bundle. Nothing is pushed onto the file stack -- there is
    /// no Unity file to parse.
    /// </summary>
    [Since("4.0")]
    [FeedsModule(typeof(GameBundleHook), nameof(GameBundleHook.CustomFilePreInitialize))]
    public static void GameBundlePreInitialize(GameBundle _this, IEnumerable<string> paths, List<FileBase> fileStack, FileSystem fileSystem, IDependencyProvider? dependencyProvider)
    {
        UnrealPackageLoader.Load(_this, paths, GameBundleHook.LoadIncludeFile, GameBundleHook.LoadSeedFile);
    }
}
