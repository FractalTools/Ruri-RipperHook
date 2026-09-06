using AssetRipper.Import.Logging;
using AssetRipper.SourceGenerated;
using AssetRipper.SourceGenerated.Classes.ClassID_1;
using AssetRipper.SourceGenerated.Classes.ClassID_108;
using AssetRipper.SourceGenerated.Classes.ClassID_21;
using AssetRipper.SourceGenerated.Classes.ClassID_4;
using AssetRipper.SourceGenerated.Classes.ClassID_43;
using AssetRipper.SourceGenerated.Extensions;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Component;
using CUE4Parse.UE4.Assets.Exports.Component.Lights;
using CUE4Parse.UE4.Assets.Exports.Component.SkeletalMesh;
using CUE4Parse.UE4.Assets.Exports.Component.StaticMesh;
using CUE4Parse.UE4.Assets.Exports.SkeletalMesh;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.UObject;
using Ruri.RipperHook.Conversion;
using System.Numerics;

namespace Ruri.FModelHook.Unreal.Converters;

/// <summary>
/// Scene components placed as a GameObject hierarchy: a component is a node carrying its
/// relative transform under the node of the component it attaches to, and what it renders --
/// a static mesh, a skinned mesh with its rig, one node per instance, a light -- hangs off that
/// node. A level's actors and a Blueprint's construction script both describe such trees; this
/// is the one place either becomes Unity objects.
/// </summary>
public sealed class UnrealComponentTree
{
    private readonly UnrealConversion conversion;
    private readonly Dictionary<USceneComponent, IGameObject> nodes = new(ReferenceEqualityComparer.Instance);

    public UnrealComponentTree(UnrealConversion conversion)
    {
        this.conversion = conversion ?? throw new ArgumentNullException(nameof(conversion));
    }

    /// <summary>What this tree will build: the components a reading stated, in its own order.</summary>
    public UnrealSceneGraph.Collector Components { get; } = new();

    public int Count => Components.Count;

    /// <summary>State one component; a parent the tree never receives leaves the component at the tree's root.</summary>
    public void Add(USceneComponent component, USceneComponent? parent, string name, bool active) =>
        Components.Add(component, parent, name, active);

    public bool Contains(USceneComponent component) => Components.Contains(component);

    /// <summary>Every node, parents before children, the roots under <paramref name="root"/> -- a scene's top level when null.</summary>
    public IReadOnlyDictionary<USceneComponent, IGameObject> Build(ITransform? root)
    {
        List<UnrealSceneGraph.Placed> ordered = Components.Ordered();
        List<IGameObject> built = new(ordered.Count);
        foreach (UnrealSceneGraph.Placed placed in ordered)
        {
            ITransform? parent = placed.Parent >= 0 ? built[placed.Parent].GetTransform() : root;
            (Vector3 position, Quaternion rotation, Vector3 scale) =
                UnrealComponents.Transform(conversion, placed.Component.GetRelativeTransform());
            IGameObject node = conversion.Hierarchy.Node(placed.Name, parent, position, rotation, scale);
            if (!placed.Active)
            {
                node.SetIsActive(false);
            }
            nodes[placed.Component] = node;
            built.Add(node);
            UnrealComponents.Render(conversion, node, placed.Component);
        }
        return nodes;
    }
}

/// <summary>
/// What one scene component renders, in Unity's terms: a static mesh component a MeshRenderer
/// of the converted mesh, a skinned mesh component the mesh's rig rebuilt under the node with a
/// SkinnedMeshRenderer over it, an instanced component one node per instance, a light
/// component a Light. Material slots take the component's override where it states one.
/// </summary>
public static class UnrealComponents
{
    private const int SpotLight = 0;
    private const int DirectionalLight = 1;
    private const int PointLight = 2;
    private const int AreaLight = 3;
    private const string HiddenInGameName = "bHiddenInGame";
    private const string VisibleName = "bVisible";

