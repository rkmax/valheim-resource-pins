using System;
using System.Globalization;
using System.IO;
using BepInEx;
using UnityEngine;

namespace ResourcePins;

/// <summary>
/// Adds the resource index and pin changes to BepInEx/ResourcePins/log.csv when Log is on. Use it to find
/// false positives and check removals and X changes. Columns: time (s since start), event, item, x, z,
/// members, detail.
/// </summary>
internal static class PinLog
{
    private static StreamWriter _out;
    private static bool _failed;

    internal static void Index(IndexEntry e) =>
        Write("index", e.Resource.Item, "", "", "", $"{e.Prefab} {e.Resource.Kind} respawn={e.Resource.RespawnMinutes}");

    internal static void Place(string location, string category, string hasInterior, float exterior, float interior) =>
        Write("place", location, "", "", "", $"{category} hasInterior={hasInterior} exterior={Num(exterior)} interior={Num(interior)}");

    internal static void Event(string evt, PinRecord pin, string detail) =>
        Write(evt, pin.Item, Num(pin.Anchor.X), Num(pin.Anchor.Z), pin.Members.Count.ToString(CultureInfo.InvariantCulture), detail);

    internal static void Sync(string evt, string detail) => Write("sync", evt, "", "", "", detail);

    private static void Write(string evt, string item, string x, string z, string members, string detail)
    {
        if (!Plugin.LogEvents.Value || !Open())
            return;
        _out.WriteLine(string.Join(",", Num(Time.time), evt, item, x, z, members, detail.Replace(',', ';')));
    }

    private static bool Open()
    {
        if (_out != null)
            return true;
        if (_failed)
            return false;
        try
        {
            string dir = Path.Combine(Paths.BepInExRootPath, "ResourcePins");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "log.csv");
            bool fresh = !File.Exists(path);
            _out = new StreamWriter(path, append: true) { AutoFlush = true };
            if (fresh)
                _out.WriteLine("time,event,item,x,z,members,detail");
            _out.WriteLine($"{Num(Time.time)},session,,,,,{DateTime.Now:s}");
            return true;
        }
        catch (Exception e)
        {
            _failed = true;
            Plugin.Log.LogWarning($"Pin log disabled: {e.Message}");
            return false;
        }
    }

    private static string Num(float v) => v.ToString("0.0", CultureInfo.InvariantCulture);
}
