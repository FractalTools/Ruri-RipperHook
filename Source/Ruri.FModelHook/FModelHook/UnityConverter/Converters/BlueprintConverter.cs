using AssetRipper.Import.Logging;
using AssetRipper.SourceGenerated;
using AssetRipper.SourceGenerated.Classes.ClassID_1;
using AssetRipper.SourceGenerated.Extensions;
using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Component;
using CUE4Parse.UE4.Assets.Exports.Engine;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.Engine;
using CUE4Parse.UE4.Objects.UObject;
using System.Numerics;

namespace Ruri.FModelHook.UnityConverter.Converters;

/// <summary>
/// A Blueprint class as a prefab: the actor its construction script builds. The class chain
/// from the native actor down contributes, root-most first, the components its script states;
/// the leaf's default object contributes the native components with the values the Blueprint
/// set on them; the leaf's inherited-component records replace the templates it overrides in an
/// ancestor's script. Each scene component is a node under the component it attaches to -- a
/// native component by property name, a script variable by name, else the actor's root -- and
/// renders exactly as it does when the actor stands in a level. A generated class that is not
/// an actor (an animation or widget Blueprint), or one reached only as a dependency, is data
/// and lands as data.
/// </summary>
public sealed class BlueprintConverter : IUnrealConverter
{
    private const string ActorClassName = "Actor";
    private const string RootComponentName = "RootComponent";
    private const string ParentVariableName = "ParentComponentOrVariableName";

    private readonly PropertyBagConverter data = new();

    public IReadOnlyList<string> ClassNames { get; } = ["BlueprintGeneratedClass"];

    public IReadOnlyList<ClassIDType> Produces { get; } =
        [ClassIDType.GameObject, ClassIDType.Transform, ClassIDType.MeshRenderer, ClassIDType.SkinnedMeshRenderer, ClassIDType.Light, ClassIDType.MonoBehaviour];

    public void Allocate(UnrealConversion conversion, ResolvedObject header)
    {
        if (!conversion.IsSeed || !IsActorClass(conversion, header))
        {
            data.Allocate(conversion, header);
            return;
        }
        string stem = UnrealPaths.UnityStem(conversion.PackagePath);
        IGameObject root = conversion.Hierarchy.Node(Path.GetFileName(stem), null, Vector3.Zero, Quaternion.Identity, Vector3.One, stem);
        conversion.Register(header, root);
    }

    public void Fill(UnrealConversion conversion, UObject export)
    {
        if (export is not UBlueprintGeneratedClass leaf || conversion.Table.Find<IGameObject>(export) is not { } root)
        {
            data.Fill(conversion, export);
            return;
        }
        List<UBlueprintGeneratedClass> chain = Chain(leaf);
        UObject? defaults = leaf.ClassDefaultObject.Load() as UObject;
        UnrealComponentTree tree = new(conversion);
        Dictionary<string, USceneComponent> byName = new(StringComparer.Ordinal);
        USceneComponent? actorRoot = Natives(tree, defaults, byName);
        Dictionary<(string OwnerClass, string Variable), USceneComponent> overrides = Overrides(chain);
        foreach (UBlueprintGeneratedClass owner in chain)
        {
            if (owner.SimpleConstructionScript?.Load<USimpleConstructionScript>() is not { } script)
            {
                continue;
            }
            foreach (FPackageIndex? pointer in script.RootNodes)
            {
                if (pointer?.Load<USCS_Node>() is { } node)
                {
                    actorRoot = Script(tree, owner, node, ParentOf(node, byName) ?? actorRoot, actorRoot, byName, overrides);
                }
            }
        }
        tree.Build(root.GetTransform());
        Logger.Info(LogCategory.Import, $"[Unreal] {conversion.PackagePath}: Blueprint '{root.Name}' placed {tree.Count} component(s) from {chain.Count} class(es).");
    }

    /// <summary>Whether the class descends from the native actor class, following super classes across packages to the first native one.</summary>
    private static bool IsActorClass(UnrealConversion conversion, ResolvedObject header)
    {
        TypeMappings? mappings = conversion.Shared.Provider.MappingsForGame;
        return mappings is not null
            && UnrealActorScan.NativeAncestor(header.Super, mappings) is { } native
            && UnrealConverters.IsA(native, ActorClassName, mappings);
    }

    /// <summary>The Blueprint classes from the root-most down to the leaf.</summary>
    private static List<UBlueprintGeneratedClass> Chain(UBlueprintGeneratedClass leaf)
    {
        List<UBlueprintGeneratedClass> chain = new();
        HashSet<string> seen = new(StringComparer.Ordinal);
        UBlueprintGeneratedClass? cursor = leaf;
        while (cursor is not null && seen.Add(cursor.GetPathName()))
        {
            chain.Add(cursor);
            cursor = cursor.SuperStruct.Load<UBlueprintGeneratedClass>();
        }
        chain.Reverse();
        return chain;
    }

