using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ResourcePins;

/// <summary>
/// The map filter on screen: a list of the options at the left of the large map (click one to pick it), and a legend
/// under the minimap that names the active one. The vanilla large map keeps the pin-type icons, the ping panel and the
/// biome name on the right, and the key hints and the share toggle along the bottom; the left edge is free.
/// </summary>
internal static class FilterPanel
{
    private const float Width = 260f, RowHeight = 34f, Pad = 4f, Top = 12f, Bottom = 80f, Margin = 10f;

    private sealed class Row
    {
        public RectTransform Rect;
        public Image Background, Icon;
        public TextMeshProUGUI Name, Count;
        public string Key = "";
    }

    private static readonly Color PanelColor = new(0f, 0f, 0f, 0.6f);
    private static readonly Color ActiveColor = new(1f, 0.75f, 0.25f, 0.4f);
    private static readonly Color IdleColor = new(1f, 1f, 1f, 0f);

    private static GameObject _panel;
    private static RectTransform _panelRect, _viewport, _content;
    private static readonly List<Row> Rows = new();
    private static int _rows, _signature;
    private static int _wheelFrame = -1;

    private static GameObject _legend;
    private static Image _legendIcon;
    private static TextMeshProUGUI _legendText;

    /// <summary>The panel belonged to a map that is gone: forget its rows.</summary>
    private static void Forget()
    {
        Rows.Clear();
        _rows = 0;
        _signature = 0;
    }

    /// <summary>A new map was loaded: nothing is filtered.</summary>
    internal static void Reset()
    {
        Mark("");
        if (_legend != null)
            _legend.SetActive(false);
    }

    /// <summary>Rebuilds the rows if the pins changed, then marks the active one. Called with the large map open.</summary>
    internal static void Refresh(PinRegistry registry, string current)
    {
        if (!Plugin.FilterPanel.Value)
        {
            if (_panel != null)
                _panel.SetActive(false);
            return;
        }
        if (_panel == null)
        {
            Forget();
            Create();
        }
        _panel.SetActive(true);
        // Order-independent and allocation-free: a pin added, removed or of another item changes it.
        int signature = registry.Pins.Count;
        for (int i = 0; i < registry.Pins.Count; i++)
        {
            PinRecord pin = registry.Pins[i];
            signature += pin.IsPlace ? 17 : pin.Item?.GetHashCode() ?? 0;
        }
        if (signature != _signature || Rows.Count == 0)
        {
            _signature = signature;
            Rebuild(registry);
            Mark(current);
        }
        Fit();
    }

    /// <summary>Updates what shows the active filter: the highlighted row and the legend.</summary>
    internal static void Show(PinRegistry registry, string current)
    {
        Mark(current);
        Reveal(current);
        UpdateLegend(registry, current);
    }

    private static void Create()
    {
        var root = new GameObject("ResourcePinsFilterPanel", typeof(RectTransform));
        // Last child of the large map so it is above the map image: the event system sends a click to the topmost
        // graphic under the pointer, so a click on the panel never reaches the map's UIInputHandler.
        root.transform.SetParent(Minimap.instance.m_largeRoot.transform, false);
        _panel = root;
        _panelRect = (RectTransform)root.transform;
        _panelRect.anchorMin = _panelRect.anchorMax = _panelRect.pivot = new Vector2(0f, 1f);
        _panelRect.anchoredPosition = new Vector2(Margin, -Top);
        _panelRect.sizeDelta = new Vector2(Width, RowHeight + 2 * Pad);
        root.AddComponent<Image>().color = PanelColor;

        var view = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        view.transform.SetParent(root.transform, false);
        _viewport = (RectTransform)view.transform;
        _viewport.anchorMin = Vector2.zero;
        _viewport.anchorMax = Vector2.one;
        _viewport.offsetMin = new Vector2(Pad, Pad);
        _viewport.offsetMax = new Vector2(-Pad, -Pad);

        var content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(view.transform, false);
        _content = (RectTransform)content.transform;
        _content.anchorMin = new Vector2(0f, 1f);
        _content.anchorMax = new Vector2(1f, 1f);
        _content.pivot = new Vector2(0.5f, 1f);
        _content.anchoredPosition = Vector2.zero;
    }

