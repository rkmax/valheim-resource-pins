using System;

namespace ResourcePins;

/// <summary>Minimal vector for the Unity-free pin logic (tests run without Unity).</summary>
[Serializable]
public struct V3
{
    public float X, Y, Z;

    public V3(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    /// <summary>Horizontal distance, the one the map uses (Utils.DistanceXZ).</summary>
    public static float DistanceXZ(V3 a, V3 b)
    {
        float dx = a.X - b.X, dz = a.Z - b.Z;
        return (float)Math.Sqrt(dx * dx + dz * dz);
    }
}