    public static (Vector3, Quaternion, Vector3) Transform(UnrealConversion conversion, FTransform transform) =>
        Transform(conversion.Basis, transform);

    /// <summary>A transform through the host's basis -- the one place a placement changes hands.</summary>
    public static (Vector3 Position, Quaternion Rotation, Vector3 Scale) Transform(SourceBasis basis, FTransform transform)
    {
        Vector3 position = basis.Position(transform.Translation.X, transform.Translation.Y, transform.Translation.Z);
        Quaternion rotation = basis.Rotation(transform.Rotation.X, transform.Rotation.Y, transform.Rotation.Z, transform.Rotation.W);
        Vector3 scale = basis.Scale(transform.Scale3D.X, transform.Scale3D.Y, transform.Scale3D.Z);
        return (position, rotation, scale);
    }

    /// <summary>A box in Unity's basis as the centre and half-extent a renderer's bounds state.</summary>
    public static (Vector3 Center, Vector3 Extent) Bounds(UnrealConversion conversion, FBox box)
    {
        Vector3 minimum = conversion.Basis.Position(box.Min.X, box.Min.Y, box.Min.Z);
        Vector3 maximum = conversion.Basis.Position(box.Max.X, box.Max.Y, box.Max.Z);
        return ((minimum + maximum) * 0.5f, Vector3.Abs(maximum - minimum) * 0.5f);
    }

    /// <summary>Whether the component shows: hidden in game or marked invisible means not.</summary>
    public static bool Visible(USceneComponent component)
    {
        if (component.TryGetValue(out bool hidden, HiddenInGameName) && hidden)
        {
            return false;
        }
        return !component.TryGetValue(out bool visible, VisibleName) || visible;
    }

    public static void Render(UnrealConversion conversion, IGameObject node, USceneComponent component)
    {
        switch (component)
        {
            case UInstancedStaticMeshComponent instanced:
                Instances(conversion, node, instanced);
                break;
            case UStaticMeshComponent staticMesh:
                Static(conversion, node, staticMesh.GetStaticMesh(), staticMesh.OverrideMaterials);
                break;
            case USkinnedMeshComponent skinned:
                Skinned(conversion, node, skinned.GetSkeletalMesh(), skinned.OverrideMaterials);
                break;
            case ULightComponentBase light:
                Light(conversion, node, light);
                break;
        }
    }

    /// <summary>The material per slot as the Unity asset table answers for it.</summary>
    public static List<IMaterial?> Materials(UnrealConversion conversion, UObject mesh, FPackageIndex?[] overrides)
    {
        string[] paths = MaterialPaths(mesh, overrides);
        List<IMaterial?> materials = new(paths.Length);
        foreach (string path in paths)
        {
            materials.Add(path.Length == 0 ? null : conversion.Table.Find<IMaterial>(path));
        }
        return materials;
    }

    /// <summary>
    /// The object path of the material each of a mesh's slots draws with: the component's
    /// override where it states one, else the mesh's own; empty for a slot nothing names.
    /// This is the whole decision, stated where both lanes read it.
    /// </summary>
    public static string[] MaterialPaths(UObject mesh, FPackageIndex?[] overrides)
    {
        FPackageIndex?[] slots = mesh switch
        {
            UStaticMesh staticMesh => staticMesh.StaticMaterials.Select(static slot => slot.MaterialInterface).ToArray(),
            USkeletalMesh skeletalMesh => skeletalMesh.SkeletalMaterials.Select(static slot => slot.Material).ToArray(),
            _ => [],
        };
        string[] paths = new string[slots.Length];
        for (int i = 0; i < slots.Length; i++)
        {
            FPackageIndex? chosen = i < overrides.Length && overrides[i] is { IsNull: false } ? overrides[i] : slots[i];
            paths[i] = chosen is { IsNull: false } ? chosen.ResolvedObject?.GetPathName() ?? string.Empty : string.Empty;
        }
        return paths;
    }

