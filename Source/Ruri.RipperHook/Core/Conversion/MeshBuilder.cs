using AssetRipper.Assets.Generics;
using AssetRipper.Checksum;
using AssetRipper.Numerics;
using AssetRipper.SourceGenerated;
using AssetRipper.SourceGenerated.Classes.ClassID_43;
using AssetRipper.SourceGenerated.Enums;
using AssetRipper.SourceGenerated.Extensions;
using AssetRipper.SourceGenerated.Extensions.Enums.Shader.ShaderChannel;
using AssetRipper.SourceGenerated.Subclasses.BlendShapeVertex;
using AssetRipper.SourceGenerated.Subclasses.ChannelInfo;
using AssetRipper.SourceGenerated.Subclasses.MeshBlendShape;
using AssetRipper.SourceGenerated.Subclasses.MeshBlendShapeChannel;
using AssetRipper.SourceGenerated.Subclasses.SubMesh;
using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Ruri.RipperHook.Conversion;

/// <summary>
/// Writes a <see cref="MeshGeometry"/> straight into a Mesh's vertex data: the modern
/// fourteen-attribute channel table, every present attribute Float32 in one interleaved
/// stream, plus the index buffer, sections, skin, bind poses and blend shapes. Nothing is
/// bit-packed on the way in, so nothing has to be unpacked on the way out -- the geometry
/// reaches every consumer as the plain buffers it already was.
/// </summary>
public static class MeshBuilder
{
    private const int ChannelCount = 14;
    private const byte Float32Format = 0;
    private const int TriangleTopology = 0;

