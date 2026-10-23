using System;
using System.Collections.Generic;
using System.Linq;

namespace ResourcePins;

/// <summary>A node's ZDOID, which the world save keeps (a partly mined MineRock5 keeps its ZDO).</summary>
[Serializable]
public struct NodeId : IEquatable<NodeId>
{
    public long User, Id;

    public NodeId(long user, long id)
    {
        User = user;
        Id = id;
    }

    public bool Equals(NodeId o) => User == o.User && Id == o.Id;
    public override bool Equals(object o) => o is NodeId n && Equals(n);
    public override int GetHashCode() => User.GetHashCode() * 31 + Id.GetHashCode();
}

[Serializable]
public sealed class Member
{
    public NodeId Node;
    public MemberKind Kind;

    /// <summary>World time (ticks) at which a picked Regrows member is ready again; 0 when not picked.</summary>
    public long ReadyAt;

    /// <summary>Picked, but the ZDO has no picked time: when it regrows is unknown.</summary>
    public bool Unknown;

    /// <summary>World time (ticks) of the latest sighting, here or by another player: newer than a gone report, it wins.</summary>
    public long SeenAt;

    [NonSerialized] public bool Absent;
    [NonSerialized] public float AbsentSince;
}

/// <summary>A world chest of a place pin, as last read.</summary>
[Serializable]
public sealed class ChestState
{
    public NodeId Node;
    public V3 Pos;
    public bool Empty;

    [NonSerialized] public bool Absent;
    [NonSerialized] public float AbsentSince;
}

/// <summary>One map pin the mod placed, and the nodes it stands for.</summary>
[Serializable]
public sealed class PinRecord
{
    /// <summary>Item prefab of a resource pin, location prefab of a place pin.</summary>
    public string Item;
    public int Type;
    public V3 Anchor;

    /// <summary>The X the mod last set; the player's own toggles differ from it until the next transition.</summary>
    public bool AppliedChecked;
    public List<Member> Members = new();

    /// <summary>Place pins only: category (Dungeons, Camps, ...), pin name, and where its chests are.</summary>
    public string Category;
    public string Label;
    public bool Interior;
    public float Radius;

    /// <summary>Loot chests whose contents are known; they set the X but never remove the pin.</summary>
    public List<ChestState> Chests = new();

    /// <summary>The map's PinData, bound at load by position and type.</summary>
    [NonSerialized] public object Handle;

    public bool IsPlace => !string.IsNullOrEmpty(Category);
}

/// <summary>A node whose ZDO is gone (mined out, one-shot picked), and the world time (ticks) it was found gone.</summary>
[Serializable]
public struct GoneNode
{
    public NodeId Node;
    public long At;

    public GoneNode(NodeId node, long at)
    {
        Node = node;
        At = at;
    }
}

/// <summary>What the registry file holds.</summary>
[Serializable]
public sealed class RegistryData
{
    public long WorldUid;
    public List<PinRecord> Pins = new();
    public List<NodeId> Ignored = new();

    /// <summary>Nodes found gone, so other players' older sightings don't bring them back.</summary>
    public List<GoneNode> Gone = new();
}

/// <summary>Pin state exchanged with other players who have the mod: a full state or what changed since the last delta.</summary>
public sealed class SyncBatch
{
    public List<Sighting> Resources = new();
    public List<PlaceSighting> Places = new();
    public List<ChestSighting> Chests = new();
    public List<GoneNode> Gone = new();

    public bool IsEmpty => Resources.Count == 0 && Places.Count == 0 && Chests.Count == 0 && Gone.Count == 0;

    /// <summary>
    /// The batch split in parts of at most <paramref name="maxItems"/> entries, at least one part. If applied in the order
    /// sent (routed RPCs arrive in order), they give the same as the whole batch: places come before the chests
    /// that need their pin, and the rest is merged entry by entry.
    /// </summary>
    public List<SyncBatch> Split(int maxItems)
    {
        var parts = new List<SyncBatch> { new() };
        int n = 0;
        SyncBatch Part()
        {
            if (n++ < maxItems)
                return parts[parts.Count - 1];
            n = 1;
            parts.Add(new SyncBatch());
            return parts[parts.Count - 1];
        }
        foreach (Sighting s in Resources)
            Part().Resources.Add(s);
        foreach (PlaceSighting s in Places)
            Part().Places.Add(s);
        foreach (ChestSighting c in Chests)
            Part().Chests.Add(c);
        foreach (GoneNode g in Gone)
            Part().Gone.Add(g);
        return parts;
    }
}

