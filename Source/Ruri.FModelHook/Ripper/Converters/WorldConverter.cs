using AssetRipper.Import.Logging;
using AssetRipper.SourceGenerated;
using AssetRipper.SourceGenerated.Classes.ClassID_1;
using AssetRipper.SourceGenerated.Classes.ClassID_104;
using AssetRipper.SourceGenerated.Classes.ClassID_108;
using AssetRipper.SourceGenerated.Classes.ClassID_21;
using AssetRipper.SourceGenerated.Classes.ClassID_4;
using AssetRipper.SourceGenerated.Classes.ClassID_43;
using AssetRipper.SourceGenerated.Extensions;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.SkeletalMesh;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.Engine;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse_Conversion.Dto;
using Ruri.RipperHook.Conversion;
using System.Numerics;

namespace Ruri.FModelHook.Ripper.Converters;

/// <summary>
/// A world as a Unity scene: the persistent level's actors become root GameObjects, each
/// component tree the actor's child hierarchy, mesh components render the converted meshes
/// with the material slots the component overrides, instanced meshes one node per instance,
/// lights Unity lights with the colour and intensity the component states. The collection is
/// marked as a scene the way Unity marks one -- by carrying a level manager -- so AssetRipper's
/// own scene processing files it under the world's path.
/// </summary>
public sealed class WorldConverter : IUnrealConverter
{
    private const int SpotLight = 0;
    private const int DirectionalLight = 1;
    private const int PointLight = 2;
    private const int AreaLight = 3;

    public IReadOnlyList<string> ClassNames { get; } = ["World"];

    public IReadOnlyList<ClassIDType> Produces { get; } =
        [ClassIDType.GameObject, ClassIDType.Transform, ClassIDType.MeshRenderer, ClassIDType.Light, ClassIDType.RenderSettings];

    public bool Handles(UObject export) => export is UWorld;

    public void Allocate(UnrealConversion conversion, UObject export)
    {
        IRenderSettings settings = conversion.Package.Create<IRenderSettings>(ClassIDType.RenderSettings, export.Name, null);
        conversion.Register(export, settings);
    }

    public void Fill(UnrealConversion conversion, UObject export)
    {
        if (export is not UWorld world)
        {
            return;
        }
        WorldDto dto = new(world);
        int actors = 0;
        foreach (ActorDto actor in dto.Actors)
        {
            try
            {
                Actor(conversion, actor, null);
                actors++;
            }
            catch (Exception exception)
            {
                Logger.Warning(LogCategory.Import, $"[Unreal] {conversion.PackagePath} actor '{actor.Name}': {exception.GetType().Name}: {exception.Message}");
            }
        }
        if (dto.StreamingLevels.Count > 0)
        {
            Logger.Info(LogCategory.Import, $"[Unreal] {conversion.PackagePath}: {dto.StreamingLevels.Count} streaming level(s) are separate worlds; load them as their own packages.");
        }
        Logger.Info(LogCategory.Import, $"[Unreal] {conversion.PackagePath}: {actors} actor(s) placed.");
        dto.Dispose();
    }

    private void Actor(UnrealConversion conversion, ActorDto actor, ITransform? parent)
    {
        SceneComponentDto? root = actor.RootComponent;
        if (root is null)
        {
            conversion.Hierarchy.Node(actor.Name, parent, Vector3.Zero, Quaternion.Identity, Vector3.One);
            return;
        }
        Component(conversion, root, parent, actor.Name, actor.IsVisible);
    }

    private void Component(UnrealConversion conversion, SceneComponentDto component, ITransform? parent, string? nameOverride, bool visible)
    {
        (Vector3 position, Quaternion rotation, Vector3 scale) = Transform(conversion, component.Transform);
        IGameObject node = conversion.Hierarchy.Node(nameOverride ?? component.Name, parent, position, rotation, scale);
        if (!visible)
        {
            node.SetIsActive(false);
        }
        ITransform transform = node.GetTransform();

        switch (component)
        {
            case InstancedStaticMeshComponentDto instanced:
                Instances(conversion, instanced, transform);
                break;
            case StaticMeshComponentDto staticMesh:
                Renderer(conversion, node, staticMesh.MeshPtr, staticMesh.OverrideMaterials);
                break;
            case SkeletalMeshComponentDto skeletalMesh:
                Renderer(conversion, node, skeletalMesh.MeshPtr, skeletalMesh.OverrideMaterials);
                break;
            case LightComponentBaseDto light:
                Light(conversion, node, light);
                break;
        }

        foreach (SceneComponentDto child in component.Children)
        {
            Component(conversion, child, transform, null, visible);
        }
        foreach (ActorDto attached in component.AttachedActors)
        {
            Actor(conversion, attached, transform);
        }
    }

