using AssetRipper.Assets;
using AssetRipper.Import.Structure.Assembly.Serializable;
using AssetRipper.SourceGenerated.Classes.ClassID_114;
using AssetRipper.SourceGenerated.Classes.ClassID_28;
using AssetRipper.SourceGenerated.Classes.ClassID_43;
using AssetRipper.SourceGenerated.Classes.ClassID_74;
using Ruri.RipperHook.Conversion;

namespace Ruri.FModelHook.UnityConverter;

/// <summary>
/// What a load produced, by the sizes that decide its memory: the bytes every mesh holds, the
/// texture bytes held inline rather than deferred, the fields every property bag carries and
/// the keys every clip keeps. Read with the heap figures of the load's summary line, it says
/// which product a memory peak is made of.
/// </summary>
public static class UnrealLoadProfile
{
    public static string Summarize(ConvertedSpace space)
    {
        ArgumentNullException.ThrowIfNull(space);
        long meshes = 0;
        long meshBytes = 0;
        long textures = 0;
        long textureBytes = 0;
        long bags = 0;
        long bagFields = 0;
        long clips = 0;
        long clipKeys = 0;
        long others = 0;
        foreach (IUnityObjectBase asset in space.Bundle.FetchAssets())
        {
            switch (asset)
            {
                case IMesh mesh:
                    meshes++;
                    meshBytes += mesh.VertexData.Data.Length + mesh.IndexBuffer.Length;
                    break;
                case ITexture2D texture:
                    textures++;
                    textureBytes += texture.ImageData_C28.Length;
                    break;
                case IMonoBehaviour behaviour:
                    bags++;
                    bagFields += behaviour.Structure is SerializableStructure structure ? Count(structure) : 0;
                    break;
                case IAnimationClip clip:
                    clips++;
                    clipKeys += Keys(clip);
                    break;
                default:
                    others++;
                    break;
            }
        }
        return $"meshes={meshes} meshBytes={meshBytes >> 20}MB textures={textures} inlineTextureBytes={textureBytes >> 20}MB "
            + $"bags={bags} bagFields={bagFields} clips={clips} clipKeys={clipKeys} others={others}";
    }

    private static long Count(object? value) => value switch
    {
        SerializableStructure structure => 1 + structure.Fields.Sum(static field => Count(field.CValue)),
        IUnityAssetBase[] array => array.Sum(static item => Count(item)),
        IUnityAssetBase[][] arrays => arrays.Sum(static array => array.Sum(static item => Count(item))),
        Array array => array.Length,
        _ => 1,
    };

    private static long Keys(IAnimationClip clip)
    {
        long keys = 0;
        foreach (var curve in clip.RotationCurves_C74)
        {
            keys += curve.Curve.Curve.Count;
        }
        foreach (var curve in clip.PositionCurves_C74)
        {
            keys += curve.Curve.Curve.Count;
        }
        foreach (var curve in clip.ScaleCurves_C74)
        {
            keys += curve.Curve.Curve.Count;
        }
        foreach (var curve in clip.FloatCurves_C74)
        {
            keys += curve.Curve.Curve.Count;
        }
        return keys;
    }
}
