using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ResourcePins;

/// <summary>
/// Shows one kind of the mod's pins on the large map and the minimap: a key cycles everything, places, then each pinned
/// item (Shift goes back), or a click on the large map's list picks one. Only the pin icons and names on screen are
/// hidden; the pins, the registry and the character file stay as they are, and pins the player placed are never touched.
/// </summary>
internal static class MapFilter
{
    // Minimap.Update calls UpdatePins only when m_pinUpdateRequired is set (pan, zoom, pins added or removed).
    private static readonly Action<Minimap> UpdatePinsNow =
        AccessTools.MethodDelegate<Action<Minimap>>(AccessTools.Method(typeof(Minimap), "UpdatePins"));

    private static string _current = "";
    private static bool _changed;

    /// <summary>A new map was loaded: start showing everything.</summary>
    internal static void Reset()
    {
        _current = "";
        FilterPanel.Reset();
    }

    /// <summary>The pin's name as the map shows it.</summary>
    internal static string ShownName(PinRecord pin)
    {
        string name = pin.Handle is Minimap.PinData p ? p.m_name : pin.Label;
        return Localization.instance != null && !string.IsNullOrEmpty(name) ? Localization.instance.Localize(name) : name ?? pin.Item;
    }

    /// <summary>The option's name as the panel and the legend show it.</summary>
    internal static string OptionName(PinRegistry registry, string option)
    {
        if (option.Length == 0)
            return "All";
        if (option == PinFilter.Places)
            return "Places";
        PinRecord pin = registry.Pins.FirstOrDefault(p => !p.IsPlace && p.Item == option);
        return pin != null ? ShownName(pin) : option;
    }

    /// <summary>Picks a filter (a click on the panel or a key) and applies it to the map at once.</summary>
    internal static void Select(string filter)
    {
        PinRegistry registry = Tracker.Registry;
        if (registry == null || Minimap.instance == null)
            return;
        _current = filter;
        _changed = true;
        UpdatePinsNow(Minimap.instance);
        FilterPanel.Show(registry, _current);
    }

    internal static void Tick()
    {
        PinRegistry registry = Tracker.Registry;
        if (registry == null || Minimap.instance == null || Minimap.instance.m_mode != Minimap.MapMode.Large ||
            Minimap.InTextInput() || Chat.instance != null && Chat.instance.HasFocus())
            return;
        KeyboardShortcut key = Plugin.FilterKey.Value;
        bool next = key.IsDown();
        // IsDown is false while any other modifier is held, so Shift + the key is checked here.
        bool back = !next && UnityInput.Current.GetKeyDown(key.MainKey) && key.Modifiers.All(UnityInput.Current.GetKey) &&
            (UnityInput.Current.GetKey(KeyCode.LeftShift) || UnityInput.Current.GetKey(KeyCode.RightShift));
        if (!next && !back)
            return;
        Select(PinFilter.Next(_current, PinFilter.Options(registry.Pins), back));
    }

    [HarmonyPatch(typeof(Minimap), "UpdatePins")]
    private static class UpdatePins
    {
        private static void Postfix(Minimap __instance)
        {
            PinRegistry registry = Tracker.Registry;
            if (registry == null)
                return;
            // Both maps are drawn by UpdatePins, which makes new icons when the map is opened or closed.
            if (__instance.m_mode == Minimap.MapMode.Large)
                FilterPanel.Refresh(registry, _current);
            if (_current.Length == 0 && !_changed)
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

    /// <summary>
    /// A name label UpdatePins just created is turned on a second later by the Minimap.DelayActivation coroutine,
    /// with no UpdatePins after it: keep a hidden pin's name off.
    /// </summary>
    [HarmonyPatch]
    private static class DelayActivation
    {
        private static MethodBase TargetMethod() =>
            AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(Minimap), "DelayActivation"));

        private static void Postfix(object __instance, bool __result)
        {
            PinRegistry registry = Tracker.Registry;
            if (__result || registry == null || _current.Length == 0)
                return;
            GameObject go = Traverse.Create(__instance).Field<GameObject>("go").Value;
            if (go == null || !go.activeSelf)
                return;
            foreach (PinRecord pin in registry.Pins)
                if (pin.Handle is Minimap.PinData data && data.m_NamePinData?.PinNameGameObject == go && PinFilter.Hidden(pin, _current))
                    go.SetActive(false);
        }
    }
}
