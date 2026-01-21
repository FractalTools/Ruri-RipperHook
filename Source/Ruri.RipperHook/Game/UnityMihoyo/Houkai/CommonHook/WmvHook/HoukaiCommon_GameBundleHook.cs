using AssetRipper.Assets.Bundles;
using AssetRipper.IO.Endian;
using AssetRipper.IO.Files;
using AssetRipper.IO.Files.Streams;
using AssetRipper.IO.Files.Streams.Smart; // 引用 SmartStream
using Ruri.RipperHook.HookUtils.GameBundleHook;
using Ruri.RipperHook.UnityMihoyo;

namespace Ruri.RipperHook.Houkai;

public partial class HoukaiCommon_Hook
{
    private static bool blockXmfInited;
    private static List<WMVInfo> wmwFileInfo;

    // 签名更新：增加 FileSystem
    public static void CustomFilePreInitialize(GameBundle _this, IEnumerable<string> paths, List<FileBase> fileStack, FileSystem fileSystem, IDependencyProvider? dependencyProvider)
    {
        foreach (var path in paths)
        {
            var extension = Path.GetExtension(path);
            var isWmv = extension == ".wmv";

            // WMV XMF 初始化逻辑保持不变
            if (!blockXmfInited && isWmv)
            {
                var wmwPath = Path.GetDirectoryName(path);
                var blockXmfPath = Path.Combine(wmwPath, "Blocks.xmf");

                if (!File.Exists(blockXmfPath))
                    blockXmfPath = Path.Combine("Game", RuriRuntimeHook.gameName, RuriRuntimeHook.gameVer, "Blocks.xmf");

                if (File.Exists(blockXmfPath))
                {
                    blockXmfInited = true;
                    // 读取 XMF 依然使用 SmartStream.OpenRead (兼容性 API) 或者 OpenReadMulti
                    // 假设 XMF 是物理文件，这里可以用 OpenReadMulti 传入 fileSystem 保持一致
                    wmwFileInfo = ReadXMF(blockXmfPath, wmwPath, fileSystem);
                }
                else
                {
                    throw new Exception($"把Blocks.xmf放到wmw所在目录下或者程序根目录 {Directory.GetCurrentDirectory()}/Game/{RuriRuntimeHook.gameName}/{RuriRuntimeHook.gameVer}/ 下");
                }
            }

            if (isWmv)
            {
                var selectedWMVInfo = wmwFileInfo.FirstOrDefault(w => w.FilePath.Equals(path, StringComparison.OrdinalIgnoreCase));
                // 使用 fileSystem 打开流，更稳健
                using (var stream = SmartStream.OpenReadMulti(path, fileSystem))
                {
                    foreach (var asset in selectedWMVInfo.UnitAssetArray)
                    {
                        var fileData = new byte[asset.FileSize];
                        stream.Position = asset.Offset;
                        stream.Read(fileData, 0, fileData.Length);

                        var filePath = asset.FilePath;
                        var directoryPath = Path.GetDirectoryName(filePath);
                        var fileName = Path.GetFileName(filePath);

                        // 调用 GameBundleHook 处理内存数据
                        fileStack.AddRange(GameBundleHook.LoadFilesAndDependencies(fileData, directoryPath, fileName, dependencyProvider));
                    }
                }
            }
            else
            {
                // 普通文件：读取为 byte[] 后处理
                using (var stream = SmartStream.OpenReadMulti(path, fileSystem))
                {
                    var fileData = new byte[stream.Length];
                    stream.Read(fileData, 0, fileData.Length);
                    // 使用 MultiFileStream 获取文件名，与新版本保持一致
                    fileStack.AddRange(GameBundleHook.LoadFilesAndDependencies(fileData, MultiFileStream.GetFilePath(path), MultiFileStream.GetFileName(path), dependencyProvider));
                }
            }
        }
    }

    // 更新 ReadXMF 以支持 FileSystem (如果需要)，或者保持原样如果是纯物理路径
    private static List<WMVInfo> ReadXMF(string xmfPath, string wmwPath, FileSystem fileSystem)
    {
        var list = new List<WMVInfo>();
        // 使用 OpenReadMulti 兼容虚拟文件系统
        using var stream = SmartStream.OpenReadMulti(xmfPath, fileSystem);
        using var reader = new EndianReader(stream, EndianType.BigEndian);

        reader.ReadBytes(16); // Skip 16 bytes
        reader.ReadByte(); // Skip 1 byte
        reader.ReadInt32();
        reader.ReadInt32();
        reader.ReadInt32();
        reader.ReadInt32();
        reader.ReadInt32();
        while (reader.BaseStream.Position < reader.BaseStream.Length - 4)
        {
            var wmvInfo = new WMVInfo();
            var data = reader.ReadBytes(16);
            var fileName = Convert.ToHexString(data);
            wmvInfo.FilePath = Path.Combine(wmwPath, fileName + ".wmv");
            reader.ReadInt32();
            reader.ReadInt32();
            reader.ReadInt32();
            wmvInfo.FileSize = reader.ReadInt32();
            wmvInfo.FileCount = reader.ReadInt32();
            var assets = new BlockAssetInfo[wmvInfo.FileCount];

            for (var i = 0; i < wmvInfo.FileCount; i++)
            {
                assets[i] = new BlockAssetInfo();
                var count = reader.ReadInt16();
                var path = new string(reader.ReadChars(count));
                assets[i].FilePath = Path.Combine(path, fileName, path);
                assets[i].Offset = reader.ReadInt32();

                if (i > 0) assets[i - 1].FileSize = assets[i].Offset - assets[i - 1].Offset;

                if (i == wmvInfo.FileCount - 1) assets[i].FileSize = wmvInfo.FileSize - assets[i].Offset;
            }

            wmvInfo.UnitAssetArray = assets;
            list.Add(wmvInfo);
        }

        return list;
    }
}