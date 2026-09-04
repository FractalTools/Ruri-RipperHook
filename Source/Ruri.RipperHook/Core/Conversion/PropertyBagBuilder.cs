using AssetRipper.Assets;
using AssetRipper.Assets.Collections;
using AssetRipper.Import.Structure.Assembly.Serializable;
using AssetRipper.Import.Structure.Assembly.TypeTrees;
using AssetRipper.SerializationLogic;
using AssetRipper.SourceGenerated;
using AssetRipper.SourceGenerated.Classes.ClassID_1;
using AssetRipper.SourceGenerated.Classes.ClassID_114;
using AssetRipper.SourceGenerated.Classes.ClassID_115;
using AssetRipper.SourceGenerated.Extensions;
using AssetRipper.SourceGenerated.Subclasses.PPtr_Object;
using Ruri.RipperHook.Core.TypeTree;
using System.Collections.Concurrent;

namespace Ruri.RipperHook.Conversion;

/// <summary>
/// An object whose shape is a type tree and whose content is a bag of named values -- what a
/// MonoBehaviour is to Unity, and what any engine's reflected object is once its schema has
/// been restated as a type tree. The schema arrives as a <see cref="TypeTreeNode"/> root;
/// the values are written through <see cref="StructureWriter"/> by whoever holds them.
/// </summary>
public static class PropertyBagBuilder
{
    private static readonly ConcurrentDictionary<TypeTreeNode, SerializableTreeType> TypeCache = new(ReferenceEqualityComparer.Instance);

    public static IMonoScript Script(ConvertedPackage package, string className, string @namespace, string assemblyName)
    {
        ArgumentNullException.ThrowIfNull(package);
        IMonoScript script = package.Create<IMonoScript>(ClassIDType.MonoScript, className, null);
        script.ClassName_R = className;
        script.Namespace = @namespace;
        script.AssemblyName = assemblyName;
        script.ExecutionOrder = 0;
        return script;
    }

    public static SerializableStructure Structure(TypeTreeNode root, AssetRipper.Primitives.UnityVersion version)
    {
        ArgumentNullException.ThrowIfNull(root);
        SerializableTreeType type = TypeCache.GetOrAdd(root, static node =>
            SerializableTreeType.FromRootNode(ToStruct(node), monoBehaviourStructure: true));
        SerializableStructure structure = type.CreateSerializableStructure();
        structure.InitializeFields(version);
        return structure;
    }

    public static IMonoBehaviour Behaviour(ConvertedPackage package, string name, string? originalPath, IMonoScript script,
        IGameObject? host, SerializableStructure structure)
    {
        ArgumentNullException.ThrowIfNull(package);
        IMonoBehaviour behaviour = package.Create<IMonoBehaviour>(ClassIDType.MonoBehaviour, name, originalPath);
        behaviour.ScriptP = script;
        behaviour.Enabled = 1;
        if (host is not null)
        {
            behaviour.GameObjectP = host;
            host.AddComponent(ClassIDType.MonoBehaviour, behaviour);
        }
        behaviour.Structure = structure;
        return behaviour;
    }

    private static TypeTreeNodeStruct ToStruct(TypeTreeNode node)
    {
        TypeTreeNodeStruct[] subNodes = new TypeTreeNodeStruct[node.SubNodes.Length];
        for (int i = 0; i < subNodes.Length; i++)
        {
            subNodes[i] = ToStruct(node.SubNodes[i]);
        }
        return new TypeTreeNodeStruct(node.TypeName, node.OriginalName, node.Version, node.MetaFlag, subNodes);
    }
}