/// <summary>A resource ZDO seen by a scan.</summary>
public struct Sighting
{
    public NodeId Node;
    public string Item;
    public string Label;
    public MemberKind Kind;
    public float RespawnMinutes;
    public V3 Pos;
    public bool Picked;
    public long PickedTime;

    /// <summary>Set by sightings that know when they are ready again (sap roots); wins over PickedTime.</summary>
    public long ReadyAt;

    /// <summary>World time (ticks) the node was seen.</summary>
    public long SeenAt;
}

/// <summary>A configured location's LocationProxy seen by a scan.</summary>
public struct PlaceSighting
{
    public NodeId Node;
    public string Location;
    public string Category;
    public string Label;
    public V3 Pos;
    public bool Interior;
    public float Radius;
}

/// <summary>A world-generated chest or loot stand (no creator) seen by a scan; a stand is empty once picked.</summary>
public struct ChestSighting
{
    public NodeId Node;
    public V3 Pos;
    public ChestFill Fill;
}

/// <summary>The minimap as the registry sees it.</summary>
public interface IPinMap
{
    object Add(V3 pos, int type, string name);
    void Remove(object pin);
    void SetChecked(object pin, bool value);

    /// <summary>Any pin of this type within 1 m XZ (the rule of Minimap.HaveSimilarPin).</summary>
    bool HasPinNear(V3 pos, int type);

    /// <summary>Every pin, of any type and owner, within <paramref name="distance"/> XZ.</summary>
    IEnumerable<object> PinsNear(V3 pos, float distance);

    /// <summary>
    /// A pin of this player (no owner) with this type and name within 1 m XZ, or null: a pin the mod made in
    /// a session whose registry was not saved. Hand pins never carry the item or location token as name.
    /// </summary>
    object FindOwn(V3 pos, int type, string name);
}

/// <summary>A registry with no map behind it: a dedicated server keeps every player's pins for the next one who joins.</summary>
public sealed class NoMap : IPinMap
{
    public object Add(V3 pos, int type, string name) => new object();
    public void Remove(object pin) { }
    public void SetChecked(object pin, bool value) { }
    public bool HasPinNear(V3 pos, int type) => false;
    public IEnumerable<object> PinsNear(V3 pos, float distance) => Enumerable.Empty<object>();
    public object FindOwn(V3 pos, int type, string name) => null;
}

/// <summary>
/// Keeps the mod's pins in sync with the resource nodes. It clusters new nodes onto pins, drops nodes whose
/// ZDO is gone, sets the X while every regrowing pickable of a pin is regrowing, and remembers the nodes
/// of pins the player deleted, so they never come back. A place pin stands for one location, is never
/// removed, and gets the X once every known loot chest of the place is empty. What this player's scans
/// change is collected for other players (<see cref="TakeDelta"/>), and their batches go through the same
/// rules (<see cref="ApplyRemote"/>). Hand deletions stay with each player. If a sighting and a gone
/// report of the same node disagree, the later one in world time wins, whichever arrives first.
/// </summary>
public sealed class PinRegistry
{
    public const float SimilarPinDistance = 1f;

    /// <summary>A place gets no pin while any pin the mod does not own is this close (a shared or hand pin).</summary>
    public const float PlaceGuardDistance = 5f;

    /// <summary>
    /// How long a member's ZDO must stay missing (with its pin in range) before the member is dropped.
    /// A client that just arrived may not have the area's ZDOs yet.
    /// </summary>
    public const float AbsenceSeconds = 10f;

    private readonly RegistryData _data;
    private readonly HashSet<NodeId> _ignored;
    private readonly Dictionary<NodeId, long> _gone = new();
    private readonly Dictionary<NodeId, PinRecord> _owner = new();

    // Changed by this player's scans since the last TakeDelta: member and place nodes, chests, gone nodes.
    private readonly HashSet<NodeId> _changed = new();
    private readonly HashSet<NodeId> _changedChests = new();
    private readonly HashSet<NodeId> _newGone = new();

    /// <summary>Set while applying another player's batch: nothing it changes goes back out.</summary>
    private bool _remote;

    /// <summary>Called with (event, pin, detail) on every change, for the log.</summary>
    public Action<string, PinRecord, string> Log;