    private static void Rebuild(PinRegistry registry)
    {
        List<string> options = PinFilter.Options(registry.Pins);
        while (Rows.Count < options.Count)
            Rows.Add(NewRow());
        for (int i = 0; i < Rows.Count; i++)
        {
            Row row = Rows[i];
            row.Rect.gameObject.SetActive(i < options.Count);
            if (i >= options.Count)
                continue;
            string key = options[i];
            row.Key = key;
            row.Rect.anchoredPosition = new Vector2(0f, -i * RowHeight);
            row.Name.text = MapFilter.OptionName(registry, key);
            row.Count.text = PinFilter.Count(registry.Pins, key).ToString();
            row.Icon.sprite = Icon(key);
        }
        _rows = options.Count;
        _content.sizeDelta = new Vector2(0f, _rows * RowHeight);
    }

    private static Row NewRow()
    {
        TMP_Text font = Minimap.instance.m_biomeNameLarge;
        var go = new GameObject("Row", typeof(RectTransform));
        go.transform.SetParent(_content, false);
        var row = new Row { Rect = (RectTransform)go.transform, Background = go.AddComponent<Image>() };
        row.Rect.anchorMin = new Vector2(0f, 1f);
        row.Rect.anchorMax = new Vector2(1f, 1f);
        row.Rect.pivot = new Vector2(0.5f, 1f);
        row.Rect.sizeDelta = new Vector2(0f, RowHeight);
        Button button = go.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => MapFilter.Select(row.Key));

        var icon = new GameObject("Icon", typeof(RectTransform));
        icon.transform.SetParent(go.transform, false);
        row.Icon = icon.AddComponent<Image>();
        row.Icon.raycastTarget = false;
        row.Icon.preserveAspect = true;
        var iconRect = (RectTransform)icon.transform;
        iconRect.anchorMin = iconRect.anchorMax = iconRect.pivot = new Vector2(0f, 0.5f);
        iconRect.anchoredPosition = new Vector2(6f, 0f);
        iconRect.sizeDelta = new Vector2(RowHeight - 6f, RowHeight - 6f);

        row.Name = NewText("Name", go.transform, font, TextAlignmentOptions.Left);
        row.Name.overflowMode = TextOverflowModes.Ellipsis;
        row.Name.rectTransform.anchorMin = Vector2.zero;
        row.Name.rectTransform.anchorMax = Vector2.one;
        row.Name.rectTransform.offsetMin = new Vector2(RowHeight + 8f, 0f);
        row.Name.rectTransform.offsetMax = new Vector2(-48f, 0f);