    /// <summary>
    /// The default object's own scene components: those its properties name, keyed by the
    /// property, plus any other scene subobject it owns. Returns the root component it states.
    /// </summary>
    private static USceneComponent? Natives(UnrealComponentTree tree, UObject? defaults, Dictionary<string, USceneComponent> byName)
    {
        if (defaults is null)
        {
            return null;
        }
        string owner = defaults.GetPathName();
        List<(USceneComponent Component, string Name)> natives = new();
        USceneComponent? root = null;
        foreach (FPropertyTag tag in defaults.Properties)
        {
            if (tag.Tag?.GenericValue is not FPackageIndex pointer || pointer.Load() is not USceneComponent component || !IsOwnedBy(component, owner))
            {
                continue;
            }
            string name = tag.Name.Text;
            byName[name] = component;
            if (string.Equals(name, RootComponentName, StringComparison.Ordinal))
            {
                root = component;
            }
            else
            {
                natives.Add((component, name));
            }
        }
        if (defaults.Owner is { } package)
        {
            foreach (UObject export in package.GetExports())
            {
                if (export is USceneComponent component && IsOwnedBy(component, owner) && !byName.ContainsValue(component))
                {
                    byName[component.Name] = component;
                    natives.Add((component, component.Name));
                }
            }
        }
        if (root is not null)
        {
            tree.Add(root, null, root.Name, UnrealComponents.Visible(root));
        }
        foreach ((USceneComponent component, string name) in natives)
        {
            if (!tree.Contains(component))
            {
                tree.Add(component, component.AttachParent?.Load<USceneComponent>() ?? root, name, UnrealComponents.Visible(component));
            }
        }
        return root;
    }

    private static bool IsOwnedBy(UObject component, string ownerPath) =>
        string.Equals(component.Outer?.GetPathName(), ownerPath, StringComparison.Ordinal);

    /// <summary>The templates the leaf-ward classes substitute for ancestor script nodes, the nearest class to the leaf winning.</summary>
    private static Dictionary<(string OwnerClass, string Variable), USceneComponent> Overrides(IReadOnlyList<UBlueprintGeneratedClass> chain)
    {
        Dictionary<(string OwnerClass, string Variable), USceneComponent> overrides = new();
        foreach (UBlueprintGeneratedClass owner in chain)
        {
            if (owner.InheritableComponentHandler?.Load<UInheritableComponentHandler>() is not { } handler)
            {
                continue;
            }
            foreach (FComponentOverrideRecord record in handler.Records)
            {
                if (record.ComponentKey.OwnerClass is { } ownerClass && record.ComponentTemplate?.Load<USceneComponent>() is { } template)
                {
                    overrides[(ownerClass.GetPathName(), record.ComponentKey.SCSVariableName.Text)] = template;
                }
            }
        }
        return overrides;
    }

    /// <summary>The component a script node states it attaches to, by native property name or script variable.</summary>
    private static USceneComponent? ParentOf(USCS_Node node, Dictionary<string, USceneComponent> byName)
    {
        FName parentName = node.GetOrDefault<FName>(ParentVariableName);
        return !parentName.IsNone && byName.TryGetValue(parentName.Text, out USceneComponent? parent) ? parent : null;
    }

    /// <summary>
    /// One script node and its children into the tree. A node without a scene template still
    /// passes its children on to its own parent. Returns the actor root, which the first scene
    /// component becomes when the default object states none.
    /// </summary>
    private static USceneComponent? Script(UnrealComponentTree tree, UBlueprintGeneratedClass owner, USCS_Node node, USceneComponent? parent,
        USceneComponent? actorRoot, Dictionary<string, USceneComponent> byName, Dictionary<(string OwnerClass, string Variable), USceneComponent> overrides)
    {
        string variable = node.InternalVariableName.Text;
        if (!overrides.TryGetValue((owner.GetPathName(), variable), out USceneComponent? template))
        {
            template = node.ComponentTemplate?.Load<USceneComponent>();
        }
        USceneComponent? childParent = parent;
        if (template is not null)
        {
            byName[variable] = template;
            actorRoot ??= template;
            tree.Add(template, ReferenceEquals(parent, template) ? null : parent, variable, UnrealComponents.Visible(template));
            childParent = template;
        }
        foreach (FPackageIndex? pointer in node.ChildNodes)
        {
            if (pointer?.Load<USCS_Node>() is { } child)
            {
                actorRoot = Script(tree, owner, child, childParent, actorRoot, byName, overrides);
            }
        }
        return actorRoot;
    }
}