    private static void Instances(UnrealConversion conversion, InstancedStaticMeshComponentDto instanced, ITransform parent)
    {
        for (int i = 0; i < instanced.Transforms.Length; i++)
        {
            (Vector3 position, Quaternion rotation, Vector3 scale) = Transform(conversion, instanced.Transforms[i]);
            IGameObject node = conversion.Hierarchy.Node($"{instanced.Name}_{i}", parent, position, rotation, scale);
            Renderer(conversion, node, instanced.MeshPtr, instanced.OverrideMaterials);
        }
    }

    private static void Renderer(UnrealConversion conversion, IGameObject node, FPackageIndex meshPointer, FPackageIndex?[] overrides)
    {
        IMesh? mesh = conversion.Table.Find<IMesh>(meshPointer);
        List<IMaterial?> materials = new();
        if (meshPointer.ResolvedObject?.Load() is UObject meshObject)
        {
            FPackageIndex?[] slots = meshObject switch
            {
                UStaticMesh staticMesh => staticMesh.StaticMaterials.Select(static slot => slot.MaterialInterface).ToArray(),
                USkeletalMesh skeletalMesh => skeletalMesh.SkeletalMaterials.Select(static slot => slot.Material).ToArray(),
                _ => [],
            };
            for (int i = 0; i < slots.Length; i++)
            {
                FPackageIndex? chosen = i < overrides.Length && overrides[i] is { IsNull: false } ? overrides[i] : slots[i];
                materials.Add(chosen is null ? null : conversion.Table.Find<IMaterial>(chosen));
            }
        }
        conversion.Hierarchy.StaticMesh(node, mesh, materials);
    }

    private static void Light(UnrealConversion conversion, IGameObject node, LightComponentBaseDto light)
    {
        ILight unityLight = conversion.Hierarchy.Component<ILight>(node, ClassIDType.Light);
        unityLight.Enabled = 1;
        FLinearColor color = light.Color;
        unityLight.Color.SetValues(color.R, color.G, color.B, color.A);
        unityLight.Intensity = light.Intensity;
        switch (light)
        {
            case SpotLightComponentDto spot:
                unityLight.Type = SpotLight;
                unityLight.Range = spot.AttenuationRadius * conversion.Basis.UnitScale;
                unityLight.SpotAngle = spot.OuterConeAngle * 2f;
                unityLight.InnerSpotAngle = spot.InnerConeAngle * 2f;
                break;
            case PointLightComponentDto point:
                unityLight.Type = PointLight;
                unityLight.Range = point.AttenuationRadius * conversion.Basis.UnitScale;
                break;
            case RectLightComponentDto rect:
                unityLight.Type = AreaLight;
                unityLight.Range = rect.AttenuationRadius * conversion.Basis.UnitScale;
                unityLight.AreaSize.SetValues(rect.SourceWidth * conversion.Basis.UnitScale, rect.SourceHeight * conversion.Basis.UnitScale);
                break;
            case DirectionalLightComponentDto:
                unityLight.Type = DirectionalLight;
                break;
        }
    }

    private static (Vector3, Quaternion, Vector3) Transform(UnrealConversion conversion, FTransform transform)
    {
        Vector3 position = conversion.Basis.Position(transform.Translation.X, transform.Translation.Y, transform.Translation.Z);
        Quaternion rotation = conversion.Basis.Rotation(transform.Rotation.X, transform.Rotation.Y, transform.Rotation.Z, transform.Rotation.W);
        Vector3 scale = conversion.Basis.Scale(transform.Scale3D.X, transform.Scale3D.Y, transform.Scale3D.Z);
        return (position, rotation, scale);
    }
}
