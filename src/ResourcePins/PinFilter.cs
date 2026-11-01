using System;
using System.Collections.Generic;
using System.Linq;

namespace ResourcePins;

/// <summary>
/// Which of the mod's pins the large map shows (display only, nothing saved). A filter is "" (everything),
/// <see cref="Places"/>, a place category (<see cref="Category"/>), a biome group (<see cref="Biome"/>) or an item prefab.
/// An item belongs to the one biome where most of its pins are.
/// </summary>
public static class PinFilter
{
    public const string Places = "@places";
    private const string CategoryPrefix = "@place:", BiomePrefix = "@biome:";

    /// <summary>Heightmap.Biome names, in progression order.</summary>
    public static readonly string[] Biomes =
        { "Meadows", "BlackForest", "Swamp", "Mountain", "Plains", "Mistlands", "AshLands", "DeepNorth", "Ocean" };

    private static readonly string[] Categories = { "Dungeons", "Camps", "Spawners", "Bosses", "Traders", "LootStands" };

    public static string Category(string category) => CategoryPrefix + category;

    public static string Biome(string biome) => BiomePrefix + biome;

    /// <summary>The category or biome name a group filter holds; null for the others.</summary>
    public static string GroupName(string filter) =>
        filter.StartsWith(CategoryPrefix) ? filter.Substring(CategoryPrefix.Length) :
        filter.StartsWith(BiomePrefix) ? filter.Substring(BiomePrefix.Length) : null;

    /// <summary>A header can be collapsed: the places group and each biome group.</summary>
    public static bool IsHeader(string filter) => filter == Places || filter.StartsWith(BiomePrefix);

    /// <summary>Listed under a header: a place category or an item.</summary>
    public static bool IsChild(string filter) => filter.Length > 0 && !IsHeader(filter);

    /// <summary>The biome each item is grouped under: the one with most of its pins, the earlier one on a tie.</summary>
    public static Dictionary<string, string> ItemBiomes(IEnumerable<PinRecord> pins, Func<PinRecord, string> biomeOf)
    {
        var votes = new Dictionary<string, int[]>();
        foreach (PinRecord pin in pins.Where(p => !p.IsPlace))
        {
            if (!votes.TryGetValue(pin.Item, out int[] v))
                votes[pin.Item] = v = new int[Biomes.Length];
            int b = Array.IndexOf(Biomes, biomeOf(pin));
            if (b >= 0)
                v[b]++;
        }
        var result = new Dictionary<string, string>();
        foreach (KeyValuePair<string, int[]> item in votes)
        {
            int best = 0;
            for (int b = 1; b < Biomes.Length; b++)
                if (item.Value[b] > item.Value[best])
                    best = b;
            result[item.Key] = Biomes[best];
        }
        return result;
    }

    public static bool Hidden(PinRecord pin, string filter, IReadOnlyDictionary<string, string> itemBiomes)
    {
        if (string.IsNullOrEmpty(filter))
            return false;
        if (filter == Places)
            return !pin.IsPlace;
        if (filter.StartsWith(CategoryPrefix))
            return !pin.IsPlace || pin.Category != GroupName(filter);
        if (filter.StartsWith(BiomePrefix))
            return pin.IsPlace || !itemBiomes.TryGetValue(pin.Item, out string biome) || biome != GroupName(filter);
        return pin.IsPlace || pin.Item != filter;
    }

    /// <summary>
    /// The filters in the order the panel lists them and the key cycles them: everything, the places group with its
    /// categories, then each biome group with its items. A collapsed header (in <paramref name="collapsed"/>) leaves
    /// its children out; a group with no pins is left out.
    /// </summary>
    public static List<string> Options(IEnumerable<PinRecord> pins, IReadOnlyDictionary<string, string> itemBiomes, ISet<string> collapsed = null)
    {
        var options = new List<string> { "" };
        List<PinRecord> all = pins.ToList();
        List<PinRecord> places = all.Where(p => p.IsPlace).ToList();
        if (places.Count > 0)
        {
            options.Add(Places);
            if (collapsed == null || !collapsed.Contains(Places))
            {
                var present = places.Select(p => p.Category).Distinct().ToList();
                options.AddRange(Categories.Where(present.Contains).Concat(present.Except(Categories).OrderBy(c => c, StringComparer.Ordinal)).Select(Category));
            }
        }
        IEnumerable<string> items = all.Where(p => !p.IsPlace).Select(p => p.Item).Distinct();
        foreach (string biome in Biomes)
        {
            List<string> inBiome = items.Where(i => itemBiomes.TryGetValue(i, out string b) && b == biome)
                .OrderBy(i => i, StringComparer.Ordinal).ToList();
            if (inBiome.Count == 0)
                continue;
            options.Add(Biome(biome));
            if (collapsed == null || !collapsed.Contains(Biome(biome)))
                options.AddRange(inBiome);
        }
        return options;
    }

    /// <summary>How many of the mod's pins the filter shows (all of them for "").</summary>
    public static int Count(IEnumerable<PinRecord> pins, string filter, IReadOnlyDictionary<string, string> itemBiomes) =>
        pins.Count(p => !Hidden(p, filter, itemBiomes));

    /// <summary>
    /// The option after <paramref name="current"/>, or before it when <paramref name="back"/>, wrapping around;
    /// back to everything when it is gone.
    /// </summary>
    public static string Next(string current, IReadOnlyList<string> options, bool back = false)
    {
        int i = -1;
        for (int k = 0; k < options.Count; k++)
            if (options[k] == (current ?? ""))
                i = k;
        return i < 0 ? "" : options[(i + (back ? options.Count - 1 : 1)) % options.Count];
    }
}
