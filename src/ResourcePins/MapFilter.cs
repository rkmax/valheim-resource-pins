using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace ResourcePins;

/// <summary>
/// Shows one kind of the mod's pins on the large map: a key cycles everything, places, then each pinned item.
/// Only the pin icons and names on screen are hidden; the pins, the registry and the character file stay as they are,
/// and pins the player placed are never touched.
/// </summary>
internal static class MapFilter
{
    private static string _current = "";
    private static bool _changed;

    /// <summary>A new map was loaded: start showing everything.</summary>
    internal static void Reset() => _current = "";

    /// <summary>The pin's name as the map shows it.</summary>
    internal static string ShownName(PinRecord pin)
    {
        string name = pin.Handle is Minimap.PinData p ? p.m_name : pin.Label;
        return Localization.instance != null && !string.IsNullOrEmpty(name) ? Localization.instance.Localize(name) : name ?? pin.Item;
    }

    internal static void Tick()
    {
        PinRegistry registry = Tracker.Registry;
        if (registry == null || Minimap.instance == null || Minimap.instance.m_mode != Minimap.MapMode.Large ||
            Minimap.InTextInput() || Chat.instance != null && Chat.instance.HasFocus() || !Plugin.FilterKey.Value.IsDown())
            return;
        _current = PinFilter.Next(_current, PinFilter.Options(registry.Pins));
        _changed = true;
        string text = _current.Length == 0 ? "all pins"
            : _current == PinFilter.Places ? "places"
            : ShownName(registry.Pins.First(p => !p.IsPlace && p.Item == _current));
        if (MessageHud.instance != null)
            MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, "Map pins: " + text);
    }

    [HarmonyPatch(typeof(Minimap), "UpdatePins")]
    private static class UpdatePins
    {
        private static void Postfix(Minimap __instance)
        {
            PinRegistry registry = Tracker.Registry;
            if (registry == null || __instance.m_mode != Minimap.MapMode.Large || _current.Length == 0 && !_changed)
                return;
            _changed = false;
            foreach (PinRecord pin in registry.Pins)
            {
                if (pin.Handle is not Minimap.PinData data)
                    continue;
                bool hidden = PinFilter.Hidden(pin, _current);
                if (data.m_uiElement != null && data.m_uiElement.gameObject.activeSelf == hidden)
                    data.m_uiElement.gameObject.SetActive(!hidden);
                // UpdatePins turns the name on by itself; only the hidden ones are kept off.
                if (hidden && data.m_NamePinData?.PinNameGameObject != null)
                    data.m_NamePinData.PinNameGameObject.SetActive(false);
            }
        }
    }
}