    public static IMesh Build(ConvertedPackage package, MeshGeometry geometry, string? originalPath)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(geometry);
        IMesh mesh = package.Create<IMesh>(ClassIDType.Mesh, geometry.Name, originalPath);
        Fill(mesh, geometry);
        return mesh;
    }

    public static void Fill(IMesh mesh, MeshGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(geometry);
        Validate(geometry);
        WriteVertexData(mesh, geometry);
        WriteIndices(mesh, geometry);
        WriteSkin(mesh, geometry);
        WriteBindPoses(mesh, geometry);
        WriteBoneNames(mesh, geometry);
        WriteMorphs(mesh, geometry);
        mesh.LocalAABB.CalculateFromVertexArray(geometry.Positions);
        mesh.IsReadable = true;
        mesh.KeepVertices = true;
        mesh.KeepIndices = true;
        mesh.MeshUsageFlags = 0;
        mesh.SetMeshCompression(ModelImporterMeshCompression.Off);
    }

    private static void Validate(MeshGeometry geometry)
    {
        int count = geometry.Positions.Length;
        Check(geometry.Normals, count, nameof(geometry.Normals));
        Check(geometry.Tangents, count, nameof(geometry.Tangents));
        Check(geometry.Colors, count, nameof(geometry.Colors));
        Check(geometry.Skin, count, nameof(geometry.Skin));
        for (int set = 0; set < geometry.TexCoords.Length; set++)
        {
            Check(geometry.TexCoords[set], count, $"TexCoords[{set}]");
        }
        if (geometry.TexCoords.Length > 8)
        {
            throw new ArgumentException($"[MeshBuilder] {geometry.Name}: Unity carries at most 8 texture coordinate sets, got {geometry.TexCoords.Length}.");
        }
        foreach (uint index in geometry.Indices)
        {
            if (index >= (uint)count)
            {
                throw new ArgumentException($"[MeshBuilder] {geometry.Name}: index {index} exceeds {count} vertices.");
            }
        }

        static void Check<T>(T[]? stream, int count, string name)
        {
            if (stream is not null && stream.Length != count)
            {
                throw new ArgumentException($"[MeshBuilder] {name} has {stream.Length} entries for {count} vertices.");
            }
        }
    }

    private static void WriteVertexData(IMesh mesh, MeshGeometry geometry)
    {
        int count = geometry.Positions.Length;
        Span<int> dimensions = stackalloc int[ChannelCount];
        dimensions[(int)ShaderChannel.Vertex] = 3;
        dimensions[(int)ShaderChannel.Normal] = geometry.Normals is null ? 0 : 3;
        dimensions[(int)ShaderChannel.Tangent] = geometry.Tangents is null ? 0 : 4;
        dimensions[(int)ShaderChannel.Color] = geometry.Colors is null ? 0 : 4;
        for (int set = 0; set < geometry.TexCoords.Length; set++)
        {
            dimensions[(int)ShaderChannel.UV0 + set] = geometry.TexCoords[set] is null ? 0 : 2;
        }

        int stride = 0;
        Span<int> offsets = stackalloc int[ChannelCount];
        for (int channel = 0; channel < ChannelCount; channel++)
        {
            offsets[channel] = stride;
            stride += dimensions[channel] * sizeof(float);
        }

        AssetList<ChannelInfo> channels = mesh.VertexData.Channels
            ?? throw new InvalidOperationException($"[MeshBuilder] Mesh at {mesh.Collection.Version} carries no channel table.");
        channels.Clear();
        for (int channel = 0; channel < ChannelCount; channel++)
        {
            ChannelInfo info = channels.AddNew();
            info.Stream = 0;
            info.Format = Float32Format;
            info.Dimension = (byte)dimensions[channel];
            info.Offset = (byte)(dimensions[channel] == 0 ? 0 : offsets[channel]);
        }

        byte[] data = new byte[checked(count * stride)];
        Span<float> floats = MemoryMarshal.Cast<byte, float>(data.AsSpan());
        int floatStride = stride / sizeof(float);
        for (int vertex = 0; vertex < count; vertex++)
        {
            int cursor = vertex * floatStride;
            Vector3 position = geometry.Positions[vertex];
            floats[cursor++] = position.X;
            floats[cursor++] = position.Y;
            floats[cursor++] = position.Z;
            if (geometry.Normals is not null)
            {
                Vector3 normal = geometry.Normals[vertex];
                floats[cursor++] = normal.X;
                floats[cursor++] = normal.Y;
                floats[cursor++] = normal.Z;
            }
            if (geometry.Tangents is not null)
            {
                Vector4 tangent = geometry.Tangents[vertex];
                floats[cursor++] = tangent.X;
                floats[cursor++] = tangent.Y;
                floats[cursor++] = tangent.Z;
                floats[cursor++] = tangent.W;
            }
            if (geometry.Colors is not null)
            {
                Vector4 color = geometry.Colors[vertex];
                floats[cursor++] = color.X;
                floats[cursor++] = color.Y;
                floats[cursor++] = color.Z;
                floats[cursor++] = color.W;
            }
            for (int set = 0; set < geometry.TexCoords.Length; set++)
            {
                Vector2[]? coordinates = geometry.TexCoords[set];
                if (coordinates is null)
                {
                    continue;
                }
                Vector2 uv = coordinates[vertex];
                floats[cursor++] = uv.X;
                floats[cursor++] = uv.Y;
            }
        }

        mesh.VertexData.VertexCount = (uint)count;
        mesh.VertexData.Data = data;
    }

    private static void WriteIndices(IMesh mesh, MeshGeometry geometry)
    {
        bool wide = geometry.Positions.Length > ushort.MaxValue;
        int indexSize = wide ? sizeof(uint) : sizeof(ushort);
        byte[] buffer = new byte[geometry.Indices.Length * indexSize];
        if (wide)
        {
            geometry.Indices.AsSpan().CopyTo(MemoryMarshal.Cast<byte, uint>(buffer.AsSpan()));
        }
        else
        {
            Span<ushort> narrow = MemoryMarshal.Cast<byte, ushort>(buffer.AsSpan());
            for (int i = 0; i < geometry.Indices.Length; i++)
            {
                narrow[i] = (ushort)geometry.Indices[i];
            }
        }
        mesh.IndexBuffer = buffer;
        mesh.SetIndexFormat(wide ? IndexFormat.UInt32 : IndexFormat.UInt16);

        AccessListBase<ISubMesh> subMeshes = mesh.SubMeshes;
        subMeshes.Clear();
        foreach (MeshSection section in geometry.Sections)
        {
            ISubMesh subMesh = subMeshes.AddNew();
            subMesh.FirstByte = (uint)(section.FirstIndex * indexSize);
            subMesh.IndexCount = (uint)section.IndexCount;
            subMesh.Topology = TriangleTopology;
            subMesh.BaseVertex = 0;
            (uint first, uint count, Vector3 minimum, Vector3 maximum) = SectionBounds(geometry, section);
            subMesh.FirstVertex = first;
            subMesh.VertexCount = count;
            Vector3 center = (minimum + maximum) * 0.5f;
            Vector3 extent = (maximum - minimum) * 0.5f;
            subMesh.LocalAABB.Center.SetValues(center.X, center.Y, center.Z);
            subMesh.LocalAABB.Extent.SetValues(extent.X, extent.Y, extent.Z);
        }
    }

    private static (uint First, uint Count, Vector3 Minimum, Vector3 Maximum) SectionBounds(MeshGeometry geometry, MeshSection section)
    {
        if (section.IndexCount == 0)
        {
            return (0, 0, Vector3.Zero, Vector3.Zero);
        }
        uint first = uint.MaxValue;
        uint last = 0;
        Vector3 minimum = new(float.MaxValue);
        Vector3 maximum = new(float.MinValue);
        for (int i = section.FirstIndex; i < section.FirstIndex + section.IndexCount; i++)
        {
            uint index = geometry.Indices[i];
            first = Math.Min(first, index);
            last = Math.Max(last, index);
            Vector3 position = geometry.Positions[index];
            minimum = Vector3.Min(minimum, position);
            maximum = Vector3.Max(maximum, position);
        }
        return (first, last - first + 1, minimum, maximum);
    }

    private static void WriteSkin(IMesh mesh, MeshGeometry geometry)
    {
        if (geometry.Skin is null || !mesh.Has_Skin())
        {
            return;
        }
        var skin = mesh.Skin!;
        skin.Clear();
        foreach (BoneWeight4 weight in geometry.Skin)
        {
            var entry = skin.AddNew();
            entry.Weight_0_ = weight.Weight0;
            entry.Weight_1_ = weight.Weight1;
            entry.Weight_2_ = weight.Weight2;
            entry.Weight_3_ = weight.Weight3;
            entry.BoneIndex_0_ = weight.Index0;
            entry.BoneIndex_1_ = weight.Index1;
            entry.BoneIndex_2_ = weight.Index2;
            entry.BoneIndex_3_ = weight.Index3;
        }
    }

    private static void WriteBindPoses(IMesh mesh, MeshGeometry geometry)
    {
        if (geometry.BindPoses is null)
        {
            return;
        }
        mesh.BindPose.Clear();
        foreach (Matrix4x4 matrix in geometry.BindPoses)
        {
            mesh.BindPose.AddNew().SetValues(
                matrix.M11, matrix.M12, matrix.M13, matrix.M14,
                matrix.M21, matrix.M22, matrix.M23, matrix.M24,
                matrix.M31, matrix.M32, matrix.M33, matrix.M34,
                matrix.M41, matrix.M42, matrix.M43, matrix.M44);
        }
    }

    private static void WriteBoneNames(IMesh mesh, MeshGeometry geometry)
    {
        if (geometry.BoneNames is null || !mesh.Has_BoneNameHashes())
        {
            return;
        }
        AssetList<uint> hashes = mesh.BoneNameHashes!;
        hashes.Clear();
        foreach (string boneName in geometry.BoneNames)
        {
            hashes.Add(Crc32Algorithm.HashUTF8(boneName));
        }
        if (geometry.RootBoneName is not null && mesh.Has_RootBoneNameHash())
        {
            mesh.RootBoneNameHash = Crc32Algorithm.HashUTF8(geometry.RootBoneName);
        }
    }

    private static void WriteMorphs(IMesh mesh, MeshGeometry geometry)
    {
        if (geometry.Morphs.Length == 0 || !mesh.Has_Shapes())
        {
            return;
        }
        var shapes = mesh.Shapes!;
        shapes.Vertices.Clear();
        shapes.Shapes.Clear();
        shapes.Channels.Clear();
        shapes.FullWeights.Clear();
        foreach (MeshMorph morph in geometry.Morphs)
        {
            uint firstVertex = (uint)shapes.Vertices.Count;
            for (int i = 0; i < morph.VertexIndices.Length; i++)
            {
                BlendShapeVertex vertex = shapes.Vertices.AddNew();
                vertex.Index = morph.VertexIndices[i];
                Vector3 position = morph.PositionDeltas[i];
                vertex.Vertex.SetValues(position.X, position.Y, position.Z);
                if (morph.NormalDeltas is not null)
                {
                    Vector3 normal = morph.NormalDeltas[i];
                    vertex.Normal.SetValues(normal.X, normal.Y, normal.Z);
                }
                if (morph.TangentDeltas is not null)
                {
                    Vector3 tangent = morph.TangentDeltas[i];
                    vertex.Tangent.SetValues(tangent.X, tangent.Y, tangent.Z);
                }
            }
            MeshBlendShape_4_3 shape = shapes.Shapes.AddNew();
            shape.FirstVertex = firstVertex;
            shape.VertexCount = (uint)morph.VertexIndices.Length;
            shape.HasNormals = morph.NormalDeltas is not null;
            shape.HasTangents = morph.TangentDeltas is not null;

            MeshBlendShapeChannel channel = shapes.Channels.AddNew();
            channel.Name = morph.Name;
            channel.NameHash = Crc32Algorithm.HashUTF8(morph.Name);
            channel.FrameIndex = shapes.Shapes.Count - 1;
            channel.FrameCount = 1;
            shapes.FullWeights.Add(100f);
        }
    }
}
