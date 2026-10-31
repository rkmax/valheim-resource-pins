using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace ResourcePins;

/// <summary>
/// Shows one kind of the mod's pins on the large map: a key cycles everything, places, then each pinned item
/// (Shift goes back). Only the pin icons and names on screen are hidden; the pins, the registry and the character file
/// stay as they are, and pins the player placed are never touched.
/// </summary>
internal static class MapFilter
{
    // Minimap.Update calls UpdatePins only when m_pinUpdateRequired is set (pan, zoom, pins added or removed).
    private static readonly Action<Minimap> UpdatePinsNow =
        AccessTools.MethodDelegate<Action<Minimap>>(AccessTools.Method(typeof(Minimap), "UpdatePins"));

    private static string _current = "";
    private static bool _changed;
    private static TextMeshProUGUI _label;

    /// <summary>A new map was loaded: start showing everything.</summary>
    internal static void Reset()
    {
        _current = "";
        if (_label != null)
            _label.gameObject.SetActive(false);
    }

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
            Minimap.InTextInput() || Chat.instance != null && Chat.instance.HasFocus())
            return;
        KeyboardShortcut key = Plugin.FilterKey.Value;
        bool next = key.IsDown();
        // IsDown is false while any other modifier is held, so Shift + the key is checked here.
        bool back = !next && UnityInput.Current.GetKeyDown(key.MainKey) && key.Modifiers.All(UnityInput.Current.GetKey) &&
            (UnityInput.Current.GetKey(KeyCode.LeftShift) || UnityInput.Current.GetKey(KeyCode.RightShift));
        if (!next && !back)
            return;
        List<string> options = PinFilter.Options(registry.Pins);
        _current = PinFilter.Next(_current, options, back);
        _changed = true;
        UpdatePinsNow(Minimap.instance);
        ShowLabel(registry, options, key);
    }

    /// <summary>The active filter, under the biome name of the large map, in its font; hidden while showing all pins.</summary>
    private static void ShowLabel(PinRegistry registry, List<string> options, KeyboardShortcut key)
    {
        if (_label == null)
        {
            TMP_Text biome = Minimap.instance.m_biomeNameLarge;
            var go = new GameObject("ResourcePinsFilter", typeof(RectTransform));
            go.transform.SetParent(biome.transform, false);
            _label = go.AddComponent<TextMeshProUGUI>();
            _label.font = biome.font;
            _label.fontSharedMaterial = biome.fontSharedMaterial;
            _label.color = biome.color;
            _label.fontSize = biome.fontSize * 0.6f;
            _label.alignment = TextAlignmentOptions.Top;
            _label.textWrappingMode = TextWrappingModes.NoWrap;
            _label.raycastTarget = false;
            RectTransform rect = _label.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(1000f, _label.fontSize * 1.5f);
        }
        _label.gameObject.SetActive(_current.Length > 0);
        if (_current.Length == 0)
            return;
        string text = _current == PinFilter.Places ? "Places"
            : ShownName(registry.Pins.First(p => !p.IsPlace && p.Item == _current));
        _label.text = $"Pins: {text} ({options.IndexOf(_current)} of {options.Count - 1}) - {key} next, Shift+{key} previous";
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
