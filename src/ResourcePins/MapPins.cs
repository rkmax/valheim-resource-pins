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
}
