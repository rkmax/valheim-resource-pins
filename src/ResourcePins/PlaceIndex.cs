using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ResourcePins;

/// <summary>A configured location: its category and the area its loot chests are in.</summary>
internal sealed class PlaceDef
{
    public ZoneSystem.ZoneLocation Location;
    public string Category;
    public bool Interior;
}

/// <summary>
/// location hash -> configured place, and the prefabs that are chests or loot stands. Built once per session from
/// ZoneSystem.m_locations (SetupLocations fills it on clients too) and ZNetScene's prefab list.
/// </summary>
internal sealed class PlaceIndex
{
    internal static readonly int ProxyPrefab = "LocationProxy".GetStableHashCode();

    private static readonly HashSet<string> _warned = new();
    private static PlaceIndex _index;
    private static ZNetScene _builtFor;

    /// <summary>LocationProxy's s_location (the location prefab's stable hash) -> place.</summary>
    public readonly Dictionary<int, PlaceDef> Places = new();
    public readonly HashSet<int> Chests = new();

    /// <summary>Pickables that count as loot like a chest (LootStands): empty once picked.</summary>
    public readonly HashSet<int> Stands = new();
    public int Icon;

    internal static void Reset() => _index = null;

    /// <summary>The index, or null until ZNetScene and ZoneSystem are ready.</summary>
    internal static PlaceIndex Get()
    {
        ZNetScene scene = ZNetScene.instance;
        ZoneSystem zones = ZoneSystem.instance;
        if (scene == null || zones == null || zones.m_locations.Count == 0)
            return null;
        if (_index != null && _builtFor == scene)
            return _index;

        var byName = new Dictionary<string, ZoneSystem.ZoneLocation>();
        foreach (ZoneSystem.ZoneLocation l in zones.m_locations)
            if (l.m_prefab.IsValid && !byName.ContainsKey(l.m_prefab.Name))
                byName[l.m_prefab.Name] = l;

        var catalog = new PlaceCatalog(new[]
        {
            (PlaceCatalog.Spawners, Plugin.Spawners.Value),
            (PlaceCatalog.Dungeons, Plugin.Dungeons.Value),
            ("Camps", Plugin.Camps.Value),
            ("Bosses", Plugin.Bosses.Value),
            ("Traders", Plugin.Traders.Value),
        }, byName.ContainsKey, out List<string> unknown);
        foreach (string name in unknown.Where(_warned.Add))
            Plugin.Log.LogWarning($"Unknown location '{name}' (names are case-sensitive); ignored.");

        _index = new PlaceIndex();
        _builtFor = scene;
        foreach (KeyValuePair<string, string> p in catalog.Places)
        {
            ZoneSystem.ZoneLocation l = byName[p.Key];
            bool? hasInterior = null;
            if (PlaceCatalog.IsInterior(p.Value) || Plugin.LogEvents.Value)
            {
                // m_hasInterior is on the prefab: load it for the check as ZoneSystem does, and let it go again.
                // Dungeons without one (CharredFortress...) keep their loot outdoors.
                l.m_prefab.Load();
                Location prefab = l.m_prefab.Asset != null ? l.m_prefab.Asset.GetComponent<Location>() : null;
                hasInterior = prefab != null ? prefab.m_hasInterior : null;
                l.m_prefab.Release();
            }
            _index.Places[l.Hash] = new PlaceDef
            {
                Location = l, Category = p.Value, Interior = PlaceCatalog.IsInterior(p.Value) && hasInterior != false,
            };
            if (Plugin.LogEvents.Value)
                PinLog.Place(p.Key, p.Value, hasInterior?.ToString() ?? "?", l.m_exteriorRadius, l.m_interiorRadius);
        }

        foreach (GameObject prefab in scene.m_prefabs)
            if (prefab != null && prefab.GetComponentInChildren<Container>(true) != null &&
                prefab.GetComponent<Character>() == null && prefab.GetComponent<TombStone>() == null)
                _index.Chests.Add(prefab.name.GetStableHashCode());

        foreach (string name in (Plugin.LootStands.Value ?? "").Split(',').Select(n => n.Trim()).Where(n => n.Length > 0))
        {
            GameObject prefab = scene.GetPrefab(name);
            if (prefab != null && prefab.GetComponent<Pickable>() != null)
                _index.Stands.Add(prefab.name.GetStableHashCode());
            else if (_warned.Add(name))
                Plugin.Log.LogWarning($"Unknown loot stand '{name}' (names are case-sensitive, it must be a Pickable); ignored.");
        }

        int pinIcon = (int)Plugin.PinIcon.Value;
        _index.Icon = PlaceIcons.Resolve((int)Plugin.PlaceIcon.Value, pinIcon, out bool fellBack);
        if (fellBack)
            Plugin.Log.LogWarning($"PlaceIcon {Plugin.PlaceIcon.Value} must be Icon0, Icon1, Icon2 or Icon4 and differ from PinIcon; " +
                                  $"using {(Minimap.PinType)_index.Icon}.");

        Plugin.Log.LogInfo($"Indexed {_index.Places.Count} places, {_index.Chests.Count} chest prefabs and {_index.Stands.Count} loot stands.");
        return _index;
    }

