using AssetRipper.SourceGenerated;
using AssetRipper.SourceGenerated.Classes.ClassID_1;
using AssetRipper.SourceGenerated.Classes.ClassID_21;
using AssetRipper.SourceGenerated.Classes.ClassID_4;
using AssetRipper.SourceGenerated.Classes.ClassID_43;
using AssetRipper.SourceGenerated.Extensions;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.SkeletalMesh;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse_Conversion.Dto;
using CUE4Parse_Conversion.Options;
using Ruri.RipperHook.Conversion;
using System.Numerics;

namespace Ruri.FModelHook.Ripper.Converters;

/// <summary>
/// A skeletal mesh: the skinned Mesh per LOD with its bind poses and bone name hashes, and the
/// rig prefab a model import would produce -- the bone hierarchy as GameObjects under a root
/// that carries the SkinnedMeshRenderer of LOD 0, bones listed in the order the mesh's weights
/// index them.
/// </summary>
public sealed class SkeletalMeshConverter : IUnrealConverter
{
    public const string PrefabSlot = StaticMeshConverter.PrefabSlot;

    public IReadOnlyList<string> ClassNames { get; } = ["SkeletalMesh"];

    public IReadOnlyList<ClassIDType> Produces { get; } = [ClassIDType.Mesh, ClassIDType.GameObject, ClassIDType.SkinnedMeshRenderer];

    public bool Handles(UObject export) => export is USkeletalMesh;

    public void Allocate(UnrealConversion conversion, UObject export)
    {
        if (export is not USkeletalMesh source || source.LODModels is not { Length: > 0 } lods)
        {
            return;
        }
        for (int lod = 0; lod < lods.Length; lod++)
        {
            string name = lod == 0 ? export.Name : export.Name + "_LOD" + lod;
            IMesh mesh = conversion.Package.Create<IMesh>(ClassIDType.Mesh, name, conversion.UnityPath(export, lod == 0 ? null : "_LOD" + lod));
            conversion.Register(export, mesh, StaticMeshConverter.LodSlot(lod));
        }
        IGameObject model = conversion.Hierarchy.Node(export.Name, null, Vector3.Zero, Quaternion.Identity, Vector3.One,
            conversion.UnityPath(export, StaticMeshConverter.PrefabSuffix));
        conversion.Register(export, model, PrefabSlot);
    }

    public void Fill(UnrealConversion conversion, UObject export)
    {
        if (export is not USkeletalMesh source)
        {
            return;
        }
        SkeletalMeshDto dto = new(source, EMeshQuality.All, ENaniteMeshFormat.NoNanite);
        UnrealRig rig = UnrealRig.From(dto.Bones, conversion.Basis);
        List<IMaterial?> materials = StaticMeshConverter.Materials(conversion, dto);
        string rootBoneName = rig.Names.Length > 0 ? rig.Names[0] : string.Empty;
        for (int lod = 0; lod < dto.LODs.Count; lod++)
        {
            MeshLodDto<SkinnedMeshVertex> lodDto = dto.LODs[lod];
            if (conversion.Table.Find<IMesh>(export, StaticMeshConverter.LodSlot((int)lodDto.SourceLodIndex)) is not { } mesh)
            {
                continue;
            }
            MeshGeometry geometry = UnrealMeshGeometry.FromLod(mesh.Name.String, dto, lodDto, conversion.Basis,
                static vertex => vertex.Influences, rig.BindPoses, rig.Names, rootBoneName);
            MeshBuilder.Fill(mesh, geometry);
        }
        if (conversion.Table.Find<IGameObject>(export, PrefabSlot) is { } model && conversion.Table.Find<IMesh>(export) is { } first)
        {
            ITransform[] bones = rig.Build(conversion.Hierarchy, model);
            FBox bounds = dto.Bounds;
            Vector3 minimum = conversion.Basis.Position(bounds.Min.X, bounds.Min.Y, bounds.Min.Z);
            Vector3 maximum = conversion.Basis.Position(bounds.Max.X, bounds.Max.Y, bounds.Max.Z);
            Vector3 center = (minimum + maximum) * 0.5f;
            Vector3 extent = Vector3.Abs(maximum - minimum) * 0.5f;
            conversion.Hierarchy.SkinnedMesh(model, first, bones, bones.Length > 0 ? bones[0] : null, materials, center, extent);
        }
        dto.Dispose();
    }
}
