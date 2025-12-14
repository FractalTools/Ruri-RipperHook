using AssetRipper.Assets.Bundles;
using AssetRipper.Import.Logging;
using AssetRipper.IO.Files;
using AssetRipper.IO.Files.Streams;
using AssetRipper.IO.Files.Streams.Smart;
using Ruri.RipperHook.HookUtils.GameBundleHook;
using System.Buffers.Binary;
using System.Numerics;
using Ruri.RipperHook.Crypto;

namespace Ruri.RipperHook.EndField_0_5;

public partial class EndField_0_5_Hook
{
    public static void CustomFilePreInitialize(GameBundle _this, IEnumerable<string> paths, List<FileBase> fileStack, FileSystem fileSystem, IDependencyProvider? dependencyProvider)
    {
        foreach (var path in paths)
        {
            using var stream = SmartStream.OpenReadMulti(path, fileSystem);
            var fileData = new byte[stream.Length];
            stream.Read(fileData, 0, fileData.Length);

            var span = fileData.AsSpan();
            long position = 0;
            bool isVFSContainer = false;

            long firstBundleSize = GetVFSBundleSize(span);
            if (firstBundleSize > 0 && firstBundleSize < fileData.Length)
            {
                isVFSContainer = true;
            }

            if (isVFSContainer)
            {
                int index = 0;
                while (position < fileData.Length)
                {
                    var remaining = span.Slice((int)position);

                    if (remaining.Length < 40)
                        break;

                    long bundleSize = GetVFSBundleSize(remaining);

                    if (bundleSize <= 0 || bundleSize > remaining.Length)
                    {
                        Logger.Warning($"[EndField] Invalid bundle size at offset {position}: {bundleSize}. Stopping parse.");
                        break;
                    }

                    var bundleData = fileData.Skip((int)position).Take((int)bundleSize).ToArray();
                    var subName = $"{Path.GetFileName(path)}_sub{index++}";

                    fileStack.AddRange(GameBundleHook.LoadFilesAndDependencies(
                        bundleData,
                        MultiFileStream.GetFilePath(path),
                        subName,
                        dependencyProvider));

                    position += bundleSize;
                }
            }
            else
            {
                fileStack.AddRange(GameBundleHook.LoadFilesAndDependencies(
                    fileData,
                    MultiFileStream.GetFilePath(path),
                    MultiFileStream.GetFileName(path),
                    dependencyProvider));
            }
        }
    }

    private static long GetVFSBundleSize(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < 40) return -1;

        uint a = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(0, 4));
        uint b = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(4, 4));

        var c1 = ((a ^ 0x91A64750) >> 3) ^ ((a ^ 0x91A64750) << 29);
        var c2 = (c1 << 16) ^ 0xD5F9BECC;
        var c3 = (c1 ^ c2) & 0xFFFFFFFF;

        if (b != c3) return -1;

        uint size1 = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(26, 4));
        uint flags2 = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(32, 4));
        uint size2 = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(36, 4));

        ulong size = VFSDecryptor.BitConcat64(32, size1 ^ size2 ^ 0x342D983F, size2);
        size = (BitOperations.RotateLeft(size, 3)) ^ 0x5B4FA98A430D0E62UL;

        return (long)size;
    }
}