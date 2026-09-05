using AssetRipper.Assets.Bundles;
using AssetRipper.IO.Files;
using AssetRipper.IO.Files.ResourceFiles;
using AssetRipper.IO.Files.Streams.Smart;

namespace Ruri.RipperHook.Conversion;

/// <summary>
/// A resource file whose bytes are fetched when the exporter reads them: Unity's own streamed
/// data mechanism (an asset's m_StreamData naming a .resS file, which AssetRipper reads back
/// through the bundle), pointed at the source archive instead of a copy. A converter reserves a
/// region for the bytes an asset will need and states how to fetch them; nothing is held until
/// the asset is written, and one region at a time is materialized, so a load's peak is what one
/// export needs, not everything it converted.
/// </summary>
public sealed class DeferredResource
{
    public const string FileName = "deferred.ress";

    private readonly object gate = new();
    private readonly List<Region> regions = new();
    private long length;
    private Region? held;
    private byte[]? heldBytes;

    public DeferredResource(Bundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        bundle.AddResource(new ResourceFile(SmartStream.OpenRead(FileName, new DeferredFileSystem(this)), FileName, FileName));
    }

    /// <summary>The offset the bytes will be read from, for the asset's stream data.</summary>
    public long Reserve(long size, Func<byte[]> fetch)
    {
        ArgumentNullException.ThrowIfNull(fetch);
        if (size <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "A region holds at least one byte.");
        }
        lock (gate)
        {
            long offset = length;
            regions.Add(new Region(offset, size, fetch));
            length += size;
            return offset;
        }
    }

    private long Length
    {
        get
        {
            lock (gate)
            {
                return length;
            }
        }
    }

    private int Read(long position, Span<byte> destination)
    {
        Region region;
        byte[] bytes;
        lock (gate)
        {
            Region? found = Find(position);
            if (found is null)
            {
                return 0;
            }
            region = found;
            if (!ReferenceEquals(region, held))
            {
                held = region;
                heldBytes = null;
            }
            heldBytes ??= Materialize(region);
            bytes = heldBytes;
        }
        int within = (int)(position - region.Offset);
        int count = (int)Math.Min(destination.Length, region.Size - within);
        bytes.AsSpan(within, count).CopyTo(destination);
        return count;
    }

    private static byte[] Materialize(Region region)
    {
        byte[] bytes = region.Fetch();
        if (bytes.Length < region.Size)
        {
            throw new InvalidDataException($"[DeferredResource] A region of {region.Size} bytes was fetched as {bytes.Length}.");
        }
        return bytes;
    }

    private Region? Find(long position)
    {
        int low = 0;
        int high = regions.Count - 1;
        while (low <= high)
        {
            int middle = (low + high) >> 1;
            Region candidate = regions[middle];
            if (position < candidate.Offset)
            {
                high = middle - 1;
            }
            else if (position >= candidate.Offset + candidate.Size)
            {
                low = middle + 1;
            }
            else
            {
                return candidate;
            }
        }
        return null;
    }

    private sealed record Region(long Offset, long Size, Func<byte[]> Fetch);

    private sealed class DeferredStream(DeferredResource resource) : Stream
    {
        private long position;

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => resource.Length;

        public override long Position
        {
            get => position;
            set => position = value;
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            int read = resource.Read(position, buffer);
            position += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => position + offset,
                SeekOrigin.End => Length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin), origin, "Unknown seek origin."),
            };
            return position;
        }

        public override void Flush()
        {
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class DeferredFileSystem : FileSystem
    {
        public DeferredFileSystem(DeferredResource resource)
        {
            File = new Files(this, resource);
            Directory = new Directories(this);
            Path = new Paths(this);
        }

        public override FileImplementation File { get; }

        public override DirectoryImplementation Directory { get; }

        public override PathImplementation Path { get; }

        public override string TemporaryDirectory { get; set; } = System.IO.Path.GetTempPath();

        private sealed class Files(FileSystem fileSystem, DeferredResource resource) : FileImplementation(fileSystem)
        {
            public override Stream OpenRead(string path) => new DeferredStream(resource);
        }

        private sealed class Directories(FileSystem fileSystem) : DirectoryImplementation(fileSystem);

        private sealed class Paths(FileSystem fileSystem) : PathImplementation(fileSystem);
    }
}
