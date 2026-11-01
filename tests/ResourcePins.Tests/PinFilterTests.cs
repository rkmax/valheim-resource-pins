using System.Collections.Generic;
using System.Linq;
using ResourcePins;
using Xunit;

public class PinFilterTests
{
    private static PinRecord Resource(string item) => new() { Item = item };
    private static PinRecord Place(string category = "Dungeons") => new() { Item = "Crypt2", Category = category };
    private static PinRecord At(string item, string biome) => new() { Item = item, Label = biome };
    private static readonly Dictionary<string, string> NoGroups = new();
    private static Dictionary<string, string> Groups(params PinRecord[] pins) => PinFilter.ItemBiomes(pins, p => p.Label);

    [Fact]
    public void Filter_shows_only_the_chosen_kind()
    {
        Assert.False(PinFilter.Hidden(Resource("SilverOre"), "", NoGroups));
        Assert.False(PinFilter.Hidden(Place(), "", NoGroups));
        Assert.False(PinFilter.Hidden(Resource("SilverOre"), "SilverOre", NoGroups));
        Assert.True(PinFilter.Hidden(Resource("CopperOre"), "SilverOre", NoGroups));
        Assert.True(PinFilter.Hidden(Place(), "SilverOre", NoGroups));
        Assert.False(PinFilter.Hidden(Place(), PinFilter.Places, NoGroups));
        Assert.True(PinFilter.Hidden(Resource("SilverOre"), PinFilter.Places, NoGroups));
    }

    [Fact]
    public void An_item_goes_under_the_biome_with_most_of_its_pins_once()
    {
        var groups = Groups(At("Flint", "Meadows"), At("Flint", "Swamp"), At("Flint", "Swamp"), At("Tin", "Mountain"), At("Tin", "Plains"));

        Assert.Equal("Swamp", groups["Flint"]);
        Assert.Equal("Mountain", groups["Tin"]);
    }

    [Fact]
    public void Biome_filter_shows_only_that_groups_items_and_places_filters_show_places()
    {
        PinRecord[] pins = { At("Flint", "Meadows"), At("Tin", "Swamp"), Place("Camps"), Place() };
        var groups = Groups(pins);
        string swamp = PinFilter.Biome("Swamp");

        Assert.Equal(new[] { "Tin" }, pins.Where(p => !PinFilter.Hidden(p, swamp, groups)).Select(p => p.Item));
        Assert.Equal(new[] { "Camps" }, pins.Where(p => !PinFilter.Hidden(p, PinFilter.Category("Camps"), groups)).Select(p => p.Category));
        Assert.Equal(2, PinFilter.Count(pins, PinFilter.Places, groups));
        Assert.Equal(1, PinFilter.Count(pins, PinFilter.Biome("Meadows"), groups));
    }

    [Fact]
    public void A_biome_header_counts_the_pins_of_its_items()
    {
        PinRecord[] pins = { At("Flint", "Meadows"), At("Flint", "Meadows"), At("Amber", "Meadows"), At("TinOre", "Swamp"), Place() };

        Assert.Equal(3, PinFilter.Count(pins, PinFilter.Biome("Meadows"), Groups(pins)));
        Assert.Equal(1, PinFilter.Count(pins, PinFilter.Biome("Swamp"), Groups(pins)));
    }

    [Fact]
    public void Options_list_all_places_categories_then_biome_groups_in_progression_order()
    {
        PinRecord[] pins = { At("TinOre", "Swamp"), At("Flint", "Meadows"), At("Amber", "Meadows"), Place("Traders"), Place("Dungeons"), At("Pearl", "Ocean") };

        Assert.Equal(
            new[]
            {
                "", PinFilter.Places, PinFilter.Category("Dungeons"), PinFilter.Category("Traders"),
                PinFilter.Biome("Meadows"), "Amber", "Flint", PinFilter.Biome("Swamp"), "TinOre", PinFilter.Biome("Ocean"), "Pearl",
            },
            PinFilter.Options(pins, Groups(pins)));
    }

    [Fact]
    public void Collapsed_groups_hide_their_children_but_keep_the_header()
    {
        PinRecord[] pins = { At("TinOre", "Swamp"), At("Flint", "Meadows"), Place() };
        var collapsed = new HashSet<string> { PinFilter.Places, PinFilter.Biome("Meadows") };

        List<string> options = PinFilter.Options(pins, Groups(pins), collapsed);

        Assert.Equal(new[] { "", PinFilter.Places, PinFilter.Biome("Meadows"), PinFilter.Biome("Swamp"), "TinOre" }, options);
        Assert.Equal(PinFilter.Biome("Meadows"), PinFilter.Next(PinFilter.Places, options));
    }

    [Fact]
    public void Key_steps_through_the_rows_headers_included_and_wraps_both_ways()
    {
        PinRecord[] pins = { At("TinOre", "Swamp"), At("Flint", "Meadows") };
        List<string> options = PinFilter.Options(pins, Groups(pins));

        Assert.Equal(PinFilter.Biome("Meadows"), PinFilter.Next("", options));
        Assert.Equal("Flint", PinFilter.Next(PinFilter.Biome("Meadows"), options));
        Assert.Equal("", PinFilter.Next("TinOre", options));
        Assert.Equal("TinOre", PinFilter.Next("", options, back: true));
        Assert.Equal("", PinFilter.Next("SilverOre", options));
    }

    [Fact]
    public void Each_option_counts_the_pins_it_shows()
    {
        PinRecord[] pins = { At("TinOre", "Swamp"), Place(), At("Flint", "Meadows"), At("TinOre", "Swamp") };
        var groups = Groups(pins);

        Assert.Equal(new[] { 4, 1, 1, 1, 1, 2, 2 }, PinFilter.Options(pins, groups).Select(o => PinFilter.Count(pins, o, groups)).ToArray());
    }
}
