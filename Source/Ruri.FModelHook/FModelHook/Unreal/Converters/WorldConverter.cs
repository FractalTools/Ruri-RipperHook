using AssetRipper.Import.Logging;
using AssetRipper.SourceGenerated;
using AssetRipper.SourceGenerated.Classes.ClassID_104;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Component;
using CUE4Parse.UE4.Objects.Engine;

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
        UnrealComponentTree tree = new(conversion);
        int actors = 0;
        foreach (UObject actor in UnrealSceneGraph.Actors(world, conversion.PackagePath))
        {
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

    /// <summary>One actor's scene components into the tree, as UnrealSceneGraph reads them.</summary>
    private static void Actor(UnrealComponentTree tree, UObject actor)
    {
        USceneComponent? root = UnrealSceneGraph.Root(actor);
        foreach (USceneComponent component in UnrealSceneGraph.Components(actor, root))
        {
            (string name, bool active) = UnrealSceneGraph.Node(actor, component, root);
            tree.Add(component, component.AttachParent?.Load<USceneComponent>(), name, active);
        }
    }
}
