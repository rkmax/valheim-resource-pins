using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using HarmonyLib;

namespace ResourcePins;

/// <summary>
/// On a dedicated server, writes what the cartography tables have explored (no pins, no names) to
/// <c>&lt;world&gt;_explored.bin</c> next to the server store, for tools outside the game. The format is in <see cref="ExploredMask"/>.
/// </summary>
internal static class ServerExplored
{
    private const string TablePrefab = "piece_cartographytable";
    private static List<byte[]> _written;

    /// <summary>The world was written (autosave or shutdown): write the tables' explored map with it, if it changed.</summary>
    [HarmonyPatch(typeof(ZNet), "SaveWorld")]
    private static class SaveWithWorld
    {
        private static void Postfix()
        {
            if (!ServerStore.Active || ZDOMan.instance == null)
                return;
            string path = Path.Combine(Paths.BepInExRootPath, "ResourcePins", Tracker.FileName(ZNet.World.m_name) + "_explored.bin");
            try
            {
                Save(path);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not save {path}: {e.Message}");
            }
        }
    }

    private static void Save(string path)
    {
        var tables = new List<ZDO>();
        int index = 0;
        while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(TablePrefab, tables, ref index)) { }
        List<byte[]> data = tables.Select(t => t.GetByteArray(ZDOVars.s_data)).Where(d => d != null).ToList();
        if (_written != null && data.Count == _written.Count && data.Zip(_written, (a, b) => a.SequenceEqual(b)).All(same => same))
            return;

        byte[] bits = null;
        int size = 0;
        foreach (byte[] d in data)
        {
            byte[] table = ExploredMask.Read(d, out int tableSize);
            if (table == null || (bits != null && tableSize != size))
            {
                Plugin.Log.LogWarning($"Skipped a cartography table: its explored map is not a {(size > 0 ? size : "square")} texture.");
                continue;
            }
            if (bits == null)
                (bits, size) = (table, tableSize);
            else
                ExploredMask.Or(bits, table);
        }
        if (bits == null)
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string tmp = path + ".tmp";
        using (FileStream file = File.Create(tmp))
            ExploredMask.Write(file, size, ExploredMask.PixelSize, bits);
        if (File.Exists(path))
            File.Replace(tmp, path, null);
        else
            File.Move(tmp, path);
        _written = data;
    }
}