    public PinRegistry(RegistryData data, long worldUid)
    {
        _data = data;
        _ignored = new HashSet<NodeId>(data.Ignored);
        foreach (GoneNode g in data.Gone ?? new List<GoneNode>())
            _gone[g.Node] = g.At;
        foreach (PinRecord pin in data.Pins)
        {
            pin.Chests ??= new List<ChestState>(); // Files written before place pins.
            foreach (Member m in pin.Members)
                _owner[m.Node] = pin;
        }
        if (data.WorldUid != worldUid)
        {
            // Same world name, different world: its regrow times mean nothing here.
            foreach (Member m in data.Pins.SelectMany(p => p.Members))
            {
                m.ReadyAt = 0;
                m.Unknown = false;
            }
            foreach (PinRecord pin in data.Pins)
                pin.Chests?.Clear();
            _gone.Clear();
            data.WorldUid = worldUid;
            Dirty = true;
        }
    }

    public IReadOnlyList<PinRecord> Pins => _data.Pins;
    public bool IsIgnored(NodeId node) => _ignored.Contains(node);

    /// <summary>The node belongs to a pin or to a deleted one: a sighting of it would add nothing.</summary>
    public bool Knows(NodeId node) => _owner.ContainsKey(node) || _ignored.Contains(node);

    /// <summary>Set on every change that should reach the registry file; the caller clears it after saving.</summary>
    public bool Dirty { get; set; }

    /// <summary>The data to save.</summary>
    public RegistryData Export()
    {
        _data.Ignored = _ignored.ToList();
        _data.Gone = _gone.Select(g => new GoneNode(g.Key, g.Value)).ToList();
        return _data;
    }

    /// <summary>
    /// Binds each entry to the loaded map pin at its anchor (1 m XZ, same type, any name). An entry whose pin
    /// is gone was lost with an unsaved character (the registry is saved with the character, so
    /// <see cref="OnRemovedByHand"/> always records a hand deletion). The entry is forgotten and its nodes can be pinned again.
    /// </summary>
    public void Bind(IEnumerable<(object Pin, V3 Pos, int Type)> loaded)
    {
        var free = loaded.ToList();
        foreach (PinRecord pin in _data.Pins.ToList())
        {
            int i = free.FindIndex(l => l.Type == pin.Type && V3.DistanceXZ(l.Pos, pin.Anchor) < SimilarPinDistance);
            if (i >= 0)
            {
                pin.Handle = free[i].Pin;
                free.RemoveAt(i);
            }
            else
            {
                Forget(pin, ignore: false);
                Log?.Invoke("missing", pin, "");
            }
        }
    }

    private bool IsBound(object handle) => _data.Pins.Any(r => ReferenceEquals(r.Handle, handle));

    /// <summary>The mod's own pin left at this spot by an unsaved registry, if no entry holds it yet.</summary>
    private object Adoptable(IPinMap map, V3 pos, int type, string label)
    {
        object own = map.FindOwn(pos, type, label);
        return own != null && !IsBound(own) ? own : null;
    }

    /// <summary>Adds new nodes to pins and refreshes the pick state of known ones.</summary>
    public void Observe(IPinMap map, IEnumerable<Sighting> sightings, int pinType, float mergeRadius)
    {
        foreach (Sighting s in sightings)
        {
            if (_ignored.Contains(s.Node))
                continue;
            if (_gone.TryGetValue(s.Node, out long goneAt))
            {
                // Seen here, or by another player after it was reported gone: the report was wrong.
                if (_remote && s.SeenAt <= goneAt)
                    continue;
                _gone.Remove(s.Node);
                Dirty = true;
            }
            if (_owner.TryGetValue(s.Node, out PinRecord owner))
            {
                Member m = owner.Members.First(x => x.Node.Equals(s.Node));
                if (!_remote)
                    m.Absent = false;
                if (s.SeenAt > m.SeenAt)
                {
                    m.SeenAt = s.SeenAt;
                    Dirty = true;
                }
                if (m.Kind == MemberKind.Oneshot && s.Picked)
                    DropMember(map, owner, m, "picked", s.SeenAt);
                else if (m.Kind == MemberKind.Regrows && ReadPick(m, s))
                    Touch(s.Node);
                continue;
            }
            if (s.Kind == MemberKind.Oneshot && s.Picked)
                continue;

            var member = new Member { Node = s.Node, Kind = s.Kind, SeenAt = s.SeenAt };
            if (s.Kind == MemberKind.Regrows)
                (member.ReadyAt, member.Unknown) = PickState(s);

            PinRecord pin = Nearest(s.Item, s.Pos, mergeRadius);
            if (pin != null)
            {
                pin.Members.Add(member);
                Log?.Invoke("merge", pin, s.Kind.ToString());
            }
            else
            {
                object adopted = Adoptable(map, s.Pos, pinType, s.Label);
                if (adopted == null && map.HasPinNear(s.Pos, pinType))
                    continue;
                pin = new PinRecord { Item = s.Item, Type = pinType, Anchor = s.Pos, Label = s.Label };
                pin.Members.Add(member);
                pin.Handle = adopted ?? map.Add(s.Pos, pinType, s.Label);
                _data.Pins.Add(pin);
                Log?.Invoke(adopted != null ? "adopt" : "add", pin, s.Kind.ToString());
            }
            _owner[s.Node] = pin;
            Touch(s.Node);
            Dirty = true;
        }
    }

