using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using ResourcePins;
using Xunit;

public class ResourceQueryTests
{
    private static ResourceQuery.Entry Silver(float x, float z) =>
        new() { Prefab = "SilverOre", Name = "Silver ore", X = x, Z = z, Count = 3 };

    [Fact]
    public void Query_returns_pins_in_radius_nearest_first_matching_prefab_or_shown_name()
    {
        var far = new ResourceQuery.Entry { Prefab = "Raspberry", Name = "Raspberries", X = 90, Z = 0, Count = 2 };
        var farSilver = Silver(500, 0);

        JArray pins = (JArray)JObject.Parse(ResourceQuery.Query(new[] { far, Silver(10, 0), Silver(5, 0), farSilver }, 0, 0, 100, null))["pins"];
        Assert.Equal(new[] { 5f, 10f, 90f }, pins.Select(p => (float)p["x"]));

        JArray byName = (JArray)JObject.Parse(ResourceQuery.Query(new[] { far, Silver(5, 0) }, 0, 0, 100, "RASPB"))["pins"];
        Assert.Single(byName);
        Assert.Equal("Raspberry", (string)byName[0]["prefab"]);

        Assert.Single((JArray)JObject.Parse(ResourceQuery.Query(new[] { far, Silver(5, 0) }, 0, 0, 100, "silverore"))["pins"]);
    }

    [Fact]
    public void Pin_has_the_contract_fields_with_null_where_it_does_not_apply()
    {
        var place = new ResourceQuery.Entry { Prefab = "Crypt2", Name = "Burial Chambers", Place = true, X = 1, Z = 2, Count = 1, Looted = true };

        JObject pin = (JObject)JObject.Parse(ResourceQuery.Query(new[] { place, Silver(3, 0) }, 0, 0, 50, null))["pins"][0];
        Assert.Equal("place", (string)pin["kind"]);
        Assert.True((bool)pin["looted"]);
        Assert.Equal(JTokenType.Null, pin["readyIn"].Type);

        JObject resource = (JObject)JObject.Parse(ResourceQuery.Query(new[] { Silver(3, 0) }, 0, 0, 50, ""))["pins"][0];
        Assert.Equal("resource", (string)resource["kind"]);
        Assert.Equal(3, (int)resource["count"]);
        Assert.Equal(JTokenType.Null, resource["looted"].Type);
    }

    [Fact]
    public void Ready_in_counts_down_only_while_every_bush_is_picked()
    {
        long now = 1000 * TimeSpan.TicksPerSecond;
        PinRecord Bushes(params long[] readyAt)
        {
            var pin = new PinRecord { Item = "Raspberry" };
            foreach (long r in readyAt)
                pin.Members.Add(new Member { Kind = MemberKind.Regrows, ReadyAt = r });
            return pin;
        }

        Assert.Equal(60d, ResourceQuery.ReadyIn(Bushes(now + 60 * TimeSpan.TicksPerSecond, now + 90 * TimeSpan.TicksPerSecond), now));
        Assert.Null(ResourceQuery.ReadyIn(Bushes(now + 60 * TimeSpan.TicksPerSecond, 0), now));
        Assert.Null(ResourceQuery.Looted(new PinRecord { Item = "Crypt2", Category = "Dungeons" }));
    }
}
