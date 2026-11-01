using System.IO;

namespace ResourcePins;

/// <summary>
/// The bytes of one PinSync message: protocol, world uid, ask-back flag, then the batch. Every version reads the
/// protocol first and drops a message of any other, so peers on different versions ignore each other.
/// Protocol 3 names nodes, places and chests by item and position; protocol 2 sent ZDOIDs, which a world load renumbers,
/// so the two cannot be mixed: every player and the server need a version on the same protocol.
/// </summary>
public static class SyncWire
{
    public const int Protocol = 3;

    public static byte[] Write(long world, bool askBack, SyncBatch b)
    {
        var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream))
        {
            w.Write(Protocol);
            w.Write(world);
            w.Write(askBack);
            w.Write(b.Resources.Count);
            foreach (Sighting s in b.Resources)
            {
                w.Write(s.Item);
                w.Write(s.Label ?? "");
                w.Write((int)s.Kind);
                w.Write(s.RespawnMinutes);
                WritePos(w, s.Pos);
                WritePos(w, s.Anchor ?? s.Pos);
                w.Write(s.Picked);
                w.Write(s.PickedTime);
                w.Write(s.ReadyAt);
                w.Write(s.SeenAt);
            }
            w.Write(b.Places.Count);
            foreach (PlaceSighting s in b.Places)
            {
                w.Write(s.Location);
                w.Write(s.Category);
                w.Write(s.Label ?? "");
                WritePos(w, s.Pos);
                w.Write(s.Interior);
                w.Write(s.Radius);
            }
            w.Write(b.Chests.Count);
            foreach (ChestSighting c in b.Chests)
            {
                WritePos(w, c.Pos);
                w.Write((int)c.Fill);
            }
            w.Write(b.Gone.Count);
            foreach (GoneNode g in b.Gone)
            {
                w.Write(g.Item);
                WritePos(w, g.Pos);
                w.Write(g.At);
            }
        }
        return stream.ToArray();
    }

    /// <summary>The message's batch, or false when it is of another protocol (<paramref name="protocol"/>) or another world.</summary>
    public static bool TryRead(byte[] data, long world, out int protocol, out bool askBack, out SyncBatch batch)
    {
        askBack = false;
        batch = null;
        using var r = new BinaryReader(new MemoryStream(data));
        protocol = r.ReadInt32();
        if (protocol != Protocol || r.ReadInt64() != world)
            return false;
        askBack = r.ReadBoolean();
        var b = new SyncBatch();
        for (int i = r.ReadInt32(); i > 0; i--)
            b.Resources.Add(new Sighting
            {
                Item = r.ReadString(), Label = r.ReadString(), Kind = (MemberKind)r.ReadInt32(), RespawnMinutes = r.ReadSingle(),
                Pos = ReadPos(r), Anchor = ReadPos(r), Picked = r.ReadBoolean(), PickedTime = r.ReadInt64(),
                ReadyAt = r.ReadInt64(), SeenAt = r.ReadInt64(),
            });
        for (int i = r.ReadInt32(); i > 0; i--)
            b.Places.Add(new PlaceSighting
            {
                Location = r.ReadString(), Category = r.ReadString(), Label = r.ReadString(),
                Pos = ReadPos(r), Interior = r.ReadBoolean(), Radius = r.ReadSingle(),
            });
        for (int i = r.ReadInt32(); i > 0; i--)
            b.Chests.Add(new ChestSighting { Pos = ReadPos(r), Fill = (ChestFill)r.ReadInt32() });
        for (int i = r.ReadInt32(); i > 0; i--)
            b.Gone.Add(new GoneNode(r.ReadString(), ReadPos(r), r.ReadInt64()));
        batch = b;
        return true;
    }

    private static void WritePos(BinaryWriter w, V3 v)
    {
        w.Write(v.X);
        w.Write(v.Y);
        w.Write(v.Z);
    }

    private static V3 ReadPos(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
}
