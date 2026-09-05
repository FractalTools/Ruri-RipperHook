using AssetRipper.Import.Logging;
using AssetRipper.Import.Structure.Assembly.Serializable;
using AssetRipper.SourceGenerated;
using AssetRipper.SourceGenerated.Classes.ClassID_114;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Engine;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.UObject;
using Ruri.RipperHook.Conversion;
using Ruri.RipperHook.Core.TypeTree;

namespace Ruri.FModelHook.Ripper.Converters;

/// <summary>
/// A data table's rows are its content, and they are not properties of the table -- Unreal
/// serializes them natively, keyed by name, each one an instance of the table's row struct.
/// That struct is a class of the schema like any other, so every row arrives as its own
/// MonoBehaviour of that class, named by its row name, filed under the table; the table itself
/// stays the bag of its own properties (its row struct, its import settings).
/// </summary>
public sealed class DataTableConverter : IUnrealConverter
{
    private const char RowSeparator = '/';

    public IReadOnlyList<string> ClassNames { get; } = ["DataTable"];

    public IReadOnlyList<ClassIDType> Produces { get; } = [ClassIDType.MonoBehaviour];

    public bool Handles(UObject export) => export is UDataTable;

    public void Allocate(UnrealConversion conversion, UObject export)
    {
        if (export is not UDataTable table)
        {
            return;
        }
        IMonoBehaviour bag = conversion.Package.Create<IMonoBehaviour>(ClassIDType.MonoBehaviour, export.Name, conversion.UnityPath(export));
        bag.ScriptP = conversion.Shared.Script(export.ExportType);
        bag.Enabled = 1;
        conversion.Register(export, bag);
        if (table.RowStructName is not { Length: > 0 } rowStruct)
        {
            Logger.Warning(LogCategory.Import, $"[Unreal] {conversion.PackagePath}:{export.Name} names no row struct; its {table.RowMap.Count} rows stay unconverted.");
            return;
        }
        foreach (FName rowName in table.RowMap.Keys)
        {
            string name = rowName.Text;
            IMonoBehaviour row = conversion.Package.Create<IMonoBehaviour>(ClassIDType.MonoBehaviour, name, conversion.UnityPath(export, RowSeparator + FileSafe(name)));
            row.ScriptP = conversion.Shared.Script(rowStruct);
            row.Enabled = 1;
            conversion.Register(export, row, name);
        }
    }

    public void Fill(UnrealConversion conversion, UObject export)
    {
        if (export is not UDataTable table)
        {
            return;
        }
        if (conversion.Table.Find<IMonoBehaviour>(export) is { } bag
            && PropertyBagConverter.Bag(export.ExportType, UnrealTypeTree.IsNative(export.Class), export.Properties, conversion, bag.Collection) is { } tableStructure)
        {
            bag.Structure = tableStructure;
        }
        if (table.RowStructName is not { Length: > 0 } rowStruct)
        {
            return;
        }
        bool nativeRows = UnrealTypeTree.IsNative(table.GetOrDefault<FPackageIndex?>("RowStruct")?.ResolvedObject);
        foreach ((FName rowName, FStructFallback rowValue) in table.RowMap)
        {
            if (conversion.Table.Find<IMonoBehaviour>(export, rowName.Text) is not { } row)
            {
                continue;
            }
            if (PropertyBagConverter.Bag(rowStruct, nativeRows, rowValue.Properties, conversion, row.Collection) is { } structure)
            {
                row.Structure = structure;
            }
        }
    }

    private static string FileSafe(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return name.IndexOfAny(invalid) < 0 ? name : string.Concat(name.Select(character => Array.IndexOf(invalid, character) < 0 ? character : '_'));
    }
}
