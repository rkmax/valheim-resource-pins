using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace ResourcePins;

/// <summary>
/// Runs the scan every ScanInterval seconds and holds the registry of the current world and character.
/// It loads the registry when the map loads and saves it whenever the game saves the character, so the registry
/// always matches the pins in the character file.
/// </summary>
internal static class Tracker
{
    private static readonly MapPins _map = new();
    private static readonly List<ZDO> _zdos = new();
    private static readonly List<Sighting> _seen = new();
    private static readonly List<PlaceSighting> _places = new();
    private static readonly List<ChestSighting> _chests = new();
    private static PinRegistry _registry;
    private static string _path;
    private static float _nextScan;

    /// <summary>
    /// Runs inside Minimap.LoadMapData. The game retries it every frame until it returns, so an exception here
    /// would keep the game loading forever. Any failure turns the mod off for the session instead.
    /// </summary>
    internal static void OnMapLoaded(Minimap minimap)
    {
        try
        {
            LoadRegistry(minimap);
        }
        catch (Exception e)
        {
            _registry = null;
            Plugin.Log.LogError($"Could not load the pin registry; pins are off for this session: {e}");
        }
    }

    private static void LoadRegistry(Minimap minimap)
    {
        World world = ZNet.World;
        PlayerProfile profile = Game.instance != null ? Game.instance.GetPlayerProfile() : null;
        if (world == null || profile == null)
            return;

        _path = Path.Combine(Paths.BepInExRootPath, "ResourcePins", FileName($"{world.m_name}_{profile.GetName()}") + ".json");
        _registry = new PinRegistry(Load(_path), world.m_uid) { Log = PinLog.Event };
        _registry.Log += Notices.Event;
        Notices.Mute();
        _registry.Bind(MapPins.All(minimap).Select(p => ((object)p, MapPins.ToV3(p.m_pos), (int)p.m_type)));
        Plugin.Log.LogInfo($"Registry {Path.GetFileName(_path)}: {_registry.Pins.Count} pins.");
    }

    internal static void OnRemovedByHand(Minimap.PinData pin)
    {
        try
        {
            _registry?.OnRemovedByHand(pin);
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Hand-deletion hook failed: {e}");
        }
    }

    internal static void Tick()
    {
        if (_registry == null)
            return;
        if (Minimap.instance == null)
        {
            // Logged out: the logout already saved the character and the registry with it.
            _registry = null;
            return;
        }
        Player player = Player.m_localPlayer;
        if (!Plugin.Enabled.Value || player == null || ZNet.instance == null || ZDOMan.instance == null || Time.time < _nextScan)
            return;
        _nextScan = Time.time + Plugin.ScanInterval.Value;
        Dictionary<int, IndexEntry> index = ResourceIndex.Get();
        if (index == null)
            return;

        Vector3 pos = player.transform.position;
        float radius = Plugin.ScanRadius.Value;
        PlaceIndex places = PlaceIndex.Get();
        Scan(index, places, pos, radius);
        _registry.Observe(_map, _seen, (int)Plugin.PinIcon.Value, Plugin.MergeRadius.Value);
        if (places != null)
            _registry.ObservePlaces(_map, _places, _chests, places.Icon);
        long now = ZNet.instance.GetTime().Ticks;
        _registry.Prune(_map, MapPins.ToV3(pos), radius, Exists, Time.realtimeSinceStartup, now);
        _registry.UpdateChecks(_map, now);
        PinSync.AfterScan(_registry, greet: true);
        Notices.Show();
    }

    /// <summary>The registry as a batch for a player who just joined, or null while it is not loaded.</summary>
    internal static SyncBatch Snapshot(Func<object, string> nameOf) => _registry?.Snapshot(nameOf);

    /// <summary>Another player's batch; a full state on joining stays out of the message feed.</summary>
    internal static void ApplyRemote(SyncBatch batch, bool full)
    {
        if (_registry == null || Minimap.instance == null || ZNet.instance == null)
            return;
        PlaceIndex places = PlaceIndex.Get();
        if (places == null)
            batch.Places.Clear();
        if (full)
            Notices.Mute();
        _registry.ApplyRemote(_map, batch, (int)Plugin.PinIcon.Value, Plugin.MergeRadius.Value, places?.Icon ?? PlaceIcons.Default);
        _registry.UpdateChecks(_map, ZNet.instance.GetTime().Ticks);
    }

