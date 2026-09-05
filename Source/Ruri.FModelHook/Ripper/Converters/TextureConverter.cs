using AssetRipper.Import.Logging;
using AssetRipper.SourceGenerated;
using AssetRipper.SourceGenerated.Classes.ClassID_28;
using AssetRipper.SourceGenerated.Enums;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse_Conversion.Textures;
using Ruri.RipperHook.Conversion;

namespace Ruri.FModelHook.Ripper.Converters;

/// <summary>
/// A two-dimensional texture, carried into Unity as the bytes Unreal cooked wherever Unity
/// knows the same texel layout -- a block-compressed mip is handed over as it is, in the
/// matching Unity format, with no decoding and a fraction of the memory -- and decoded by
/// CUE4Parse only for a layout Unity has no name for or a platform whose texels are swizzled.
/// Either way the rows stay in the source's top-down order: the texture is registered as such
/// and AssetRipper's exporter leaves it unturned.
/// </summary>
public sealed class TextureConverter : IUnrealConverter
{
    private readonly record struct Layout(TextureFormat Format, int BlockWidth, int BlockHeight, int BlockBytes);

    /// <summary>Every cooked layout Unity stores under a name of its own, with the block geometry that sizes one mip.</summary>
    private static readonly IReadOnlyDictionary<EPixelFormat, Layout> Cooked = new Dictionary<EPixelFormat, Layout>
    {
        [EPixelFormat.PF_DXT1] = new(TextureFormat.DXT1, 4, 4, 8),
        [EPixelFormat.PF_DXT3] = new(TextureFormat.DXT3, 4, 4, 16),
        [EPixelFormat.PF_DXT5] = new(TextureFormat.DXT5, 4, 4, 16),
        [EPixelFormat.PF_BC4] = new(TextureFormat.BC4, 4, 4, 8),
        [EPixelFormat.PF_BC5] = new(TextureFormat.BC5, 4, 4, 16),
        [EPixelFormat.PF_BC6H] = new(TextureFormat.BC6H, 4, 4, 16),
        [EPixelFormat.PF_BC7] = new(TextureFormat.BC7, 4, 4, 16),
        [EPixelFormat.PF_ASTC_4x4] = new(TextureFormat.ASTC_4x4, 4, 4, 16),
        [EPixelFormat.PF_ASTC_6x6] = new(TextureFormat.ASTC_6x6, 6, 6, 16),
        [EPixelFormat.PF_ASTC_8x8] = new(TextureFormat.ASTC_8x8, 8, 8, 16),
        [EPixelFormat.PF_ASTC_10x10] = new(TextureFormat.ASTC_10x10, 10, 10, 16),
        [EPixelFormat.PF_ASTC_12x12] = new(TextureFormat.ASTC_12x12, 12, 12, 16),
        [EPixelFormat.PF_ETC2_RGB] = new(TextureFormat.ETC2_RGB, 4, 4, 8),
        [EPixelFormat.PF_ETC2_RGBA] = new(TextureFormat.ETC2_RGBA8, 4, 4, 16),
        [EPixelFormat.PF_B8G8R8A8] = new(TextureFormat.BGRA32_14, 1, 1, 4),
        [EPixelFormat.PF_R8G8B8A8] = new(TextureFormat.RGBA32, 1, 1, 4),
        [EPixelFormat.PF_A8R8G8B8] = new(TextureFormat.ARGB32, 1, 1, 4),
        [EPixelFormat.PF_G8] = new(TextureFormat.R8, 1, 1, 1),
        [EPixelFormat.PF_R8G8] = new(TextureFormat.RG16, 1, 1, 2),
        [EPixelFormat.PF_G16] = new(TextureFormat.R16, 1, 1, 2),
        [EPixelFormat.PF_R16F] = new(TextureFormat.RHalf, 1, 1, 2),
        [EPixelFormat.PF_G16R16F] = new(TextureFormat.RGHalf, 1, 1, 4),
        [EPixelFormat.PF_FloatRGBA] = new(TextureFormat.RGBAHalf, 1, 1, 8),
        [EPixelFormat.PF_R32_FLOAT] = new(TextureFormat.RFloat, 1, 1, 4),
        [EPixelFormat.PF_G32R32F] = new(TextureFormat.RGFloat, 1, 1, 8),
        [EPixelFormat.PF_A32B32G32R32F] = new(TextureFormat.RGBAFloat, 1, 1, 16),
    };