/// <summary>
/// Writes values into a <see cref="SerializableStructure"/> by field name, converting to the
/// primitive the field declares. A name the structure has no field for is reported through
/// <see cref="TryField"/> and skipped by every setter, so a source with more properties than
/// the schema knows loses only those.
/// </summary>
public readonly struct StructureWriter
{
    private readonly SerializableStructure structure;
    private readonly AssetCollection collection;
    private readonly AssetRipper.Primitives.UnityVersion version;

    public StructureWriter(SerializableStructure structure, AssetCollection collection, AssetRipper.Primitives.UnityVersion version)
    {
        this.structure = structure ?? throw new ArgumentNullException(nameof(structure));
        this.collection = collection ?? throw new ArgumentNullException(nameof(collection));
        this.version = version;
    }

    public SerializableStructure Structure => structure;

    public AssetCollection Collection => collection;

    public bool TryField(string name, out int index) => structure.TryGetIndex(name, out index);

    public SerializableType.Field FieldAt(int index) => structure.Type.Fields[index];

    public void SetBoolean(int index, bool value) => structure.Fields[index].AsBoolean = value;

    public void SetInteger(int index, long value)
    {
        ref SerializableValue field = ref structure.Fields[index];
        switch (FieldAt(index).Type.Type)
        {
            case PrimitiveType.Bool: field.AsBoolean = value != 0; break;
            case PrimitiveType.SByte: field.AsSByte = (sbyte)value; break;
            case PrimitiveType.Byte: field.AsByte = (byte)value; break;
            case PrimitiveType.Short: field.AsInt16 = (short)value; break;
            case PrimitiveType.UShort: field.AsUInt16 = (ushort)value; break;
            case PrimitiveType.Int: field.AsInt32 = (int)value; break;
            case PrimitiveType.UInt: field.AsUInt32 = (uint)value; break;
            case PrimitiveType.Long: field.AsInt64 = value; break;
            case PrimitiveType.ULong: field.AsUInt64 = (ulong)value; break;
            case PrimitiveType.Single: field.AsSingle = value; break;
            case PrimitiveType.Double: field.AsDouble = value; break;
            case PrimitiveType.Char: field.AsChar = (char)value; break;
            default:
                throw new InvalidOperationException($"[StructureWriter] '{FieldAt(index).Name}' is {FieldAt(index).Type.Type}, not a number.");
        }
    }

    public void SetReal(int index, double value)
    {
        ref SerializableValue field = ref structure.Fields[index];
        switch (FieldAt(index).Type.Type)
        {
            case PrimitiveType.Single: field.AsSingle = (float)value; break;
            case PrimitiveType.Double: field.AsDouble = value; break;
            default: SetInteger(index, (long)value); break;
        }
    }

    public void SetString(int index, string value) => structure.Fields[index].AsString = value ?? string.Empty;

    public void SetPointer(int index, IUnityObjectBase? target)
    {
        object holder = structure.Fields[index].CValue;
        if (holder is IPPtr_Object pointer)
        {
            pointer.SetAsset(collection, target as AssetRipper.SourceGenerated.Classes.ClassID_0.IObject);
            return;
        }
        throw new InvalidOperationException($"[StructureWriter] '{FieldAt(index).Name}' is not a pointer field.");
    }

    /// <summary>The nested structure a complex field holds, to be written with its own writer.</summary>
    public StructureWriter Nested(int index)
    {
        object holder = structure.Fields[index].CValue;
        if (holder is SerializableStructure nested)
        {
            return new StructureWriter(nested, collection, version);
        }
        throw new InvalidOperationException($"[StructureWriter] '{FieldAt(index).Name}' is not a structure field.");
    }

    /// <summary>
    /// Size an array field to <paramref name="count"/> elements of its declared type, returning
    /// the per-element writers for a complex element type (empty for primitives, which are set
    /// whole through <see cref="SetPrimitiveArray"/>).
    /// </summary>
    public StructureWriter[] SizeArray(int index, int count)
    {
        SerializableType.Field field = FieldAt(index);
        if (field.ArrayDepth != 1)
        {
            throw new InvalidOperationException($"[StructureWriter] '{field.Name}' is not an array field.");
        }
        ref SerializableValue value = ref structure.Fields[index];
        switch (field.Type.Type)
        {
            case PrimitiveType.Complex:
            {
                IUnityAssetBase[] elements = new IUnityAssetBase[count];
                StructureWriter[] writers = new StructureWriter[count];
                for (int i = 0; i < count; i++)
                {
                    IUnityAssetBase element = field.Type.CreateInstance(structure.Depth + 1, version);
                    elements[i] = element;
                    writers[i] = element is SerializableStructure nested
                        ? new StructureWriter(nested, collection, version)
                        : default;
                }
                value.AsAssetArray = elements;
                return writers;
            }
            case PrimitiveType.Pair or PrimitiveType.MapPair:
            {
                SerializablePair[] pairs = new SerializablePair[count];
                for (int i = 0; i < count; i++)
                {
                    pairs[i] = new SerializablePair(field.Type, structure.Depth + 1);
                }
                value.AsPairArray = pairs;
                return [];
            }
            default:
                return [];
        }
    }

    public IUnityAssetBase[] ArrayElements(int index) => structure.Fields[index].AsAssetArray;

    public SerializablePair[] PairElements(int index) => structure.Fields[index].AsPairArray;

    public void SetPrimitiveArray(int index, Array values)
    {
        ref SerializableValue field = ref structure.Fields[index];
        switch (FieldAt(index).Type.Type)
        {
            case PrimitiveType.Bool: field.AsBooleanArray = (bool[])values; break;
            case PrimitiveType.SByte: field.AsSByteArray = (sbyte[])values; break;
            case PrimitiveType.Byte: field.AsByteArray = (byte[])values; break;
            case PrimitiveType.Short: field.AsInt16Array = (short[])values; break;
            case PrimitiveType.UShort: field.AsUInt16Array = (ushort[])values; break;
            case PrimitiveType.Int: field.AsInt32Array = (int[])values; break;
            case PrimitiveType.UInt: field.AsUInt32Array = (uint[])values; break;
            case PrimitiveType.Long: field.AsInt64Array = (long[])values; break;
            case PrimitiveType.ULong: field.AsUInt64Array = (ulong[])values; break;
            case PrimitiveType.Single: field.AsSingleArray = (float[])values; break;
            case PrimitiveType.Double: field.AsDoubleArray = (double[])values; break;
            case PrimitiveType.String: field.AsStringArray = (string[])values; break;
            case PrimitiveType.Char: field.AsCharArray = (char[])values; break;
            default:
                throw new InvalidOperationException($"[StructureWriter] '{FieldAt(index).Name}' is not a primitive array.");
        }
    }
}
