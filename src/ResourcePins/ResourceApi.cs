using System;
using System.Collections.Generic;
using System.Linq;

namespace ResourcePins;

/// <summary>
///     Public API for other mods. Uses only BCL types so a caller can bind it by reflection without
///     referencing this assembly. Call it on the Unity main thread.
/// </summary>
public static class ResourceApi
{
    /// <summary>
    ///     The mod's pins within <paramref name="radius" /> metres (XZ) of <paramref name="x" />, <paramref name="z" />,
    ///     nearest first, as JSON: {"pins":[{"prefab","name","kind":"resource"|"place","x","z","count","readyIn","looted"}]}.
    ///     <paramref name="filter" />, if not empty, is a case-insensitive substring of the prefab or the shown name.
    ///     readyIn is seconds until a pin showing the X (all bushes picked) has a bush ready, else null; looted is for
    ///     places with known loot chests, else null. Returns {"error":"..."} when no world is loaded.
    /// </summary>
    public static string Query(float x, float z, float radius, string filter)
    {
        PinRegistry registry = Tracker.Registry;
        if (registry == null || Minimap.instance == null || ZNet.instance == null)
            return ResourceQuery.Error("not_in_game");
        if (float.IsNaN(x) || float.IsNaN(z) || float.IsNaN(radius))
            return ResourceQuery.Error("x, z and radius must be numbers");

        long now = ZNet.instance.GetTime().Ticks;
        IEnumerable<ResourceQuery.Entry> pins = registry.Pins.Select(p => new ResourceQuery.Entry
        {
            Prefab = p.Item,
            Name = MapFilter.ShownName(p),
            Place = p.IsPlace,
            X = p.Anchor.X,
            Z = p.Anchor.Z,
            Count = p.IsPlace ? 1 : p.Members.Count,
            ReadyIn = ResourceQuery.ReadyIn(p, now),
            Looted = ResourceQuery.Looted(p),
        });
        return ResourceQuery.Query(pins, x, z, radius, filter);
    }
}
