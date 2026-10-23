using System;
using System.Collections.Generic;
using System.Linq;
using ResourcePins;
using Xunit;

public class PinRegistryTests
{
    private const int Icon = 3;
    private const float Merge = 12f;
    private static readonly long Minute = TimeSpan.TicksPerMinute;

    private sealed class FakePin
    {
        public V3 Pos;
        public int Type;
        public bool Checked;
        public string Name;
    }

    private sealed class FakeMap : IPinMap
    {
        public readonly List<FakePin> Pins = new();

        public object Add(V3 pos, int type, string name)
        {
            var p = new FakePin { Pos = pos, Type = type, Name = name };
            Pins.Add(p);
            return p;
        }

        public void Remove(object pin) => Pins.Remove((FakePin)pin);
        public void SetChecked(object pin, bool value) => ((FakePin)pin).Checked = value;
        public bool HasPinNear(V3 pos, int type) => Pins.Any(p => p.Type == type && V3.DistanceXZ(p.Pos, pos) < 1f);
        public object FindOwn(V3 pos, int type, string name) =>
            Pins.FirstOrDefault(p => p.Type == type && p.Name == name && V3.DistanceXZ(p.Pos, pos) < 1f);
        public IEnumerable<object> PinsNear(V3 pos, float distance) => Pins.Where(p => V3.DistanceXZ(p.Pos, pos) < distance);
    }

    private readonly FakeMap _map = new();
    private readonly PinRegistry _reg = new(new RegistryData(), worldUid: 1);

    private static Sighting Node(long id, float x, string item = "CopperOre") =>
        new() { Node = new NodeId(1, id), Item = item, Label = "$item_" + item, Kind = MemberKind.Mined, Pos = new V3(x, 0, 0) };

    private static Sighting Bush(long id, float x, bool picked = false, long pickedTime = 0) =>
        new()
        {
            Node = new NodeId(1, id), Item = "Raspberry", Kind = MemberKind.Regrows, RespawnMinutes = 300,
            Pos = new V3(x, 0, 0), Picked = picked, PickedTime = pickedTime,
        };

    private void See(params Sighting[] s) => _reg.Observe(_map, s, Icon, Merge);

    private void Scan(long now, params Sighting[] s)
    {
        See(s);
        _reg.UpdateChecks(_map, now);
    }

    private bool X => _map.Pins.Single().Checked;

    [Fact]
    public void Same_item_within_merge_radius_of_the_anchor_joins_its_pin()
    {
        See(Node(1, 0), Node(2, 10));

        Assert.Single(_map.Pins);
        Assert.Equal(2, _reg.Pins[0].Members.Count);
    }

    [Fact]
    public void Farther_node_or_other_item_gets_its_own_pin()
    {
        See(Node(1, 0), Node(2, 13), Node(3, 5, "TinOre"));

        Assert.Equal(3, _map.Pins.Count);
    }

    [Fact]
    public void Distance_is_always_to_the_anchor_so_pins_do_not_chain()
    {
        See(Node(1, 0), Node(2, 10), Node(3, 20));

        Assert.Equal(2, _map.Pins.Count);
        Assert.Equal(20f, _reg.Pins[1].Anchor.X);
    }

    [Fact]
    public void Seeing_the_same_node_again_adds_nothing()
    {
        See(Node(1, 0));
        See(Node(1, 0));

        Assert.Single(_map.Pins);
        Assert.Single(_reg.Pins[0].Members);
    }

    [Fact]
    public void Existing_similar_pin_is_not_duplicated()
    {
        _map.Add(new V3(0.5f, 0, 0), Icon, "my own pin");

        See(Node(1, 0));

        Assert.Single(_map.Pins);
        Assert.Empty(_reg.Pins);
    }

    [Fact]
    public void Pin_is_removed_once_all_members_stay_gone()
    {
        See(Node(1, 0), Node(2, 5));
        var alive = new HashSet<long> { 2 };
        V3 player = new(0, 0, 0);

        _reg.Prune(_map, player, 40, n => alive.Contains(n.Id), 0f, 0);
        _reg.Prune(_map, player, 40, n => alive.Contains(n.Id), PinRegistry.AbsenceSeconds, 0);
        Assert.Single(_reg.Pins[0].Members);
        Assert.Single(_map.Pins);

        alive.Clear();
        _reg.Prune(_map, player, 40, _ => false, 20f, 0);
        _reg.Prune(_map, player, 40, _ => false, 20f + PinRegistry.AbsenceSeconds, 0);
        Assert.Empty(_map.Pins);
        Assert.Empty(_reg.Pins);
    }

    [Fact]
    public void Missing_zdo_does_not_remove_a_pin_out_of_range_or_before_the_area_arrives()
    {
        See(Node(1, 0));

        _reg.Prune(_map, new V3(100, 0, 0), 40, _ => false, 0f, 0);
        _reg.Prune(_map, new V3(100, 0, 0), 40, _ => false, 60f, 0);
        _reg.Prune(_map, new V3(0, 0, 0), 40, _ => false, 61f, 0);
        _reg.Prune(_map, new V3(0, 0, 0), 40, _ => true, 62f, 0);
        _reg.Prune(_map, new V3(0, 0, 0), 40, _ => false, 63f, 0);

        Assert.Single(_map.Pins);
    }

    [Fact]
    public void Hand_deleted_pin_never_comes_back()
    {
        See(Node(1, 0), Node(2, 5));
        object pin = _reg.Pins[0].Handle;
        _map.Remove(pin);

        _reg.OnRemovedByHand(pin);
        See(Node(1, 0), Node(2, 5));

        Assert.Empty(_map.Pins);
        Assert.True(_reg.IsIgnored(new NodeId(1, 2)));
    }

