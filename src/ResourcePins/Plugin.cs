using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace ResourcePins;

[BepInPlugin(Guid, Name, Version)]
public class Plugin : BaseUnityPlugin
{
    public const string Guid = "rkmax.resourcepins";
    public const string Name = "ResourcePins";
    public const string Version = "0.2.0";

    internal static ManualLogSource Log;

    internal static ConfigEntry<bool> Enabled;
    internal static ConfigEntry<string> Items;
    internal static ConfigEntry<string> Blocklist;
    internal static ConfigEntry<float> ScanRadius;
    internal static ConfigEntry<float> ScanInterval;
    internal static ConfigEntry<float> MergeRadius;
    internal static ConfigEntry<Minimap.PinType> PinIcon;
    internal static ConfigEntry<Minimap.PinType> PlaceIcon;
    internal static ConfigEntry<string> Dungeons;
    internal static ConfigEntry<string> Camps;
    internal static ConfigEntry<string> Bosses;
    internal static ConfigEntry<string> Traders;
    internal static ConfigEntry<string> LootStands;
    internal static ConfigEntry<bool> NotifyAdded;
    internal static ConfigEntry<bool> NotifyRemoved;
    internal static ConfigEntry<bool> NotifyRegrow;
    internal static ConfigEntry<bool> NotifyPlaces;
    internal static ConfigEntry<bool> LogEvents;
    internal static ConfigEntry<bool> Sync;

    private void Awake()
    {
        Log = Logger;
        Enabled = Config.Bind("General", "Enabled", true, "Pins resource nodes around you on the map.");
        Items = Config.Bind("General", "Items",
            "CopperOre,TinOre,IronScrap,SilverOre,Obsidian,BlackMarble,Softtissue,FlametalOreNew,Raspberry,Blueberries," +
            "Cloudberry,Mushroom,MushroomYellow,DragonEgg,Sap",
            "Item prefab names (case-sensitive) whose nodes get a pin. Unknown names are logged and ignored.");
        Blocklist = Config.Bind("General", "Blocklist", "dvergr*,prop_*,*trashpile*,barrell*,morgenhole*",
            "Prefab names that never get a pin, comma-separated; * matches anything, case-insensitive.");

        ScanRadius = Config.Bind("Scan", "ScanRadius", 40f,
            new ConfigDescription("Nodes within this many metres of you get pinned; pins this close are checked for removal.",
                new AcceptableValueRange<float>(10f, 120f)));
        ScanInterval = Config.Bind("Scan", "ScanInterval", 2f,
            new ConfigDescription("Seconds between scans.", new AcceptableValueRange<float>(0.5f, 30f)));
        MergeRadius = Config.Bind("Pins", "MergeRadius", 12f,
            new ConfigDescription("A node of the same item this close to an existing pin joins it instead of adding one.",
                new AcceptableValueRange<float>(0f, 50f)));
        PinIcon = Config.Bind("Pins", "PinIcon", Minimap.PinType.Icon3, "Map icon of the mod's pins.");
        PlaceIcon = Config.Bind("Pins", "PlaceIcon", Minimap.PinType.Icon1,
            "Map icon of place pins: Icon0, Icon1, Icon2 or Icon4, different from PinIcon (otherwise Icon1, or Icon0 if PinIcon is Icon1).");

        const string places = "Location prefab names (case-sensitive, comma-separated) that get a place pin. Unknown names are logged and ignored.";
        Dungeons = Config.Bind("Places", "Dungeons",
            "Crypt2,Crypt3,Crypt4,SunkenCrypt4,HalfBurried_ForestCrypt,TrollCave02,MountainCave02,BearCave," +
            "Mistlands_DvergrTownEntrance1,Mistlands_DvergrTownEntrance2,Mistlands_DvergrBossEntrance1,MorgenHole1,MorgenHole2," +
            "MorgenHole3,TheHole01,TheDarkestHole,FireHole,Hildir_crypt,Hildir_cave,Hildir_plainsfortress,CharredFortress",
            places + " Their loot chests are the ones in the interior.");
        Camps = Config.Bind("Places", "Camps",
            "GoblinCamp2,GoblinCamp2_1,Greydwarf_camp1,LumberCamp,Mistlands_Harbour1,Mistlands_Excavation1,Mistlands_Excavation2," +
            "Mistlands_Excavation3,Mistlands_Lighthouse1_new",
            places + " Their loot chests are the ones within the location's radius.");
        Bosses = Config.Bind("Places", "Bosses", "Eikthyrnir,GDKing,Bonemass,Dragonqueen,GoblinKing,FaderLocation", places);
        Traders = Config.Bind("Places", "Traders", "Vendor_BlackForest,Hildir_camp,BogWitch_Camp", places);
        LootStands = Config.Bind("Places", "LootStands", "Pickable_BlackCoreStand",
            "Pickable prefab names (case-sensitive, comma-separated) that count as a place's loot like its chests: " +
            "the place gets the X once they are picked too. Unknown names are logged and ignored.");

        NotifyAdded = Config.Bind("Notices", "Added", true, "Shows a top-left message when a resource gets a pin.");
        NotifyRemoved = Config.Bind("Notices", "Removed", true, "Shows a top-left message when a pin goes away because its resource is gone.");
        NotifyRegrow = Config.Bind("Notices", "Regrow", false,
            "Shows a top-left message when a regrowing resource is picked (its pin gets the X) or ready again.");
        NotifyPlaces = Config.Bind("Notices", "Places", true,
            "Shows a top-left message when a place gets a pin and when all its loot is taken (its pin gets the X).");

        Sync = Config.Bind("Sync", "Enabled", true,
            "Shares the mod's pins live with other players who have the mod: new pins, pins whose resource is gone and the X. " +
            "Pins you delete by hand stay deleted only for you. Works on a vanilla server. If the dedicated server has the mod, " +
            "it also keeps every player's pins for whoever joins later.");

        LogEvents = Config.Bind("Debug", "Log", false,
            "Writes the resource index and every pin add, merge, removal and X change to BepInEx/ResourcePins/log.csv.");

        Items.SettingChanged += (_, _) => ResourceIndex.Reset();
        Blocklist.SettingChanged += (_, _) => ResourceIndex.Reset();
        Dungeons.SettingChanged += (_, _) => PlaceIndex.Reset();
        Camps.SettingChanged += (_, _) => PlaceIndex.Reset();
        Bosses.SettingChanged += (_, _) => PlaceIndex.Reset();
        Traders.SettingChanged += (_, _) => PlaceIndex.Reset();
        LootStands.SettingChanged += (_, _) => PlaceIndex.Reset();
        PlaceIcon.SettingChanged += (_, _) => PlaceIndex.Reset();
        PinIcon.SettingChanged += (_, _) => PlaceIndex.Reset();

        new Harmony(Guid).PatchAll();
    }

    private void Update()
    {
        Tracker.Tick();
        ServerStore.Tick();
        PinSync.Flush();
    }
}
