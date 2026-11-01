using System;
using System.Collections.Generic;
using System.Linq;

namespace ResourcePins;

/// <summary>
/// An item or location at a position. ZDO.Load gives every ZDO a new ZDOID on each world load, so an id names an
/// object for one session only. Nodes, locations and chests never move, so what they are and where they are names them.
/// </summary>
[Serializable]
public struct Spot
{
    public string Item;
    public V3 Pos;

    public Spot(string item, V3 pos)
    {
        Item = item;
        Pos = pos;
    }
}

[Serializable]
public sealed class Member
{
    /// <summary>Where the node is; with its pin's item, what names it (<see cref="PinRegistry.SameNodeDistance"/>).</summary>
    public V3 Pos;
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
    /// <summary>Where the chest is, what names it (<see cref="PinRegistry.SameChestDistance"/>).</summary>
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

    /// <summary>A resource pin with no members yet (from a format 2 file) while no node of its item turns up around it.</summary>
    [NonSerialized] public bool Absent;
    [NonSerialized] public float AbsentSince;

    public bool IsPlace => !string.IsNullOrEmpty(Category);
}

/// <summary>A node whose ZDO is gone (mined out, one-shot picked), and the world time (ticks) it was found gone.</summary>
[Serializable]
public sealed class GoneNode
{
    public string Item;
    public V3 Pos;
    public long At;

    public GoneNode(string item, V3 pos, long at)
    {
        Item = item;
        Pos = pos;
        At = at;
    }
}

/// <summary>What the registry file holds.</summary>
[Serializable]
public sealed class RegistryData
{
    /// <summary>Nodes, places and chests named by item and position. Format 2 named them by ZDOID.</summary>
    public const int CurrentVersion = 3;

    /// <summary>The format of the file; 0 in files of format 2, which did not write it.</summary>
    public int Version;

    public long WorldUid;
    public List<PinRecord> Pins = new();

    /// <summary>Item (or location) and anchor of each pin the player deleted by hand.</summary>
    public List<Spot> Ignored = new();

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
    public string Item;
    public string Label;
    public MemberKind Kind;
    public float RespawnMinutes;

    /// <summary>Where the node is.</summary>
    public V3 Pos;

    /// <summary>Another player's: the anchor of their pin, so the node joins the same pin here. Null from a scan.</summary>
    public V3? Anchor;
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
    public V3 Pos;
    public ChestFill Fill;
}

/// <summary>The minimap as the registry sees it.</summary>
public interface IPinMap
{
    object Add(V3 pos, int type, string name);
    void Remove(object pin);
    void SetChecked(object pin, bool value);
    void Rename(object pin, string name);

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
    public void Rename(object pin, string name) { }
    public bool HasPinNear(V3 pos, int type) => false;
    public IEnumerable<object> PinsNear(V3 pos, float distance) => Enumerable.Empty<object>();
    public object FindOwn(V3 pos, int type, string name) => null;
}

/// <summary>Values kept by item and position, found within <see cref="PinRegistry.SameNodeDistance"/> XZ.</summary>
internal sealed class SpotIndex<T> where T : class
{
    // A cell as wide as the distance: the 3x3 cells around a position hold every match.
    private const float Cell = PinRegistry.SameNodeDistance;
    private readonly Dictionary<(string, int, int), List<(V3 Pos, T Value)>> _cells = new();

    public void Add(string item, V3 pos, T value)
    {
        (string, int, int) key = Key(item, pos);
        if (!_cells.TryGetValue(key, out List<(V3 Pos, T Value)> list))
            _cells[key] = list = new List<(V3 Pos, T Value)>();
        list.Add((pos, value));
    }

    public void Remove(string item, V3 pos, T value)
    {
        if (_cells.TryGetValue(Key(item, pos), out List<(V3 Pos, T Value)> list))
            list.RemoveAll(e => ReferenceEquals(e.Value, value));
    }

    public T Find(string item, V3 pos)
    {
        (_, int x, int z) = Key(item, pos);
        for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
                if (_cells.TryGetValue((item, x + dx, z + dz), out List<(V3 Pos, T Value)> list))
                    foreach ((V3 p, T value) in list)
                        if (V3.DistanceXZ(p, pos) < Cell)
                            return value;
        return null;
    }

