using System;
using System.Collections.Generic;
using System.Linq;

namespace ResourcePins;

/// <summary>What a world chest's ZDO says about its contents.</summary>
public enum ChestFill
{
    /// <summary>Default loot not rolled yet, or no saved inventory: left out of the place's known chests.</summary>
    Unknown,
    Empty,
    Full,
}

/// <summary>
/// Which locations get a place pin, plus the Unity-free rules for their loot chests and icon. Location names
/// are the exact prefab names from ZoneSystem.m_locations, grouped by category.
/// </summary>
public sealed class PlaceCatalog
{
    public const string Dungeons = "Dungeons";

    /// <summary>Interiors sit 5000 m above the entrance (Location.Awake); nothing outdoors is this high.</summary>
    public const float InteriorMinY = 4000f;

    private readonly Dictionary<string, string> _category = new();

    /// <param name="lists">(category, comma-separated location names); a name listed twice keeps its first category.</param>
    /// <param name="known">Whether the game has a location by that name.</param>
    public PlaceCatalog(IEnumerable<(string Category, string Names)> lists, Func<string, bool> known, out List<string> unknown)
    {
        unknown = new List<string>();
        foreach ((string category, string names) in lists)
            foreach (string name in (names ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0))
            {
                if (!known(name))
                    unknown.Add(name);
                else if (!_category.ContainsKey(name))
                    _category[name] = category;
            }
    }

    public IReadOnlyDictionary<string, string> Places => _category;

    /// <summary>The category of a location, or null if it gets no pin.</summary>
    public string Category(string location) => _category.TryGetValue(location, out string c) ? c : null;

    /// <summary>Only dungeons and camps get the looted X; boss altars and traders never do.</summary>
    public static bool HasLoot(string category) => category == Dungeons || category == "Camps";

    /// <summary>Dungeon loot is in the interior; every other place keeps its chests outdoors.</summary>
    public static bool IsInterior(string category) => category == Dungeons;

    /// <summary>Pin name for a location the game gives no name.</summary>
    public static string Fallback(string category) => category switch
    {
        Dungeons => "Dungeon",
        "Camps" => "Camp",
        "Bosses" => "Boss altar",
        "Traders" => "Trader",
        _ => category,
    };

    /// <summary>
    /// Reads Container's s_items blob (Inventory.Save: int version, then ushort item count from version 108,
    /// int before) without loading the items. Unknown until AddDefaultItems has run and the inventory was saved.
    /// </summary>
    public static ChestFill ReadChest(bool addedDefaultItems, byte[] items)
    {
        if (!addedDefaultItems || items == null || items.Length < 6)
            return ChestFill.Unknown;
        int version = items[0] | items[1] << 8 | items[2] << 16 | items[3] << 24;
        int count;
        if (version >= 108)
            count = items[4] | items[5] << 8;
        else if (items.Length >= 8)
            count = items[4] | items[5] << 8 | items[6] << 16 | items[7] << 24;
        else
            return ChestFill.Unknown;
        return count == 0 ? ChestFill.Empty : ChestFill.Full;
    }

    /// <summary>
    /// Whether a chest belongs to the place at <paramref name="anchor"/>. For an interior: any interior chest
    /// in the entrance's zone (it is built at the zone centre). Outdoors: chests inside the location's
    /// exterior radius.
    /// </summary>
    public static bool OwnsChest(V3 anchor, bool interior, float radius, V3 chest)
    {
        if (interior)
            return chest.Y > InteriorMinY && Zone(chest.X) == Zone(anchor.X) && Zone(chest.Z) == Zone(anchor.Z);
        return chest.Y <= InteriorMinY && V3.DistanceXZ(anchor, chest) <= radius;
    }

    /// <summary>One axis of ZoneSystem.GetZone (64 m zones centred on multiples of 64).</summary>
    private static int Zone(float v) => (int)Math.Floor((float)((v + 32.0) / 64.0));
}

/// <summary>Map icons a place pin may use; numbers are Minimap.PinType values.</summary>
public static class PlaceIcons
{
    public const int Icon0 = 0, Icon1 = 1, Icon2 = 2, Icon4 = 6;
    public const int Default = Icon1;
    private static readonly int[] Allowed = { Icon1, Icon0, Icon2, Icon4 };

    /// <summary>
    /// The place icon to use. It must be one of the plain icons and differ from the resource icon, because the
    /// registry binds its entries to map pins by type and position.
    /// </summary>
    public static int Resolve(int requested, int pinIcon, out bool fellBack)
    {
        fellBack = requested == pinIcon || Array.IndexOf(Allowed, requested) < 0;
        return fellBack ? Allowed.First(i => i != pinIcon) : requested;
    }
}