    /// <summary>The Unity format of each layout CUE4Parse decodes to, with its bytes per texel.</summary>
    private static readonly IReadOnlyDictionary<EPixelFormat, Layout> Decoded = new Dictionary<EPixelFormat, Layout>
    {
        [EPixelFormat.PF_B8G8R8A8] = new(TextureFormat.BGRA32_14, 1, 1, 4),
        [EPixelFormat.PF_R8G8B8A8] = new(TextureFormat.RGBA32, 1, 1, 4),
        [EPixelFormat.PF_A8R8G8B8] = new(TextureFormat.ARGB32, 1, 1, 4),
        [EPixelFormat.PF_G8] = new(TextureFormat.R8, 1, 1, 1),
        [EPixelFormat.PF_G16] = new(TextureFormat.R16, 1, 1, 2),
        [EPixelFormat.PF_R16F] = new(TextureFormat.RHalf, 1, 1, 2),
        [EPixelFormat.PF_G16R16F] = new(TextureFormat.RGHalf, 1, 1, 4),
        [EPixelFormat.PF_FloatRGBA] = new(TextureFormat.RGBAHalf, 1, 1, 8),
        [EPixelFormat.PF_R32_FLOAT] = new(TextureFormat.RFloat, 1, 1, 4),
        [EPixelFormat.PF_G32R32F] = new(TextureFormat.RGFloat, 1, 1, 8),
        [EPixelFormat.PF_A32B32G32R32F] = new(TextureFormat.RGBAFloat, 1, 1, 16),
    };

    public IReadOnlyList<string> ClassNames { get; } = ["Texture2D", "LightMapTexture2D", "ShadowMapTexture2D", "VirtualTexture2D"];

    public IReadOnlyList<ClassIDType> Produces { get; } = [ClassIDType.Texture2D];

    public void Allocate(UnrealConversion conversion, ResolvedObject header)
    {
        ITexture2D texture = conversion.Package.Create<ITexture2D>(ClassIDType.Texture2D, header.Name.Text, conversion.UnityPath(header));
        conversion.Register(header, texture);
    }

    public void Fill(UnrealConversion conversion, UObject export)
    {
        if (export is not UTexture2D source || conversion.Table.Find<ITexture2D>(export) is not { } texture)
        {
            return;
        }
        ETexturePlatform platform = UnrealSourceOptions.TexturePlatformChoice();
        if (platform == ETexturePlatform.DesktopMobile
            && Cooked.TryGetValue(source.Format, out Layout cooked)
            && source.GetFirstMip() is { } mip)
        {
            int expected = Blocks(mip.SizeX, cooked.BlockWidth) * Blocks(mip.SizeY, cooked.BlockHeight) * cooked.BlockBytes;
            int available = mip.BulkData.Header.ElementCount;
            if (available < expected)
            {
                Logger.Warning(LogCategory.Import, $"[Unreal] {conversion.PackagePath}:{export.Name} holds {available} bytes for {mip.SizeX}x{mip.SizeY} {source.Format}; {expected} were expected.");
                return;
            }
            UnrealFileProvider provider = conversion.Shared.Provider;
            string packagePath = conversion.PackagePath;
            string exportName = export.Name;
            TextureBuilder.Defer(texture, Shape(source, mip.SizeX, mip.SizeY, cooked.Format, []), expected, conversion.Package.Space.Deferred,
                () => CookedBytes(provider, packagePath, exportName, expected));
            return;
        }

        if (platform == ETexturePlatform.DesktopMobile
            && source.PlatformData is { FirstMipToSerialize: >= 0, VTData: { } tiles }
            && VirtualTiles.Format(tiles) is { } layerFormat
            && Cooked.TryGetValue(layerFormat, out Layout lifted)
            && VirtualTiles.Aligned(tiles, lifted.BlockWidth, lifted.BlockHeight))
        {
            int width = checked((int)tiles.Width);
            int height = checked((int)tiles.Height);
            int expected = Blocks(width, lifted.BlockWidth) * Blocks(height, lifted.BlockHeight) * lifted.BlockBytes;
            UnrealFileProvider provider = conversion.Shared.Provider;
            string packagePath = conversion.PackagePath;
            string exportName = export.Name;
            TextureBuilder.Defer(texture, Shape(source, width, height, lifted.Format, []), expected, conversion.Package.Space.Deferred,
                () => LiftedTiles(provider, packagePath, exportName, lifted));
            return;
        }

        Logger.Verbose(LogCategory.Import, $"[Unreal] {conversion.PackagePath}:{export.Name} ({export.ExportType}, {source.Format}, {source.PlatformData.Mips.Length} mips, first mip {(source.GetFirstMip() is { } first ? first.SizeX + "x" + first.SizeY : "none")}) decodes rather than defers.");
        CTexture? decoded = source.Decode(platform);
        if (decoded is null)
        {
            Logger.Warning(LogCategory.Import, $"[Unreal] {conversion.PackagePath}:{export.Name} has no decodable mip.");
            return;
        }
        if (!Decoded.TryGetValue(decoded.PixelFormat, out Layout layout))
        {
            Logger.Warning(LogCategory.Import, $"[Unreal] {conversion.PackagePath}:{export.Name} decodes to {decoded.PixelFormat}, which Unity has no texel layout for.");
            return;
        }
        byte[] data = decoded.Data;
        int size = decoded.Width * decoded.Height * layout.BlockBytes;
        if (data.Length < size)
        {
            Logger.Warning(LogCategory.Import, $"[Unreal] {conversion.PackagePath}:{export.Name} decoded to {data.Length} bytes for {decoded.Width}x{decoded.Height} {decoded.PixelFormat}.");
            return;
        }
        TextureBuilder.Fill(texture, Shape(source, decoded.Width, decoded.Height, layout.Format, data.Length == size ? data : data[..size]));
    }

