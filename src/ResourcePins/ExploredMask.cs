using System;
using System.IO;
using System.IO.Compression;

namespace ResourcePins;

/// <summary>
/// The explored part of the cartography tables' shared map, packed one bit per minimap pixel.
///
/// A table keeps gzip(Minimap.GetSharedMapData): int version, int count (size * size), count bools of one byte each
/// (row-major, index = y * size + x), then the shared pins, which are not read here.
///
/// File written by <see cref="Write"/> (all little-endian, the whole file gzip'd):
///   "RPEX"          4 bytes, magic
///   version         int32, 1
///   size            int32, minimap texture size in pixels (2048)
///   pixelSize       float32, world metres per pixel (12)
///   bits            size * size / 8 bytes; pixel i = y * size + x is explored when bit (i &amp; 7) of byte (i >> 3) is set
/// Pixel (x, y) is centred on world x = (x - size / 2) * pixelSize, z = (y - size / 2) * pixelSize, as Minimap.WorldToPixel.
/// </summary>
internal static class ExploredMask
{
    internal const int FileVersion = 1;

    /// <summary>Minimap.m_pixelSize on the Minimap prefab. A dedicated server has no minimap to read it from.</summary>
    internal const float PixelSize = 12f;

    /// <summary>The explored bits of one table's data, or null when its explored array is not a square texture.</summary>
    internal static byte[] Read(byte[] tableData, out int size)
    {
        size = 0;
        using var reader = new BinaryReader(new GZipStream(new MemoryStream(tableData), CompressionMode.Decompress));
        reader.ReadInt32();
        int count = reader.ReadInt32();
        int side = (int)Math.Round(Math.Sqrt(count));
        if (count <= 0 || side * side != count || count % 8 != 0)
            return null;
        byte[] explored = reader.ReadBytes(count);
        if (explored.Length != count)
            return null;
        var bits = new byte[count / 8];
        for (int i = 0; i < count; i++)
            if (explored[i] != 0)
                bits[i >> 3] |= (byte)(1 << (i & 7));
        size = side;
        return bits;
    }

    /// <summary>Adds the explored bits of <paramref name="other"/> to <paramref name="bits"/>.</summary>
    internal static void Or(byte[] bits, byte[] other)
    {
        for (int i = 0; i < bits.Length; i++)
            bits[i] |= other[i];
    }

    internal static void Write(Stream output, int size, float pixelSize, byte[] bits)
    {
        using var gzip = new GZipStream(output, CompressionMode.Compress, leaveOpen: true);
        using var writer = new BinaryWriter(gzip);
        writer.Write(new[] { (byte)'R', (byte)'P', (byte)'E', (byte)'X' });
        writer.Write(FileVersion);
        writer.Write(size);
        writer.Write(pixelSize);
        writer.Write(bits);
    }
}