    /// <summary>
    /// Indexed ZDOs within <paramref name="radius"/>, read from the sectors around the player. Places and
    /// chests come from every sector read, at any distance. A dungeon's chests are in its interior,
    /// far above the entrance, and a place's chests can be farther than the radius.
    /// </summary>
    private static void Scan(Dictionary<int, IndexEntry> index, PlaceIndex places, Vector3 pos, float radius)
    {
        _zdos.Clear();
        _seen.Clear();
        long now = ZNet.instance.GetTime().Ticks;
        _places.Clear();
        _chests.Clear();
        int rings = Mathf.CeilToInt(radius / ZoneSystem.c_ZoneSize);
        ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(pos), new SimulationDistance(rings, 0, classic: true), _zdos);
        foreach (ZDO zdo in _zdos)
        {
            int prefab = zdo.GetPrefab();
            if (places != null && ScanPlace(places, zdo, prefab))
                continue;
            if (!index.TryGetValue(prefab, out IndexEntry e))
                continue;
            Vector3 at = zdo.GetPosition();
            if (Utils.DistanceXZ(at, pos) > radius || zdo.GetLong(ZDOVars.s_creator) != 0L)
                continue;
            Resource r = e.Resource;
            bool pickable = r.Kind != MemberKind.Mined && r.Root == null;
            long rootReadyAt = r.Root != null
                ? r.Root.ReadyAt(zdo.GetFloat(ZDOVars.s_level, r.Root.MaxLevel), zdo.GetLong(ZDOVars.s_lastTime, now), now)
                : 0L;
            _seen.Add(new Sighting
            {
                Node = Id(zdo),
                Item = r.Item,
                Label = e.Label,
                Kind = r.Kind,
                RespawnMinutes = r.RespawnMinutes,
                Pos = MapPins.ToV3(at),
                Picked = rootReadyAt > 0 || pickable && zdo.GetBool(ZDOVars.s_picked, r.DefaultPicked),
                PickedTime = pickable ? zdo.GetLong(ZDOVars.s_pickedTime) : 0L,
                ReadyAt = rootReadyAt,
                SeenAt = now,
            });
        }
    }

    /// <summary>Collects a configured location's proxy, a world chest or a loot stand; false when the ZDO is none of them.</summary>
    private static bool ScanPlace(PlaceIndex places, ZDO zdo, int prefab)
    {
        if (prefab == PlaceIndex.ProxyPrefab)
        {
            NodeId node = Id(zdo);
            if (!places.Places.TryGetValue(zdo.GetInt(ZDOVars.s_location), out PlaceDef place) || _registry.Knows(node))
                return true;
            string label = PlaceIndex.Label(place, zdo);
            if (label != null)
                _places.Add(new PlaceSighting
                {
                    Node = node,
                    Location = place.Location.m_prefab.Name,
                    Category = place.Category,
                    Label = label,
                    Pos = MapPins.ToV3(zdo.GetPosition()),
                    Interior = place.Interior,
                    Radius = place.Location.m_exteriorRadius,
                });
            return true;
        }
        bool stand = places.Stands.Contains(prefab);
        if (!stand && !places.Chests.Contains(prefab))
            return false;
        if (zdo.GetLong(ZDOVars.s_creator) == 0L)
            _chests.Add(new ChestSighting
            {
                Node = Id(zdo),
                Pos = MapPins.ToV3(zdo.GetPosition()),
                Fill = stand
                    ? zdo.GetBool(ZDOVars.s_picked) ? ChestFill.Empty : ChestFill.Full
                    : PlaceCatalog.ReadChest(zdo.GetBool(ZDOVars.s_addedDefaultItems), zdo.GetByteArray(ZDOVars.s_items)),
            });
        return true;
    }

    private static NodeId Id(ZDO zdo) => new(zdo.m_uid.UserID, zdo.m_uid.ID);

    private static bool Exists(NodeId n) => ZDOMan.instance.GetZDO(new ZDOID(n.User, (uint)n.Id)) != null;

    private static void Flush()
    {
        if (_registry == null || !_registry.Dirty)
            return;
        try
        {
            Save(_registry, _path);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Could not save {_path}: {e.Message}");
        }
    }

    /// <summary>Writes the registry file to a temporary file first, so a crash in the middle keeps the old one.</summary>
    internal static void Save(PinRegistry registry, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, RegistryJson.Write(registry.Export()));
        if (File.Exists(path))
            File.Delete(path);
        File.Move(tmp, path);
        registry.Dirty = false;
    }

    /// <summary>The character file (with its pins) was written: write the registry with it.</summary>
    [HarmonyPatch(typeof(PlayerProfile), nameof(PlayerProfile.Save))]
    private static class SaveWithCharacter
    {
        private static void Postfix(PlayerProfile __instance, bool __result)
        {
            if (__result && Game.instance != null && Game.instance.GetPlayerProfile() == __instance)
                Flush();
        }
    }

    internal static RegistryData Load(string path)
    {
        if (!File.Exists(path))
            return new RegistryData();
        try
        {
            return RegistryJson.Read(File.ReadAllText(path)) ?? new RegistryData();
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Could not read {path}, starting empty: {e.Message}");
            return new RegistryData();
        }
    }

    internal static string FileName(string s) => new(s.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
}
