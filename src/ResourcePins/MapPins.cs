using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace ResourcePins;

/// <summary>The registry's view of Minimap, plus hooks for hand deletions and map loads.</summary>
internal sealed class MapPins : IPinMap
{
    internal static readonly AccessTools.FieldRef<Minimap, List<Minimap.PinData>> All =
        AccessTools.FieldRefAccess<Minimap, List<Minimap.PinData>>("m_pins");

    /// <summary>True while the mod removes its own pin, so the RemovePin hook does not think the player did it.</summary>
    private static bool _removingOwn;

    public object Add(V3 pos, int type, string name) =>
        Minimap.instance.AddPin(ToVector(pos), (Minimap.PinType)type, name, save: true, isChecked: false, ownerID: 0L);

    public void Remove(object pin)
    {
        _removingOwn = true;
        try
        {
            Minimap.instance.RemovePin((Minimap.PinData)pin);
        }
        finally
        {
            _removingOwn = false;
        }
    }

    public void SetChecked(object pin, bool value) => ((Minimap.PinData)pin).m_checked = value;

    public void Rename(object pin, string name)
    {
        var data = (Minimap.PinData)pin;
        data.m_name = name;
        // The large map's name text is built once from m_name (PinNameData.SetTextAndGameObject).
        if (data.m_NamePinData?.PinNameText != null)
            data.m_NamePinData.PinNameText.text = Localization.instance.Localize(name);
    }

    public bool HasPinNear(V3 pos, int type)
    {
        Vector3 p = ToVector(pos);
        foreach (Minimap.PinData pin in All(Minimap.instance))
            if ((int)pin.m_type == type && Utils.DistanceXZ(p, pin.m_pos) < PinRegistry.SimilarPinDistance)
                return true;
        return false;
    }

    public object FindOwn(V3 pos, int type, string name)
    {
        Vector3 p = ToVector(pos);
        return All(Minimap.instance).FirstOrDefault(pin => pin.m_ownerID == 0L && (int)pin.m_type == type &&
            pin.m_name == name && Utils.DistanceXZ(p, pin.m_pos) < PinRegistry.SimilarPinDistance);
    }

    public IEnumerable<object> PinsNear(V3 pos, float distance)
    {
        Vector3 p = ToVector(pos);
        return All(Minimap.instance).Where(pin => Utils.DistanceXZ(p, pin.m_pos) < distance).ToList();
    }

    internal static V3 ToV3(Vector3 v) => new(v.x, v.y, v.z);
    private static Vector3 ToVector(V3 v) => new(v.X, v.Y, v.Z);

    /// <summary>Every deletion path (right click, ping cleanup, shared map sync) ends here.</summary>
    [HarmonyPatch(typeof(Minimap), nameof(Minimap.RemovePin), typeof(Minimap.PinData))]
    private static class RemovePin
    {
        private static void Prefix(Minimap.PinData pin)
        {
            if (!_removingOwn)
                Tracker.OnRemovedByHand(pin);
        }
    }

    /// <summary>The character's saved pins are in place: bind the registry to them.</summary>
    [HarmonyPatch(typeof(Minimap), "LoadMapData")]
    private static class LoadMapData
    {
        private static void Postfix(Minimap __instance) => Tracker.OnMapLoaded(__instance);
    }

    /// <summary>
    /// A cartography table must not receive the mod's pins (players with the mod get them through PinSync).
    /// GetSharedMapData counts and writes whatever m_pins holds, so it runs against a copy without them.
    /// </summary>
    [HarmonyPatch(typeof(Minimap), nameof(Minimap.GetSharedMapData))]
    private static class GetSharedMapData
    {
        /// <summary>The real m_pins list while the call runs on the filtered copy; the list itself is never changed.</summary>
        private static List<Minimap.PinData> _real;

        private static void Prefix(Minimap __instance)
        {
            _real = null;
            PinRegistry registry = Tracker.Registry;
            if (registry == null)
                return;
            var own = new HashSet<object>(registry.Pins.Select(p => p.Handle).Where(h => h != null));
            ref List<Minimap.PinData> pins = ref All(__instance);
            _real = pins;
            pins = _real.Where(p => !own.Contains(p)).ToList();
        }

        /// <summary>Runs on exceptions too, so m_pins is always the real list again.</summary>
        private static void Finalizer(Minimap __instance)
        {
            if (_real == null)
                return;
            All(__instance) = _real;
            _real = null;
        }
    }
}
