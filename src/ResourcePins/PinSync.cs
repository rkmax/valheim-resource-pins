using System;
using System.Collections.Generic;
using HarmonyLib;

namespace ResourcePins;

/// <summary>
/// Shares the registry with other players who have the mod, using routed RPCs. The server relays them to
/// every peer and ignores names it has no handler for, so a vanilla server works. A player who joins sends
/// its full state to everyone and every peer answers with its own. After that each player sends only what its
/// own scans changed. A dedicated server with the mod answers too, from a store of what every
/// player sent (<see cref="ServerStore"/>), so what one group found reaches the next. A full state goes out
/// in small parts for the crossplay relay, one per frame. Players without the mod never see the
/// messages.
/// </summary>
internal static class PinSync
{
    private const int Protocol = 2;
    private const string State = "ResourcePins_State";
    private const string Delta = "ResourcePins_Delta";

    /// <summary>Entries per message: about 150 bytes each at most, to stay under 10 KB (Valheim keeps its own packages under that).</summary>
    private const int PartSize = 50;

    private static readonly Queue<(long Target, string Method, ZPackage Pkg)> _outbox = new();
    private static bool _greeted;

    /// <summary>ZNet.Awake builds a new ZRoutedRpc for every session: register on it.</summary>
    [HarmonyPatch(typeof(ZNet), "Awake")]
    private static class Register
    {
        private static void Postfix()
        {
            _greeted = false;
            _outbox.Clear();
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null)
                return;
            rpc.Register<ZPackage>(State, (sender, pkg) => OnBatch(sender, pkg, full: true));
            rpc.Register<ZPackage>(Delta, (sender, pkg) => OnBatch(sender, pkg, full: false));
        }
    }

    /// <summary>
    /// Runs after each scan of a player, or each tick of the server's store. A player sends its full state once per
    /// session, then only what changed.
    /// </summary>
    internal static void AfterScan(PinRegistry registry, bool greet)
    {
        SyncBatch delta = registry.TakeDelta(NameOf);
        if (!Plugin.Sync.Value || ZRoutedRpc.instance == null || ZNet.World == null)
            return;
        if (greet && !_greeted)
        {
            _greeted = true;
            Send(ZRoutedRpc.Everybody, State, registry.Snapshot(NameOf), askBack: true);
        }
        if (delta != null && !delta.IsEmpty)
            Send(ZRoutedRpc.Everybody, Delta, delta, askBack: false);
    }

    /// <summary>Sends one queued message per frame, so a full state does not land on the relay at once.</summary>
    internal static void Flush()
    {
        if (_outbox.Count == 0)
            return;
        if (ZRoutedRpc.instance == null)
        {
            _outbox.Clear();
            return;
        }
        (long target, string method, ZPackage pkg) = _outbox.Dequeue();
        ZRoutedRpc.instance.InvokeRoutedRPC(target, method, pkg);
    }

    private static void OnBatch(long sender, ZPackage pkg, bool full)
    {
        if (IsSelf(sender) || !Accept(pkg))
            return;
        try
        {
            bool askBack = pkg.ReadBool();
            SyncBatch batch = Read(pkg);
            PinLog.Sync(full ? "state" : "delta", $"from {sender}: {Count(batch)}");
            bool server = ServerStore.Active;
            if (server)
                ServerStore.ApplyRemote(batch);
            else
                Tracker.ApplyRemote(batch, full);
            if (askBack)
            {
                SyncBatch state = server ? ServerStore.Snapshot(NameOf) : Tracker.Snapshot(NameOf);
                if (state != null)
                    Send(sender, State, state, askBack: false);
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Ignored a pin batch from {sender}: {e.Message}");
        }
    }

    // Everybody includes the sender itself.
    private static bool IsSelf(long sender) => sender == ZDOMan.GetSessionID();

    /// <summary>Same protocol and same world, with sharing on.</summary>
    private static bool Accept(ZPackage pkg) =>
        Plugin.Sync.Value && ZNet.World != null && pkg.ReadInt() == Protocol && pkg.ReadLong() == ZNet.World.m_uid;

    /// <summary>Queues the batch in parts; only the first part of a full state asks the receivers for theirs.</summary>
    private static void Send(long target, string method, SyncBatch batch, bool askBack)
    {
        List<SyncBatch> parts = batch.Split(PartSize);
        for (int i = 0; i < parts.Count; i++)
        {
            var pkg = new ZPackage();
            pkg.Write(Protocol);
            pkg.Write(ZNet.World.m_uid);
            pkg.Write(askBack && i == 0);
            Write(pkg, parts[i]);
            _outbox.Enqueue((target, method, pkg));
        }
        PinLog.Sync(method == State ? "state" : "delta", $"to {target}: {Count(batch)} in {parts.Count} parts");
    }

    private static string NameOf(object pin) => pin is Minimap.PinData p ? p.m_name : "";

    private static string Count(SyncBatch b) =>
        $"{b.Resources.Count} resources {b.Places.Count} places {b.Chests.Count} chests {b.Gone.Count} gone";

    private static void Write(ZPackage pkg, SyncBatch b)
    {
        pkg.Write(b.Resources.Count);
        foreach (Sighting s in b.Resources)
        {
            WriteNode(pkg, s.Node);
            pkg.Write(s.Item);
            pkg.Write(s.Label ?? "");
            pkg.Write((int)s.Kind);
            pkg.Write(s.RespawnMinutes);
            WritePos(pkg, s.Pos);
            pkg.Write(s.Picked);
            pkg.Write(s.PickedTime);
            pkg.Write(s.ReadyAt);
            pkg.Write(s.SeenAt);
        }
        pkg.Write(b.Places.Count);
        foreach (PlaceSighting s in b.Places)
        {
            WriteNode(pkg, s.Node);
            pkg.Write(s.Location);
            pkg.Write(s.Category);
            pkg.Write(s.Label ?? "");
            WritePos(pkg, s.Pos);
            pkg.Write(s.Interior);
            pkg.Write(s.Radius);
        }
        pkg.Write(b.Chests.Count);
        foreach (ChestSighting c in b.Chests)
        {
            WriteNode(pkg, c.Node);
            WritePos(pkg, c.Pos);
            pkg.Write((int)c.Fill);
        }
        pkg.Write(b.Gone.Count);
        foreach (GoneNode g in b.Gone)
        {
            WriteNode(pkg, g.Node);
            pkg.Write(g.At);
        }
    }

    private static SyncBatch Read(ZPackage pkg)
    {
        var b = new SyncBatch();
        for (int i = pkg.ReadInt(); i > 0; i--)
            b.Resources.Add(new Sighting
            {
                Node = ReadNode(pkg), Item = pkg.ReadString(), Label = pkg.ReadString(), Kind = (MemberKind)pkg.ReadInt(),
                RespawnMinutes = pkg.ReadSingle(), Pos = ReadPos(pkg), Picked = pkg.ReadBool(), PickedTime = pkg.ReadLong(),
                ReadyAt = pkg.ReadLong(), SeenAt = pkg.ReadLong(),
            });
        for (int i = pkg.ReadInt(); i > 0; i--)
            b.Places.Add(new PlaceSighting
            {
                Node = ReadNode(pkg), Location = pkg.ReadString(), Category = pkg.ReadString(), Label = pkg.ReadString(),
                Pos = ReadPos(pkg), Interior = pkg.ReadBool(), Radius = pkg.ReadSingle(),
            });
        for (int i = pkg.ReadInt(); i > 0; i--)
            b.Chests.Add(new ChestSighting { Node = ReadNode(pkg), Pos = ReadPos(pkg), Fill = (ChestFill)pkg.ReadInt() });
        for (int i = pkg.ReadInt(); i > 0; i--)
            b.Gone.Add(new GoneNode(ReadNode(pkg), pkg.ReadLong()));
        return b;
    }

    private static void WriteNode(ZPackage pkg, NodeId n)
    {
        pkg.Write(n.User);
        pkg.Write(n.Id);
    }

    private static NodeId ReadNode(ZPackage pkg) => new(pkg.ReadLong(), pkg.ReadLong());

    private static void WritePos(ZPackage pkg, V3 v)
    {
        pkg.Write(v.X);
        pkg.Write(v.Y);
        pkg.Write(v.Z);
    }

    private static V3 ReadPos(ZPackage pkg) => new(pkg.ReadSingle(), pkg.ReadSingle(), pkg.ReadSingle());
}