    /// <summary>
    /// Pins new places and records the contents of their loot chests. A chest belongs to the place pin whose
    /// area holds it (<see cref="PlaceCatalog.OwnsChest"/>); a chest read as unknown leaves the known set.
    /// </summary>
    public void ObservePlaces(IPinMap map, IEnumerable<PlaceSighting> places, IEnumerable<ChestSighting> chests, int placeType)
    {
        foreach (PlaceSighting s in places)
        {
            if (Knows(s.Node))
                continue;
            object adopted = Adoptable(map, s.Pos, placeType, s.Label);
            if (adopted == null && map.PinsNear(s.Pos, PlaceGuardDistance).Any(p => !IsBound(p)))
                continue;
            var pin = new PinRecord
            {
                Item = s.Location, Type = placeType, Anchor = s.Pos,
                Category = s.Category, Label = s.Label, Interior = s.Interior, Radius = s.Radius,
            };
            pin.Members.Add(new Member { Node = s.Node, Kind = MemberKind.Place });
            pin.Handle = adopted ?? map.Add(s.Pos, placeType, s.Label);
            _data.Pins.Add(pin);
            _owner[s.Node] = pin;
            Touch(s.Node);
            Dirty = true;
            Log?.Invoke(adopted != null ? "adopt" : "add", pin, s.Category);
        }

        foreach (ChestSighting c in chests)
        {
            PinRecord pin = _data.Pins.FirstOrDefault(p =>
                p.IsPlace && PlaceCatalog.HasLoot(p.Category) && PlaceCatalog.OwnsChest(p.Anchor, p.Interior, p.Radius, c.Pos));
            if (pin == null)
                continue;
            ChestState chest = pin.Chests.FirstOrDefault(x => x.Node.Equals(c.Node));
            // Loot never comes back: another player's older copy of a chest never refills it here.
            if (_remote && (c.Fill == ChestFill.Unknown || chest != null && chest.Empty))
                continue;
            if (c.Fill == ChestFill.Unknown)
            {
                if (chest != null && pin.Chests.Remove(chest))
                    Dirty = true;
                continue;
            }
            bool empty = c.Fill == ChestFill.Empty;
            if (chest == null)
            {
                pin.Chests.Add(new ChestState { Node = c.Node, Pos = c.Pos, Empty = empty });
                TouchChest(c.Node);
                Dirty = true;
            }
            else
            {
                chest.Absent = false;
                if (chest.Empty != empty)
                {
                    chest.Empty = empty;
                    TouchChest(c.Node);
                    Dirty = true;
                }
            }
        }
    }

    /// <summary>
    /// For pins within <paramref name="radius"/> of the player, drops members whose ZDO is gone and removes
    /// pins left empty. ZDO absence is the only removal signal. Place members are never dropped. A place's
    /// chests in range whose ZDO is gone (destroyed) leave its known set.
    /// </summary>
    /// <param name="now">World time (ticks), recorded as the time a dropped node was found gone.</param>
    public void Prune(IPinMap map, V3 player, float radius, Func<NodeId, bool> exists, float realtime, long now)
    {
        foreach (PinRecord pin in _data.Pins.ToList())
        {
            bool inRange = V3.DistanceXZ(pin.Anchor, player) <= radius;
            foreach (ChestState c in pin.Chests.ToList())
            {
                if (V3.DistanceXZ(c.Pos, player) > radius || exists(c.Node))
                    c.Absent = false;
                else if (!c.Absent)
                {
                    c.Absent = true;
                    c.AbsentSince = realtime;
                }
                else if (realtime - c.AbsentSince >= AbsenceSeconds)
                {
                    pin.Chests.Remove(c);
                    Dirty = true;
                }
            }
            foreach (Member m in pin.Members.ToList())
            {
                if (m.Kind == MemberKind.Place)
                    continue;
                if (!inRange || exists(m.Node))
                    m.Absent = false;
                else if (!m.Absent)
                {
                    m.Absent = true;
                    m.AbsentSince = realtime;
                }
                else if (realtime - m.AbsentSince >= AbsenceSeconds)
                    DropMember(map, pin, m, "gone", now);
            }
        }
    }

