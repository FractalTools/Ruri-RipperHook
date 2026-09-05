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
using System.Numerics;

namespace Ruri.FModelHook.Ripper.Converters;

/// <summary>
/// Scene components placed as a GameObject hierarchy: a component is a node carrying its
/// relative transform under the node of the component it attaches to, and what it renders --
/// a static mesh, a skinned mesh with its rig, one node per instance, a light -- hangs off that
/// node. A level's actors and a Blueprint's construction script both describe such trees; this
/// is the one place either becomes Unity objects.
/// </summary>
public sealed class UnrealComponentTree
{
    private readonly record struct Entry(USceneComponent Component, USceneComponent? Parent, string Name, bool Active);

    private readonly UnrealConversion conversion;
    private readonly List<Entry> entries = new();
    private readonly Dictionary<USceneComponent, int> index = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<USceneComponent, IGameObject> nodes = new(ReferenceEqualityComparer.Instance);

    public UnrealComponentTree(UnrealConversion conversion)
    {
        this.conversion = conversion ?? throw new ArgumentNullException(nameof(conversion));
    }

    public int Count => entries.Count;

    /// <summary>State one component; a parent the tree never receives leaves the component at the tree's root.</summary>
    public void Add(USceneComponent component, USceneComponent? parent, string name, bool active)
    {
        if (index.ContainsKey(component))
        {
            return;
        }
        index[component] = entries.Count;
        entries.Add(new Entry(component, parent, name, active));
    }

    public bool Contains(USceneComponent component) => index.ContainsKey(component);

    /// <summary>Every node, parents before children, the roots under <paramref name="root"/> -- a scene's top level when null.</summary>
    public IReadOnlyDictionary<USceneComponent, IGameObject> Build(ITransform? root)
    {
        foreach (Entry entry in entries)
        {
            Ensure(entry, root, 0);
        }
        return nodes;
    }

    private IGameObject Ensure(Entry entry, ITransform? root, int depth)
    {
        if (nodes.TryGetValue(entry.Component, out IGameObject? existing))
        {
            return existing;
        }
        ITransform? parent = root;
        if (entry.Parent is { } parentComponent && index.TryGetValue(parentComponent, out int parentIndex) && depth < entries.Count)
        {
            parent = Ensure(entries[parentIndex], root, depth + 1).GetTransform();
        }
        (Vector3 position, Quaternion rotation, Vector3 scale) = UnrealComponents.Transform(conversion, entry.Component.GetRelativeTransform());
        IGameObject node = conversion.Hierarchy.Node(entry.Name, parent, position, rotation, scale);
        if (!entry.Active)
        {
            node.SetIsActive(false);
        }
        nodes[entry.Component] = node;
        UnrealComponents.Render(conversion, node, entry.Component);
        return node;
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

    public static (Vector3, Quaternion, Vector3) Transform(UnrealConversion conversion, FTransform transform)
    {
        Vector3 position = conversion.Basis.Position(transform.Translation.X, transform.Translation.Y, transform.Translation.Z);
        Quaternion rotation = conversion.Basis.Rotation(transform.Rotation.X, transform.Rotation.Y, transform.Rotation.Z, transform.Rotation.W);
        Vector3 scale = conversion.Basis.Scale(transform.Scale3D.X, transform.Scale3D.Y, transform.Scale3D.Z);
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

    /// <summary>The material per slot: the component's override where it states one, else the mesh's own.</summary>
    public static List<IMaterial?> Materials(UnrealConversion conversion, UObject mesh, FPackageIndex?[] overrides)
    {
        FPackageIndex?[] slots = mesh switch
        {
            UStaticMesh staticMesh => staticMesh.StaticMaterials.Select(static slot => slot.MaterialInterface).ToArray(),
            USkeletalMesh skeletalMesh => skeletalMesh.SkeletalMaterials.Select(static slot => slot.Material).ToArray(),
            _ => [],
        };
        List<IMaterial?> materials = new(slots.Length);
        for (int i = 0; i < slots.Length; i++)
        {
            FPackageIndex? chosen = i < overrides.Length && overrides[i] is { IsNull: false } ? overrides[i] : slots[i];
            materials.Add(chosen is null ? null : conversion.Table.Find<IMaterial>(chosen));
        }
        return materials;
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
