using AssetRipper.Assets;
using AssetRipper.Import.Logging;
using AssetRipper.SerializationLogic;
using CUE4Parse.UE4;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Objects.Core.i18N;
using CUE4Parse.UE4.Objects.UObject;
using Ruri.FModelHook.UnityConverter.TypeTree;
using Ruri.RipperHook.Conversion;
using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;

namespace Ruri.FModelHook.UnityConverter.Converters;

/// <summary>
/// Writes Unreal property values into a Unity structure whose shape came from the same
/// reflection schema: the tag's name finds the field, the tag's value kind decides how it is
/// stated. Native structs CUE4Parse reads into its own types (a vector, a colour, a transform)
/// are written member by member, matched by name to the schema's fields -- the schema and the
/// native type describe the same struct, so the names agree.
/// </summary>
public sealed class UnrealValueWriter
{
    private static readonly ConcurrentDictionary<Type, IReadOnlyList<(string Name, Func<object, object?> Read)>> NativeMembers = new();
    private static readonly ConcurrentDictionary<string, bool> Reported = new(StringComparer.Ordinal);

    private readonly UnrealAssetTable table;

    public UnrealValueWriter(UnrealAssetTable table)
    {
        this.table = table;
    }

    public void WriteProperties(StructureWriter writer, IEnumerable<FPropertyTag> properties)
    {
        foreach (FPropertyTag tag in properties)
        {
            if (tag.Tag is null)
            {
                continue;
            }
            if (!writer.TryField(tag.Name.Text, out int index))
            {
                continue;
            }
            Write(writer, index, tag.Tag.GenericValue);
        }
    }

    /// <summary>
    /// The field's shape decides how a value is stated, never the value's own type: a schema
    /// field is a pointer, a structure, a string or a number, and a value that cannot take that
    /// shape is reported once and left at its default instead of failing the whole export.
    /// </summary>
    private void Write(StructureWriter writer, int index, object? value)
    {
        if (value is null)
        {
            return;
        }
        SerializableType.Field field = writer.FieldAt(index);
        if (field.ArrayDepth == 1)
        {
            WriteSequence(writer, index, field, value);
            return;
        }
        if (field.Type.IsEnginePointer())
        {
            if (value is FPackageIndex pointer)
            {
                writer.SetPointer(index, Target(pointer));
            }
            else
            {
                ReportOnce($"{value.GetType().Name} into pointer '{field.Name}'");
            }
            return;
        }
        switch (field.Type.Type)
        {
            case PrimitiveType.Complex:
                switch (value)
                {
                    case FScriptStruct scriptStruct: WriteStruct(writer.Nested(index), scriptStruct.StructType); break;
                    case FStructFallback fallback: WriteProperties(writer.Nested(index), fallback.Properties); break;
                    case string or FName or FText or Enum or bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double:
                        ReportOnce($"{value.GetType().Name} into structure '{field.Name}'");
                        break;
                    default: WriteNative(writer.Nested(index), value); break;
                }
                break;
            case PrimitiveType.String:
                if (value is FScriptStruct or FStructFallback)
                {
                    ReportOnce($"{value.GetType().Name} into string '{field.Name}'");
                }
                else
                {
                    writer.SetString(index, Text(value));
                }
                break;
            case PrimitiveType.Bool:
                writer.SetBoolean(index, Integer(value) != 0);
                break;
            case PrimitiveType.Single:
            case PrimitiveType.Double:
                writer.SetReal(index, Real(value));
                break;
            default:
                writer.SetInteger(index, Integer(value));
                break;
        }
    }

    /// <summary>
    /// The Unity asset an object reference lands on: none when the reference never resolved
    /// (a script class import with script data unread, an object outside the load) or when
    /// the resolved object was not converted.
    /// </summary>
    private IUnityObjectBase? Target(FPackageIndex pointer) =>
        pointer.ResolvedObject is null ? null : table.Find(pointer.ResolvedObject);

    /// <summary>One map entry into the entry structure the schema states a map as: the key into its first field, the value into its second.</summary>
    private void WriteEntry(StructureWriter entryWriter, KeyValuePair<object?, object?> entry)
    {
        if (entryWriter.TryField(UsmapTypeTreeBuilder.MapKeyName, out int key))
        {
            Write(entryWriter, key, entry.Key);
        }
        if (entryWriter.TryField(UsmapTypeTreeBuilder.MapValueName, out int value))
        {
            Write(entryWriter, value, entry.Value);
        }
    }

    private void WriteStruct(StructureWriter nested, IUStruct structValue)
    {
        if (structValue is FStructFallback fallback)
        {
            WriteProperties(nested, fallback.Properties);
            return;
        }
        WriteNative(nested, structValue);
    }

    private void WriteNative(StructureWriter nested, object native)
    {
        foreach ((string name, Func<object, object?> read) in Members(native.GetType()))
        {
            if (!nested.TryField(name, out int index))
            {
                continue;
            }
            Write(nested, index, read(native));
        }
    }

