using System.Collections.Generic;
using System.Linq;
using ResourcePins;
using Xunit;

public class PinFilterTests
{
    private static PinRecord Resource(string item) => new() { Item = item };
    private static PinRecord Place() => new() { Item = "Crypt2", Category = "Dungeons" };

    [Fact]
    public void Filter_shows_only_the_chosen_kind()
    {
        Assert.False(PinFilter.Hidden(Resource("SilverOre"), ""));
        Assert.False(PinFilter.Hidden(Place(), ""));
        Assert.False(PinFilter.Hidden(Resource("SilverOre"), "SilverOre"));
        Assert.True(PinFilter.Hidden(Resource("CopperOre"), "SilverOre"));
        Assert.True(PinFilter.Hidden(Place(), "SilverOre"));
        Assert.False(PinFilter.Hidden(Place(), PinFilter.Places));
        Assert.True(PinFilter.Hidden(Resource("SilverOre"), PinFilter.Places));
    }

    [Fact]
    public void Key_cycles_everything_places_then_each_item_and_back()
    {
        List<string> options = PinFilter.Options(new[] { Resource("TinOre"), Place(), Resource("CopperOre"), Resource("TinOre") });

        Assert.Equal(new[] { "", PinFilter.Places, "CopperOre", "TinOre" }, options);
        Assert.Equal(PinFilter.Places, PinFilter.Next("", options));
        Assert.Equal("", PinFilter.Next("TinOre", options));
    }

    [Fact]
    public void Shift_key_steps_back_and_wraps_from_everything_to_the_last_item()
    {
        List<string> options = PinFilter.Options(new[] { Resource("TinOre"), Place(), Resource("CopperOre") });

        Assert.Equal("TinOre", PinFilter.Next("", options, back: true));
        Assert.Equal(PinFilter.Places, PinFilter.Next("CopperOre", options, back: true));
        Assert.Equal("", PinFilter.Next(PinFilter.Places, options, back: true));
        Assert.Equal("", PinFilter.Next("SilverOre", options, back: true));
    }

    [Fact]
    public void Filter_on_an_item_that_is_gone_goes_back_to_everything()
    {
        List<string> options = PinFilter.Options(new[] { Resource("TinOre") });

        Assert.Equal("", PinFilter.Next("SilverOre", options));
        Assert.Equal(new[] { "", "TinOre" }, options.ToArray());
    }
}
