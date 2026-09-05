using CUE4Parse.FileProvider.Objects;
using CUE4Parse.UE4.IO;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Shaders;
using CUE4Parse.UE4.VirtualFileSystem;

namespace Ruri.FModelHook.ShaderDecompiler;

/// <summary>
/// Writes a shipped shader archive out as a library file. A serialized archive already is one
/// and is copied as it is; an IoStore archive is a header whose code lives in its container's
/// group chunks, so its library is assembled: the same tables, preload entries covering each
/// map's shaders, and the code body concatenated in shader order out of the groups.
/// </summary>
internal static class Pass010_SaveShaderArchive
{
    public static bool SaveShaderLibrary(GameFile entry, string outputPath, ExportPipelineState? state = null)
    {
        FShaderCodeArchive archive = new(entry.CreateReader());
        if (archive.SerializedShaders is not FIoStoreShaderCodeArchive ioArchive)
        {
            state?.CurrentArchiveShaderMapHashes.Clear();
            File.WriteAllBytes(outputPath, entry.Read());
            return true;
        }
        if (entry is not VfsEntry vfsEntry || vfsEntry.Vfs is not IoStoreReader store)
        {
            return false;
        }
        if (state is not null)
        {
            PopulateArchiveHashes(state, ioArchive.ShaderMapHashes);
        }
        using ShaderLibrary library = ShaderLibrary.FromIoStore(ioArchive, store);
        using FileStream outStream = new(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 1024 * 1024);
        using BinaryWriter writer = new(outStream);
        writer.Write(ShaderLibrary.FileVersion);
        WriteHashes(writer, ioArchive.ShaderMapHashes);
        WriteHashes(writer, ioArchive.ShaderHashes);
        List<(long Offset, long Size)> preloadEntries = new();
        ShaderMapEntry[] mapEntries = new ShaderMapEntry[library.ShaderMapEntries.Length];
        for (int mapIndex = 0; mapIndex < mapEntries.Length; mapIndex++)
        {
            ShaderMapEntry map = library.ShaderMapEntries[mapIndex];
            map.FirstPreloadIndex = (uint)preloadEntries.Count;
            map.NumPreloadEntries = 0;
            for (int member = 0; member < map.NumShaders; member++)
            {
                long position = (long)map.ShaderIndicesOffset + member;
                if (position < library.ShaderIndices.Length)
                {
                    ShaderCodeEntry shader = library.ShaderEntries[(int)library.ShaderIndices[position]];
                    preloadEntries.Add(((long)shader.Offset, shader.Size));
                    map.NumPreloadEntries++;
                }
            }
            mapEntries[mapIndex] = map;
        }
        writer.Write(mapEntries.Length);
        foreach (ShaderMapEntry map in mapEntries)
        {
            writer.Write(map.ShaderIndicesOffset);
            writer.Write(map.NumShaders);
            writer.Write(map.FirstPreloadIndex);
            writer.Write(map.NumPreloadEntries);
        }
        writer.Write(library.ShaderEntries.Length);
        foreach (ShaderCodeEntry shader in library.ShaderEntries)
        {
            writer.Write(shader.Offset);
            writer.Write(shader.Size);
            writer.Write(shader.UncompressedSize);
            writer.Write(shader.Frequency);
        }
        writer.Write(preloadEntries.Count);
        foreach ((long offset, long size) in preloadEntries)
        {
            writer.Write(offset);
            writer.Write(size);
        }
        writer.Write(library.ShaderIndices.Length);
        foreach (uint index in library.ShaderIndices)
        {
            writer.Write(index);
        }
        writer.Flush();
        for (int shader = 0; shader < library.ShaderEntries.Length; shader++)
        {
            int length = (int)library.ShaderEntries[shader].Size;
            if (length <= 0)
            {
                continue;
            }
            if (!library.CopyShaderCode(shader, outStream))
            {
                outStream.Write(new byte[length], 0, length);
            }
        }
        outStream.Flush();
        return true;
    }

    private static void PopulateArchiveHashes(ExportPipelineState state, FSHAHash[]? hashes)
    {
        state.CurrentArchiveShaderMapHashes.Clear();
        if (hashes is null)
        {
            return;
        }
        foreach (FSHAHash hash in hashes)
        {
            state.CurrentArchiveShaderMapHashes.Add(hash.ToString());
        }
    }

    private static void WriteHashes(BinaryWriter writer, FSHAHash[] hashes)
    {
        writer.Write(hashes.Length);
        foreach (FSHAHash hash in hashes)
        {
            writer.Write(hash.Hash);
        }
    }
}
