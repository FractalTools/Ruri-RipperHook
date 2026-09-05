using CUE4Parse.Compression;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.Utils;

namespace Ruri.FModelHook.Ripper.Converters;

/// <summary>
/// A virtual texture's first mip as the cooked block image it was built from: every level-0
/// tile holds the same DXT/BC blocks the mip is made of, framed by a border, so copying each
/// tile's inner blocks into place gives the mip without decoding a texel. Tile addressing,
/// chunk lookup and chunk codecs mirror CUE4Parse's DecodeVT tile for tile; that decoder turns
/// every tile into pixels instead, which is why a decoded virtual texture is held as full RGBA.
/// A texture whose tiles cannot be lifted as blocks -- more than one layer, UDIM blocks, a
/// border or tile that is not whole blocks, a chunk codec other than raw or zipped -- stays with
/// the decoder.
/// </summary>
public static class VirtualTiles
{
    private const int FirstLevel = 0;
    private const uint FirstLayer = 0;

    /// <summary>The one layer's pixel format when the tiles can be lifted as blocks at all, else null.</summary>
    public static EPixelFormat? Format(FVirtualTextureBuiltData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (!data.IsInitialized() || data.NumLayers != 1 || data.LayerTypes.Length != 1 || data.WidthInBlocks != 1 || data.HeightInBlocks != 1 || data.NumMips == 0)
        {
            return null;
        }
        foreach (FVirtualTextureDataChunk chunk in data.Chunks)
        {
            if (chunk.CodecType.Length == 0 || chunk.CodecType[0] is not (EVirtualTextureCodec.RawGPU or EVirtualTextureCodec.ZippedGPU_DEPRECATED))
            {
                return null;
            }
        }
        return data.LayerTypes[0];
    }

    /// <summary>Whether tiles and borders fall on whole blocks of the given size, so inner blocks can be cut out.</summary>
    public static bool Aligned(FVirtualTextureBuiltData data, int blockWidth, int blockHeight)
    {
        ArgumentNullException.ThrowIfNull(data);
        return data.TileSize % blockWidth == 0 && data.TileSize % blockHeight == 0
            && data.TileBorderSize % blockWidth == 0 && data.TileBorderSize % blockHeight == 0;
    }

    public static byte[] Lift(FVirtualTextureBuiltData data, int blockWidth, int blockHeight, int blockBytes)
    {
        ArgumentNullException.ThrowIfNull(data);
        int width = checked((int)data.Width);
        int height = checked((int)data.Height);
        int columns = Blocks(width, blockWidth);
        int rows = Blocks(height, blockHeight);
        byte[] image = new byte[checked(columns * rows * blockBytes)];

        int tileSize = checked((int)data.TileSize);
        int border = checked((int)data.TileBorderSize);
        int physical = checked((int)data.GetPhysicalTileSize());
        int tileColumns = physical / blockWidth;
        int tileRows = physical / blockHeight;
        int borderColumns = border / blockWidth;
        int borderRows = border / blockHeight;
        int innerColumns = tileSize / blockWidth;
        int innerRows = tileSize / blockHeight;
        int packedStride = tileColumns * blockBytes;
        int packedSize = packedStride * tileRows;
        byte[] tile = new byte[packedSize];

        FVirtualTextureTileOffsetData offsets = data.GetTileOffsetData(FirstLevel);
        for (uint address = 0; address < offsets.MaxAddress; address++)
        {
            if (!data.IsValidAddress(FirstLevel, address))
            {
                continue;
            }
            int tileX = checked((int)MathUtils.ReverseMortonCode2(address)) * tileSize;
            int tileY = checked((int)MathUtils.ReverseMortonCode2(address >> 1)) * tileSize;
            (int chunkIndex, uint start, uint length) = data.GetTileData(FirstLevel, address, FirstLayer);
            FVirtualTextureDataChunk chunk = data.Chunks[chunkIndex];
            byte[] chunkBytes = chunk.BulkData.Data
                ?? throw new InvalidDataException($"[VirtualTiles] Chunk {chunkIndex} of a virtual texture has no bulk data.");
            if (chunk.CodecType[0] == EVirtualTextureCodec.ZippedGPU_DEPRECATED)
            {
                Compression.Decompress(chunkBytes, checked((int)start), checked((int)length), tile, 0, packedSize, CompressionMethod.Zlib);
            }
            else
            {
                Array.Copy(chunkBytes, checked((int)start), tile, 0, packedSize);
            }

            int firstColumn = tileX / blockWidth;
            int firstRow = tileY / blockHeight;
            int copyColumns = Math.Min(innerColumns, columns - firstColumn);
            if (copyColumns <= 0)
            {
                continue;
            }
            for (int row = 0; row < innerRows; row++)
            {
                int target = firstRow + row;
                if (target >= rows)
                {
                    break;
                }
                Buffer.BlockCopy(tile, ((row + borderRows) * tileColumns + borderColumns) * blockBytes,
                    image, (target * columns + firstColumn) * blockBytes, copyColumns * blockBytes);
            }
        }
        return image;
    }

    private static int Blocks(int texels, int block) => (texels + block - 1) / block;
}
