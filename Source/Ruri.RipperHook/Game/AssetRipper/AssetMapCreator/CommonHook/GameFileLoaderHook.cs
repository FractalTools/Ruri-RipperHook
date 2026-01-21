using AssetRipper.Export.UnityProjects;
using AssetRipper.IO.Files;
using AssetRipper.Processing;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Ruri.RipperHook.AR;

public partial class AR_AssetMapCreator_Hook
{
    // 修改 Hook 目标为 ExportHandler.Export，此时目录已被 GameFileLoader 清理重建
    // 使用实例方法以匹配目标方法的参数数量 (隐式 this + 3 参数)
    [RetargetMethod(typeof(ExportHandler), nameof(ExportHandler.Export), isReturn: false)]
    public void Export(GameData gameData, string rootPath, FileSystem fileSystem)
    {
        // 直接使用 rootPath (导出目录)，并在其下创建 RuriInfo 子文件夹
        var outputPath = Path.Combine(rootPath, "RuriInfo");
        if (!Directory.Exists(outputPath))
        {
            Directory.CreateDirectory(outputPath);
        }

        ExportDictionaryToFile(assetClassIDLookup, Path.Combine(outputPath, "AssetClassIDLookup.json"));
        ExportDictionaryToFile(assetDependenciesLookup, Path.Combine(outputPath, "AssetDependenciesLookup.json"));
        ExportDictionaryToFile(assetListLookup, Path.Combine(outputPath, "AssetListLookup.json"));
    }

    public static void ExportDictionaryToFile<T>(Dictionary<string, HashSet<T>> dictionary, string filePath)
    {
        var settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            Converters = new List<JsonConverter> { new StringEnumConverter() }
        };
        string json = JsonConvert.SerializeObject(dictionary, settings);
        File.WriteAllText(filePath, json);
    }
}