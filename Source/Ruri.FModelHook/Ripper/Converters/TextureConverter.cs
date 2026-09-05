using AssetRipper.Import.Logging;
using AssetRipper.SourceGenerated;
using AssetRipper.SourceGenerated.Classes.ClassID_28;
using AssetRipper.SourceGenerated.Enums;
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

    public bool Handles(UObject export) => export is UTexture2D;

    public void Allocate(UnrealConversion conversion, UObject export)
    {
        ITexture2D texture = conversion.Package.Create<ITexture2D>(ClassIDType.Texture2D, export.Name, conversion.UnityPath(export));
        conversion.Register(export, texture);
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
            && source.GetFirstMip() is { BulkData.Data: { } raw } mip)
        {
            int expected = Blocks(mip.SizeX, cooked.BlockWidth) * Blocks(mip.SizeY, cooked.BlockHeight) * cooked.BlockBytes;
            if (raw.Length < expected)
            {
                Logger.Warning(LogCategory.Import, $"[Unreal] {conversion.PackagePath}:{export.Name} holds {raw.Length} bytes for {mip.SizeX}x{mip.SizeY} {source.Format}; {expected} were expected.");
                return;
            }
            Store(texture, source, mip.SizeX, mip.SizeY, cooked.Format, raw.Length == expected ? raw : raw[..expected]);
            return;
        }

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
        Store(texture, source, decoded.Width, decoded.Height, layout.Format, data.Length == size ? data : data[..size]);
    }

    private static int Blocks(int texels, int block) => (texels + block - 1) / block;

    private static void Store(ITexture2D texture, UTexture2D source, int width, int height, TextureFormat format, byte[] data)
    {
        TextureBuilder.Fill(texture, new TexturePixels
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
        });
    }
}
