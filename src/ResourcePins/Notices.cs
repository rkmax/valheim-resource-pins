using System.Collections.Generic;
using UnityEngine;

namespace ResourcePins;

/// <summary>
/// Shows pin changes in the game's top-left message feed, with the item icon, like item pickups do.
/// Changes of one scan are grouped per event and item ("Pin: Copper Ore x3"). The first scan after the map
/// loads stays quiet, so you only see changes made while you play. Place pins use their label
/// and have no item icon.
/// </summary>
internal static class Notices
{
    private static readonly Dictionary<(string Evt, string Item, bool Place), int> _pending = new();
    private static bool _muted;

    /// <summary>The map just loaded: drop whatever the first scan reports.</summary>
    internal static void Mute() => _muted = true;

    internal static void Event(string evt, PinRecord pin, string detail)
    {
        if (Text(evt, "", pin.IsPlace) == null)
            return;
        var key = (evt, pin.IsPlace ? pin.Label : pin.Item, pin.IsPlace);
        _pending.TryGetValue(key, out int n);
        _pending[key] = n + 1;
    }

    /// <summary>Called once per scan, after every change of that scan.</summary>
    internal static void Show()
    {
        if (_muted || MessageHud.instance == null)
        {
            _muted = false;
            _pending.Clear();
            return;
        }
        foreach (KeyValuePair<(string Evt, string Item, bool Place), int> p in _pending)
        {
            ItemDrop drop = !p.Key.Place && ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(p.Key.Item)?.GetComponent<ItemDrop>() : null;
            string name = drop != null ? drop.m_itemData.m_shared.m_name : p.Key.Item;
            Sprite icon = drop != null ? drop.m_itemData.GetIcon() : null;
            MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, Text(p.Key.Evt, name, p.Key.Place), p.Value > 1 ? p.Value : 0, icon);
        }
        _pending.Clear();
    }

    /// <summary>The message for an event, or null when that kind of notice is off.</summary>
    private static string Text(string evt, string name, bool place) => (evt, place) switch
    {
        ("add", true) when Plugin.NotifyPlaces.Value => $"Pin: {name}",
        ("check", true) when Plugin.NotifyPlaces.Value => $"{name}: looted",
        (_, true) => null,
        ("add", _) when Plugin.NotifyAdded.Value => $"Pin: {name}",
        ("remove", _) when Plugin.NotifyRemoved.Value => $"Pin removed: {name}",
        ("check", _) when Plugin.NotifyRegrow.Value => $"{name}: picked",
        ("uncheck", _) when Plugin.NotifyRegrow.Value => $"{name}: ready",
        _ => null,
    };
}