        row.Count = NewText("Count", go.transform, font, TextAlignmentOptions.Right);
        row.Count.rectTransform.anchorMin = Vector2.zero;
        row.Count.rectTransform.anchorMax = Vector2.one;
        row.Count.rectTransform.offsetMin = new Vector2(RowHeight + 8f, 0f);
        row.Count.rectTransform.offsetMax = new Vector2(-8f, 0f);
        return row;
    }

    private static TextMeshProUGUI NewText(string name, Transform parent, TMP_Text font, TextAlignmentOptions alignment)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
        text.font = font.font;
        text.fontSharedMaterial = font.fontSharedMaterial;
        text.color = font.color;
        text.fontSize = 18f;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        return text;
    }

    private static void Mark(string current)
    {
        if (_panel == null)
            return;
        for (int i = 0; i < _rows; i++)
            Rows[i].Background.color = Rows[i].Key == current ? ActiveColor : IdleColor;
    }

    /// <summary>Scrolls the active row into view when F / Shift+F move it past the visible part.</summary>
    private static void Reveal(string current)
    {
        if (_panel == null || !_panel.activeSelf)
            return;
        for (int i = 0; i < _rows; i++)
        {
            if (Rows[i].Key != current)
                continue;
            float top = i * RowHeight, y = _content.anchoredPosition.y, height = _viewport.rect.height;
            if (top < y)
                Scroll(top);
            else if (top + RowHeight > y + height)
                Scroll(top + RowHeight - height);
        }
    }

    /// <summary>Panel height: its rows, or all the room the vanilla bottom hints leave.</summary>
    private static void Fit()
    {
        float room = ((RectTransform)Minimap.instance.m_largeRoot.transform).rect.height - Top - Bottom;
        float height = Mathf.Max(RowHeight + 2 * Pad, Mathf.Min(_rows * RowHeight + 2 * Pad, room));
        if (!Mathf.Approximately(_panelRect.sizeDelta.y, height))
            _panelRect.sizeDelta = new Vector2(Width, height);
        Scroll(_content.anchoredPosition.y);
    }

    private static void Scroll(float y)
    {
        float max = Mathf.Max(0f, _rows * RowHeight - (_panelRect.sizeDelta.y - 2 * Pad));
        _content.anchoredPosition = new Vector2(0f, Mathf.Clamp(y, 0f, max));
    }

    private static Sprite Icon(string key)
    {
        if (key.Length == 0)
            return PinSprite(Plugin.PinIcon.Value);
        if (key == PinFilter.Places)
            return PinSprite(Plugin.PlaceIcon.Value);
        Sprite icon = ObjectDB.instance != null && ObjectDB.instance.TryGetItemPrefab(key, out GameObject prefab)
            ? prefab.GetComponent<ItemDrop>()?.m_itemData.GetIcon() : null;
        return icon != null ? icon : PinSprite(Plugin.PinIcon.Value);
    }

    private static Sprite PinSprite(Minimap.PinType type)
    {
        foreach (Minimap.SpriteData data in Minimap.instance.m_icons)
            if (data.m_name == type)
                return data.m_icon;
        return null;
    }

    private static void UpdateLegend(PinRegistry registry, string current)
    {
        if (_legend == null && current.Length > 0)
            CreateLegend();
        if (_legend == null)
            return;
        _legend.SetActive(current.Length > 0);
        if (current.Length == 0)
            return;
        _legendIcon.sprite = Icon(current);
        _legendText.text = "Pins: " + MapFilter.OptionName(registry, current);
    }

    private static void CreateLegend()
    {
        TMP_Text font = Minimap.instance.m_biomeNameSmall;
        _legend = new GameObject("ResourcePinsLegend", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        // Right-aligned just under the minimap frame; the vanilla elements below it start further down.
        _legend.transform.SetParent(Minimap.instance.m_smallRoot.transform, false);
        var rect = (RectTransform)_legend.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(0f, -4f);
        rect.sizeDelta = new Vector2(320f, 24f);
        HorizontalLayoutGroup layout = _legend.GetComponent<HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleRight;
        layout.spacing = 6f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;

        var icon = new GameObject("Icon", typeof(RectTransform), typeof(LayoutElement));
        icon.transform.SetParent(_legend.transform, false);
        _legendIcon = icon.AddComponent<Image>();
        _legendIcon.raycastTarget = false;
        _legendIcon.preserveAspect = true;
        LayoutElement size = icon.GetComponent<LayoutElement>();
        size.preferredWidth = size.preferredHeight = 20f;

        _legendText = NewText("Text", _legend.transform, font, TextAlignmentOptions.Right);
        _legendText.fontSize = 16f;
    }

    /// <summary>
    /// The wheel over the panel scrolls the list and not the map: Minimap.UpdateMap zooms with
    /// ZInput.GetMouseScrollWheel, so the panel takes the value there. Returns true when it took it.
    /// </summary>
    internal static bool TakeWheel(float delta)
    {
        if (_panel == null || !_panel.activeInHierarchy ||
            !RectTransformUtility.RectangleContainsScreenPoint(_panelRect, ZInput.pointerPosition, null))
            return false;
        // Several readers can ask in one frame; scroll once.
        if (_wheelFrame != Time.frameCount)
        {
            _wheelFrame = Time.frameCount;
            Scroll(_content.anchoredPosition.y - Mathf.Sign(delta) * RowHeight);
        }
        return true;
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetMouseScrollWheel))]
    private static class MouseWheel
    {
        private static void Postfix(ref float __result)
        {
            if (__result != 0f && TakeWheel(__result))
                __result = 0f;
        }
    }
}
