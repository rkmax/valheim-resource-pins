using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ResourcePins;

/// <summary>An indexed prefab: its resource and the item's name token for the pin label.</summary>
internal sealed class IndexEntry
{
    public string Prefab;
    public Resource Resource;
    public string Label;
}

/// <summary>
/// prefab hash -> resource. Built once per session from ZNetScene's prefab list and the components' own
/// drop data, so new game content is picked up with no hand-written list.
/// </summary>
internal static class ResourceIndex
{
    private static readonly HashSet<string> _warned = new();
    private static Dictionary<int, IndexEntry> _index;
    private static ZNetScene _builtFor;

    internal static void Reset() => _index = null;

    /// <summary>The index, or null until ZNetScene and ObjectDB are ready.</summary>
    internal static Dictionary<int, IndexEntry> Get()
    {
        ZNetScene scene = ZNetScene.instance;
        ObjectDB db = ObjectDB.instance;
        if (scene == null || db == null || db.m_items.Count == 0)
            return null;
        if (_index != null && _builtFor == scene)
            return _index;

        List<string> items = Classifier.ParseItems(Plugin.Items.Value, n => db.GetItemPrefab(n) != null, out List<string> unknown);
        foreach (string name in unknown.Where(_warned.Add))
            Plugin.Log.LogWarning($"Unknown item '{name}' in Items (names are case-sensitive); ignored.");
        var rules = new Classifier(items, Plugin.Blocklist.Value);

        _index = new Dictionary<int, IndexEntry>();
        _builtFor = scene;
        foreach (GameObject prefab in scene.m_prefabs)
        {
            if (prefab == null)
                continue;
            Resource r = rules.Classify(Describe(prefab));
            if (r == null)
                continue;
            var drop = db.GetItemPrefab(r.Item).GetComponent<ItemDrop>();
            string label = r.Root?.Name ?? (drop != null ? drop.m_itemData.m_shared.m_name : r.Item);
            var entry = new IndexEntry { Prefab = prefab.name, Resource = r, Label = label };
            _index[prefab.name.GetStableHashCode()] = entry;
            PinLog.Index(entry);
        }
        Plugin.Log.LogInfo($"Indexed {_index.Count} resource prefabs for {items.Count} items.");
        return _index;
    }

    private static PrefabInfo Describe(GameObject prefab)
    {
        var rb = prefab.GetComponent<Rigidbody>();
        var info = new PrefabInfo
        {
            Name = prefab.name,
            HasCharacter = prefab.GetComponent<Character>() != null,
            HasLeviathan = prefab.GetComponent<Leviathan>() != null,
            NonKinematicRigidbody = rb != null && !rb.isKinematic,
            MineDrops = MineDrops(prefab, out bool keeps),
            KeepsZdoWhenMined = keeps,
        };

        var destructible = prefab.GetComponent<Destructible>();
        if (destructible != null && destructible.m_spawnWhenDestroyed != null)
        {
            List<string> fracture = MineDrops(destructible.m_spawnWhenDestroyed, out bool fractureKeeps);
            if (!fractureKeeps)
                info.FractureDrops = fracture;
        }

        var dod = prefab.GetComponent<DropOnDestroyed>();
        if (dod != null)
            info.DestroyDrops = Names(dod.m_dropWhenDestroyed);

        var pickable = prefab.GetComponent<Pickable>();
        if (pickable != null && pickable.m_itemPrefab != null)
        {
            info.PickItem = pickable.m_itemPrefab.name;
            info.RespawnMinutes = pickable.m_respawnTimeMinutes;
            info.DefaultPicked = pickable.m_defaultPicked;
        }
        var root = prefab.GetComponent<ResourceRoot>();
        if (root != null)
            info.Root = new SapRoot
            {
                Name = root.m_name, MaxLevel = root.m_maxLevel, RegenPerSec = root.m_regenPerSec, EmptyThreshold = root.m_emptyTreshold,
            };
        return info;
    }

    /// <summary>Drops of a MineRock5, or of a MineRock that loses its ZDO when mined out.</summary>
    private static List<string> MineDrops(GameObject go, out bool keepsZdo)
    {
        keepsZdo = false;
        var mr5 = go.GetComponent<MineRock5>();
        if (mr5 != null)
            return Names(mr5.m_dropItems);
        var mr = go.GetComponent<MineRock>();
        if (mr == null)
            return null;
        keepsZdo = !mr.m_removeWhenDestroyed;
        return keepsZdo ? null : Names(mr.m_dropItems);
    }

    private static List<string> Names(DropTable table) =>
        table?.m_drops.Where(d => d.m_item != null).Select(d => d.m_item.name).ToList();
}
