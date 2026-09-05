using AssetRipper.Import.Structure.Assembly.Serializable;
using AssetRipper.SourceGenerated;
using AssetRipper.SourceGenerated.Classes.ClassID_114;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using Ruri.FModelHook.Ripper.TypeTree;
using Ruri.RipperHook.Conversion;
using Ruri.RipperHook.Core.TypeTree;

namespace Ruri.FModelHook.Ripper.Converters;

/// <summary>
/// Every export no other converter claims: a reflected object whose shape is its class's. It
/// becomes a MonoBehaviour bound to a MonoScript named after the class, its fields the class's
/// properties, so data assets, settings and component records all arrive as Unity data rather
/// than vanishing. A class compiled into the game has its shape in the reflection schema; a
/// Blueprint class is content the schema never saw, and the object's own property tags state
/// its shape. Without either the object still exists -- named, typed, empty -- which is exactly
/// what is known about it.
/// </summary>
public sealed class PropertyBagConverter : IUnrealConverter
{
    public IReadOnlyList<string> ClassNames { get; } = [];

    public IReadOnlyList<ClassIDType> Produces { get; } = [ClassIDType.MonoBehaviour];

    public void Allocate(UnrealConversion conversion, ResolvedObject header)
    {
        IMonoBehaviour behaviour = conversion.Package.Create<IMonoBehaviour>(ClassIDType.MonoBehaviour, header.Name.Text, conversion.UnityPath(header));
        behaviour.ScriptP = conversion.Shared.Script(UnrealConversion.ClassOf(header));
        behaviour.Enabled = 1;
        conversion.Register(header, behaviour);
    }

    public void Fill(UnrealConversion conversion, UObject export)
    {
        if (conversion.Table.Find<IMonoBehaviour>(export) is not { } behaviour)
        {
            return;
        }
        SerializableStructure? structure = Bag(export.ExportType, UnrealTypeTree.IsNative(export.Class), export.Properties, conversion, behaviour.Collection);
        if (structure is null)
        {
            return;
        }
        behaviour.Structure = structure;
    }

    /// <summary>
    /// The filled structure of one object: through the reflection schema when its class is
    /// compiled in, through the tags it carries when its class is content.
    /// </summary>
    public static SerializableStructure? Bag(string className, bool native, IReadOnlyList<CUE4Parse.UE4.Assets.Objects.FPropertyTag> properties,
        UnrealConversion conversion, AssetRipper.Assets.Collections.AssetCollection collection)
    {
        SerializableStructure structure;
        if (native)
        {
            if (UnrealTypeTree.RootFor(className) is not { } root)
            {
                return null;
            }
            structure = PropertyBagBuilder.Structure(root, conversion.Space.Version);
        }
        else
        {
            structure = PropertyBagBuilder.Structure(TagSchema.Root(className, properties), conversion.Space.Version, shared: false);
        }
        new UnrealValueWriter(conversion.Table).WriteProperties(new StructureWriter(structure, collection, conversion.Space.Version), properties);
        return structure;
    }
}