    /// <summary>
    /// The first mip's cooked bytes, read from the archive when the exporter asks for them
    /// through a package instance nobody keeps, so no texture is held between fill and export.
    /// </summary>
    private static byte[] CookedBytes(UnrealFileProvider provider, string packagePath, string exportName, int expected)
    {
        if (Reload(provider, packagePath, exportName).GetFirstMip() is not { BulkData.Data: { } raw })
        {
            throw new InvalidDataException($"[Unreal] {packagePath}:{exportName} no longer yields its first mip.");
        }
        if (raw.Length < expected)
        {
            throw new InvalidDataException($"[Unreal] {packagePath}:{exportName} holds {raw.Length} bytes, {expected} were reserved.");
        }
        return raw.Length == expected ? raw : raw[..expected];
    }

    /// <summary>The first mip of a virtual texture lifted from its tiles when the exporter asks (see <see cref="VirtualTiles"/>).</summary>
    private static byte[] LiftedTiles(UnrealFileProvider provider, string packagePath, string exportName, Layout layout)
    {
        if (Reload(provider, packagePath, exportName).PlatformData?.VTData is not { } tiles)
        {
            throw new InvalidDataException($"[Unreal] {packagePath}:{exportName} no longer carries virtual texture data.");
        }
        return VirtualTiles.Lift(tiles, layout.BlockWidth, layout.BlockHeight, layout.BlockBytes);
    }

    /// <summary>The texture read again through a package instance nobody keeps.</summary>
    private static UTexture2D Reload(UnrealFileProvider provider, string packagePath, string exportName)
    {
        if (provider.LoadUncached(provider[packagePath]) is not AbstractUePackage package)
        {
            throw new InvalidDataException($"[Unreal] {packagePath} is not a package with exports.");
        }
        int index = package.GetExportIndex(exportName);
        if (index < 0 || package.ExportsLazy[index].Value is not UTexture2D source)
        {
            throw new InvalidDataException($"[Unreal] {packagePath}:{exportName} is no longer a Texture2D export.");
        }
        return source;
    }

    private static int Blocks(int texels, int block) => (texels + block - 1) / block;

    private static TexturePixels Shape(UTexture2D source, int width, int height, TextureFormat format, byte[] data)
    {
        return new TexturePixels
        {
            Width = width,
            Height = height,
            Format = format,
            Data = data,
            TopDown = true,
            Srgb = source.SRGB,
            IsNormalMap = source.IsNormalMap,
            RepeatU = source.GetTextureAddressX() == TextureAddress.TA_Wrap,
            RepeatV = source.GetTextureAddressY() == TextureAddress.TA_Wrap,
        };
    }
}
