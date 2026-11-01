using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ResourcePins;
using Xunit;

/// <summary>Players with the mod, and the server's store: what one player's scans change reaches the others, and what must not.</summary>
public class SyncTests
{
    private const int Icon = 3;
    private const int PlaceIcon = 1;
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
        public void Rename(object pin, string name) => ((FakePin)pin).Name = name;
        public bool HasPinNear(V3 pos, int type) => Pins.Any(p => p.Type == type && V3.DistanceXZ(p.Pos, pos) < 1f);
        public object FindOwn(V3 pos, int type, string name) =>
            Pins.FirstOrDefault(p => p.Type == type && p.Name == name && V3.DistanceXZ(p.Pos, pos) < 1f);
        public IEnumerable<object> PinsNear(V3 pos, float distance) => Pins.Where(p => V3.DistanceXZ(p.Pos, pos) < distance);
    }

    private sealed class Player
    {
        public readonly FakeMap Map = new();
        public readonly PinRegistry Reg = new(new RegistryData(), worldUid: 1);

        public void See(params Sighting[] s) => Reg.Observe(Map, s, Icon, Merge);

        public void Receive(SyncBatch batch, long now = 0)
        {
            if (batch != null)
                Reg.ApplyRemote(Map, batch, Icon, Merge, PlaceIcon);
            Reg.UpdateChecks(Map, now);
        }

        public SyncBatch Delta() => Reg.TakeDelta(p => ((FakePin)p).Name);
        public SyncBatch State() => Reg.Snapshot(p => ((FakePin)p).Name);

        /// <summary>The node at <paramref name="x"/> stays missing with the player next to it: its member is dropped at world time <paramref name="at"/>.</summary>
        public void LoseNode(float x, long at)
        {
            Reg.Prune(Map, new V3(0, 0, 0), 40, Merge, (_, p) => p.X != x, _ => true, 0f, at);
            Reg.Prune(Map, new V3(0, 0, 0), 40, Merge, (_, p) => p.X != x, _ => true, PinRegistry.AbsenceSeconds, at);
        }
    }

    /// <summary>The dedicated server's registry: no map, fed only by what players send.</summary>
    private sealed class Store
    {
        private readonly PinRegistry _reg = new(new RegistryData(), worldUid: 1);

        public void Receive(SyncBatch batch)
        {
            if (batch != null)
                _reg.ApplyRemote(new NoMap(), batch, Icon, Merge, PlaceIcon);
        }

        public SyncBatch State() => _reg.Snapshot(_ => "");
    }

    private readonly Player _a = new();
    private readonly Player _b = new();

    private static Sighting Ore(float x, long seenAt = 0) =>
        new()
        {
            Item = "CopperOre", Label = "$item_copperore", Kind = MemberKind.Mined, Pos = new V3(x, 0, 0),
            SeenAt = seenAt,
        };

    private static Sighting Bush(bool picked = false, long pickedTime = 0) =>
        new()
        {
            Item = "Raspberry", Label = "$item_raspberries", Kind = MemberKind.Regrows,
            RespawnMinutes = 300, Pos = new V3(0, 0, 0), Picked = picked, PickedTime = pickedTime,
        };

    private static PlaceSighting Mine() =>
        new()
        {
            Location = "Mistlands_DvergrTownEntrance2", Category = PlaceCatalog.Dungeons,
            Label = "Dungeon", Pos = new V3(10, 40, 10), Interior = true, Radius = 32,
        };

    private static ChestSighting Stand(ChestFill fill) =>
        new() { Pos = new V3(5, 5040, -8), Fill = fill };

    [Fact]
    public void A_pin_one_player_finds_appears_for_the_other_with_its_name()
    {
        _a.See(Ore(0));

        _b.Receive(_a.Delta());

        FakePin pin = Assert.Single(_b.Map.Pins);
        Assert.Equal("$item_copperore", pin.Name);
        Assert.Equal(Icon, pin.Type);
    }

    [Fact]
    public void A_node_one_player_mines_out_removes_the_pin_for_the_other()
    {
        _a.See(Ore(0));
        _b.Receive(_a.Delta());

        _a.LoseNode(0, at: 2 * Minute);
        _b.Receive(_a.Delta());

        Assert.Empty(_b.Map.Pins);
    }

    [Fact]
    public void A_player_who_joins_gets_everything_known_so_far()
    {
        _a.See(Ore(0), Ore(50), Ore(52));
        _a.Delta();

        _b.Receive(_a.State());

        Assert.Equal(2, _b.Map.Pins.Count);
        Assert.Equal(3, _b.Reg.Pins.Sum(p => p.Members.Count));
    }

    [Fact]
    public void A_pin_deleted_by_hand_stays_deleted_for_that_player_only()
    {
        _a.See(Ore(0));
        _b.Receive(_a.Delta());

        _b.Reg.OnRemovedByHand(_b.Map.Pins[0]);
        _b.Map.Pins.Clear();
        _b.Receive(_a.State());

        Assert.Empty(_b.Map.Pins);
        Assert.Single(_a.Map.Pins);
    }

    [Fact]
    public void An_old_copy_from_someone_who_missed_the_mining_does_not_bring_the_pin_back()
    {
        _a.See(Ore(0, seenAt: Minute));
        SyncBatch old = _a.State();
        _b.Receive(old);
        _b.LoseNode(0, at: 2 * Minute);

        _b.Receive(old);

        Assert.Empty(_b.Map.Pins);
    }

    [Fact]
    public void A_player_who_missed_the_mining_learns_it_on_joining()
    {
        _a.See(Ore(0, seenAt: Minute));
        _b.Receive(_a.State());
        _b.LoseNode(0, at: 2 * Minute);

        _a.Receive(_b.State());

        Assert.Empty(_a.Map.Pins);
    }

    [Fact]
    public void Picked_berries_get_the_x_for_everyone_and_an_older_copy_does_not_clear_it()
    {
        _a.See(Bush());
        SyncBatch unpicked = _a.State();
        _b.Receive(_a.Delta());

        _a.See(Bush(picked: true, pickedTime: 10 * Minute));
        _b.Receive(_a.Delta(), now: 20 * Minute);
        Assert.True(_b.Map.Pins.Single().Checked);

        _b.Receive(unpicked, now: 20 * Minute);
        Assert.True(_b.Map.Pins.Single().Checked);
    }

    [Fact]
    public void A_mine_looted_by_one_player_gets_the_x_for_the_other_and_stays_looted()
    {
        _a.Reg.ObservePlaces(_a.Map, new[] { Mine() }, new[] { Stand(ChestFill.Full) }, PlaceIcon);
        SyncBatch beforeLooting = _a.State();
        _b.Receive(_a.Delta());
        Assert.False(_b.Map.Pins.Single().Checked);

        _a.Reg.ObservePlaces(_a.Map, new[] { Mine() }, new[] { Stand(ChestFill.Empty) }, PlaceIcon);
        _b.Receive(_a.Delta());
        Assert.True(_b.Map.Pins.Single().Checked);

        _b.Receive(beforeLooting);
        Assert.True(_b.Map.Pins.Single().Checked);
    }

    [Fact]
    public void What_a_player_receives_is_not_sent_back_out()
    {
        _a.See(Ore(0), Bush(picked: true, pickedTime: Minute));
        _a.Reg.ObservePlaces(_a.Map, new[] { Mine() }, new[] { Stand(ChestFill.Empty) }, PlaceIcon);

        _b.Receive(_a.State());

        Assert.Null(_b.Delta());
    }

    [Fact]
    public void A_node_wrongly_found_gone_comes_back_for_everyone_once_someone_sees_it_later()
    {
        _a.See(Ore(0, seenAt: Minute));
        _b.Receive(_a.Delta());
        _a.LoseNode(0, at: 2 * Minute);
        _b.See(Ore(0, seenAt: 3 * Minute));

        _b.Receive(_a.Delta());
        _a.Receive(_b.Delta());

        Assert.Single(_a.Map.Pins);
        Assert.Single(_b.Map.Pins);

        _a.Receive(_b.State());
        _b.Receive(_a.State());

        Assert.Single(_a.Map.Pins);
        Assert.Single(_b.Map.Pins);
    }

    [Fact]
    public void The_server_store_hands_what_one_group_found_to_the_next_group()
    {
        var server = new Store();
        _a.See(Ore(0, seenAt: Minute), Ore(30, seenAt: Minute));
        server.Receive(_a.Delta());
        _a.LoseNode(30, at: 2 * Minute);
        server.Receive(_a.Delta());

        _b.Receive(server.State());

        FakePin pin = Assert.Single(_b.Map.Pins);
        Assert.Equal(0f, pin.Pos.X);
    }

    [Fact]
    public void A_full_state_sent_in_parts_gives_the_same_pins_as_in_one_piece()
    {
        _a.See(Ore(0, seenAt: Minute), Ore(5, seenAt: Minute), Ore(50, seenAt: Minute), Ore(30, seenAt: Minute));
        _a.Reg.ObservePlaces(_a.Map, new[] { Mine() }, new[] { Stand(ChestFill.Empty) }, PlaceIcon);
        _a.LoseNode(30, at: 2 * Minute);
        var old = new Player();
        old.See(Ore(30, seenAt: Minute));
        var whole = new Player();
        whole.Receive(_a.State());

        List<SyncBatch> parts = _a.State().Split(2);
        foreach (SyncBatch part in parts)
        {
            _b.Receive(part);
            _b.Receive(old.State());
        }

        Assert.True(parts.Count > 1);
        Assert.Equal(whole.Map.Pins.Select(p => (p.Pos.X, p.Checked)).OrderBy(p => p.X), _b.Map.Pins.Select(p => (p.Pos.X, p.Checked)).OrderBy(p => p.X));
        Assert.True(_b.Map.Pins.Single(p => p.Type == PlaceIcon).Checked);
        Assert.DoesNotContain(_b.Map.Pins, p => p.Pos.X == 30);
    }

    [Fact]
    public void Someone_who_heard_a_node_is_gone_before_seeing_it_ignores_an_older_copy()
    {
        var server = new Store();
        var old = new Player();
        old.See(Ore(0, seenAt: Minute));
        _a.See(Ore(0, seenAt: Minute));
        _a.Delta();
        _a.LoseNode(0, at: 2 * Minute);

        server.Receive(_a.Delta());
        server.Receive(old.State());
        _b.Receive(server.State());

        Assert.Empty(_b.Map.Pins);
    }

    private static IEnumerable<(float, float, int, string, bool)> Shown(Player p) =>
        p.Map.Pins.Select(x => (x.Pos.X, x.Pos.Z, x.Type, x.Name, x.Checked)).OrderBy(x => x);

    [Fact]
    public void A_state_sent_over_the_wire_gives_the_same_pins_as_handed_over_directly()
    {
        _a.See(Ore(0, seenAt: Minute), Ore(5, seenAt: Minute));
        _a.Reg.ObservePlaces(_a.Map, new[] { Mine() }, new[] { Stand(ChestFill.Empty) }, PlaceIcon);
        _a.LoseNode(5, at: 2 * Minute);
        var direct = new Player();
        direct.Receive(_a.State(), now: 2 * Minute);
        var old = new Player();
        old.See(Ore(5, seenAt: Minute));

        Assert.True(SyncWire.TryRead(SyncWire.Write(7, true, _a.State()), 7, out _, out bool askBack, out SyncBatch batch));
        _b.Receive(batch, now: 2 * Minute);
        _b.Receive(old.State(), now: 2 * Minute);

        Assert.True(askBack);
        Assert.Equal(Shown(direct), Shown(_b));
        Assert.Equal(2, _b.Map.Pins.Count);
        Assert.True(_b.Map.Pins.Single(p => p.Type == PlaceIcon).Checked);
    }

    [Fact]
    public void Messages_of_protocol_2_and_3_are_dropped_by_the_other_version()
    {
        // A message of the previous version: protocol, world, ask-back, then one resource named by its ZDOID.
        var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream))
        {
            w.Write(2);
            w.Write(7L);
            w.Write(true);
            w.Write(1);
            w.Write(1L);
            w.Write(228603L);
            w.Write("CopperOre");
            w.Write("$item_copperore");
            w.Write(0);
            w.Write(0f);
            w.Write(0f);
            w.Write(0f);
            w.Write(0f);
            w.Write(false);
            w.Write(0L);
            w.Write(0L);
            w.Write(0L);
            w.Write(0);
            w.Write(0);
            w.Write(0);
        }
        Assert.False(SyncWire.TryRead(stream.ToArray(), 7, out int protocol, out _, out SyncBatch batch));
        Assert.Equal(2, protocol);
        Assert.Null(batch);

        // The previous version reads the protocol first and drops anything but 2.
        _a.See(Ore(0));
        byte[] ours = SyncWire.Write(7, false, _a.State());
        Assert.NotEqual(2, new BinaryReader(new MemoryStream(ours)).ReadInt32());
        Assert.False(SyncWire.TryRead(ours, 8, out _, out _, out _));
    }
}
