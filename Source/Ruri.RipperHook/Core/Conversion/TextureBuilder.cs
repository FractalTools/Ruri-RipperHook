using AssetRipper.SourceGenerated;
using AssetRipper.SourceGenerated.Classes.ClassID_28;
using AssetRipper.SourceGenerated.Enums;

namespace Ruri.RipperHook.Conversion;

/// <summary>
/// One image as Unity stores it: a texel block in a Unity <see cref="TextureFormat"/>, rows
/// running BOTTOM-UP the way Unity keeps them (a source whose rows run top-down flips them
/// with <see cref="FlipRows"/> first), one mip.
/// </summary>
public sealed class TexturePixels
{
    public required int Width { get; init; }

    public required int Height { get; init; }

    public required TextureFormat Format { get; init; }

    public required byte[] Data { get; init; }

    public bool Srgb { get; init; } = true;

    public bool IsNormalMap { get; init; }

    public bool RepeatU { get; init; } = true;

    public bool RepeatV { get; init; } = true;

    /// <summary>
    /// Reverse the row order of a tightly packed <paramref name="bytesPerPixel"/> image in place.
    /// </summary>
    public static void FlipRows(byte[] data, int width, int height, int bytesPerPixel)
    {
        int stride = width * bytesPerPixel;
        if (stride <= 0 || (long)stride * height != data.Length)
        {
            throw new ArgumentException($"[TexturePixels] {data.Length} bytes is not {width}x{height} at {bytesPerPixel} bytes per pixel.");
        }
        byte[] row = new byte[stride];
        for (int top = 0, bottom = height - 1; top < bottom; top++, bottom--)
        {
            Buffer.BlockCopy(data, top * stride, row, 0, stride);
            Buffer.BlockCopy(data, bottom * stride, data, top * stride, stride);
            Buffer.BlockCopy(row, 0, data, bottom * stride, stride);
        }
    }
}

public static class TextureBuilder
{
    private const int TwoDimensional = 2;
    private const int BilinearFilter = 1;
    private const int RepeatWrap = 0;
    private const int ClampWrap = 1;
    private const int GammaColorSpace = 0;
    private const int LinearColorSpace = 1;

    public static ITexture2D Build(ConvertedPackage package, string name, string? originalPath, TexturePixels pixels)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(pixels);

        ITexture2D texture = package.Create<ITexture2D>(ClassIDType.Texture2D, name, originalPath);
        texture.Width_C28 = pixels.Width;
        texture.Height_C28 = pixels.Height;
        texture.Format_C28E = pixels.Format;
        texture.ImageData_C28 = pixels.Data;
        if (texture.Has_CompleteImageSize_C28_Int32())
        {
            texture.CompleteImageSize_C28_Int32 = pixels.Data.Length;
        }
        if (texture.Has_CompleteImageSize_C28_UInt32())
        {
            texture.CompleteImageSize_C28_UInt32 = (uint)pixels.Data.Length;
        }
        texture.MipCount_C28 = 1;
        if (texture.Has_MipMap_C28())
        {
            texture.MipMap_C28 = false;
        }
        texture.ImageCount_C28 = 1;
        texture.Dimension_C28 = TwoDimensional;
        texture.IsReadable_C28 = true;
        texture.ColorSpace_C28 = pixels.Srgb ? GammaColorSpace : LinearColorSpace;
        texture.LightmapFormat_C28 = 0;
        texture.TextureSettings_C28.FilterMode = BilinearFilter;
        texture.TextureSettings_C28.Aniso = 1;
        texture.TextureSettings_C28.MipBias = 0f;
        texture.TextureSettings_C28.WrapU = pixels.RepeatU ? RepeatWrap : ClampWrap;
        texture.TextureSettings_C28.WrapV = pixels.RepeatV ? RepeatWrap : ClampWrap;
        texture.TextureSettings_C28.WrapW = RepeatWrap;
        return texture;
    }
}