    private void WriteSequence(StructureWriter writer, int index, SerializableType.Field field, object value)
    {
        List<object?> elements = new();
        switch (value)
        {
            case UScriptArray array:
                foreach (FPropertyTagType element in array.Properties)
                {
                    elements.Add(element.GenericValue);
                }
                break;
            case UScriptSet set:
                foreach (FPropertyTagType element in set.Properties)
                {
                    elements.Add(element.GenericValue);
                }
                break;
            case UScriptMap map:
                foreach ((FPropertyTagType key, FPropertyTagType? entry) in map.Properties)
                {
                    elements.Add(new KeyValuePair<object?, object?>(key.GenericValue, entry?.GenericValue));
                }
                break;
            case string:
                elements.Add(value);
                break;
            case IDictionary dictionary:
                foreach (DictionaryEntry entry in dictionary)
                {
                    elements.Add(new KeyValuePair<object?, object?>(entry.Key, entry.Value));
                }
                break;
            case IEnumerable enumerable:
                foreach (object? element in enumerable)
                {
                    elements.Add(element);
                }
                break;
            default:
                elements.Add(value);
                break;
        }

        StructureWriter[] complexWriters = writer.SizeArray(index, elements.Count);
        if (field.Type.Type == PrimitiveType.Complex)
        {
            for (int i = 0; i < elements.Count; i++)
            {
                object? element = elements[i];
                if (element is null)
                {
                    continue;
                }
                if (field.Type.IsEnginePointer())
                {
                    if (element is FPackageIndex pointer && writer.ArrayElements(index)[i] is AssetRipper.SourceGenerated.Subclasses.PPtr_Object.IPPtr_Object holder)
                    {
                        holder.SetAsset(writer.Collection, Target(pointer) as AssetRipper.SourceGenerated.Classes.ClassID_0.IObject);
                    }
                    continue;
                }
                StructureWriter elementWriter = complexWriters[i];
                if (elementWriter.Structure is null)
                {
                    continue;
                }
                switch (element)
                {
                    case FScriptStruct scriptStruct: WriteStruct(elementWriter, scriptStruct.StructType); break;
                    case FStructFallback fallback: WriteProperties(elementWriter, fallback.Properties); break;
                    case KeyValuePair<object?, object?> entry: WriteEntry(elementWriter, entry); break;
                    default: WriteNative(elementWriter, element); break;
                }
            }
            return;
        }

        Array primitives = PrimitiveArray(field.Type.Type, elements);
        writer.SetPrimitiveArray(index, primitives);
    }

    private static Array PrimitiveArray(PrimitiveType kind, List<object?> elements)
    {
        int count = elements.Count;
        switch (kind)
        {
            case PrimitiveType.Bool: return elements.Select(static e => e is bool b && b).ToArray();
            case PrimitiveType.SByte: return elements.Select(static e => unchecked((sbyte)Integer(e))).ToArray();
            case PrimitiveType.Byte: return elements.Select(static e => unchecked((byte)Integer(e))).ToArray();
            case PrimitiveType.Short: return elements.Select(static e => unchecked((short)Integer(e))).ToArray();
            case PrimitiveType.UShort: return elements.Select(static e => unchecked((ushort)Integer(e))).ToArray();
            case PrimitiveType.Int: return elements.Select(static e => unchecked((int)Integer(e))).ToArray();
            case PrimitiveType.UInt: return elements.Select(static e => unchecked((uint)Integer(e))).ToArray();
            case PrimitiveType.Long: return elements.Select(static e => Integer(e)).ToArray();
            case PrimitiveType.ULong: return elements.Select(static e => unchecked((ulong)Integer(e))).ToArray();
            case PrimitiveType.Single: return elements.Select(static e => (float)Real(e)).ToArray();
            case PrimitiveType.Double: return elements.Select(static e => Real(e)).ToArray();
            case PrimitiveType.Char: return elements.Select(static e => unchecked((char)Integer(e))).ToArray();
            case PrimitiveType.String: return elements.Select(static e => Text(e)).ToArray();
            default: return new object[count];
        }
    }

    private static long Integer(object? value) => value switch
    {
        bool b => b ? 1 : 0,
        ulong u => unchecked((long)u),
        byte or sbyte or short or ushort or int or uint or long => Convert.ToInt64(value),
        float or double => unchecked((long)Convert.ToDouble(value)),
        Enum e => Enum.GetUnderlyingType(e.GetType()) == typeof(ulong) ? unchecked((long)Convert.ToUInt64(e)) : Convert.ToInt64(e),
        _ => 0,
    };

    private static double Real(object? value) => value switch
    {
        float or double or byte or sbyte or short or ushort or int or uint or long or ulong => Convert.ToDouble(value),
        _ => 0d,
    };

    private static string Text(object? value) => value switch
    {
        null => string.Empty,
        string s => s,
        FName name => name.Text,
        FText text => text.Text,
        FSoftObjectPath soft => soft.AssetPathName.Text,
        _ => value.ToString() ?? string.Empty,
    };

    private static IReadOnlyList<(string, Func<object, object?>)> Members(Type type)
    {
        return NativeMembers.GetOrAdd(type, static t =>
        {
            List<(string, Func<object, object?>)> members = new();
            foreach (FieldInfo field in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                members.Add((field.Name, field.GetValue));
            }
            foreach (PropertyInfo property in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetIndexParameters().Length == 0 && property.CanRead)
                {
                    members.Add((property.Name, instance => property.GetValue(instance)));
                }
            }
            return members;
        });
    }

    private static void ReportOnce(string what)
    {
        if (Reported.TryAdd(what, true))
        {
            Logger.Info(LogCategory.Import, $"[Unreal] Property value not representable: {what}");
        }
    }
}
