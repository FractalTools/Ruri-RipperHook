using AssetRipper.Assets.Bundles;
using AssetRipper.IO.Files;
using AssetRipper.IO.Files.Streams;
using AssetRipper.IO.Files.Streams.Smart;
using Ruri.RipperHook.HookUtils.GameBundleHook;
using Ruri.RipperHook.UnityMihoyo;

namespace Ruri.RipperHook.StarRailCommon;

public partial class StarRailCommon_Hook
{
    public static readonly byte[] encrHead = { 0x45, 0x4E, 0x43, 0x52, 0x00 };

    // 修改签名：增加 FileSystem 参数
    public static void CustomFilePreInitialize(GameBundle _this, IEnumerable<string> paths, List<FileBase> fileStack, FileSystem fileSystem, IDependencyProvider? dependencyProvider)
    {
        foreach (var path in paths)
        {
            var extension = Path.GetExtension(path);

            if (extension == ".block")
            {
                // 使用 OpenReadMulti 并传入 fileSystem
                using (var stream = SmartStream.OpenReadMulti(path, fileSystem))
                {
                    // 假设 MihoyoCommon.FindBlockFiles 逻辑不需要变动，它只负责读取流
                    var assetBundleBlocks = MihoyoCommon.FindBlockFiles(stream, encrHead);
                    for (int i = 0; i < assetBundleBlocks.Count; i++)
                    {
                        var filePath = path;
                        var directoryPath = Path.GetDirectoryName(filePath);
                        var fileName = Path.GetFileName(filePath);

                        // 调用 GameBundleHook 的静态方法加载 Block 切分出来的文件
                        fileStack.AddRange(GameBundleHook.LoadFilesAndDependencies(assetBundleBlocks[i], directoryPath, fileName, dependencyProvider));
                    }
                }
            }
            else
            {
                // 普通文件也使用 OpenReadMulti
                using (var stream = SmartStream.OpenReadMulti(path, fileSystem))
                {
                    var fileData = new byte[stream.Length];
                    stream.Read(fileData, 0, fileData.Length);

                    // MultiFileStream 相关的帮助方法在新版本中通常位于 AssetRipper.IO.Files 中
                    // 这里的逻辑保持不变，将 byte[] 传回通用加载器
                    fileStack.AddRange(GameBundleHook.LoadFilesAndDependencies(
                        fileData,
                        MultiFileStream.GetFilePath(path),
                        MultiFileStream.GetFileName(path),
                        dependencyProvider));
                }
            }
        }
    }
}