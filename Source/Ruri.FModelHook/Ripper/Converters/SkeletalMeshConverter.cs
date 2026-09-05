using AssetRipper.SourceGenerated;
using AssetRipper.SourceGenerated.Classes.ClassID_1;
using AssetRipper.SourceGenerated.Classes.ClassID_21;
using AssetRipper.SourceGenerated.Classes.ClassID_4;
using AssetRipper.SourceGenerated.Classes.ClassID_43;
using AssetRipper.SourceGenerated.Extensions;
using CUE4Parse.UE4.Assets;
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

    public void Allocate(UnrealConversion conversion, ResolvedObject header)
    {
        conversion.Register(header, conversion.Package.Create<IMesh>(ClassIDType.Mesh, header.Name.Text, conversion.UnityPath(header)));
        if (!conversion.IsSeed)
        {
            return;
        }
        IGameObject model = conversion.Hierarchy.Node(header.Name.Text, null, Vector3.Zero, Quaternion.Identity, Vector3.One,
            conversion.UnityPath(header, StaticMeshConverter.PrefabSuffix));
        conversion.Register(header, model, PrefabSlot);
    }

    public void Fill(UnrealConversion conversion, UObject export)
    {
        if (export is not USkeletalMesh source)
        {
            return;
        }
        SkeletalMeshDto dto = new(source, EMeshQuality.All, ENaniteMeshFormat.NoNanite);
        UnrealRig rig = conversion.Shared.Rig(source, conversion.Basis);
        List<IMaterial?> materials = StaticMeshConverter.Materials(conversion, dto);
        string rootBoneName = rig.Names.Length > 0 ? rig.Names[0] : string.Empty;
        for (int lod = 0; lod < dto.LODs.Count; lod++)
        {
            MeshLodDto<SkinnedMeshVertex> lodDto = dto.LODs[lod];
            IMesh mesh = StaticMeshConverter.Lod(conversion, export, (int)lodDto.SourceLodIndex);
            MeshGeometry geometry = UnrealMeshGeometry.FromLod(mesh.Name.String, dto, lodDto, conversion.Basis,
                static vertex => vertex.Influences, rig.BindPoses, rig.Names, rootBoneName);
            MeshBuilder.Fill(mesh, geometry);
        }
        if (conversion.Table.Find<IGameObject>(export, PrefabSlot) is { } model && conversion.Table.Find<IMesh>(export) is { } first)
        {
            ITransform[] bones = rig.Build(conversion.Hierarchy, model);
            (Vector3 center, Vector3 extent) = UnrealComponents.Bounds(conversion, dto.Bounds);
            conversion.Hierarchy.SkinnedMesh(model, first, bones, bones.Length > 0 ? bones[0] : null, materials, center, extent);
        }
        dto.Dispose();
    }
}