    /// <summary>
    /// Sets or clears each pin's X on transitions only, from the stored ready times and chest states, so it
    /// also clears for pins far away. A pin the player toggled by hand keeps their choice until the next
    /// transition.
    /// </summary>
    public void UpdateChecks(IPinMap map, long now)
    {
        foreach (PinRecord pin in _data.Pins)
        {
            bool want = pin.IsPlace ? Looted(pin) : Regrowing(pin, now);
            if (want == pin.AppliedChecked)
                continue;
            map.SetChecked(pin.Handle, want);
            pin.AppliedChecked = want;
            Dirty = true;
            Log?.Invoke(want ? "check" : "uncheck", pin, "");
        }
    }

    /// <summary>The player deleted one of the mod's pins: never pin its nodes again.</summary>
    public void OnRemovedByHand(object handle)
    {
        PinRecord pin = _data.Pins.FirstOrDefault(p => ReferenceEquals(p.Handle, handle));
        if (pin == null)
            return;
        Forget(pin, ignore: true);
        Log?.Invoke("ignore", pin, "");
    }

    /// <summary>Applies another player's batch with the rules of a scan; nothing it changes is sent back out.</summary>
    public void ApplyRemote(IPinMap map, SyncBatch batch, int pinType, float mergeRadius, int placeType)
    {
        _remote = true;
        try
        {
            foreach (GoneNode g in batch.Gone)
            {
                if (_owner.TryGetValue(g.Node, out PinRecord pin))
                {
                    if (pin.IsPlace)
                        continue;
                    Member m = pin.Members.First(x => x.Node.Equals(g.Node));
                    if (m.SeenAt > g.At)
                    {
                        // Seen here after they found it gone: send the newer sighting so they pin it again.
                        _changed.Add(g.Node);
                        continue;
                    }
                    DropMember(map, pin, m, "gone", g.At);
                }
                else if (!_gone.TryGetValue(g.Node, out long at) || at < g.At)
                {
                    _gone[g.Node] = g.At;
                    Dirty = true;
                }
            }
            Observe(map, batch.Resources, pinType, mergeRadius);
            ObservePlaces(map, batch.Places, batch.Chests, placeType);
        }
        finally
        {
            _remote = false;
        }
    }

    /// <summary>Everything the registry knows, for a player who just joined.</summary>
    /// <param name="nameOf">Name of a map pin, for resource pins saved before the registry kept their label.</param>
    public SyncBatch Snapshot(Func<object, string> nameOf)
    {
        SyncBatch batch = Build(nameOf, null, null);
        batch.Gone.AddRange(_gone.Select(g => new GoneNode(g.Key, g.Value)));
        return batch;
    }

    /// <summary>What this player's scans changed since the last call, or null when nothing did.</summary>
    public SyncBatch TakeDelta(Func<object, string> nameOf)
    {
        if (_changed.Count == 0 && _changedChests.Count == 0 && _newGone.Count == 0)
            return null;
        SyncBatch batch = Build(nameOf, _changed, _changedChests);
        batch.Gone.AddRange(_newGone.Where(_gone.ContainsKey).Select(n => new GoneNode(n, _gone[n])));
        _changed.Clear();
        _changedChests.Clear();
        _newGone.Clear();
        return batch;
    }

