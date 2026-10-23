using System.Collections.Generic;
using ResourcePins;
using Xunit;

public class ClassifierTests
{
    private static readonly Classifier Rules = new(new[] { "CopperOre", "BlackMarble", "Raspberry", "Mushroom" },
        "dvergr*,prop_*,*trashpile*");

    [Fact]
    public void Node_dropping_a_configured_item_is_indexed()
    {
        Resource r = Rules.Classify(new PrefabInfo { Name = "rock4_copper", MineDrops = new() { "Stone", "CopperOre" } });

        Assert.Equal("CopperOre", r.Item);
        Assert.Equal(MemberKind.Mined, r.Kind);
    }

    [Fact]
    public void Destructible_takes_its_item_from_the_fracture()
    {
        Resource r = Rules.Classify(new PrefabInfo { Name = "giant_skull", FractureDrops = new() { "BlackMarble" } });

        Assert.Equal("BlackMarble", r.Item);
    }

    [Fact]
    public void Drop_on_destroyed_with_few_items_is_a_node_but_a_loot_table_is_not()
    {
        Assert.NotNull(Rules.Classify(new PrefabInfo { Name = "blackmarble_post01", DestroyDrops = new() { "BlackMarble", "Stone" } }));
        Assert.Null(Rules.Classify(new PrefabInfo
        {
            Name = "crate", DestroyDrops = new() { "BlackMarble", "Coins", "Amber" },
        }));
    }

    [Fact]
    public void Blocklisted_names_creatures_and_stone_only_tables_are_not_indexed()
    {
        Assert.Null(Rules.Classify(new PrefabInfo { Name = "dvergrprops_crate", DestroyDrops = new() { "BlackMarble" } }));
        Assert.Null(Rules.Classify(new PrefabInfo { Name = "Elka_TrashPile", MineDrops = new() { "CopperOre" } }));
        Assert.Null(Rules.Classify(new PrefabInfo { Name = "BlobMorkBig", HasCharacter = true, DestroyDrops = new() { "CopperOre" } }));
        Assert.Null(Rules.Classify(new PrefabInfo { Name = "rock1", MineDrops = new() { "Stone" } }));
    }

    [Fact]
    public void Minerock_that_keeps_its_zdo_when_mined_is_skipped()
    {
        Assert.Null(Rules.Classify(new PrefabInfo { Name = "old_rock", MineDrops = new() { "CopperOre" }, KeepsZdoWhenMined = true }));
    }

    [Fact]
    public void Pickables_regrow_only_with_a_respawn_time()
    {
        Assert.Equal(MemberKind.Regrows, Rules.Classify(new PrefabInfo { Name = "RaspberryBush", PickItem = "Raspberry", RespawnMinutes = 300 }).Kind);
        Assert.Equal(MemberKind.Oneshot, Rules.Classify(new PrefabInfo { Name = "Pickable_Mushroom", PickItem = "Mushroom" }).Kind);
    }

    [Fact]
    public void Unknown_config_names_are_set_aside()
    {
        var known = new HashSet<string> { "CopperOre", "TinOre" };

        List<string> items = Classifier.ParseItems(" CopperOre, copperore,TinOre,,", known.Contains, out List<string> unknown);

        Assert.Equal(new[] { "CopperOre", "TinOre" }, items);
        Assert.Equal(new[] { "copperore" }, unknown);
    }

    private static readonly SapRoot Root = new() { Name = "$item_ancientroot", MaxLevel = 50, RegenPerSec = 0.0025f, EmptyThreshold = 10 };

    [Fact]
    public void Ancient_root_is_indexed_only_when_sap_is_configured()
    {
        var withSap = new Classifier(new[] { "Sap" }, "");

        Resource r = withSap.Classify(new PrefabInfo { Name = "YggdrasilRoot", Root = Root });

        Assert.Equal(MemberKind.Regrows, r.Kind);
        Assert.Same(Root, r.Root);
        Assert.Null(Rules.Classify(new PrefabInfo { Name = "YggdrasilRoot", Root = Root }));
    }

    [Fact]
    public void Drained_root_is_ready_once_regen_lifts_it_past_empty()
    {
        long t0 = 1000 * System.TimeSpan.TicksPerSecond;
        long readyAt = Root.ReadyAt(level: 2, lastTime: t0, now: t0);

        Assert.InRange(readyAt, t0 + 3199 * System.TimeSpan.TicksPerSecond, t0 + 3201 * System.TimeSpan.TicksPerSecond);
        Assert.Equal(0, Root.ReadyAt(level: 2, lastTime: t0, now: readyAt));
        Assert.Equal(0, Root.ReadyAt(level: 40, lastTime: t0, now: t0));
    }
}
