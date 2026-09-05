using System.Collections;
using System.Reflection;
using AssetRipper.IO.Files.SerializedFiles;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Objects.Core.i18N;
using CUE4Parse.UE4.Objects.UObject;
using Ruri.RipperHook.Core.TypeTree;

namespace Ruri.FModelHook.UnityConverter.TypeTree;

/// <summary>
/// The shape of an object whose class the reflection schema never saw -- a Blueprint class, a
/// user-defined struct -- read off the object itself: every property tag names its type, a
/// nested struct is its own tags, a container is its element's, a native struct CUE4Parse read
/// into a type of its own is that type's members. One root per object, in the vocabulary the
/// packed schema uses, so the same interpreter and the same value writer read it.
/// </summary>
public static class TagSchema
{
    private const int NestingLimit = 10;

    private static readonly IReadOnlyDictionary<string, string> LeafTypeNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["BoolProperty"] = "bool",
        ["Int8Property"] = "SInt8",
        ["ByteProperty"] = "UInt8",
        ["Int16Property"] = "SInt16",
        ["UInt16Property"] = "UInt16",
        ["IntProperty"] = "SInt32",
        ["UInt32Property"] = "UInt32",
        ["Int64Property"] = "SInt64",
        ["UInt64Property"] = "UInt64",
        ["FloatProperty"] = "float",
        ["DoubleProperty"] = "double",
    };

    private static readonly HashSet<string> StringPropertyTypes = new(StringComparer.Ordinal)
    {
        "NameProperty", "StrProperty", "TextProperty", "Utf8StrProperty", "AnsiStrProperty", "EnumProperty",
        "SoftObjectProperty", "SoftClassProperty", "FieldPathProperty", "VerseStringProperty",
    };

    private static readonly HashSet<string> PointerPropertyTypes = new(StringComparer.Ordinal)
    {
        "ObjectProperty", "ClassProperty", "InterfaceProperty", "WeakObjectProperty", "LazyObjectProperty", "AssetObjectProperty",
    };

    private static readonly HashSet<string> SequencePropertyTypes = new(StringComparer.Ordinal)
    {
        "ArrayProperty", "SetProperty", "OptionalProperty",
    };

    private static readonly IReadOnlyDictionary<Type, string> NativeLeafTypeNames = new Dictionary<Type, string>
    {
        [typeof(bool)] = "bool",
        [typeof(sbyte)] = "SInt8",
        [typeof(byte)] = "UInt8",
        [typeof(short)] = "SInt16",
        [typeof(ushort)] = "UInt16",
        [typeof(int)] = "SInt32",
        [typeof(uint)] = "UInt32",
        [typeof(long)] = "SInt64",
        [typeof(ulong)] = "UInt64",
        [typeof(float)] = "float",
        [typeof(double)] = "double",
    };

    public static TypeTreeNode Root(string className, IEnumerable<FPropertyTag> tags)
    {
        ArgumentException.ThrowIfNullOrEmpty(className);
        ArgumentNullException.ThrowIfNull(tags);
        List<TypeTreeNode> children = new()
        {
            Pointer("PPtr<GameObject>", "m_GameObject"),
            Leaf("UInt8", "m_Enabled", TransferMetaFlags.AlignBytes),
            Pointer("PPtr<MonoScript>", "m_Script"),
            String("m_Name"),
        };
        children.AddRange(Fields(tags, 0));
        return TypeTreeNode.Create(className, UsmapTypeTreeBuilder.RootNodeName, TransferMetaFlags.NoTransferFlags, children.ToArray());
    }

    private static IEnumerable<TypeTreeNode> Fields(IEnumerable<FPropertyTag> tags, int depth)
    {
        HashSet<string> emitted = new(StringComparer.Ordinal);
        foreach (FPropertyTag tag in tags)
        {
            if (!emitted.Add(tag.Name.Text))
            {
                continue;
            }
            TypeTreeNode? field = Field(tag.Name.Text, tag.PropertyType.Text, tag.TagData, tag.Tag?.GenericValue, depth);
            if (field is not null)
            {
                yield return field;
            }
        }
    }

    private static TypeTreeNode? Field(string name, string kind, FPropertyTagData? data, object? value, int depth)
    {
        if (LeafTypeNames.TryGetValue(kind, out string? leaf))
        {
            return kind == "ByteProperty" && data?.EnumName is not null ? String(name) : Leaf(leaf, name, TransferMetaFlags.NoTransferFlags);
        }
        if (StringPropertyTypes.Contains(kind))
        {
            return String(name);
        }
        if (PointerPropertyTypes.Contains(kind))
        {
            return Pointer("PPtr<$Object>", name);
        }
        if (SequencePropertyTypes.Contains(kind))
        {
            TypeTreeNode? element = data?.InnerType is null ? null : Field("data", data.InnerType, data.InnerTypeData, FirstElement(value), depth + 1);
            return element is null ? null : Vector(name, element);
        }
        if (kind == "MapProperty")
        {
            (object? firstKey, object? firstValue) = FirstEntry(value);
            TypeTreeNode? key = data?.InnerType is null ? null : Field(UsmapTypeTreeBuilder.MapKeyName, data.InnerType, data.InnerTypeData, firstKey, depth + 1);
            TypeTreeNode? entryValue = data?.ValueType is null ? null : Field(UsmapTypeTreeBuilder.MapValueName, data.ValueType, data.ValueTypeData, firstValue, depth + 1);
            return key is null || entryValue is null ? null : Map(name, key, entryValue);
        }
        if (kind == "StructProperty")
        {
            return Struct(name, data?.StructType, value, depth);
        }
        return null;
    }

    private static TypeTreeNode? Struct(string name, string? structName, object? value, int depth)
    {
        if (depth >= NestingLimit)
        {
            return null;
        }
        string typeName = structName ?? "Struct";
        switch (value)
        {
            case FScriptStruct { StructType: FStructFallback fallback }:
                return TypeTreeNode.Create(typeName, name, TransferMetaFlags.NoTransferFlags, Fields(fallback.Properties, depth + 1).ToArray());
            case FStructFallback fallback:
                return TypeTreeNode.Create(typeName, name, TransferMetaFlags.NoTransferFlags, Fields(fallback.Properties, depth + 1).ToArray());
            case FScriptStruct { StructType: { } native }:
                return TypeTreeNode.Create(typeName, name, TransferMetaFlags.NoTransferFlags, NativeMembers(native, depth + 1).ToArray());
            case null:
                return null;
            default:
                return TypeTreeNode.Create(typeName, name, TransferMetaFlags.NoTransferFlags, NativeMembers(value, depth + 1).ToArray());
        }
    }

    /// <summary>A struct CUE4Parse read into a type of its own: its public members, by the names the value writer matches on.</summary>
    private static IEnumerable<TypeTreeNode> NativeMembers(object native, int depth)
    {
        Type type = native.GetType();
        foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            TypeTreeNode? node = NativeMember(field.Name, field.FieldType, () => field.GetValue(native), depth);
            if (node is not null)
            {
                yield return node;
            }
        }
        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length != 0 || !property.CanRead)
            {
                continue;
            }
            TypeTreeNode? node = NativeMember(property.Name, property.PropertyType, () => property.GetValue(native), depth);
            if (node is not null)
            {
                yield return node;
            }
        }
    }

    private static TypeTreeNode? NativeMember(string name, Type type, Func<object?> read, int depth)
    {
        if (NativeLeafTypeNames.TryGetValue(type, out string? leaf))
        {
            return Leaf(leaf, name, TransferMetaFlags.NoTransferFlags);
        }
        if (type == typeof(string) || type == typeof(FName) || type == typeof(FText) || type == typeof(FSoftObjectPath) || type.IsEnum)
        {
            return String(name);
        }
        if (type == typeof(FPackageIndex) || typeof(ResolvedObject).IsAssignableFrom(type))
        {
            return Pointer("PPtr<$Object>", name);
        }
        if (depth >= NestingLimit || type == typeof(object) || type.IsPrimitive)
        {
            return null;
        }
        if (typeof(IDictionary).IsAssignableFrom(type))
        {
            return null;
        }
        if (type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type))
        {
            Type? elementType = type.IsArray ? type.GetElementType() : type.IsGenericType ? type.GetGenericArguments()[0] : null;
            if (elementType is null)
            {
                return null;
            }
            object? first = FirstElement(read());
            TypeTreeNode? element = NativeMember("data", elementType, () => first, depth + 1);
            return element is null ? null : Vector(name, element);
        }
        if (type.IsValueType || type.IsClass)
        {
            object? value = read();
            if (value is null)
            {
                return null;
            }
            return TypeTreeNode.Create(type.Name.TrimStart('F'), name, TransferMetaFlags.NoTransferFlags, NativeMembers(value, depth + 1).ToArray());
        }
        return null;
    }

    private static object? FirstElement(object? value) => value switch
    {
        UScriptArray array => array.Properties.Count > 0 ? array.Properties[0].GenericValue : null,
        UScriptSet set => set.Properties.Count > 0 ? set.Properties[0].GenericValue : null,
        string => null,
        IEnumerable enumerable => enumerable.Cast<object?>().FirstOrDefault(),
        _ => null,
    };

    private static (object? Key, object? Value) FirstEntry(object? value)
    {
        switch (value)
        {
            case UScriptMap map when map.Properties.Count > 0:
                KeyValuePair<FPropertyTagType, FPropertyTagType?> first = map.Properties.First();
                return (first.Key.GenericValue, first.Value?.GenericValue);
            case IDictionary dictionary when dictionary.Count > 0:
                IDictionaryEnumerator enumerator = dictionary.GetEnumerator();
                enumerator.MoveNext();
                return (enumerator.Key, enumerator.Value);
            default:
                return (null, null);
        }
    }

    private static TypeTreeNode Leaf(string typeName, string name, TransferMetaFlags flags) =>
        TypeTreeNode.Create(typeName, name, flags);

    private static TypeTreeNode String(string name)
    {
        TypeTreeNode character = Leaf("char", "data", TransferMetaFlags.NoTransferFlags);
        TypeTreeNode size = Leaf("SInt32", "size", TransferMetaFlags.NoTransferFlags);
        TypeTreeNode array = TypeTreeNode.Create("Array", "Array", TransferMetaFlags.AlignBytes, size, character);
        return TypeTreeNode.Create("string", name, TransferMetaFlags.NoTransferFlags, array);
    }

    private static TypeTreeNode Pointer(string typeName, string name)
    {
        TypeTreeNode fileId = Leaf("SInt32", "m_FileID", TransferMetaFlags.NoTransferFlags);
        TypeTreeNode pathId = Leaf("SInt64", "m_PathID", TransferMetaFlags.NoTransferFlags);
        return TypeTreeNode.Create(typeName, name, TransferMetaFlags.NoTransferFlags, fileId, pathId);
    }

    private static TypeTreeNode Vector(string name, TypeTreeNode element)
    {
        TypeTreeNode size = Leaf("SInt32", "size", TransferMetaFlags.NoTransferFlags);
        TypeTreeNode array = TypeTreeNode.Create("Array", "Array", TransferMetaFlags.AlignBytes, size, element);
        return TypeTreeNode.Create("vector", name, TransferMetaFlags.NoTransferFlags, array);
    }

    private static TypeTreeNode Map(string name, TypeTreeNode key, TypeTreeNode value)
    {
        TypeTreeNode entry = TypeTreeNode.Create(UsmapTypeTreeBuilder.MapEntryTypeName, "data", TransferMetaFlags.NoTransferFlags, key, value);
        TypeTreeNode size = Leaf("SInt32", "size", TransferMetaFlags.NoTransferFlags);
        TypeTreeNode array = TypeTreeNode.Create("Array", "Array", TransferMetaFlags.AlignBytes, size, entry);
        return TypeTreeNode.Create("vector", name, TransferMetaFlags.NoTransferFlags, array);
    }
}