    public void Clear() => _cells.Clear();

    private static (string, int, int) Key(string item, V3 pos) => (item, (int)Math.Floor(pos.X / Cell), (int)Math.Floor(pos.Z / Cell));
}

/// <summary>
/// Keeps the mod's pins in sync with the resource nodes. It clusters new nodes onto pins, drops nodes whose
/// ZDO is gone, sets the X while every regrowing pickable of a pin is regrowing, and remembers where the
/// player deleted pins, so they never come back. A place pin stands for one location, is never
/// removed, and gets the X once every known loot chest of the place is empty. Nodes, places and chests are
/// known by item and position (<see cref="Spot"/>), the same on every world load. What this player's scans
/// change is collected for other players (<see cref="TakeDelta"/>), and their batches go through the same
/// rules (<see cref="ApplyRemote"/>). Hand deletions stay with each player. If a sighting and a gone
/// report of the same node disagree, the later one in world time wins, whichever arrives first.
/// </summary>
public sealed class PinRegistry
{
    public const float SimilarPinDistance = 1f;

    /// <summary>
    /// A sighting this close (XZ) to a node of the same item is that node. A node comes back at the same position on
    /// each world load, and two nodes of one item are never this close.
    /// </summary>
    public const float SameNodeDistance = 0.5f;

    /// <summary>A chest this close (3D: a dungeon's rooms stack) to a known one is that chest; a place's chests are 1.6 m apart or more.</summary>
    public const float SameChestDistance = 0.5f;

    /// <summary>A place gets no pin while any pin the mod does not own is this close (a shared or hand pin).</summary>
    public const float PlaceGuardDistance = 5f;

    /// <summary>
    /// How long a member's ZDO must stay missing (with the member in range) before the member is dropped.
    /// A client that just arrived may not have the area's ZDOs yet.
    /// </summary>
    public const float AbsenceSeconds = 10f;

    private readonly RegistryData _data;
    private readonly SpotIndex<Member> _members = new();
    private readonly Dictionary<Member, PinRecord> _owner = new();
    private readonly SpotIndex<GoneNode> _gone = new();

    /// <summary>
    /// Place pins of a format 2 file merged into another copy of the same place (Kept); <see cref="Bind"/> takes their
    /// map pins away.
    /// </summary>
    private readonly List<(PinRecord Copy, PinRecord Kept)> _copies = new();

    // Changed by this player's scans since the last TakeDelta: members (a place's one for the place), chests, gone nodes.
    private readonly HashSet<Member> _changed = new();
    private readonly HashSet<ChestState> _changedChests = new();
    private readonly HashSet<GoneNode> _newGone = new();

    /// <summary>Set while applying another player's batch: nothing it changes goes back out.</summary>
    private bool _remote;

    /// <summary>Called with (event, pin, detail) on every change, for the log.</summary>
    public Action<string, PinRecord, string> Log;

