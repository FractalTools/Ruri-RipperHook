using AssetRipper.IO.Files.SerializedFiles;
using AssetRipper.Primitives;
using AssetRipper.Tpk.TypeTrees;
using Ruri.RipperHook.Core.TypeTree;
using CUE4Parse.MappingsProvider;
using System.Collections.Generic;
using System.Linq;

namespace Ruri.FModelHook.Ripper.TypeTree;

/// <summary>
/// Every struct a .usmap declares, restated as the type trees of a custom engine: one class per
/// struct, its serialized shape a Unity node tree whose leaves are Unity's primitive type names,
/// so the same interpreter that reads a Unity fork's classes reads Unreal's reflected objects.
/// The root of every class is a MonoBehaviour: the four header nodes Unity puts first, then the
/// struct's properties with its super chain's properties ahead of its own, exactly the order
/// Unreal serializes them in.
///
/// Class ids are ordinals in name order -- a schema names its types, it does not number them --
/// and the blob is split whenever its node buffer would overflow the sixteen-bit node index,
/// which a game's whole reflection dump routinely does.
/// </summary>
public sealed class UsmapTypeTreeBuilder
{
    public const string AssemblyName = "UnrealEngine";
    public const string RootNodeName = "Base";

    /// <summary>The lineage's one version key: a schema is a snapshot, not a version series.</summary>
    public const string VersionKey = "usmap";

    /// <summary>The Unity layout every converted asset -- and every schema class -- is emitted at.</summary>
    public static readonly UnityVersion LayoutVersion = new(2022, 3, 62, UnityVersionType.Final, 1);

    private const int NodeBudget = 60000;
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

    private readonly TypeMappings mappings;
    private readonly List<TpkTypeTreeBlob> blobs = new();
    private readonly Dictionary<string, int> classIds = new(StringComparer.Ordinal);
    private readonly UnityVersion ordinalVersion = TypeTreeOrdinal.ToUnityVersion(0);
    private TpkTypeTreeBlob current;

    private UsmapTypeTreeBuilder(TypeMappings mappings)
    {
        this.mappings = mappings;
        current = NewBlob();
    }

    public IReadOnlyList<TpkTypeTreeBlob> Blobs => blobs;

    public IReadOnlyDictionary<string, int> ClassIds => classIds;

    public static UsmapTypeTreeBuilder Build(TypeMappings mappings)
    {
        ArgumentNullException.ThrowIfNull(mappings);
        UsmapTypeTreeBuilder builder = new(mappings);
        builder.Run();
        return builder;
    }

    private void Run()
    {
        List<string> names = mappings.Types.Keys.ToList();
        names.Sort(StringComparer.Ordinal);
        for (int index = 0; index < names.Count; index++)
        {
            string name = names[index];
            int id = index + 1;
            classIds[name] = id;
            Struct schema = mappings.Types[name];

            if (current.NodeBuffer.Count > NodeBudget)
            {
                Seal();
                current = NewBlob();
            }

            TpkUnityClass unityClass = new()
            {
                Name = current.StringBuffer.AddString(name),
                Base = current.StringBuffer.AddString(schema.SuperType ?? string.Empty),
                Flags = TpkUnityClassFlags.HasReleaseRootNode | TpkUnityClassFlags.HasEditorRootNode,
            };
            ushort root = ClassRoot(name, schema);
            unityClass.ReleaseRootNode = root;
            unityClass.EditorRootNode = root;

            TpkClassInformation information = new(id);
            information.Classes.Add(new KeyValuePair<UnityVersion, TpkUnityClass?>(ordinalVersion, unityClass));
            current.ClassInformation.Add(information);
        }
        Seal();
    }

    private TpkTypeTreeBlob NewBlob()
    {
        TpkTypeTreeBlob blob = new();
        blob.Versions.Add(ordinalVersion);
        blob.CommonString.Add(UnityVersion.MinVersion, []);
        blob.CreationTime = DateTime.UtcNow;
        return blob;
    }

    private void Seal()
    {
        if (current.ClassInformation.Count > 0)
        {
            blobs.Add(current);
        }
    }

    private ushort ClassRoot(string name, Struct schema)
    {
        List<ushort> children = new()
        {
            Pointer("PPtr<GameObject>", "m_GameObject"),
            Leaf("UInt8", "m_Enabled", TransferMetaFlags.AlignBytes),
            Pointer("PPtr<MonoScript>", "m_Script"),
            String("m_Name"),
        };
        foreach ((PropertyInfo property, _) in Chain(schema))
        {
            ushort? node = Property(property, 0);
            if (node is not null)
            {
                children.Add(node.Value);
            }
        }
        return Node(name, RootNodeName, children, TransferMetaFlags.NoTransferFlags);
    }

