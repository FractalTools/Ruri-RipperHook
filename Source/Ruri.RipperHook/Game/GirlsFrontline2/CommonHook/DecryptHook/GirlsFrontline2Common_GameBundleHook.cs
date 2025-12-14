using AssetRipper.Assets.Bundles;
using AssetRipper.IO.Files;
using AssetRipper.IO.Files.Streams; // MultiFileStream 通常移到了这里
using AssetRipper.IO.Files.Streams.Smart;
using Ruri.RipperHook.HookUtils.GameBundleHook;

namespace Ruri.RipperHook.GirlsFrontline2Common;

public partial class GirlsFrontline2Common_Hook
{
    public static readonly byte[] dataHead = { 0x47, 0x46, 0x46, 0x00 };
    public static readonly byte[] unityFSHead = { 0x55, 0x6E, 0x69, 0x74, 0x79, 0x46, 0x53, 0x00 };

    // 1. 修改签名：增加 FileSystem 参数
    public static void CustomFilePreInitialize(GameBundle _this, IEnumerable<string> paths, List<FileBase> fileStack, FileSystem fileSystem, IDependencyProvider? dependencyProvider)
    {
        foreach (var path in paths)
        {
            var extension = Path.GetExtension(path);

            // 2. 修改流打开方式：传入 fileSystem，使用 OpenReadMulti
            using (var stream = SmartStream.OpenReadMulti(path, fileSystem))
            {
                var fileData = new byte[stream.Length];
                stream.Read(fileData, 0, fileData.Length);

                // 保持原有的字节判断逻辑
                // 注意：StartsWith 应该是你项目里的扩展方法，如果报错请检查命名空间引用
                if (fileData.StartsWith(dataHead))
                    continue; // GFF 文件跳过

                if (extension == ".bundle")
                {
                    if (!fileData.StartsWith(unityFSHead))
                    {
                        // 逻辑字节级一致：解密并替换 buffer
                        fileData = Decryptor.Decrypt(fileData).ToArray();
                    }
                }

                // 3. 调用 GameBundleHook (MultiFileStream 的引用已在上方修正)
                fileStack.AddRange(GameBundleHook.LoadFilesAndDependencies(
                    fileData,
                    MultiFileStream.GetFilePath(path),
                    MultiFileStream.GetFileName(path),
                    dependencyProvider));
            }
        }
    }
}