using Ruri.RipperHook;
using Ruri.RipperHook.Attributes;
using Ruri.RipperHook.Data;
using Ruri.RipperHook.HookUtils.GameBundleHook;

namespace Ruri.FModelHook.UnityConverter;

/// <summary>
/// The decoder for every Unreal build: its container is CUE4Parse's provider, its objects are
/// converted in memory into stock Unity assets the moment AssetRipper asks for the archive
/// files, and the rest of the pipeline never learns the data was not Unity's. Declared for the
/// engine family, so any title without a decoder of its own is read through it.
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
