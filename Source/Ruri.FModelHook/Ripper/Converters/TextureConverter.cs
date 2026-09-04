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
/// A two-dimensional texture: the top mip decoded by CUE4Parse into plain texels, carried
/// into Unity in the matching uncompressed format with its rows turned the way Unity keeps
/// them. Decoding once here is the whole cost -- AssetRipper's exporter then reads a format
/// it needs no decoder for.
/// </summary>
public sealed class TextureConverter : IUnrealConverter
{
    /// <summary>The Unity format each decoded texel layout is stored as, with its bytes per texel.</summary>
    private static readonly IReadOnlyDictionary<EPixelFormat, (TextureFormat Format, int BytesPerPixel)> Formats =
        new Dictionary<EPixelFormat, (TextureFormat, int)>
        {
            [EPixelFormat.PF_B8G8R8A8] = (TextureFormat.BGRA32_14, 4),
            [EPixelFormat.PF_R8G8B8A8] = (TextureFormat.RGBA32, 4),
            [EPixelFormat.PF_A8R8G8B8] = (TextureFormat.ARGB32, 4),
            [EPixelFormat.PF_G8] = (TextureFormat.R8, 1),
            [EPixelFormat.PF_G16] = (TextureFormat.R16, 2),
            [EPixelFormat.PF_R16F] = (TextureFormat.RHalf, 2),
            [EPixelFormat.PF_G16R16F] = (TextureFormat.RGHalf, 4),
            [EPixelFormat.PF_FloatRGBA] = (TextureFormat.RGBAHalf, 8),
            [EPixelFormat.PF_R32_FLOAT] = (TextureFormat.RFloat, 4),
            [EPixelFormat.PF_G32R32F] = (TextureFormat.RGFloat, 8),
            [EPixelFormat.PF_A32B32G32R32F] = (TextureFormat.RGBAFloat, 16),
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
        CTexture? decoded = source.Decode(UnrealSourceOptions.TexturePlatformChoice());
        if (decoded is null)
        {
            Logger.Warning(LogCategory.Import, $"[Unreal] {conversion.PackagePath}:{export.Name} has no decodable mip.");
            return;
        }
        if (!Formats.TryGetValue(decoded.PixelFormat, out (TextureFormat Format, int BytesPerPixel) layout))
        {
            Logger.Warning(LogCategory.Import, $"[Unreal] {conversion.PackagePath}:{export.Name} decodes to {decoded.PixelFormat}, which Unity has no texel layout for.");
            return;
        }
        byte[] data = decoded.Data;
        int expected = decoded.Width * decoded.Height * layout.BytesPerPixel;
        if (data.Length < expected)
        {
            Logger.Warning(LogCategory.Import, $"[Unreal] {conversion.PackagePath}:{export.Name} decoded to {data.Length} bytes for {decoded.Width}x{decoded.Height} {decoded.PixelFormat}.");
            return;
        }
        if (data.Length > expected)
        {
            data = data[..expected];
        }
        TexturePixels.FlipRows(data, decoded.Width, decoded.Height, layout.BytesPerPixel);
        TextureBuilder.Fill(texture, new TexturePixels
        {
            Width = decoded.Width,
            Height = decoded.Height,
            Format = layout.Format,
            Data = data,
            Srgb = source.SRGB,
            IsNormalMap = source.IsNormalMap,
            RepeatU = source.GetTextureAddressX() == TextureAddress.TA_Wrap,
            RepeatV = source.GetTextureAddressY() == TextureAddress.TA_Wrap,
        });
    }
}
