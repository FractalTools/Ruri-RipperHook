using AssetRipper.Import.Logging;
using AssetRipper.Import.Structure.Platforms;
using AssetRipper.IO.Endian;
using System.Reflection;

namespace Ruri.RipperHook.GirlsFrontline2;

public partial class GirlsFrontline2Common_Hook
{
    public static bool CustomAssetBundlesCheck(string filePath)
    {
        return filePath.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase);
    }
    public static bool CustomCollectStreamingAssets(PlatformGameStructure _this, List<KeyValuePair<string, string>> files, MethodInfo CollectAssetBundlesRecursively)
    {
        // 获取文件系统引用
        var fs = _this.FileSystem;
        string saPath = _this.StreamingAssetsPath;

        // 计算 LocalCache 路径
        // 使用 fs.Path.Join 替代 Path.Combine 以保证跨平台/虚拟文件系统的兼容性
        string parentDir = string.IsNullOrEmpty(saPath) ? "" : Path.GetDirectoryName(saPath);
        // 注意：Path.GetDirectoryName 可能会返回 null 或特定分隔符，但在 AssetRipper 环境通常是标准的
        string localCachePath = fs.Path.Join(parentDir, "LocalCache");

        // 检查路径是否为空
        if (string.IsNullOrWhiteSpace(saPath) && string.IsNullOrWhiteSpace(localCachePath))
            return false;

        Logger.Info(LogCategory.Import, "Collecting Streaming Assets And LocalCache Assets...");

        // 1. 处理 StreamingAssets (原版逻辑)
        if (!string.IsNullOrWhiteSpace(saPath) && fs.Directory.Exists(saPath))
        {
            // [关键修复] 必须传递 string 路径，而不是 DirectoryInfo 对象
            CollectAssetBundlesRecursively.Invoke(_this, new object[] { saPath, files });
        }

        // 2. 处理 LocalCache (自定义逻辑)
        if (fs.Directory.Exists(localCachePath))
        {
            Logger.Info(LogCategory.Import, $"Found LocalCache at: {localCachePath}");
            // [关键修复] 必须传递 string 路径
            CollectAssetBundlesRecursively.Invoke(_this, new object[] { localCachePath, files });
        }

        // 返回 true 表示我们已经接管并完成了逻辑，不需要外层再执行默认逻辑
        return true;
    }
}