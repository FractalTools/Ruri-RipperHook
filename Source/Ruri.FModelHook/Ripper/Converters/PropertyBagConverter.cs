using AssetRipper.Import.Structure.Assembly.Serializable;
using AssetRipper.SourceGenerated;
using AssetRipper.SourceGenerated.Classes.ClassID_114;
using CUE4Parse.UE4.Assets.Exports;
using Ruri.RipperHook.Conversion;
using Ruri.RipperHook.Core.TypeTree;

namespace Ruri.FModelHook.Ripper.Converters;

/// <summary>
/// Every export no other converter claims: a reflected object whose shape is its class's
/// schema. It becomes a MonoBehaviour bound to a MonoScript named after the class, its fields
/// the class's properties, so data assets, settings and component records all arrive as Unity
/// data rather than vanishing. Without a reflection schema the object still exists -- named,
/// typed, empty -- which is exactly what is known about it.
/// </summary>
public sealed class PropertyBagConverter : IUnrealConverter
{
    public IReadOnlyList<string> ClassNames { get; } = [];

    public IReadOnlyList<ClassIDType> Produces { get; } = [ClassIDType.MonoBehaviour];

    public bool Handles(UObject export) => true;

    public void Allocate(UnrealConversion conversion, UObject export)
    {
        IMonoBehaviour behaviour = conversion.Package.Create<IMonoBehaviour>(ClassIDType.MonoBehaviour, export.Name, conversion.UnityPath(export));
        behaviour.ScriptP = conversion.Shared.Script(export.ExportType);
        behaviour.Enabled = 1;
        conversion.Register(export, behaviour);
    }

    public void Fill(UnrealConversion conversion, UObject export)
    {
        if (conversion.Table.Find<IMonoBehaviour>(export) is not { } behaviour)
        {
            return;
        }
        TypeTreeNode? root = UnrealTypeTree.RootFor(export.ExportType);
        if (root is null)
        {
            return;
        }
        SerializableStructure structure = PropertyBagBuilder.Structure(root, conversion.Space.Version);
        StructureWriter writer = new(structure, behaviour.Collection, conversion.Space.Version);
        new UnrealValueWriter(conversion.Table).WriteProperties(writer, export.Properties);
        behaviour.Structure = structure;
    }
}