    [Fact]
    public void Entry_whose_pin_is_missing_at_load_is_pinned_again_and_others_bind_by_position()
    {
        See(Node(1, 0), Node(2, 50));
        var saved = new PinRegistry(_reg.Export(), worldUid: 1);
        var kept = new object();
        var map = new FakeMap();

        saved.Bind(new[] { (kept, new V3(50.4f, 9, 0), Icon) });
        saved.Observe(map, new[] { Node(1, 0) }, Icon, Merge);

        Assert.Same(kept, saved.Pins.Single(p => p.Handle == kept).Handle);
        Assert.False(saved.IsIgnored(new NodeId(1, 1)));
        Assert.Single(map.Pins);
    }

    [Fact]
    public void Registry_survives_the_file_round_trip()
    {
        See(Node(1, 0), Node(2, 50));
        _reg.Pins[0].Members[0].Node = new NodeId(-3074457345618258602L, 4294967295L);
        _reg.OnRemovedByHand(_map.Pins[1]);

        RegistryData back = RegistryJson.Read(RegistryJson.Write(_reg.Export()));

        Assert.Equal(new NodeId(-3074457345618258602L, 4294967295L), back.Pins.Single().Members.Single().Node);
        Assert.Equal(new[] { new NodeId(1, 2) }, back.Ignored);
        Assert.Null(back.Pins.Single().Handle);
    }

    [Fact]
    public void Registry_file_without_lists_loads_empty()
    {
        RegistryData data = RegistryJson.Read("{ \"WorldUid\": 7, \"Pins\": [ { \"Item\": \"CopperOre\" } ] }");

        var reg = new PinRegistry(data, worldUid: 7);

        Assert.Empty(data.Ignored);
        Assert.Empty(reg.Pins.Single().Members);
    }

    [Fact]
    public void Own_pin_left_by_an_unsaved_registry_is_adopted_not_duplicated()
    {
        See(Node(1, 0));
        var fresh = new PinRegistry(new RegistryData(), worldUid: 1);

        fresh.Observe(_map, new[] { Node(1, 0) }, Icon, Merge);

        Assert.Single(_map.Pins);
        Assert.Same(_map.Pins[0], fresh.Pins.Single().Handle);
    }

    [Fact]
    public void Drained_root_gets_the_x_until_its_sap_refills()
    {
        var root = new Sighting
        {
            Node = new NodeId(1, 9), Item = "Sap", Kind = MemberKind.Regrows, Pos = new V3(0, 0, 0),
            Picked = true, ReadyAt = 2000 * Minute,
        };

        Scan(1000 * Minute, root);
        Assert.True(X);

        _reg.UpdateChecks(_map, 2001 * Minute);
        Assert.False(X);
    }

    [Fact]
    public void Picked_bush_gets_the_x()
    {
        Scan(1000 * Minute, Bush(1, 0, picked: true, pickedTime: 1000 * Minute));

        Assert.True(X);
    }

    [Fact]
    public void X_clears_when_world_time_passes_ready_without_a_scan()
    {
        Scan(1000 * Minute, Bush(1, 0, picked: true, pickedTime: 1000 * Minute));

        _reg.UpdateChecks(_map, 1301 * Minute);

        Assert.False(X);
    }

    [Fact]
    public void Stale_picked_flag_after_ready_counts_as_ready()
    {
        Scan(1301 * Minute, Bush(1, 0, picked: true, pickedTime: 1000 * Minute));

        Assert.False(X);
    }

    [Fact]
    public void Picked_without_a_picked_time_sets_no_x()
    {
        Scan(1000 * Minute, Bush(1, 0, picked: true, pickedTime: 0));

        Assert.False(X);
        Assert.True(_reg.Pins[0].Members[0].Unknown);
    }

    [Fact]
    public void Cluster_with_one_ready_bush_has_no_x()
    {
        Scan(1000 * Minute, Bush(1, 0, picked: true, pickedTime: 1000 * Minute), Bush(2, 3));

        Assert.False(X);
    }

    [Fact]
    public void Mined_members_never_cause_an_x()
    {
        Scan(1000 * Minute, Node(1, 0, "Raspberry"));

        Assert.False(X);
        Assert.False(PinRegistry.Regrowing(_reg.Pins[0], 1000 * Minute));
    }

    [Fact]
    public void Hand_toggle_survives_until_the_next_transition()
    {
        Scan(1000 * Minute, Bush(1, 0, picked: true, pickedTime: 1000 * Minute));
        _map.Pins[0].Checked = false; // The player clears the X by hand.

        Scan(1100 * Minute, Bush(1, 0, picked: true, pickedTime: 1000 * Minute));
        Assert.False(X);

        Scan(1400 * Minute, Bush(1, 0, picked: false));
        Scan(1500 * Minute, Bush(1, 0, picked: true, pickedTime: 1500 * Minute));
        Assert.True(X);
    }

    [Fact]
    public void Picked_oneshot_leaves_its_pin()
    {
        var mushroom = new Sighting { Node = new NodeId(1, 1), Item = "Mushroom", Kind = MemberKind.Oneshot, Pos = new V3(0, 0, 0) };
        See(mushroom);

        mushroom.Picked = true;
        See(mushroom);
        See(mushroom);

        Assert.Empty(_map.Pins);
        Assert.Empty(_reg.Pins);
    }
}
