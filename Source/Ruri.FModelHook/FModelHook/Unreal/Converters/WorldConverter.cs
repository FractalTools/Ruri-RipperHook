using AssetRipper.Import.Logging;
using AssetRipper.SourceGenerated;
using AssetRipper.SourceGenerated.Classes.ClassID_104;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Actor;
using CUE4Parse.UE4.Assets.Exports.Component;
using CUE4Parse.UE4.Objects.Engine;
using CUE4Parse.UE4.Objects.UObject;

namespace Ruri.FModelHook.Unreal.Converters;

/// <summary>
/// A world as a Unity scene: every scene component of the persistent level's actors becomes a
/// node under the component it attaches to -- across actors too, an actor attached to another
/// hangs under that one's component -- named after the actor where it is the actor's root, and
/// rendering what the component renders. The collection is marked as a scene the way Unity
/// marks one, by carrying a render-settings manager, so AssetRipper files it under the world's
/// path.
/// </summary>
public sealed class WorldConverter : IUnrealConverter
{
    private const string HiddenName = "bHidden";
    private const string RootComponentName = "RootComponent";
    private static readonly string[] ComponentListNames = ["InstanceComponents", "BlueprintCreatedComponents"];

    public IReadOnlyList<string> ClassNames { get; } = ["World"];

    public IReadOnlyList<ClassIDType> Produces { get; } =
        [ClassIDType.GameObject, ClassIDType.Transform, ClassIDType.MeshRenderer, ClassIDType.SkinnedMeshRenderer, ClassIDType.Light, ClassIDType.RenderSettings];

    public void Allocate(UnrealConversion conversion, ResolvedObject header)
    {
        IRenderSettings settings = conversion.Package.Create<IRenderSettings>(ClassIDType.RenderSettings, header.Name.Text, null);
        conversion.Register(header, settings);
    }

    public void Fill(UnrealConversion conversion, UObject export)
    {
        if (export is not UWorld world)
        {
            return;
        }
        if (world.PersistentLevel.Load<ULevel>() is not { } level)
        {
            Logger.Warning(LogCategory.Import, $"[Unreal] {conversion.PackagePath}: the persistent level did not load; no actors placed.");
            return;
        }
        UnrealComponentTree tree = new(conversion);
        int actors = 0;
        foreach (FPackageIndex? pointer in level.Actors)
        {
            if (pointer?.Load() is not UObject actor)
            {
                continue;
            }
            try
            {
                Actor(tree, actor);
                actors++;
            }
            catch (Exception exception)
            {
                Logger.Warning(LogCategory.Import, $"[Unreal] {conversion.PackagePath} actor '{actor.Name}': {exception.GetType().Name}: {exception.Message}");
            }
        }
        tree.Build(null);
        if (world.StreamingLevels.Length > 0)
        {
            Logger.Info(LogCategory.Import, $"[Unreal] {conversion.PackagePath}: {world.StreamingLevels.Length} streaming level(s) are separate worlds; load them as their own packages.");
        }
        Logger.Info(LogCategory.Import, $"[Unreal] {conversion.PackagePath}: {actors} actor(s), {tree.Count} component(s) placed.");
    }

    /// <summary>
    /// One actor's scene components into the tree: its root by the actor's label, hidden when
    /// the actor is; the rest by their own names, each under the component it attaches to.
    /// </summary>
    private static void Actor(UnrealComponentTree tree, UObject actor)
    {
        bool hidden = actor.GetOrDefault<bool>(HiddenName);
        string label = actor is AActor { ActorLabel: { Length: > 0 } actorLabel } ? actorLabel : actor.Name;
        USceneComponent? root = actor.GetOrDefault<FPackageIndex?>(RootComponentName)?.Load<USceneComponent>();
        foreach (USceneComponent component in Components(actor, root))
        {
            bool isRoot = ReferenceEquals(component, root);
            bool active = UnrealComponents.Visible(component) && !(isRoot && hidden);
            tree.Add(component, component.AttachParent?.Load<USceneComponent>(), isRoot ? label : component.Name, active);
        }
    }

    private static IEnumerable<USceneComponent> Components(UObject actor, USceneComponent? root)
    {
        if (root is not null)
        {
            yield return root;
        }
        foreach (string listName in ComponentListNames)
        {
            foreach (FPackageIndex? pointer in actor.GetOrDefault<FPackageIndex?[]>(listName, []))
            {
                if (pointer?.Load<USceneComponent>() is { } component)
                {
                    yield return component;
                }
            }
        }
        if (actor is AInstancedFoliageActor { FoliageInfos: { } foliage })
        {
            foreach (FFoliageInfo info in foliage.Values)
            {
                if (info.Implementation is FFoliageStaticMesh { Component: { IsNull: false } pointer } && pointer.Load<USceneComponent>() is { } component)
                {
                    yield return component;
                }
            }
        }
    }
}
