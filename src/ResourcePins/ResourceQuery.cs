using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace ResourcePins;

/// <summary>What <see cref="ResourceApi"/> answers, as plain logic.</summary>
public static class ResourceQuery
{
    /// <summary>API filter: empty matches all, else a case-insensitive substring of the prefab or the shown name.</summary>
    public static bool Matches(string prefab, string name, string filter) =>
        string.IsNullOrEmpty(filter) ||
        (prefab ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
        (name ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>
    /// Seconds until a regrowing pin's X goes away (its first bush is ready), null when it shows no X
    /// (some bush is ready, the time is unknown) or for a place.
    /// </summary>
    public static double? ReadyIn(PinRecord pin, long now)
    {
        if (pin.IsPlace || !PinRegistry.Regrowing(pin, now))
            return null;
        long readyAt = pin.Members.Where(m => m.Kind == MemberKind.Regrows).Min(m => m.ReadyAt);
        return Math.Max(0, readyAt - now) / (double)TimeSpan.TicksPerSecond;
    }

    /// <summary>Places only: true when every known loot chest is empty, null when none is known.</summary>
    public static bool? Looted(PinRecord pin) =>
        pin.IsPlace && pin.Chests.Count > 0 ? PinRegistry.Looted(pin) : null;

    /// <summary>One pin as <see cref="Query"/> reports it.</summary>
    public sealed class Entry
    {
        public string Prefab, Name;
        public bool Place;
        public float X, Z;
        public int Count;
        public double? ReadyIn;
        public bool? Looted;
    }

    /// <summary>The API answer: pins within <paramref name="radius"/> of (x, z) matching the filter, nearest first.</summary>
    public static string Query(IEnumerable<Entry> pins, float x, float z, float radius, string filter)
    {
        var found = pins
            .Select(p => (Pin: p, Dist: V3.DistanceXZ(new V3(p.X, 0, p.Z), new V3(x, 0, z))))
            .Where(t => t.Dist <= radius && Matches(t.Pin.Prefab, t.Pin.Name, filter))
            .OrderBy(t => t.Dist)
            .Select(t => new
            {
                prefab = t.Pin.Prefab, name = t.Pin.Name, kind = t.Pin.Place ? "place" : "resource",
                x = t.Pin.X, z = t.Pin.Z, count = t.Pin.Count, readyIn = t.Pin.ReadyIn, looted = t.Pin.Looted,
            });
        return JsonConvert.SerializeObject(new { pins = found });
    }

    public static string Error(string message) => JsonConvert.SerializeObject(new { error = message });
}
