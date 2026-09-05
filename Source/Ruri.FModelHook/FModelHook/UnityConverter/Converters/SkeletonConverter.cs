using AssetRipper.SourceGenerated;
using AssetRipper.SourceGenerated.Classes.ClassID_1;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Animation;
using CUE4Parse_Conversion.Dto;
using Ruri.RipperHook.Conversion;
using System.Numerics;

namespace Ruri.FModelHook.UnityConverter.Converters;

/// <summary>
/// A skeleton asset on its own: the rig prefab -- the bone hierarchy as GameObjects under a
/// root named after the skeleton -- so a clip can be played on a rig that no mesh in the
/// closure carries.
/// </summary>
public sealed class SkeletonConverter : IUnrealConverter
{
    public IReadOnlyList<string> ClassNames { get; } = ["Skeleton"];

    public IReadOnlyList<ClassIDType> Produces { get; } = [ClassIDType.GameObject, ClassIDType.Transform];

    public void Allocate(UnrealConversion conversion, ResolvedObject header)
    {
        if (!conversion.IsSeed)
        {
            return;
        }
        IGameObject root = conversion.Hierarchy.Node(header.Name.Text, null, Vector3.Zero, Quaternion.Identity, Vector3.One, conversion.UnityPath(header));
        conversion.Register(header, root);
    }

    public void Fill(UnrealConversion conversion, UObject export)
    {
        if (export is not USkeleton source || conversion.Table.Find<IGameObject>(export) is not { } root)
        {
            return;
        }
        SkeletonDto dto = new(source);
        UnrealRig rig = UnrealRig.From(dto.Bones, conversion.Basis);
        rig.Build(conversion.Hierarchy, root);
        dto.Dispose();
    }
}