    /// <summary>The struct's properties in serialization order: the root-most ancestor's first.</summary>
    private IEnumerable<(PropertyInfo Property, Struct Owner)> Chain(Struct schema)
    {
        List<Struct> lineage = new();
        Struct? cursor = schema;
        HashSet<string> seen = new(StringComparer.Ordinal);
        while (cursor is not null && seen.Add(cursor.Name))
        {
            lineage.Add(cursor);
            cursor = cursor.SuperType is not null && mappings.Types.TryGetValue(cursor.SuperType, out Struct? super) ? super : null;
        }
        lineage.Reverse();
        foreach (Struct owner in lineage)
        {
            HashSet<string> emitted = new(StringComparer.Ordinal);
            foreach (KeyValuePair<int, PropertyInfo> slot in owner.Properties.OrderBy(static pair => pair.Key))
            {
                if (emitted.Add(slot.Value.Name))
                {
                    yield return (slot.Value, owner);
                }
            }
        }
    }

    private ushort? Property(PropertyInfo property, int depth)
    {
        PropertyType type = property.MappingType;
        if (property.ArraySize is > 1)
        {
            ushort? element = TypeNode(type, "data", depth + 1);
            return element is null ? null : Vector(property.Name, element.Value);
        }
        return TypeNode(type, property.Name, depth);
    }

    private ushort? TypeNode(PropertyType type, string nodeName, int depth)
    {
        string kind = type.Type;
        if (LeafTypeNames.TryGetValue(kind, out string? leaf))
        {
            return kind == "ByteProperty" && type.EnumName is not null
                ? String(nodeName)
                : Leaf(leaf, nodeName, TransferMetaFlags.NoTransferFlags);
        }
        if (StringPropertyTypes.Contains(kind))
        {
            return String(nodeName);
        }
        if (PointerPropertyTypes.Contains(kind))
        {
            return Pointer("PPtr<$Object>", nodeName);
        }
        if (SequencePropertyTypes.Contains(kind))
        {
            ushort? element = type.InnerType is null ? null : TypeNode(type.InnerType, "data", depth + 1);
            return element is null ? null : Vector(nodeName, element.Value);
        }
        if (kind == "MapProperty")
        {
            ushort? key = type.InnerType is null ? null : TypeNode(type.InnerType, "first", depth + 1);
            ushort? value = type.ValueType is null ? null : TypeNode(type.ValueType, "second", depth + 1);
            return key is null || value is null ? null : Map(nodeName, key.Value, value.Value);
        }
        if (kind == "StructProperty")
        {
            return StructNode(type.StructType ?? string.Empty, nodeName, depth);
        }
        return null;
    }

    private ushort? StructNode(string structName, string nodeName, int depth)
    {
        if (structName.Length == 0 || depth >= NestingLimit)
        {
            return null;
        }
        List<ushort> children = new();
        if (mappings.Types.TryGetValue(structName, out Struct? schema))
        {
            foreach ((PropertyInfo property, _) in Chain(schema))
            {
                ushort? child = Property(property, depth + 1);
                if (child is not null)
                {
                    children.Add(child.Value);
                }
            }
        }
        return Node(structName, nodeName, children, TransferMetaFlags.NoTransferFlags);
    }

    private ushort Leaf(string typeName, string nodeName, TransferMetaFlags flags)
        => Node(typeName, nodeName, [], flags);

    private ushort String(string nodeName)
    {
        ushort character = Leaf("char", "data", TransferMetaFlags.NoTransferFlags);
        ushort size = Leaf("SInt32", "size", TransferMetaFlags.NoTransferFlags);
        ushort array = Node("Array", "Array", [size, character], TransferMetaFlags.AlignBytes);
        return Node("string", nodeName, [array], TransferMetaFlags.NoTransferFlags);
    }

    private ushort Pointer(string typeName, string nodeName)
    {
        ushort fileId = Leaf("SInt32", "m_FileID", TransferMetaFlags.NoTransferFlags);
        ushort pathId = Leaf("SInt64", "m_PathID", TransferMetaFlags.NoTransferFlags);
        return Node(typeName, nodeName, [fileId, pathId], TransferMetaFlags.NoTransferFlags);
    }

    private ushort Vector(string nodeName, ushort element)
    {
        ushort size = Leaf("SInt32", "size", TransferMetaFlags.NoTransferFlags);
        ushort array = Node("Array", "Array", [size, element], TransferMetaFlags.AlignBytes);
        return Node("vector", nodeName, [array], TransferMetaFlags.NoTransferFlags);
    }

    private ushort Map(string nodeName, ushort key, ushort value)
    {
        ushort pair = Node("pair", "data", [key, value], TransferMetaFlags.NoTransferFlags);
        ushort size = Leaf("SInt32", "size", TransferMetaFlags.NoTransferFlags);
        ushort array = Node("Array", "Array", [size, pair], TransferMetaFlags.AlignBytes);
        return Node("map", nodeName, [array], TransferMetaFlags.NoTransferFlags);
    }

    private ushort Node(string typeName, string nodeName, List<ushort> children, TransferMetaFlags flags)
    {
        TpkUnityNode node = new()
        {
            TypeName = current.StringBuffer.AddString(typeName),
            Name = current.StringBuffer.AddString(nodeName),
            Version = 1,
            MetaFlag = (uint)flags,
            SubNodes = children.Count == 0 ? Array.Empty<ushort>() : children.ToArray(),
        };
        return current.NodeBuffer.AddNode(node);
    }
}
