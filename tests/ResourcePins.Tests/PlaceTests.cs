using System.Collections.Generic;
using System.Linq;
using ResourcePins;
using Xunit;

public class PlaceTests
{
    private const int ResourceIcon = 3;
    private const int PlaceIcon = 1;

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

    private static readonly V3 Entrance = new(10, 30, 10);

    private static PlaceSighting Crypt(long id = 100, V3? pos = null) =>
        new()
        {
            Node = new NodeId(1, id), Location = "Crypt2", Category = PlaceCatalog.Dungeons, Label = "$location_forestcrypt",
            Pos = pos ?? Entrance, Interior = true, Radius = 12,
        };

    private static ChestSighting Chest(long id, ChestFill fill) =>
        new() { Node = new NodeId(1, id), Pos = new V3(5, 5030, -8), Fill = fill };

    private void Scan(PlaceSighting[] places, params ChestSighting[] chests)
    {
        _reg.ObservePlaces(_map, places, chests, PlaceIcon);
        _reg.UpdateChecks(_map, 0);
    }

    private void Scan(params ChestSighting[] chests) => Scan(new[] { Crypt() }, chests);

    private bool X => _map.Pins.Single().Checked;

    [Fact]
    public void Configured_locations_match_by_exact_name_and_unknown_names_are_set_aside()
    {
        var known = new HashSet<string> { "Crypt2", "GoblinCamp2" };
        var catalog = new PlaceCatalog(new[] { ("Dungeons", "Crypt2, crypt2, Nowhere"), ("Camps", "GoblinCamp2,Crypt2") },
            known.Contains, out List<string> unknown);

        Assert.Equal("Dungeons", catalog.Category("Crypt2"));
        Assert.Equal("Camps", catalog.Category("GoblinCamp2"));
        Assert.Null(catalog.Category("crypt2"));
        Assert.Equal(new[] { "crypt2", "Nowhere" }, unknown);
    }

    [Fact]
    public void Place_icon_equal_to_the_resource_icon_or_not_allowed_falls_back()
    {
        Assert.Equal(PlaceIcons.Icon2, PlaceIcons.Resolve(PlaceIcons.Icon2, ResourceIcon, out bool ok));
        Assert.False(ok);
        Assert.Equal(PlaceIcons.Icon1, PlaceIcons.Resolve(ResourceIcon, ResourceIcon, out bool same));
        Assert.True(same);
        Assert.Equal(PlaceIcons.Icon0, PlaceIcons.Resolve(PlaceIcons.Icon1, PlaceIcons.Icon1, out _));
    }

    [Fact]
    public void Chest_blob_item_count_decides_empty_and_unrolled_or_unsaved_chests_are_unknown()
    {
        byte[] empty = { 110, 0, 0, 0, 0, 0 };
        byte[] full = { 110, 0, 0, 0, 2, 0, 1, 2, 3 };

        Assert.Equal(ChestFill.Empty, PlaceCatalog.ReadChest(true, empty));
        Assert.Equal(ChestFill.Full, PlaceCatalog.ReadChest(true, full));
        Assert.Equal(ChestFill.Unknown, PlaceCatalog.ReadChest(false, empty));
        Assert.Equal(ChestFill.Unknown, PlaceCatalog.ReadChest(true, null));
    }

    [Fact]
    public void Interior_chest_belongs_to_the_entrance_zone_only()
    {
        Assert.True(PlaceCatalog.OwnsChest(Entrance, true, 12, new V3(-20, 5030, 25)));
        Assert.False(PlaceCatalog.OwnsChest(Entrance, true, 12, new V3(40, 5030, 10)));
        Assert.False(PlaceCatalog.OwnsChest(Entrance, true, 12, new V3(12, 30, 10)));
    }

    [Fact]
    public void Outdoor_chest_belongs_within_the_exterior_radius()
    {
        Assert.True(PlaceCatalog.OwnsChest(Entrance, false, 12, new V3(20, 31, 10)));
        Assert.False(PlaceCatalog.OwnsChest(Entrance, false, 12, new V3(23, 31, 10)));
    }

    [Fact]
    public void All_known_chests_empty_sets_the_x()
    {
        Scan(Chest(1, ChestFill.Empty), Chest(2, ChestFill.Empty));

        Assert.True(X);
    }

    [Fact]
    public void One_chest_with_loot_keeps_the_x_off()
    {
        Scan(Chest(1, ChestFill.Empty), Chest(2, ChestFill.Full));

        Assert.False(X);
    }

