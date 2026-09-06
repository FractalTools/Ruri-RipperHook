using AssetRipper.Import.Logging;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Actor;
using CUE4Parse.UE4.Assets.Exports.Component;
using CUE4Parse.UE4.Objects.Engine;
using CUE4Parse.UE4.Objects.UObject;

namespace Ruri.FModelHook.Unreal.Converters;

/// <summary>
/// What a world places, read once for every consumer.
///
/// A level is a list of actors, each a small tree of scene components: the component the actor
/// calls its root, the components it was built with, and -- for a foliage actor -- the instanced
/// component of every foliage type it carries. Which of those count, what each is called, and
/// whether it shows are decisions about UNREAL, not about whatever is being built from them, so
/// they are made here: the Unity conversion builds GameObjects from this reading, and a host
/// reading the decoder's datasets builds its own objects from the same one.
/// </summary>
public static class UnrealSceneGraph
{
    /// <summary>One placed component: where it sits in the tree, what it is called, whether it shows.</summary>
    public readonly record struct Placed(USceneComponent Component, int Parent, string Name, bool Active);

    private const string HiddenName = "bHidden";
    private const string RootComponentName = "RootComponent";
    private static readonly string[] ComponentListNames = ["InstanceComponents", "BlueprintCreatedComponents"];

    /// <summary>Every actor of a world's persistent level; none, with a line saying so, when the level does not load.</summary>
    public static IEnumerable<UObject> Actors(UWorld world, string package)
    {
        if (world.PersistentLevel.Load<ULevel>() is not { } level)
        {
            Logger.Warning(LogCategory.Import, $"[Unreal] {package}: the persistent level did not load; no actors placed.");
            yield break;
        }
        foreach (FPackageIndex? pointer in level.Actors)
        {
            if (pointer?.Load() is { } actor)
            {
                yield return actor;
            }
        }
    }

    /// <summary>The component an actor calls its root, or null when it names none.</summary>
    public static USceneComponent? Root(UObject actor) =>
        actor.GetOrDefault<FPackageIndex?>(RootComponentName)?.Load<USceneComponent>();

    /// <summary>What the actor is called: the label it was given in the editor, else its own name.</summary>
    public static string Label(UObject actor) =>
        actor is AActor { ActorLabel: { Length: > 0 } label } ? label : actor.Name;

    /// <summary>Whether the actor itself is hidden, which hides the node its root component becomes.</summary>
    public static bool Hidden(UObject actor) => actor.GetOrDefault<bool>(HiddenName);

    /// <summary>
    /// The scene components of one actor: its root first, then the ones it was built with, then
    /// the instanced component of every foliage type a foliage actor carries.
    /// </summary>
    public static IEnumerable<USceneComponent> Components(UObject actor, USceneComponent? root)
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
                if (info.Implementation is FFoliageStaticMesh { Component: { IsNull: false } pointer }
                    && pointer.Load<USceneComponent>() is { } component)
                {
                    yield return component;
                }
            }
        }
    }

    /// <summary>
    /// One component's name and whether it shows: a component that IS the actor's root carries
    /// the actor's label and the actor's own hidden flag; every other carries its own name.
    /// </summary>
    public static (string Name, bool Active) Node(UObject actor, USceneComponent component, USceneComponent? root)
    {
        bool isRoot = ReferenceEquals(component, root);
        bool active = UnrealComponents.Visible(component) && !(isRoot && Hidden(actor));
        return (isRoot ? Label(actor) : component.Name, active);
    }

    /// <summary>
    /// Every scene component of every actor in a world, parents before children, each stating
    /// the index of the component it attaches to (-1 for one whose parent this world does not
    /// place). An actor that throws while being read is reported and skipped; the rest still
    /// place, because one broken actor is not a broken level.
    /// </summary>
    public static List<Placed> Ordered(UWorld world, string package)
    {
        List<USceneComponent> components = new();
        List<USceneComponent?> parents = new();
        List<string> names = new();
        List<bool> actives = new();
        Dictionary<USceneComponent, int> seen = new(ReferenceEqualityComparer.Instance);
        foreach (UObject actor in Actors(world, package))
        {
            try
            {
                USceneComponent? root = Root(actor);
                foreach (USceneComponent component in Components(actor, root))
                {
                    if (seen.ContainsKey(component))
                    {
                        continue;
                    }
                    (string name, bool active) = Node(actor, component, root);
                    seen[component] = components.Count;
                    components.Add(component);
                    parents.Add(component.AttachParent?.Load<USceneComponent>());
                    names.Add(name);
                    actives.Add(active);
                }
            }
            catch (Exception exception)
            {
                Logger.Warning(LogCategory.Import, $"[Unreal] {package} actor '{actor.Name}': {exception.GetType().Name}: {exception.Message}");
            }
        }

        List<Placed> ordered = new(components.Count);
        int[] placedAt = new int[components.Count];
        Array.Fill(placedAt, -1);
        for (int index = 0; index < components.Count; index++)
        {
            Place(index, components, parents, names, actives, seen, placedAt, ordered, 0);
        }
        return ordered;
    }

    private static int Place(int index, List<USceneComponent> components, List<USceneComponent?> parents,
        List<string> names, List<bool> actives, Dictionary<USceneComponent, int> seen, int[] placedAt,
        List<Placed> ordered, int depth)
    {
        if (placedAt[index] >= 0)
        {
            return placedAt[index];
        }
        int parent = -1;
        if (parents[index] is { } parentComponent && seen.TryGetValue(parentComponent, out int parentIndex)
            && parentIndex != index && depth < components.Count)
        {
            parent = Place(parentIndex, components, parents, names, actives, seen, placedAt, ordered, depth + 1);
        }
        placedAt[index] = ordered.Count;
        ordered.Add(new Placed(components[index], parent, names[index], actives[index]));
        return placedAt[index];
    }
}
