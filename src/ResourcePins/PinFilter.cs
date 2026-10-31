using System;
using System.Collections.Generic;
using System.Linq;

namespace ResourcePins;

/// <summary>
/// Which of the mod's pins the large map shows (display only, nothing saved).
/// A filter is "" (everything), <see cref="Places"/> or an item prefab.
/// </summary>
public static class PinFilter
{
    public const string Places = "@places";

    public static bool Hidden(PinRecord pin, string filter)
    {
        if (string.IsNullOrEmpty(filter))
            return false;
        return filter == Places ? !pin.IsPlace : pin.IsPlace || pin.Item != filter;
    }

    /// <summary>The filters to cycle through: everything, places (if any), then each pinned item.</summary>
    public static List<string> Options(IEnumerable<PinRecord> pins)
    {
        var options = new List<string> { "" };
        List<PinRecord> all = pins.ToList();
        if (all.Any(p => p.IsPlace))
            options.Add(Places);
        options.AddRange(all.Where(p => !p.IsPlace).Select(p => p.Item).Distinct().OrderBy(i => i, StringComparer.Ordinal));
        return options;
    }

    /// <summary>The option after <paramref name="current"/>; back to everything after the last, or when it is gone.</summary>
    public static string Next(string current, IReadOnlyList<string> options)
    {
        int i = -1;
        for (int k = 0; k < options.Count; k++)
            if (options[k] == (current ?? ""))
                i = k;
        return i < 0 ? "" : options[(i + 1) % options.Count];
    }
}