    [Fact]
    public void Unknown_chests_are_not_known_and_no_known_chest_means_no_x()
    {
        Scan(Chest(1, ChestFill.Unknown));

        Assert.False(X);
        Assert.Empty(_reg.Pins.Single().Chests);

        Scan(Chest(1, ChestFill.Unknown), Chest(2, ChestFill.Empty));
        Assert.True(X);
    }

    [Fact]
    public void Chest_out_of_this_scan_keeps_its_last_state()
    {
        Scan(Chest(1, ChestFill.Empty), Chest(2, ChestFill.Full));
        Scan(Chest(2, ChestFill.Empty));

        Assert.True(X);
    }

    [Fact]
    public void Boss_altar_never_gets_the_x_from_a_nearby_chest()
    {
        PlaceSighting altar = Crypt(200, new V3(500, 30, 500));
        altar.Category = "Bosses";
        altar.Interior = false;
        Scan(new[] { altar }, new ChestSighting { Node = new NodeId(1, 9), Pos = new V3(505, 30, 500), Fill = ChestFill.Empty });

        Assert.False(X);
        Assert.Empty(_reg.Pins.Single().Chests);
    }

    [Fact]
    public void Hand_toggle_survives_until_the_next_transition()
    {
        Scan(Chest(1, ChestFill.Empty));
        _map.Pins[0].Checked = false; // The player clears the X by hand.

        Scan(Chest(1, ChestFill.Empty));
        Assert.False(X);

        Scan(Chest(1, ChestFill.Full));
        Scan(Chest(1, ChestFill.Empty));
        Assert.True(X);
    }

    [Fact]
    public void Location_pin_is_never_removed_even_when_looted_or_its_zdo_is_missing()
    {
        Scan(Chest(1, ChestFill.Empty));
        V3 player = Entrance;

        _reg.Prune(_map, player, 40, _ => false, 0f, 0);
        _reg.Prune(_map, player, 40, _ => false, PinRegistry.AbsenceSeconds, 0);

        Assert.Single(_map.Pins);
        Assert.Single(_reg.Pins);
    }

    [Fact]
    public void Destroyed_chest_leaves_the_known_set_but_not_the_pin()
    {
        Scan(Chest(1, ChestFill.Empty), Chest(2, ChestFill.Full));
        var proxy = new NodeId(1, 100);

        _reg.Prune(_map, Entrance, 40, n => n.Equals(proxy) || n.Id == 1, 0f, 0);
        _reg.Prune(_map, Entrance, 40, n => n.Equals(proxy) || n.Id == 1, PinRegistry.AbsenceSeconds, 0);
        _reg.UpdateChecks(_map, 0);

        Assert.True(X);
    }

    [Fact]
    public void Egg_pin_is_removed_when_its_zdo_is_gone()
    {
        var egg = new Sighting { Node = new NodeId(1, 7), Item = "DragonEgg", Kind = MemberKind.Oneshot, Pos = new V3(0, 0, 0) };
        _reg.Observe(_map, new[] { egg }, ResourceIcon, 12);

        _reg.Prune(_map, new V3(0, 0, 0), 40, _ => false, 0f, 0);
        _reg.Prune(_map, new V3(0, 0, 0), 40, _ => false, PinRegistry.AbsenceSeconds, 0);

        Assert.Empty(_map.Pins);
        Assert.Empty(_reg.Pins);
    }

    [Fact]
    public void No_place_pin_within_5_m_of_a_pin_the_mod_does_not_own()
    {
        _map.Add(new V3(14, 0, 10), 9, "teammate's boss pin");

        Scan(new[] { Crypt() });

        Assert.Single(_map.Pins);
        Assert.Empty(_reg.Pins);
    }

    [Fact]
    public void The_mods_own_resource_pin_nearby_does_not_block_a_place()
    {
        _reg.Observe(_map, new[] { new Sighting { Node = new NodeId(1, 1), Item = "Raspberry", Kind = MemberKind.Regrows, Pos = new V3(12, 0, 10) } },
            ResourceIcon, 12);

        Scan(new[] { Crypt() });

        Assert.Equal(2, _map.Pins.Count);
    }

    [Fact]
    public void Seeing_a_place_again_or_after_deleting_its_pin_adds_nothing()
    {
        Scan(new[] { Crypt() });
        Scan(new[] { Crypt() });
        Assert.Single(_map.Pins);

        object pin = _reg.Pins[0].Handle;
        _map.Remove(pin);
        _reg.OnRemovedByHand(pin);
        Scan(new[] { Crypt() });

        Assert.Empty(_map.Pins);
    }
}
