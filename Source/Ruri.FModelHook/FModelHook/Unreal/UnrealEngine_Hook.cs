using Ruri.RipperHook;
using Ruri.RipperHook.Attributes;
using Ruri.RipperHook.Data;
using Ruri.RipperHook.HookUtils.GameBundleHook;

namespace Ruri.FModelHook.Unreal;

/// <summary>
/// The decoder for every Unreal build: its container is CUE4Parse's provider, and what it holds
/// is published as data -- placements, mesh buffers, reference skeletons, material parameters,
/// texture pixels and animation curves -- for a host to build from directly. Nothing is turned
/// into another engine's assets on the way. Declared for the engine family, so any title
/// without a decoder of its own is read through it.
/// </summary>
[RipperHook(Ruri.RipperHook.GameType.UnrealEngine, "4.0", "")]
public partial class UnrealEngine_Hook : RipperHookCommon
{
    protected UnrealEngine_Hook()
    {
    }

    protected override void InitAttributeHook()
    {
        ApplyCapabilities(Ruri.RipperHook.GameType.UnrealEngine);

        GameBundleHook.ScanIncludeFile = UnrealInstall.IsArchive;
        GameBundleHook.ScanChunkFull = UnrealArchiveScan.ScanFull;

        UnrealDatasets.Register();
        Session.DeclareLayout(UnrealInstall.ContentRoots);
        Ruri.Hook.Core.HookManager.RegisterCleanup(() =>
        {
            Datasets.Clear(UnrealDatasets.IdPrefix);
            Session.ForgetLayout();
            UnrealProviderSession.Close();
        });
        base.InitAttributeHook();
    }
}
