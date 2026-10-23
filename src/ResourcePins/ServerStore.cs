using System;
using System.IO;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace ResourcePins;

/// <summary>
/// On a dedicated server, keeps one registry with no map. Every player's states and deltas go in, and it gives
/// the registry to each player who joins. So what one group found reaches the next, even if nobody stayed online in between.
/// It is saved whenever the server saves the world. Not active on players' games, a host included.
/// </summary>
internal static class ServerStore
{
    private static readonly NoMap _map = new();
    private static PinRegistry _registry;
    private static string _path;
    private static float _nextTick;

    internal static bool Active => ZNet.instance != null && ZNet.instance.IsDedicated() && ZNet.World != null;

    internal static void Tick()
    {
        if (!Active || Time.time < _nextTick || Get() == null)
            return;
        _nextTick = Time.time + Plugin.ScanInterval.Value;
        PinSync.AfterScan(_registry, greet: false);
    }

    internal static SyncBatch Snapshot(Func<object, string> nameOf) => Get()?.Snapshot(nameOf);

    internal static void ApplyRemote(SyncBatch batch) =>
        Get()?.ApplyRemote(_map, batch, (int)Plugin.PinIcon.Value, Plugin.MergeRadius.Value, (int)Plugin.PlaceIcon.Value);

    /// <summary>The store of the loaded world, read from its file the first time.</summary>
    private static PinRegistry Get()
    {
        World world = ZNet.World;
        string path = Path.Combine(Paths.BepInExRootPath, "ResourcePins", Tracker.FileName(world.m_name) + "_server.json");
        if (_registry != null && _path == path)
            return _registry;
        _path = path;
        _registry = new PinRegistry(Tracker.Load(path), world.m_uid) { Log = PinLog.Event };
        Plugin.Log.LogInfo($"Server store {Path.GetFileName(path)}: {_registry.Pins.Count} pins.");
        return _registry;
    }

    /// <summary>The world was written (autosave or shutdown): write the store with it.</summary>
    [HarmonyPatch(typeof(ZNet), "SaveWorld")]
    private static class SaveWithWorld
    {
        private static void Postfix()
        {
            if (_registry == null || !_registry.Dirty || !Active)
                return;
            try
            {
                Tracker.Save(_registry, _path);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not save {_path}: {e.Message}");
            }
        }
    }
}