    /// <summary>
    /// The registry as sightings, all of it or only the given nodes and chests. A resource member is sent at
    /// its pin's anchor, so the receiver clusters it onto the same pin.
    /// </summary>
    private SyncBatch Build(Func<object, string> nameOf, HashSet<NodeId> nodes, HashSet<NodeId> chests)
    {
        var batch = new SyncBatch();
        foreach (PinRecord pin in _data.Pins)
        {
            if (pin.IsPlace)
            {
                NodeId node = pin.Members[0].Node;
                if (nodes == null || nodes.Contains(node))
                    batch.Places.Add(new PlaceSighting
                    {
                        Node = node, Location = pin.Item, Category = pin.Category, Label = pin.Label,
                        Pos = pin.Anchor, Interior = pin.Interior, Radius = pin.Radius,
                    });
                foreach (ChestState c in pin.Chests)
                    if (chests == null || chests.Contains(c.Node))
                        batch.Chests.Add(new ChestSighting { Node = c.Node, Pos = c.Pos, Fill = c.Empty ? ChestFill.Empty : ChestFill.Full });
                continue;
            }
            string label = pin.Label ?? nameOf(pin.Handle);
            foreach (Member m in pin.Members)
                if (nodes == null || nodes.Contains(m.Node))
                    batch.Resources.Add(new Sighting
                    {
                        Node = m.Node, Item = pin.Item, Label = label, Kind = m.Kind, Pos = pin.Anchor,
                        Picked = m.ReadyAt > 0 || m.Unknown, ReadyAt = m.ReadyAt, SeenAt = m.SeenAt,
                    });
        }
        return batch;
    }

    /// <summary>
    /// X = at least one regrowing member, and all of them picked and not yet past their ready time. A stale
    /// picked flag after the ready time counts as ready (Pickable.ShouldRespawn). An unknown time never sets X.
    /// </summary>
    public static bool Regrowing(PinRecord pin, long now)
    {
        bool any = false;
        foreach (Member m in pin.Members)
        {
            if (m.Kind != MemberKind.Regrows)
                continue;
            if (m.Unknown || m.ReadyAt == 0 || now > m.ReadyAt)
                return false;
            any = true;
        }
        return any;
    }

    /// <summary>X = at least one known loot chest, and every known chest empty (only dungeons and camps have any).</summary>
    public static bool Looted(PinRecord pin) => pin.Chests.Count > 0 && pin.Chests.All(c => c.Empty);

    /// <summary>
    /// Stores a sighting's pick state; true when it changed. Another player's copy only moves the ready time
    /// later (a newer pick), so an older copy never clears a pick seen here.
    /// </summary>
    private bool ReadPick(Member m, Sighting s)
    {
        (long readyAt, bool unknown) = PickState(s);
        if (_remote)
        {
            if (readyAt <= m.ReadyAt)
                return false;
            unknown = false;
        }
        if (m.ReadyAt == readyAt && m.Unknown == unknown)
            return false;
        m.ReadyAt = readyAt;
        m.Unknown = unknown;
        return true;
    }

    private static (long ReadyAt, bool Unknown) PickState(Sighting s)
    {
        if (s.Picked && s.ReadyAt > 0)
            return (s.ReadyAt, false);
        if (s.Picked && s.PickedTime <= 0)
            return (0, true);
        if (s.Picked)
            return (s.PickedTime + (long)(s.RespawnMinutes * TimeSpan.TicksPerMinute), false);
        return (0, false);
    }

    private void Touch(NodeId node)
    {
        if (!_remote)
            _changed.Add(node);
    }

    private void TouchChest(NodeId node)
    {
        if (!_remote)
            _changedChests.Add(node);
    }

    private PinRecord Nearest(string item, V3 pos, float mergeRadius)
    {
        PinRecord best = null;
        float bestDist = mergeRadius;
        foreach (PinRecord pin in _data.Pins)
        {
            float d = V3.DistanceXZ(pin.Anchor, pos);
            if (pin.Item == item && d <= bestDist)
            {
                best = pin;
                bestDist = d;
            }
        }
        return best;
    }

    private void DropMember(IPinMap map, PinRecord pin, Member m, string why, long at)
    {
        pin.Members.Remove(m);
        _owner.Remove(m.Node);
        _gone[m.Node] = _gone.TryGetValue(m.Node, out long known) ? Math.Max(known, at) : at;
        if (!_remote)
            _newGone.Add(m.Node);
        Dirty = true;
        Log?.Invoke("drop", pin, why);
        if (pin.Members.Count > 0)
            return;
        Forget(pin, ignore: false);
        map.Remove(pin.Handle);
        Log?.Invoke("remove", pin, "");
    }

    private void Forget(PinRecord pin, bool ignore)
    {
        foreach (Member m in pin.Members)
        {
            _owner.Remove(m.Node);
            if (ignore)
                _ignored.Add(m.Node);
        }
        _data.Pins.Remove(pin);
        Dirty = true;
    }
}
