namespace Ruri.RipperHook;

public enum GameHookType
{
    // AssetRipper
    AR_ShaderDecompiler,
    AR_StaticMeshSeparation,
    // AR_PrefabOutlining, 新版被删了 用老代码无法正常导出
    AR_BundledAssetsExportMode,
    AR_ExportDirectly,
    AR_SkipProcessingAnimation,
    AR_AssetMapCreator,
    AR_SkipStreamingAssets,

    // Game
    PunishingGrayRaven_2_11,
    AzurPromilia_0_1_1_3,
    EndField_0_1,
    EndField_0_5_27,
    EndField_0_8_25,
    GirlsFrontline2_1_0,
    ExAstris_1_0,
    StarRail_2_0,
    StarRail_3_2,
    Houkai_3_8,
    Houkai_7_1, // Or Newer
}
public enum CustomEngineType
{
    // Based RazTools
    Genshit,
    Houkai,
    StarRail,
    ZenlessZoneZero,

    ExAstris,
    EndField,
}