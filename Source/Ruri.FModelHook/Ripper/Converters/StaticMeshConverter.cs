using AssetRipper.SourceGenerated;
using AssetRipper.SourceGenerated.Classes.ClassID_1;
using AssetRipper.SourceGenerated.Classes.ClassID_21;
using AssetRipper.SourceGenerated.Classes.ClassID_43;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse_Conversion.Dto;
using CUE4Parse_Conversion.Options;
using Ruri.RipperHook.Conversion;
using System.Numerics;

namespace Ruri.FModelHook.Ripper.Converters;

/// <summary>
/// A static mesh: one Unity Mesh per LOD (the first under the mesh's own name, the rest
/// suffixed) and the prefab a model import would produce -- a GameObject rendering LOD 0 with
/// the mesh's material slots -- so a scene can instance it and a host can import it whole.
/// </summary>
public sealed class StaticMeshConverter : IUnrealConverter
{
    public const string PrefabSlot = "prefab";
    public const string PrefabSuffix = "_model";

    public IReadOnlyList<string> ClassNames { get; } = ["StaticMesh"];

    public IReadOnlyList<ClassIDType> Produces { get; } = [ClassIDType.Mesh, ClassIDType.GameObject, ClassIDType.MeshRenderer];

    public bool Handles(UObject export) => export is UStaticMesh;

    public static string LodSlot(int lod) => lod == 0 ? UnrealAssetTable.PrimarySlot : "lod" + lod;

    public void Allocate(UnrealConversion conversion, UObject export)
    {
        if (export is not UStaticMesh source || source.RenderData?.LODs is not { Length: > 0 } lods)
        {
            return;
        }
        for (int lod = 0; lod < lods.Length; lod++)
        {
            string name = lod == 0 ? export.Name : export.Name + "_LOD" + lod;
            IMesh mesh = conversion.Package.Create<IMesh>(ClassIDType.Mesh, name, conversion.UnityPath(export, lod == 0 ? null : "_LOD" + lod));
            conversion.Register(export, mesh, LodSlot(lod));
        }
        IGameObject model = conversion.Hierarchy.Node(export.Name, null, Vector3.Zero, Quaternion.Identity, Vector3.One,
            conversion.UnityPath(export, PrefabSuffix));
        conversion.Register(export, model, PrefabSlot);
    }

    public void Fill(UnrealConversion conversion, UObject export)
    {
        if (export is not UStaticMesh source)
        {
            return;
        }
        StaticMeshDto dto = new(source, EMeshQuality.All, ENaniteMeshFormat.NoNanite);
        List<IMaterial?> materials = Materials(conversion, dto);
        for (int lod = 0; lod < dto.LODs.Count; lod++)
        {
            MeshLodDto<MeshVertex> lodDto = dto.LODs[lod];
            if (conversion.Table.Find<IMesh>(export, LodSlot((int)lodDto.SourceLodIndex)) is not { } mesh)
            {
                continue;
            }
            MeshGeometry geometry = UnrealMeshGeometry.FromLod(mesh.Name.String, dto, lodDto, conversion.Basis, null, null, null, null);
            MeshBuilder.Fill(mesh, geometry);
        }
        if (conversion.Table.Find<IGameObject>(export, PrefabSlot) is { } model && conversion.Table.Find<IMesh>(export) is { } first)
        {
            conversion.Hierarchy.StaticMesh(model, first, materials);
        }
        dto.Dispose();
    }

    public static List<IMaterial?> Materials<TVertex>(UnrealConversion conversion, MeshDto<TVertex> dto) where TVertex : struct, IMeshVertex
    {
        List<IMaterial?> materials = new(dto.Materials.Length);
        foreach (MeshMaterialDto slot in dto.Materials)
        {
            FPackageIndex? pointer = slot.Material;
            materials.Add(pointer is null ? null : conversion.Table.Find<IMaterial>(pointer));
        }
        return materials;
    }
}
