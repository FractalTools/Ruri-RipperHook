using AssetRipper.IO.Files.BundleFiles;
using AssetRipper.IO.Files.BundleFiles.FileStream;
using AssetRipper.IO.Files.Exceptions;
using AssetRipper.IO.Files.Streams.Smart;
using K4os.Compression.LZ4;
using Ruri.RipperHook.Crypto;
using AssetRipper.Import.Logging;

namespace Ruri.RipperHook.EndField_0_8_25;

public partial class EndField_0_8_25_Hook
{
    public static void CustomBlockCompression(FileStreamNode entry, Stream m_stream, StorageBlock block, SmartStream m_cachedBlockStream, CompressionType compressType, int m_cachedBlockIndex)
    {
        switch (compressType)
        {
            case CompressionType.Lzma:
                LzmaCompression.DecompressLzmaStream(m_stream, block.CompressedSize, m_cachedBlockStream, block.UncompressedSize);
                break;

            case CompressionType.Lz4:
            case CompressionType.Lz4HC:
                // 标准 LZ4，用于普通压缩
                {
                    uint uncompressedSize = block.UncompressedSize;
                    byte[] uncompressedBytes = new byte[uncompressedSize];
                    Span<byte> compressedBytes = new BinaryReader(m_stream).ReadBytes((int)block.CompressedSize);
                    int bytesWritten = LZ4Codec.Decode(compressedBytes, uncompressedBytes);
                    if (bytesWritten != uncompressedSize)
                    {
                        ARIntelnalReflection.ThrowIncorrectNumberBytesWrittenMethod.Invoke(null, new object[] { entry.PathFixed, compressType, (long)uncompressedSize, (long)bytesWritten });
                    }
                    new MemoryStream(uncompressedBytes).CopyTo(m_cachedBlockStream);
                }
                break;

            case (CompressionType)5: // Endfield Encrypted Block (Type 5)
                {
                    var compressedSize = (int)block.CompressedSize;
                    var uncompressedSize = (int)block.UncompressedSize;

                    // 读取压缩数据
                    var compressedBytes = new BinaryReader(m_stream).ReadBytes(compressedSize);
                    var uncompressedBytes = new byte[uncompressedSize];

                    RuriRuntimeHook.CurrentDecryptor.Decrypt(compressedBytes);

                    // 2. Decompress (LZ4Inv - 必须使用自定义混淆 LZ4)
                    // AnimeStudio: LZ4Inv.Instance.Decompress
                    var numWrite = customLZ4.Decompress(compressedBytes, uncompressedBytes);

                    if (numWrite != uncompressedSize)
                    {
                        // 如果自定义解压也失败，尝试回退到标准 LZ4 (防止某些块未加密)
                        Logger.Warning($"[EndField] Custom LZ4 failed (Expected {uncompressedSize}, Got {numWrite}). Retrying with Standard LZ4...");
                        numWrite = LZ4Codec.Decode(compressedBytes, uncompressedBytes);
                    }

                    if (numWrite != uncompressedSize)
                    {
                        Logger.Error($"[EndField] Block {m_cachedBlockIndex} decompression CRITICAL failure. Expected {uncompressedSize}, Got {numWrite}.");
                    }

                    new MemoryStream(uncompressedBytes).CopyTo(m_cachedBlockStream);
                }
                break;

            default:
                if (ZstdCompression.IsZstd(m_stream))
                {
                    ZstdCompression.DecompressStream(m_stream, block.CompressedSize, m_cachedBlockStream, block.UncompressedSize);
                }
                else
                {
                    UnsupportedBundleDecompression.Throw("UnsupportedBundleDecompression", compressType);
                }
                break;
        }
    }
}