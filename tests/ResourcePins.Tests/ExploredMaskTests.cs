using System.IO;
using System.IO.Compression;
using System.Linq;
using ResourcePins;
using Xunit;

/// <summary>The server's explored map file: what the tables explored, and only that, at the right pixels.</summary>
public class ExploredMaskTests
{
    private const int Size = 4;

    /// <summary>A table's data as MapTable stores it: gzip of version 3, the explored array, then pins.</summary>
    private static byte[] Table(params int[] explored)
    {
        var raw = new MemoryStream();
        using (var w = new BinaryWriter(raw))
        {
            w.Write(3);
            w.Write(Size * Size);
            for (int i = 0; i < Size * Size; i++)
                w.Write(explored.Contains(i));
            w.Write(1);
            w.Write(42L);
            w.Write("Copper");
        }
        var gz = new MemoryStream();
        using (var z = new GZipStream(gz, CompressionMode.Compress))
            z.Write(raw.ToArray());
        return gz.ToArray();
    }

    [Fact]
    public void FileHasThePixelsExploredOnAnyTable()
    {
        byte[] bits = ExploredMask.Read(Table(0, 5), out int size);
        ExploredMask.Or(bits, ExploredMask.Read(Table(5, 14), out _));
        var file = new MemoryStream();
        ExploredMask.Write(file, size, 12f, bits);

        using var r = new BinaryReader(new GZipStream(new MemoryStream(file.ToArray()), CompressionMode.Decompress));
        Assert.Equal("RPEX", new string(r.ReadChars(4)));
        Assert.Equal(1, r.ReadInt32());
        Assert.Equal(Size, r.ReadInt32());
        Assert.Equal(12f, r.ReadSingle());
        byte[] packed = r.ReadBytes(100);
        int[] explored = Enumerable.Range(0, Size * Size).Where(i => (packed[i >> 3] >> (i & 7) & 1) == 1).ToArray();
        Assert.Equal(new[] { 0, 5, 14 }, explored);
        Assert.Equal(Size * Size / 8, packed.Length);
    }

    [Fact]
    public void TableWithoutSquareMapIsSkipped()
    {
        var raw = new MemoryStream();
        using (var w = new BinaryWriter(raw))
        {
            w.Write(3);
            w.Write(24);
            w.Write(new byte[24]);
        }
        var gz = new MemoryStream();
        using (var z = new GZipStream(gz, CompressionMode.Compress))
            z.Write(raw.ToArray());
        Assert.Null(ExploredMask.Read(gz.ToArray(), out _));
    }
}