    /// <summary>
    /// The location's own name token (its discover label, or the text the game shows when you enter its
    /// dungeon through a Teleport), else a name from the creature it spawns, or the category's name if it has none. Null while there is no prefab or spawned location to read it from.
    /// </summary>
    internal static string Label(PlaceDef place, ZDO proxy)
    {
        Location loc = place.Location.m_prefab.IsLoaded ? place.Location.m_prefab.Asset.GetComponent<Location>() : null;
        if (loc == null)
        {
            // LocationProxy spawns the location as its child.
            ZNetView view = ZNetScene.instance.FindInstance(proxy);
            loc = view != null ? view.GetComponentInChildren<Location>() : null;
        }
        if (loc == null)
            return null;
        return Named(loc, place.Category) ?? PlaceCatalog.Fallback(place.Category);
    }

    private static string Named(Location loc, string category) =>
        NameToken(loc) ?? PlaceCatalog.CreatureName(category, Creature(loc));

    /// <summary>
    /// The creature a location spawns most, by its Character name: each CreatureSpawner counts one, each SpawnArea
    /// one shared out by the weights of its entries. Null if it spawns none.
    /// </summary>
    private static string Creature(Location loc)
    {
        var found = new List<(string, float)>();
        foreach (CreatureSpawner s in loc.GetComponentsInChildren<CreatureSpawner>(true))
            found.Add((CharacterName(s.m_creaturePrefab), 1f));
        foreach (SpawnArea a in loc.GetComponentsInChildren<SpawnArea>(true))
        {
            float total = a.m_prefabs.Sum(p => p.m_weight);
            foreach (SpawnArea.SpawnData p in a.m_prefabs)
                found.Add((CharacterName(p.m_prefab), p.m_weight / total));
        }
        return PlaceCatalog.Dominant(found);
    }

    private static string CharacterName(GameObject prefab)
    {
        Character c = prefab != null ? prefab.GetComponent<Character>() : null;
        return c != null && IsNamed(c.m_name) ? Localization.instance.Localize(c.m_name) : null;
    }

    private static string NameToken(Location loc)
    {
        string token = loc.m_discoverLabel;
        return IsNamed(token) ? token : loc.GetComponentsInChildren<Teleport>(true).Select(t => t.m_enterText).FirstOrDefault(IsNamed);
    }

    /// <summary>
    /// The name of a configured location, read from its prefab (loaded for the read, as Get does for
    /// m_hasInterior), or null if it is not configured or the game gives it no name.
    /// </summary>
    internal string PrefabLabel(string location)
    {
        PlaceDef place = Places.Values.FirstOrDefault(p => p.Location.m_prefab.Name == location);
        if (place == null)
            return null;
        place.Location.m_prefab.Load();
        try
        {
            GameObject asset = place.Location.m_prefab.Asset;
            Location loc = asset != null ? asset.GetComponent<Location>() : null;
            return loc != null ? Named(loc, place.Category) : null;
        }
        finally
        {
            place.Location.m_prefab.Release();
        }
    }

    private static bool IsNamed(string token) =>
        !string.IsNullOrEmpty(token) && !Localization.instance.Localize(token).StartsWith("[");
}