    private static void Instances(UnrealConversion conversion, IGameObject node, UInstancedStaticMeshComponent instanced)
    {
        FPackageIndex meshPointer = instanced.GetStaticMesh();
        if (meshPointer.IsNull)
        {
            return;
        }
        FInstancedStaticMeshInstanceData[] instances = instanced.GetInstances();
        ITransform parent = node.GetTransform();
        IMesh? mesh = conversion.Table.Find<IMesh>(meshPointer);
        List<IMaterial?> materials = meshPointer.Load() is UObject source ? Materials(conversion, source, instanced.OverrideMaterials) : new List<IMaterial?>();
        for (int i = 0; i < instances.Length; i++)
        {
            (Vector3 position, Quaternion rotation, Vector3 scale) = Transform(conversion, instances[i].TransformData);
            IGameObject instance = conversion.Hierarchy.Node($"{node.Name}_{i}", parent, position, rotation, scale);
            conversion.Hierarchy.StaticMesh(instance, mesh, materials);
        }
    }

    private static void Static(UnrealConversion conversion, IGameObject node, FPackageIndex meshPointer, FPackageIndex?[] overrides)
    {
        if (meshPointer.IsNull)
        {
            return;
        }
        List<IMaterial?> materials = meshPointer.Load() is UObject source ? Materials(conversion, source, overrides) : new List<IMaterial?>();
        conversion.Hierarchy.StaticMesh(node, conversion.Table.Find<IMesh>(meshPointer), materials);
    }

    private static void Skinned(UnrealConversion conversion, IGameObject node, FPackageIndex meshPointer, FPackageIndex?[] overrides)
    {
        if (meshPointer.IsNull)
        {
            return;
        }
        if (meshPointer.Load() is not USkeletalMesh source)
        {
            Logger.Warning(LogCategory.Import, $"[Unreal] {conversion.PackagePath} '{node.Name}': skeletal mesh {meshPointer} did not load; nothing rendered.");
            return;
        }
        UnrealRig rig = conversion.Shared.Rig(source, conversion.Basis);
        ITransform[] bones = rig.Build(conversion.Hierarchy, node);
        (Vector3 center, Vector3 extent) = Bounds(conversion, source.ImportedBounds.GetBox());
        conversion.Hierarchy.SkinnedMesh(node, conversion.Table.Find<IMesh>(meshPointer), bones, bones.Length > 0 ? bones[0] : null,
            Materials(conversion, source, overrides), center, extent);
    }

    private static void Light(UnrealConversion conversion, IGameObject node, ULightComponentBase light)
    {
        ILight unityLight = conversion.Hierarchy.Component<ILight>(node, ClassIDType.Light);
        unityLight.Enabled = 1;
        FLinearColor color = light.GetLightColor();
        unityLight.Color.SetValues(color.R, color.G, color.B, color.A);
        unityLight.Intensity = light.Intensity;
        float unitScale = conversion.Basis.UnitScale;
        switch (light)
        {
            case USpotLightComponent spot:
                unityLight.Type = SpotLight;
                unityLight.Range = spot.AttenuationRadius * unitScale;
                unityLight.SpotAngle = spot.OuterConeAngle * 2f;
                unityLight.InnerSpotAngle = spot.InnerConeAngle * 2f;
                break;
            case URectLightComponent rect:
                unityLight.Type = AreaLight;
                unityLight.Range = rect.AttenuationRadius * unitScale;
                unityLight.AreaSize.SetValues(rect.SourceWidth * unitScale, rect.SourceHeight * unitScale);
                break;
            case UPointLightComponent point:
                unityLight.Type = PointLight;
                unityLight.Range = point.AttenuationRadius * unitScale;
                break;
            case UDirectionalLightComponent:
                unityLight.Type = DirectionalLight;
                break;
        }
    }
}
