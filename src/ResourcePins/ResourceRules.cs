using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ResourcePins;

/// <summary>How a pinned node behaves over time.</summary>
public enum MemberKind
{
    /// <summary>Mined or destroyed: gone when its ZDO is gone.</summary>
    Mined,
    /// <summary>Pickable that grows back (m_respawnTimeMinutes &gt; 0).</summary>
    Regrows,
    /// <summary>Pickable that never grows back: gone once picked.</summary>
    Oneshot,
    /// <summary>LocationProxy of a place pin: never removed.</summary>
    Place,
}

/// <summary>What the classifier needs from a prefab, read from its components by the game-side index.</summary>
public sealed class PrefabInfo
{
    public string Name;
    public bool HasCharacter, HasLeviathan, NonKinematicRigidbody;

    /// <summary>Drop items of a MineRock5, or of a MineRock that is removed when mined; null otherwise.</summary>
    public List<string> MineDrops;

    /// <summary>A MineRock that keeps its ZDO when mined: its pin could never be cleared, so it is skipped.</summary>
    public bool KeepsZdoWhenMined;

    /// <summary>Drop items of the MineRock a Destructible turns into (giant_skull -> giant_skull_frac); null otherwise.</summary>
    public List<string> FractureDrops;

    /// <summary>Drop items of a DropOnDestroyed; null otherwise.</summary>
    public List<string> DestroyDrops;

    /// <summary>Item prefab a Pickable gives; null otherwise.</summary>
    public string PickItem;
    public float RespawnMinutes;
    public bool DefaultPicked;

    /// <summary>A ResourceRoot (ancient root a Sap Extractor drains); null otherwise.</summary>
    public SapRoot Root;
}

/// <summary>
/// An ancient root's sap rules (ResourceRoot): it refills at RegenPerSec from its level at LastTime and shows
/// as empty below EmptyThreshold.
/// </summary>
public sealed class SapRoot
{
    public const string Item = "Sap";

    public string Name;
    public float MaxLevel, RegenPerSec, EmptyThreshold;

    /// <summary>
    /// World time (ticks) at which a root last updated at <paramref name="level"/> is no longer empty, or 0
    /// when it is not empty at <paramref name="now"/> (ResourceRoot.UpdateTick adds RegenPerSec per second).
    /// </summary>
    public long ReadyAt(float level, long lastTime, long now)
    {
        if (level >= EmptyThreshold || RegenPerSec <= 0f)
            return 0;
        long readyAt = lastTime + (long)Math.Ceiling((EmptyThreshold - level) / (double)RegenPerSec * TimeSpan.TicksPerSecond);
        return now >= readyAt ? 0 : readyAt;
    }
}

/// <summary>An indexed resource prefab.</summary>
public sealed class Resource
{
    public string Item;
    public MemberKind Kind;
    public float RespawnMinutes;
    public bool DefaultPicked;

    /// <summary>Sap roots only: their rules, and the root's own name token as pin label.</summary>
    public SapRoot Root;
}

/// <summary>Decides from plain drop data which prefabs are resource nodes worth a pin.</summary>
public sealed class Classifier
{
    private const string Stone = "Stone";
    private const int MaxDestroyItems = 2;

    private readonly HashSet<string> _items;
    private readonly List<Regex> _blocklist;

    /// <param name="items">Configured item prefab names already checked against the game (see <see cref="ParseItems"/>).</param>
    /// <param name="blocklist">Comma-separated prefab name globs, case-insensitive, `*` matches anything.</param>
    public Classifier(IEnumerable<string> items, string blocklist)
    {
        _items = new HashSet<string>(items);
        _blocklist = Split(blocklist)
            .Select(g => new Regex("^" + Regex.Escape(g).Replace(@"\*", ".*") + "$", RegexOptions.IgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Splits the configured item list and keeps the names the game knows; the rest go to
    /// <paramref name="unknown"/> so the caller can log them.
    /// </summary>
    public static List<string> ParseItems(string config, Func<string, bool> known, out List<string> unknown)
    {
        var items = new List<string>();
        unknown = new List<string>();
        foreach (string name in Split(config).Distinct())
            (known(name) ? items : unknown).Add(name);
        return items;
    }

    public bool Blocked(string prefab) => _blocklist.Any(r => r.IsMatch(prefab));

    /// <summary>The resource a prefab stands for, or null if it gets no pin.</summary>
    public Resource Classify(PrefabInfo p)
    {
        if (p.HasCharacter || p.HasLeviathan || p.NonKinematicRigidbody || p.KeepsZdoWhenMined || Blocked(p.Name))
            return null;

        string item = FirstConfigured(p.MineDrops) ?? FirstConfigured(p.FractureDrops);
        if (item == null && p.DestroyDrops != null && p.DestroyDrops.Where(d => d != Stone).Distinct().Count() <= MaxDestroyItems)
            item = FirstConfigured(p.DestroyDrops);
        if (item != null)
            return new Resource { Item = item, Kind = MemberKind.Mined };

        if (p.Root != null && _items.Contains(SapRoot.Item))
            return new Resource { Item = SapRoot.Item, Kind = MemberKind.Regrows, Root = p.Root };

        if (p.PickItem != null && _items.Contains(p.PickItem))
            return new Resource
            {
                Item = p.PickItem,
                Kind = p.RespawnMinutes > 0f ? MemberKind.Regrows : MemberKind.Oneshot,
                RespawnMinutes = p.RespawnMinutes,
                DefaultPicked = p.DefaultPicked,
            };
        return null;
    }

    private string FirstConfigured(List<string> drops) => drops?.FirstOrDefault(_items.Contains);

    private static IEnumerable<string> Split(string list) =>
        (list ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0);
}
