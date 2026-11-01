using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using ResourcePins;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// World loads: ZDO.Load renumbers every ZDO, so what the registry keeps must come back the same, and files of format 2
/// (nodes named by ZDOID) must load into one pin per object.
/// </summary>
public class ReloadTests
{
    private const int Icon = 3;
    private const int PlaceIcon = 1;
    private const float Merge = 12f;
    private static readonly long Minute = TimeSpan.TicksPerMinute;

    private readonly ITestOutputHelper _out;

    public ReloadTests(ITestOutputHelper output) => _out = output;

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

        /// <summary>The character's saved pins, as the registry binds them when the map loads.</summary>
        public List<(object, V3, int)> Loaded() => Pins.Select(p => ((object)p, p.Pos, p.Type)).ToList();
    }

    private static Sighting Ore(float x, long seenAt = 0) =>
        new() { Item = "CopperOre", Label = "$item_copperore", Kind = MemberKind.Mined, Pos = new V3(x, 0, 0), SeenAt = seenAt };

    private static Sighting Bush(float x, float z, bool picked = false, long pickedTime = 0) =>
        new()
        {
            Item = "Raspberry", Label = "$item_raspberries", Kind = MemberKind.Regrows, RespawnMinutes = 300,
            Pos = new V3(x, 0, z), Picked = picked, PickedTime = pickedTime,
        };

    private static readonly V3 CryptAt = new(-644, 30, -4285);

    private static PlaceSighting Crypt() =>
        new()
        {
            Location = "Crypt3", Category = PlaceCatalog.Dungeons, Label = "$location_forestcrypt", Pos = CryptAt,
            Interior = true, Radius = 12,
        };

    /// <summary>The world was saved and loaded again: the registry comes back from its file, bound to the character's pins.</summary>
    private static PinRegistry Reload(PinRegistry registry, FakeMap map)
    {
        var loaded = new PinRegistry(RegistryJson.Read(RegistryJson.Write(registry.Export())), worldUid: 1);
        loaded.Bind(map, map.Loaded());
        return loaded;
    }

    /// <summary>
    /// A registry as format 2 wrote it after several world loads: the crypt twice (one copy per load, each with its own
    /// map pin and the same chests under other ids), a picked raspberry pin, and ids of hand deletions and gone nodes.
    /// </summary>
    private const string Format2 = @"{
  ""WorldUid"": 1,
  ""Pins"": [
    { ""Item"": ""Crypt3"", ""Type"": 1, ""Anchor"": { ""X"": -644.0, ""Y"": 30.0, ""Z"": -4285.0 }, ""AppliedChecked"": false,
      ""Members"": [ { ""Node"": { ""User"": 1, ""Id"": 228100 }, ""Kind"": 3, ""ReadyAt"": 0, ""Unknown"": false, ""SeenAt"": 0 } ],
      ""Category"": ""Dungeons"", ""Label"": ""$location_forestcrypt"", ""Interior"": true, ""Radius"": 12.0,
      ""Chests"": [
        { ""Node"": { ""User"": 1, ""Id"": 228200 }, ""Pos"": { ""X"": -640.0, ""Y"": 5030.0, ""Z"": -4280.0 }, ""Empty"": false },
        { ""Node"": { ""User"": 1, ""Id"": 228201 }, ""Pos"": { ""X"": -650.0, ""Y"": 5030.0, ""Z"": -4290.0 }, ""Empty"": false } ] },
    { ""Item"": ""Crypt3"", ""Type"": 1, ""Anchor"": { ""X"": -644.0, ""Y"": 30.0, ""Z"": -4285.0 }, ""AppliedChecked"": false,
      ""Members"": [ { ""Node"": { ""User"": 1, ""Id"": 230900 }, ""Kind"": 3, ""ReadyAt"": 0, ""Unknown"": false, ""SeenAt"": 0 } ],
      ""Category"": ""Dungeons"", ""Label"": ""$location_forestcrypt"", ""Interior"": true, ""Radius"": 12.0,
      ""Chests"": [
        { ""Node"": { ""User"": 1, ""Id"": 230950 }, ""Pos"": { ""X"": -640.0, ""Y"": 5030.0, ""Z"": -4280.0 }, ""Empty"": true },
        { ""Node"": { ""User"": 1, ""Id"": 230951 }, ""Pos"": { ""X"": -650.0, ""Y"": 5030.0, ""Z"": -4290.0 }, ""Empty"": true } ] },
    { ""Item"": ""Raspberry"", ""Type"": 3, ""Anchor"": { ""X"": -900.0, ""Y"": 0.0, ""Z"": -4372.0 }, ""AppliedChecked"": true,
      ""Members"": [
        { ""Node"": { ""User"": 1, ""Id"": 228603 }, ""Kind"": 1, ""ReadyAt"": 9000000000000, ""Unknown"": false, ""SeenAt"": 5 },
        { ""Node"": { ""User"": 1, ""Id"": 231003 }, ""Kind"": 1, ""ReadyAt"": 9000000000000, ""Unknown"": false, ""SeenAt"": 5 } ],
      ""Category"": null, ""Label"": ""$item_raspberries"", ""Interior"": false, ""Radius"": 0.0, ""Chests"": [] }
  ],
  ""Ignored"": [ { ""User"": 1, ""Id"": 228617 }, { ""User"": -1209534535, ""Id"": 441352 } ],
  ""Gone"": [ { ""Node"": { ""User"": 1, ""Id"": 228618 }, ""At"": 12 } ]
}";

    /// <summary>The character's map for <see cref="Format2"/>: one pin per entry, the raspberry pin with the X.</summary>
    private static FakeMap MapOf(RegistryData data)
    {
        var map = new FakeMap();
        foreach (PinRecord pin in data.Pins)
            ((FakePin)map.Add(pin.Anchor, pin.Type, pin.Label)).Checked = pin.AppliedChecked;
        return map;
    }

    [Fact]
    public void A_format_2_registry_keeps_one_pin_per_place_with_its_loot_and_its_resource_pins()
    {
        RegistryData data = RegistryJson.Read(Format2);
        FakeMap map = MapOf(data);
        var reg = new PinRegistry(data, worldUid: 1);
        reg.Bind(map, map.Loaded());
        reg.UpdateChecks(map, 0);

        Assert.Equal(2, map.Pins.Count);
        FakePin crypt = map.Pins.Single(p => p.Type == PlaceIcon);
        Assert.True(crypt.Checked);
        Assert.Equal(2, reg.Pins.Single(p => p.IsPlace).Chests.Count);
        FakePin berries = map.Pins.Single(p => p.Type == Icon);
        Assert.True(berries.Checked);

        // The bushes come back under new ZDOs: they rejoin their pin, which takes their pick state.
        reg.Observe(map, new[] { Bush(-900, -4372), Bush(-896, -4370) }, Icon, Merge);
        reg.ObservePlaces(map, new[] { Crypt() }, new ChestSighting[0], PlaceIcon);
        reg.UpdateChecks(map, 10 * Minute);

        Assert.Equal(2, map.Pins.Count);
        Assert.Equal(2, reg.Pins.Single(p => !p.IsPlace).Members.Count);
        Assert.False(berries.Checked);
    }

    [Fact]
    public void A_format_2_pin_whose_nodes_are_gone_leaves_once_its_whole_area_was_in_range()
    {
        RegistryData data = RegistryJson.Read(Format2);
        FakeMap map = MapOf(data);
        var reg = new PinRegistry(data, worldUid: 1);
        reg.Bind(map, map.Loaded());
        V3 farSide = new(-900 + 35, 0, -4372);
        V3 next = new(-900 + 5, 0, -4372);

        reg.Prune(map, farSide, 40, Merge, (_, _) => false, _ => true, 0f, 0);
        reg.Prune(map, farSide, 40, Merge, (_, _) => false, _ => true, PinRegistry.AbsenceSeconds, 0);
        Assert.Contains(map.Pins, p => p.Type == Icon);

        reg.Prune(map, next, 40, Merge, (_, _) => false, _ => true, 20f, 0);
        reg.Prune(map, next, 40, Merge, (_, _) => false, _ => true, 20f + PinRegistry.AbsenceSeconds, 0);
        Assert.DoesNotContain(map.Pins, p => p.Type == Icon);
        Assert.Single(map.Pins);
    }

    /// <summary>Format 2 resource pins of one item, members not saved (their ZDOIDs mean nothing after a load).</summary>
    private static RegistryData Format2Berries(params float[] anchors)
    {
        var data = new RegistryData { WorldUid = 1 };
        foreach (float x in anchors)
            data.Pins.Add(new PinRecord
            {
                Item = "Raspberry", Type = Icon, Anchor = new V3(x, 0, 0), Label = "$item_raspberries",
                Members = { new Member { Kind = MemberKind.Regrows } },
            });
        return data;
    }

    [Fact]
    public void A_format_2_pin_takes_back_the_nodes_nearest_to_it_and_one_without_nodes_leaves()
    {
        RegistryData data = Format2Berries(0, 15);
        FakeMap map = MapOf(data);
        var reg = new PinRegistry(data, worldUid: 1);
        reg.Bind(map, map.Loaded());

        reg.Observe(map, new[] { Bush(0, 0), Bush(5, 0), Bush(7, 0) }, Icon, Merge);
        reg.Prune(map, new V3(15, 0, 0), 40, Merge, (_, p) => p.X <= 7, _ => true, 0f, Minute);
        reg.Prune(map, new V3(15, 0, 0), 40, Merge, (_, p) => p.X <= 7, _ => true, PinRegistry.AbsenceSeconds, Minute);

        PinRecord pin = Assert.Single(reg.Pins);
        Assert.Equal(new[] { 0f, 5f, 7f }, pin.Members.Select(m => m.Pos.X));
        Assert.Equal(0f, Assert.Single(map.Pins).Pos.X);
    }

    [Fact]
    public void A_format_2_pin_a_player_finds_without_nodes_leaves_the_server_store_too()
    {
        RegistryData data = Format2Berries(0);
        FakeMap map = MapOf(data);
        var player = new PinRegistry(data, worldUid: 1);
        player.Bind(map, map.Loaded());
        var store = new PinRegistry(Format2Berries(0), worldUid: 1);

        player.Prune(map, new V3(0, 0, 0), 40, Merge, (_, _) => false, _ => true, 0f, Minute);
        player.Prune(map, new V3(0, 0, 0), 40, Merge, (_, _) => false, _ => true, PinRegistry.AbsenceSeconds, Minute);
        store.ApplyRemote(new NoMap(), player.TakeDelta(_ => ""), Icon, Merge, PlaceIcon);

        Assert.Empty(map.Pins);
        Assert.Empty(store.Export().Pins);
    }

    [Fact]
    public void Merged_copies_of_a_place_show_the_x_of_their_loot_on_the_pin_kept()
    {
        var data = new RegistryData { WorldUid = 1 };
        var map = new FakeMap();
        foreach (bool x in new[] { false, true })
            data.Pins.Add(new PinRecord
            {
                Item = "Crypt3", Type = PlaceIcon, Anchor = CryptAt, Category = PlaceCatalog.Dungeons, Label = "$location_forestcrypt",
                Interior = true, Radius = 12, AppliedChecked = x, Members = { new Member { Kind = MemberKind.Place } },
                Chests = { new ChestState { Pos = new V3(-640, 5030, -4280), Empty = false } },
            });
        // The character's map lists the copy's pin, with its X, first.
        ((FakePin)map.Add(CryptAt, PlaceIcon, "$location_forestcrypt")).Checked = true;
        map.Add(CryptAt, PlaceIcon, "$location_forestcrypt");
        var reg = new PinRegistry(data, worldUid: 1);

        reg.Bind(map, map.Loaded());
        reg.UpdateChecks(map, 0);

        Assert.False(Assert.Single(map.Pins).Checked);
    }

    [Fact]
    public void After_a_world_load_each_node_keeps_one_pin_and_a_gone_node_stays_gone()
    {
        var map = new FakeMap();
        var reg = new PinRegistry(new RegistryData { WorldUid = 1 }, worldUid: 1);
        reg.Observe(map, new[] { Ore(0, Minute), Ore(5, Minute) }, Icon, Merge);
        reg.ObservePlaces(map, new[] { Crypt() }, new ChestSighting[0], PlaceIcon);
        reg.Prune(map, new V3(0, 0, 0), 40, Merge, (_, p) => p.X != 5, _ => true, 0f, 2 * Minute);
        reg.Prune(map, new V3(0, 0, 0), 40, Merge, (_, p) => p.X != 5, _ => true, PinRegistry.AbsenceSeconds, 2 * Minute);
        var other = new PinRegistry(new RegistryData { WorldUid = 1 }, worldUid: 1);
        other.Observe(new FakeMap(), new[] { Ore(0, Minute), Ore(5, Minute) }, Icon, Merge);

        PinRegistry loaded = Reload(reg, map);
        loaded.Observe(map, new[] { Ore(0, 3 * Minute) }, Icon, Merge);
        loaded.ObservePlaces(map, new[] { Crypt() }, new ChestSighting[0], PlaceIcon);
        loaded.ApplyRemote(map, other.Snapshot(_ => ""), Icon, Merge, PlaceIcon);

        Assert.Equal(2, map.Pins.Count);
        Assert.Equal(new[] { 0f }, loaded.Pins.Single(p => !p.IsPlace).Members.Select(m => m.Pos.X));
    }

    [Fact]
    public void A_pin_deleted_by_hand_stays_deleted_after_a_world_load()
    {
        var map = new FakeMap();
        var reg = new PinRegistry(new RegistryData { WorldUid = 1 }, worldUid: 1);
        reg.Observe(map, new[] { Ore(0), Ore(5) }, Icon, Merge);
        reg.ObservePlaces(map, new[] { Crypt() }, new ChestSighting[0], PlaceIcon);
        var other = new PinRegistry(new RegistryData { WorldUid = 1 }, worldUid: 1);
        other.Observe(new FakeMap(), new[] { Ore(0), Ore(5), Ore(9) }, Icon, Merge);
        other.ObservePlaces(new FakeMap(), new[] { Crypt() }, new ChestSighting[0], PlaceIcon);
        foreach (PinRecord pin in reg.Pins.ToList())
        {
            map.Remove(pin.Handle);
            reg.OnRemovedByHand(pin.Handle);
        }

        PinRegistry loaded = Reload(reg, map);
        loaded.Observe(map, new[] { Ore(0), Ore(5), Ore(9) }, Icon, Merge);
        loaded.ObservePlaces(map, new[] { Crypt() }, new ChestSighting[0], PlaceIcon);
        loaded.ApplyRemote(map, other.Snapshot(_ => ""), Icon, Merge, PlaceIcon);

        Assert.Empty(map.Pins);
    }

    [Fact]
    public void A_format_2_server_store_is_backed_up_once_and_hands_out_one_copy_of_each_place()
    {
        string dir = Path.Combine(Path.GetTempPath(), "ResourcePinsTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "ANAAF2_server.json");
            File.WriteAllText(path, Format2);
            var store = new PinRegistry(RegistryJson.Read(File.ReadAllText(path)), worldUid: 1);

            // A player who joins sends its state: the store's raspberry pin takes the bushes back instead of a copy.
            var player = new PinRegistry(new RegistryData { WorldUid = 1 }, worldUid: 1);
            player.Observe(new FakeMap(), new[] { Bush(-901, -4372), Bush(-896, -4370) }, Icon, Merge);
            store.ApplyRemote(new NoMap(), player.Snapshot(_ => ""), Icon, Merge, PlaceIcon);
            RegistryJson.Save(store, path);
            RegistryJson.Save(store, path);

            Assert.Equal(Format2, File.ReadAllText(path + ".v2.bak"));
            var reloaded = new PinRegistry(RegistryJson.Read(File.ReadAllText(path)), worldUid: 1);
            Assert.False(reloaded.Migrated);
            SyncBatch state = reloaded.Snapshot(_ => "");
            PlaceSighting crypt = Assert.Single(state.Places);
            Assert.Equal(CryptAt, crypt.Pos);
            Assert.Equal(2, state.Chests.Count);
            Assert.All(state.Chests, c => Assert.Equal(ChestFill.Empty, c.Fill));
            Assert.Equal(2, state.Resources.Count);
            Assert.All(state.Resources, s => Assert.Equal(new V3(-900, 0, -4372), s.Anchor));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// Offline check of a real format 2 registry: set RESOURCEPINS_REGISTRY_COPY to a copy of one (never the game's
    /// own file). It is migrated in a temporary folder and the counts are written to the test output. Without it the test does nothing.
    /// </summary>
    [Fact]
    public void A_real_format_2_registry_migrates_without_losing_pins()
    {
        string source = Environment.GetEnvironmentVariable("RESOURCEPINS_REGISTRY_COPY");
        if (string.IsNullOrEmpty(source))
            return;
        string dir = Path.Combine(Path.GetTempPath(), "ResourcePinsTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, Path.GetFileName(source));
            File.Copy(source, path);
            string json = File.ReadAllText(path);
            JObject raw = JObject.Parse(json);
            var oldPins = (JArray)raw["Pins"];
            var oldPlaces = oldPins.Where(p => !string.IsNullOrEmpty((string)p["Category"])).ToList();
            _out.WriteLine($"before: {oldPins.Count} pins ({oldPlaces.Count} places, {oldPins.Count - oldPlaces.Count} resources), " +
                           $"{oldPins.Sum(p => ((JArray)p["Members"]).Count)} members, {oldPlaces.Sum(p => ((JArray)p["Chests"]).Count)} chests, " +
                           $"{((JArray)raw["Ignored"]).Count} ignored, {((JArray)raw["Gone"]).Count} gone");

            RegistryData data = RegistryJson.Read(json);
            FakeMap map = MapOf(data);
            int mapBefore = map.Pins.Count;
            var reg = new PinRegistry(data, (long)raw["WorldUid"]);
            var merged = new List<string>();
            reg.Log = (evt, pin, detail) =>
            {
                if (detail == "duplicate")
                    merged.Add($"{pin.Item} ({pin.Anchor.X:0},{pin.Anchor.Z:0})");
            };
            reg.Bind(map, map.Loaded());
            RegistryJson.Save(reg, path);

            List<PinRecord> places = reg.Pins.Where(p => p.IsPlace).ToList();
            _out.WriteLine($"after: {reg.Pins.Count} pins ({places.Count} places, {reg.Pins.Count - places.Count} resources), " +
                           $"{places.Sum(p => p.Chests.Count)} chests, {reg.Export().Ignored.Count} ignored, {reg.Export().Gone.Count} gone; " +
                           $"map pins {mapBefore} -> {map.Pins.Count}");
            foreach (IGrouping<string, string> g in merged.GroupBy(m => m))
                _out.WriteLine($"merged: {g.Key} x{g.Count() + 1}");

            Assert.Equal(json, File.ReadAllText(path + ".v2.bak"));
            Assert.Equal(oldPins.Count - merged.Count, reg.Pins.Count);
            Assert.Equal(mapBefore - merged.Count, map.Pins.Count);
            Assert.All(reg.Pins, p => Assert.Contains(map.Pins, m => ReferenceEquals(m, p.Handle)));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
