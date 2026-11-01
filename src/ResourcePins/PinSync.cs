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
/// messages. Peers on another protocol (<see cref="SyncWire"/>) are ignored, and logged once.
/// </summary>
internal static class PinSync
{
    private const string State = "ResourcePins_State";
    private const string Delta = "ResourcePins_Delta";

    /// <summary>Entries per message: about 150 bytes each at most, to stay under 10 KB (Valheim keeps its own packages under that).</summary>
    private const int PartSize = 50;

    private static readonly Queue<(long Target, string Method, ZPackage Pkg)> _outbox = new();
    private static bool _greeted;
    private static readonly HashSet<long> _mismatched = new();

    /// <summary>ZNet.Awake builds a new ZRoutedRpc for every session: register on it.</summary>
    [HarmonyPatch(typeof(ZNet), "Awake")]
    private static class Register
    {
        private static void Postfix()
        {
            _greeted = false;
            _outbox.Clear();
            _mismatched.Clear();
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
        if (IsSelf(sender) || !Plugin.Sync.Value || ZNet.World == null)
            return;
        try
        {
            if (!SyncWire.TryRead(pkg.GetArray(), ZNet.World.m_uid, out int protocol, out bool askBack, out SyncBatch batch))
            {
                if (protocol != SyncWire.Protocol && _mismatched.Add(sender))
                    Plugin.Log.LogWarning($"Ignored pins from {sender}: it sends ResourcePins protocol {protocol}, this version " +
                                          $"speaks {SyncWire.Protocol}. Every player and the server need a version on the same protocol.");
                return;
            }
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

    /// <summary>Queues the batch in parts; only the first part of a full state asks the receivers for theirs.</summary>
    private static void Send(long target, string method, SyncBatch batch, bool askBack)
    {
        List<SyncBatch> parts = batch.Split(PartSize);
        for (int i = 0; i < parts.Count; i++)
            _outbox.Enqueue((target, method, new ZPackage(SyncWire.Write(ZNet.World.m_uid, askBack && i == 0, parts[i]))));
        PinLog.Sync(method == State ? "state" : "delta", $"to {target}: {Count(batch)} in {parts.Count} parts");
    }

    private static string NameOf(object pin) => pin is Minimap.PinData p ? p.m_name : "";

    private static string Count(SyncBatch b) =>
        $"{b.Resources.Count} resources {b.Places.Count} places {b.Chests.Count} chests {b.Gone.Count} gone";
}
