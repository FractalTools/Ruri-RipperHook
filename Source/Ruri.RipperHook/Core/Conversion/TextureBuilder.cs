using AssetRipper.SourceGenerated.Extensions;
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
    /// The data's first row is the image's top row, the way most sources keep it; Unity keeps the
    /// bottom row first. Stated true, the texture is registered with <see cref="TextureOrientation"/>
    /// and exported as it is; stated false, the rows were already turned to Unity's order.
    /// </summary>
    public bool TopDown { get; init; }

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
    // Texture2D.m_ColorSpace says whether THESE BYTES are sRGB-encoded, not which space a
    // project renders in: the exporter reads it back as `sRGBTexture = value == 1`
    // (AssetRipper.Export.UnityProjects/Textures/ImporterFactory.cs). The generated property
    // is typed as UnityEngine.ColorSpace, whose Gamma=0/Linear=1 naming means the opposite of
    // what this field holds -- so the two values are named here after what they actually say.
    private const int LinearBytes = 0;
    private const int SrgbBytes = 1;

    public static ITexture2D Build(ConvertedPackage package, string name, string? originalPath, TexturePixels pixels)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(pixels);

        ITexture2D texture = package.Create<ITexture2D>(ClassIDType.Texture2D, name, originalPath);
        Fill(texture, pixels);
        return texture;
    }

    /// <summary>
    /// The texture described by <paramref name="pixels"/> with its bytes fetched at export: the
    /// description carries no data, <paramref name="size"/> bytes are reserved in the load's
    /// deferred resource and <paramref name="fetch"/> produces them when the exporter asks, so
    /// the texture costs nothing to hold.
    /// </summary>
    public static void Defer(ITexture2D texture, TexturePixels pixels, long size, DeferredResource resource, Func<byte[]> fetch)
    {
        ArgumentNullException.ThrowIfNull(texture);
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentNullException.ThrowIfNull(resource);
        if (pixels.Data.Length != 0)
        {
            throw new ArgumentException("A deferred texture describes its data, it does not carry it.", nameof(pixels));
        }
        if (!texture.Has_StreamData_C28())
        {
            throw new NotSupportedException($"[TextureBuilder] Unity {texture.Collection.Version} keeps no stream data on a Texture2D.");
        }
        Fill(texture, pixels);
        SetCompleteImageSize(texture, size);
        texture.StreamData_C28!.Path = DeferredResource.FileName;
        texture.StreamData_C28.SetOffset((ulong)resource.Reserve(size, fetch));
        texture.StreamData_C28.Size = (uint)size;
    }

    private static void SetCompleteImageSize(ITexture2D texture, long size)
    {
        if (texture.Has_CompleteImageSize_C28_Int32())
        {
            texture.CompleteImageSize_C28_Int32 = checked((int)size);
        }
        if (texture.Has_CompleteImageSize_C28_UInt32())
        {
            texture.CompleteImageSize_C28_UInt32 = checked((uint)size);
        }
    }

    public static void Fill(ITexture2D texture, TexturePixels pixels)
    {
        ArgumentNullException.ThrowIfNull(texture);
        ArgumentNullException.ThrowIfNull(pixels);
        texture.Width_C28 = pixels.Width;
        texture.Height_C28 = pixels.Height;
        texture.Format_C28E = pixels.Format;
        texture.ImageData_C28 = pixels.Data;
        if (pixels.TopDown)
        {
            TextureOrientation.MarkTopDown(texture);
        }
        SetCompleteImageSize(texture, pixels.Data.Length);
        texture.MipCount_C28 = 1;
        if (texture.Has_MipMap_C28())
        {
            texture.MipMap_C28 = false;
        }
        texture.ImageCount_C28 = 1;
        texture.Dimension_C28 = TwoDimensional;
        texture.IsReadable_C28 = true;
        texture.ColorSpace_C28 = pixels.Srgb ? SrgbBytes : LinearBytes;
        texture.LightmapFormat_C28 = 0;
        texture.TextureSettings_C28.FilterMode = BilinearFilter;
        texture.TextureSettings_C28.Aniso = 1;
        texture.TextureSettings_C28.MipBias = 0f;
        texture.TextureSettings_C28.WrapU = pixels.RepeatU ? RepeatWrap : ClampWrap;
        texture.TextureSettings_C28.WrapV = pixels.RepeatV ? RepeatWrap : ClampWrap;
        texture.TextureSettings_C28.WrapW = RepeatWrap;
    }
}
