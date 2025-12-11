using AssetRipper.GUI.Web;
using AssetRipper.SourceGenerated.Classes.ClassID_21;
using AssetRipper.SourceGenerated.Classes.ClassID_25;
using Ruri.SourceGenerated.Subclasses.AclClip;

namespace Ruri.RipperHook;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        DebugAssemblyDumperError();

        Hook(args);
        //Debug();
        RunAssetRipper(args);
    }

    private static void Hook(string[] args)
    {
        //RuriRuntimeHook.Init(GameHookType.AR_StaticMeshSeparation);
        //RuriRuntimeHook.Init(GameHookType.AR_PrefabOutlining);
        RuriRuntimeHook.Init(GameHookType.AR_BundledAssetsExportMode);
        RuriRuntimeHook.Init(GameHookType.AR_ExportDirectly);
        RuriRuntimeHook.Init(GameHookType.AR_SkipProcessingAnimation);
        RuriRuntimeHook.Init(GameHookType.AR_AssetMapCreator);
        RuriRuntimeHook.Init(GameHookType.AR_ShaderDecompiler);
        //RuriRuntimeHook.Init(GameHookType.StarRail_3_2);
        RuriRuntimeHook.Init(GameHookType.EndField_0_8_25);
        //RuriRuntimeHook.Init(GameHookType.GirlsFrontline2_1_0);
        //RuriRuntimeHook.Init(GameHookType.AzurPromilia_0_1_1_3); 
    }

    private static void RunAssetRipper(string[] args)
    {
        WebApplicationLauncher.Launch(args);
    }
    private static void Debug()
    {
        DebugExtension.SubClassFinder(typeof(Renderer_2019_3_0_a6), "AssetRipper.SourceGenerated", "AssetRipper.SourceGenerated");
    }
    // todo Analyzing JVM native errors
    private static void DebugAssemblyDumperError()
    {
        Console.WriteLine(typeof(AclClip).ToString());
        Console.WriteLine(typeof(Material_2021).ToString());
        Console.WriteLine(typeof(AssetRipper.SourceGenerated.ReferenceAssembliesJson).ToString());
        var dummyThis = AssetRipper.SourceGenerated.Classes.ClassID_74.AnimationClip.Create(new AssetRipper.Assets.Metadata.AssetInfo(), AssetRipper.Primitives.UnityVersion.MaxVersion);
    }
}