    public PinRegistry(RegistryData data, long worldUid)
    {
        _data = data;
        foreach (PinRecord pin in data.Pins)
            pin.Chests ??= new List<ChestState>(); // Files written before place pins.
        if (data.Version < RegistryData.CurrentVersion)
            Migrate();
        foreach (PinRecord pin in data.Pins)
            foreach (Member m in pin.Members.Where(m => m.Kind != MemberKind.Place))
                Own(pin, m);
        foreach (GoneNode g in data.Gone)
            _gone.Add(g.Item, g.Pos, g);
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
            data.Gone.Clear();
            data.WorldUid = worldUid;
            Dirty = true;
        }
    }

    public IReadOnlyList<PinRecord> Pins => _data.Pins;

    /// <summary>The place has a pin, or had one the player deleted: a sighting of it would add nothing.</summary>
    public bool KnowsPlace(string location, V3 pos) => Place(location, pos) != null || Deleted(location, pos, SimilarPinDistance);

    /// <summary>Set on every change that should reach the registry file; the caller clears it after saving.</summary>
    public bool Dirty { get; set; }

    /// <summary>Loaded from a file of an older format: keep a copy of that file before the first save over it.</summary>
    public bool Migrated { get; set; }

    /// <summary>The data to save.</summary>
    public RegistryData Export() => _data;

    /// <summary>
    /// A file of format 2 named every node, place and chest by its ZDOID, which ZDO.Load renumbers on each world load.
    /// Places and chests keep their saved positions, and the copies of one place or chest saved under several ids become
    /// one, with the loot taken from any of them. Resource pins keep their anchor, name and X but not their members,
    /// whose positions were never saved: the next scan around a pin gives them back, as it clusters new nodes. Hand
    /// deletions and gone nodes have no position and are dropped.
    /// </summary>
    private void Migrate()
    {
        var kept = new List<PinRecord>();
        foreach (PinRecord pin in _data.Pins)
        {
            if (!pin.IsPlace)
            {
                pin.Members.Clear();
                kept.Add(pin);
                continue;
            }
            List<ChestState> chests = pin.Chests;
            PinRecord same = Place(kept, pin.Item, pin.Anchor);
            if (same == null)
            {
                pin.Members = new List<Member> { new() { Pos = pin.Anchor, Kind = MemberKind.Place } };
                pin.Chests = new List<ChestState>();
                kept.Add(pin);
                same = pin;
            }
            else
                _copies.Add((pin, same));
            foreach (ChestState c in chests)
            {
                ChestState known = same.Chests.FirstOrDefault(x => V3.Distance(x.Pos, c.Pos) < SameChestDistance);
                if (known == null)
                    same.Chests.Add(c);
                else
                    known.Empty |= c.Empty; // Loot never comes back.
            }
        }
        _data.Pins = kept;
        _data.Ignored.Clear();
        _data.Gone.Clear();
        _data.Version = RegistryData.CurrentVersion;
        Migrated = true;
        Dirty = true;
    }

    /// <summary>
    /// Binds each entry to the loaded map pin at its anchor (1 m XZ, same type, any name). An entry whose pin
    /// is gone was lost with an unsaved character (the registry is saved with the character, so
    /// <see cref="OnRemovedByHand"/> always records a hand deletion). The entry is forgotten and its nodes can be pinned again.
    /// The pins of place copies merged by <see cref="Migrate"/> are taken off the map, and the pin kept shows the X of
    /// the merged loot, whichever copy's pin it was.
    /// </summary>
    public void Bind(IPinMap map, IEnumerable<(object Pin, V3 Pos, int Type)> loaded)
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
        foreach ((PinRecord copy, PinRecord kept) in _copies)
        {
            if (kept.Handle != null)
            {
                kept.AppliedChecked = Looted(kept);
                map.SetChecked(kept.Handle, kept.AppliedChecked);
            }
            int i = free.FindIndex(l => l.Type == copy.Type && V3.DistanceXZ(l.Pos, copy.Anchor) < SimilarPinDistance);
            if (i < 0)
                continue;
            map.Remove(free[i].Pin);
            free.RemoveAt(i);
            Log?.Invoke("remove", copy, "duplicate");
        }
        _copies.Clear();
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
            GoneNode gone = _gone.Find(s.Item, s.Pos);
            if (gone != null)
            {
                // Seen here, or by another player after it was reported gone: the report was wrong.
                if (_remote && s.SeenAt <= gone.At)
                    continue;
                ForgetGone(gone);
            }
            Member m = _members.Find(s.Item, s.Pos);
            if (m != null)
            {
                PinRecord owner = _owner[m];
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
                    Touch(m);
                continue;
            }
            if (s.Kind == MemberKind.Oneshot && s.Picked)
                continue;

            var member = new Member { Pos = s.Pos, Kind = s.Kind, SeenAt = s.SeenAt };
            if (s.Kind == MemberKind.Regrows)
                (member.ReadyAt, member.Unknown) = PickState(s);

            V3 at = s.Anchor ?? s.Pos;
            PinRecord pin = Nearest(s.Item, at, mergeRadius);
            if (pin != null)
            {
                pin.Members.Add(member);
                Log?.Invoke("merge", pin, s.Kind.ToString());
            }
            else
            {
                // A pin deleted by hand stands for every node it would take.
                if (Deleted(s.Item, at, mergeRadius))
                    continue;
                object adopted = Adoptable(map, at, pinType, s.Label);
                if (adopted == null && map.HasPinNear(at, pinType))
                    continue;
                pin = new PinRecord { Item = s.Item, Type = pinType, Anchor = at, Label = s.Label };
                pin.Members.Add(member);
                pin.Handle = adopted ?? map.Add(at, pinType, s.Label);
                _data.Pins.Add(pin);
                Log?.Invoke(adopted != null ? "adopt" : "add", pin, s.Kind.ToString());
            }
            Own(pin, member);
            Touch(member);
            Dirty = true;
        }
    }

    private void Relabel(IPinMap map, PinRecord pin, string label)
    {
        pin.Label = label;
        if (pin.Handle != null)
            map.Rename(pin.Handle, label);
        Dirty = true;
    }

    /// <summary>
    /// Gives the place pins still carrying the generic name the name of their location, from
    /// <paramref name="labelOf"/> (location prefab name -> name token, null when the game gives none), which
    /// is asked once per location.
    /// </summary>
    public void RelabelPlaces(IPinMap map, Func<string, string> labelOf)
    {
        var labels = new Dictionary<string, string>();
        foreach (PinRecord pin in _data.Pins.Where(p => p.IsPlace && p.Label == PlaceCatalog.Fallback(p.Category)))
        {
            if (!labels.TryGetValue(pin.Item, out string label))
                labels[pin.Item] = label = labelOf(pin.Item);
            if (label != null && label != pin.Label)
            {
                Relabel(map, pin, label);
                Touch(pin.Members[0]);
            }
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
            PinRecord known = Place(s.Location, s.Pos);
            if (known != null)
            {
                // A pin saved with the generic name takes the kind of place once the game gives one.
                if (known.Label == PlaceCatalog.Fallback(known.Category) && s.Label != known.Label)
                {
                    Relabel(map, known, s.Label);
                    Touch(known.Members[0]);
                }
                continue;
            }
            if (Deleted(s.Location, s.Pos, SimilarPinDistance))
                continue;
            object adopted = Adoptable(map, s.Pos, placeType, s.Label);
            if (adopted == null && map.PinsNear(s.Pos, PlaceGuardDistance).Any(p => !IsBound(p)))
                continue;
            var pin = new PinRecord
            {
                Item = s.Location, Type = placeType, Anchor = s.Pos,
                Category = s.Category, Label = s.Label, Interior = s.Interior, Radius = s.Radius,
            };
            var place = new Member { Pos = s.Pos, Kind = MemberKind.Place };
            pin.Members.Add(place);
            pin.Handle = adopted ?? map.Add(s.Pos, placeType, s.Label);
            _data.Pins.Add(pin);
            Touch(place);
            Dirty = true;
            Log?.Invoke(adopted != null ? "adopt" : "add", pin, s.Category);
        }

        foreach (ChestSighting c in chests)
        {
            PinRecord pin = _data.Pins.FirstOrDefault(p =>
                p.IsPlace && PlaceCatalog.HasLoot(p.Category) && PlaceCatalog.OwnsChest(p.Anchor, p.Interior, p.Radius, c.Pos));
            if (pin == null)
                continue;
            ChestState chest = pin.Chests.FirstOrDefault(x => V3.Distance(x.Pos, c.Pos) < SameChestDistance);
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
                chest = new ChestState { Pos = c.Pos, Empty = empty };
                pin.Chests.Add(chest);
                TouchChest(chest);
                Dirty = true;
            }
            else
            {
                chest.Absent = false;
                if (chest.Empty != empty)
                {
                    chest.Empty = empty;
                    TouchChest(chest);
                    Dirty = true;
                }
            }
        }
    }

    /// <summary>
    /// Drops the members within <paramref name="radius"/> of the player whose node is gone, and removes pins left
    /// empty. The node's absence is the only removal signal. Place members are never dropped. A place's chests in
    /// range that are gone (destroyed) leave its known set. A resource pin with no members yet (from a format 2
    /// file) goes when no node of its item turns up within <paramref name="mergeRadius"/> of it, all of that in range,
    /// and is reported gone at its anchor.
    /// </summary>
    /// <param name="nodeAt">A node of this item is at this position in the loaded world.</param>
    /// <param name="chestAt">A world chest or loot stand is at this position in the loaded world.</param>
    /// <param name="now">World time (ticks), recorded as the time a dropped node was found gone.</param>
    public void Prune(IPinMap map, V3 player, float radius, float mergeRadius, Func<string, V3, bool> nodeAt,
        Func<V3, bool> chestAt, float realtime, long now)
    {
        foreach (PinRecord pin in _data.Pins.ToList())
        {
            foreach (ChestState c in pin.Chests.ToList())
            {
                if (V3.DistanceXZ(c.Pos, player) > radius || chestAt(c.Pos))
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
            if (!pin.IsPlace && pin.Members.Count == 0)
            {
                // nodeAt answers true for an item the scan cannot see: then the pin is not judged.
                if (V3.DistanceXZ(pin.Anchor, player) + mergeRadius > radius || nodeAt(pin.Item, pin.Anchor))
                    pin.Absent = false;
                else if (!pin.Absent)
                {
                    pin.Absent = true;
                    pin.AbsentSince = realtime;
                }
                else if (realtime - pin.AbsentSince >= AbsenceSeconds)
                {
                    // Gone at its anchor: other players and the server's store drop their copy too.
                    GoneNode gone = MarkGone(pin.Item, pin.Anchor, now);
                    if (!_remote)
                        _newGone.Add(gone);
                    DropUnclaimed(map, pin);
                }
                continue;
            }
            foreach (Member m in pin.Members.ToList())
            {
                if (m.Kind == MemberKind.Place)
                    continue;
                if (V3.DistanceXZ(m.Pos, player) > radius || nodeAt(pin.Item, m.Pos))
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
            // A pin from a format 2 file keeps its X until its nodes are seen again.
            if (!pin.IsPlace && pin.Members.Count == 0)
                continue;
            bool want = pin.IsPlace ? Looted(pin) : Regrowing(pin, now);
            if (want == pin.AppliedChecked)
                continue;
            map.SetChecked(pin.Handle, want);
            pin.AppliedChecked = want;
            Dirty = true;
            Log?.Invoke(want ? "check" : "uncheck", pin, "");
        }
    }

    /// <summary>The player deleted one of the mod's pins: never pin its place, or the nodes it would take, again.</summary>
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
                Member m = _members.Find(g.Item, g.Pos);
                if (m == null)
                {
                    MarkGone(g.Item, g.Pos, g.At);
                    PinRecord unclaimed = _data.Pins.FirstOrDefault(p =>
                        !p.IsPlace && p.Members.Count == 0 && p.Item == g.Item && V3.DistanceXZ(p.Anchor, g.Pos) < SameNodeDistance);
                    if (unclaimed != null)
                        DropUnclaimed(map, unclaimed);
                }
                else if (m.SeenAt > g.At)
                    _changed.Add(m); // Seen here after they found it gone: send the newer sighting so they pin it again.
                else
                    DropMember(map, _owner[m], m, "gone", g.At);
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
        batch.Gone.AddRange(_data.Gone.Select(g => new GoneNode(g.Item, g.Pos, g.At)));
        return batch;
    }

    /// <summary>What this player's scans changed since the last call, or null when nothing did.</summary>
    public SyncBatch TakeDelta(Func<object, string> nameOf)
    {
        if (_changed.Count == 0 && _changedChests.Count == 0 && _newGone.Count == 0)
            return null;
        SyncBatch batch = Build(nameOf, _changed, _changedChests);
        batch.Gone.AddRange(_newGone.Where(g => _gone.Find(g.Item, g.Pos) == g).Select(g => new GoneNode(g.Item, g.Pos, g.At)));
        _changed.Clear();
        _changedChests.Clear();
        _newGone.Clear();
        return batch;
    }

    /// <summary>
    /// The registry as sightings, all of it or only the given members and chests. A resource member is sent with
    /// its pin's anchor, so the receiver clusters it onto the same pin.
    /// </summary>
    private SyncBatch Build(Func<object, string> nameOf, HashSet<Member> members, HashSet<ChestState> chests)
    {
        var batch = new SyncBatch();
        foreach (PinRecord pin in _data.Pins)
        {
            if (pin.IsPlace)
            {
                if (members == null || members.Contains(pin.Members[0]))
                    batch.Places.Add(new PlaceSighting
                    {
                        Location = pin.Item, Category = pin.Category, Label = pin.Label,
                        Pos = pin.Anchor, Interior = pin.Interior, Radius = pin.Radius,
                    });
                foreach (ChestState c in pin.Chests)
                    if (chests == null || chests.Contains(c))
                        batch.Chests.Add(new ChestSighting { Pos = c.Pos, Fill = c.Empty ? ChestFill.Empty : ChestFill.Full });
                continue;
            }
            string label = pin.Label ?? nameOf(pin.Handle);
            foreach (Member m in pin.Members)
                if (members == null || members.Contains(m))
                    batch.Resources.Add(new Sighting
                    {
                        Item = pin.Item, Label = label, Kind = m.Kind, Pos = m.Pos, Anchor = pin.Anchor,
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

    private void Touch(Member m)
    {
        if (!_remote)
            _changed.Add(m);
    }

    private void TouchChest(ChestState c)
    {
        if (!_remote)
            _changedChests.Add(c);
    }

    private PinRecord Place(string location, V3 pos) => Place(_data.Pins, location, pos);

    private static PinRecord Place(IEnumerable<PinRecord> pins, string location, V3 pos) =>
        pins.FirstOrDefault(p => p.IsPlace && p.Item == location && V3.DistanceXZ(p.Anchor, pos) < SimilarPinDistance);

    /// <summary>The player deleted a pin of this item (or location) within <paramref name="radius"/>.</summary>
    private bool Deleted(string item, V3 pos, float radius) =>
        _data.Ignored.Any(d => d.Item == item && V3.DistanceXZ(d.Pos, pos) <= radius);

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

    private void Own(PinRecord pin, Member m)
    {
        _members.Add(pin.Item, m.Pos, m);
        _owner[m] = pin;
    }

    /// <summary>Records the node gone at <paramref name="at"/>, unless it is known gone at a later time.</summary>
    private GoneNode MarkGone(string item, V3 pos, long at)
    {
        GoneNode g = _gone.Find(item, pos);
        if (g == null)
        {
            g = new GoneNode(item, pos, at);
            _gone.Add(item, pos, g);
            _data.Gone.Add(g);
        }
        else if (g.At < at)
            g.At = at;
        else
            return g;
        Dirty = true;
        return g;
    }

    private void ForgetGone(GoneNode g)
    {
        _gone.Remove(g.Item, g.Pos, g);
        _data.Gone.Remove(g);
        Dirty = true;
    }

    private void DropMember(IPinMap map, PinRecord pin, Member m, string why, long at)
    {
        pin.Members.Remove(m);
        _members.Remove(pin.Item, m.Pos, m);
        _owner.Remove(m);
        GoneNode gone = MarkGone(pin.Item, m.Pos, at);
        if (!_remote)
            _newGone.Add(gone);
        Dirty = true;
        Log?.Invoke("drop", pin, why);
        if (pin.Members.Count > 0)
            return;
        Forget(pin, ignore: false);
        map.Remove(pin.Handle);
        Log?.Invoke("remove", pin, "");
    }

    /// <summary>A resource pin with no members yet (from a format 2 file) whose nodes are all gone.</summary>
    private void DropUnclaimed(IPinMap map, PinRecord pin)
    {
        Forget(pin, ignore: false);
        map.Remove(pin.Handle);
        Log?.Invoke("remove", pin, "unclaimed");
    }

    private void Forget(PinRecord pin, bool ignore)
    {
        foreach (Member m in pin.Members.Where(m => m.Kind != MemberKind.Place))
        {
            _members.Remove(pin.Item, m.Pos, m);
            _owner.Remove(m);
        }
        if (ignore)
            _data.Ignored.Add(new Spot(pin.Item, pin.Anchor));
        _data.Pins.Remove(pin);
        Dirty = true;
    }
}
