# ResourcePins

Valheim mod that adds pins on your map for resources while you explore: ore, Black Marble, giant remains, berries, mushrooms and dragon eggs. It also pins places like dungeons, camps, villages, boss altars and traders, and puts the map's **X** on a dungeon or camp once the loot is taken. It never touch the pins you add yourself.

**You don't need to install anything on the server.** Each player install the mod in their game and that's it. Installing it on the server is optional, only to share pins between players that are not online at the same time (see [Sharing](#sharing)).

## Install

You need [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/). If you already play with mods from r2modman or Thunderstore Mod Manager you have it.

Download `ResourcePins-<version>.zip` from [Releases](../../releases), then:

- **r2modman / Thunderstore Mod Manager:** Settings > Import local mod, pick the zip.
- **Manual:** copy `plugins/ResourcePins.dll` from the zip into `BepInEx/plugins` of your Valheim folder.

## What it does

- Every 2 s it checks the nodes 40 m around you and adds a pin named after the item (Copper ore, Raspberries, etc).
- Nodes of the same item within 12 m of a pin join that pin instead of adding another one. The distance is always to the first node of the pin, so a long vein don't turn into one huge pin.
- When you come back near a pin and all its nodes are gone (mined out, picked for good), the pin is removed.
- A berry pin gets the **X** while all the bushes are picked and regrowing, and the X goes away when they are ready again, even if you are far. If you toggle the X yourself, the mod leaves it alone until the next pick or regrow.
- If you delete one of its pins, the mod remembers those nodes and never pins them again.
- New and removed pins show in the game's message feed (top left) with the item's icon, grouped per scan ("Pin: Copper ore x3"). Pins you already had when you log in are not announced.

### Places

- A place is pinned once you are in its zone or the next one (zones are 64 m), with its own icon (`PlaceIcon`). The pin has the name the game shows when you enter (Burial Chambers, Troll Cave, etc), or the category when the game has no name for it (Camp, Boss altar, Trader).
- A dungeon or camp gets the **X** when every loot chest the game filled is empty and every loot stand (`LootStands`, by default the Black Core stands in infested mines) is picked. For a dungeon the chests are the ones inside; for a camp the ones in the location's area. The X can show up without going in, as long as you are close enough for the game to send you the chests. Toggling the X by hand works like for berries.
- Place pins stay on the map, looting a place never removes it. If you delete one it never comes back.
- No place pin is added when there is another pin not made by the mod within 5 m (your own marker, a Vegvisir boss pin, a teammate's pin).
- "Pin: Burial Chambers" when a place gets pinned and "Burial Chambers: looted" when it gets the X, in the same message feed.

If something that is not a resource gets a pin, add its name to `Blocklist`. With `Log = true` every pin change goes to `BepInEx/ResourcePins/log.csv`.

## Configuration

File: `BepInEx/config/rkmax.resourcepins.cfg`. Default value in parentheses.

### General

- `Enabled` (true): adds and updates pins.
- `Items`: item prefab names to pin (case-sensitive). Unknown names go to the log and are ignored. `Sap` pins the ancient roots for the Sap Extractor. An old config file keeps its old list, add new names by hand.
- `Blocklist` (`dvergr*,prop_*,*trashpile*,barrell*,morgenhole*`): prefab names that never get a pin, `*` matches anything.

### Scan

- `ScanRadius` (40): meters around you that are scanned and checked for removals.
- `ScanInterval` (2): seconds between scans.

### Pins

- `MergeRadius` (12): meters where a node joins an existing pin.
- `PinIcon` (Icon3): map icon for resource pins.
- `PlaceIcon` (Icon1): map icon for place pins. Icon0, Icon1, Icon2 or Icon4, and not the same as `PinIcon`. Otherwise it uses Icon1 (Icon0 if `PinIcon` is Icon1) and logs a warning.

### Places

Location prefab names (exact, case-sensitive) for each kind of place.

- `Dungeons`: loot chests are the ones inside, or the ones in the location's area for the ones without interior (CharredFortress, FireHole, HalfBurried_ForestCrypt, Hildir_plainsfortress).
- `Camps`: loot chests are the ones in the location's area.
- `Bosses`: boss altars, no X.
- `Traders`: traders, no X.
- `LootStands` (`Pickable_BlackCoreStand`): pickable prefab names that count as loot of a place like its chests, searched where the chests are. The place gets the X once they are picked too. Infested mines have no chests, so without this their pin never gets the X.

### Notices

Messages in the top-left feed.

- `Added` (true): with the item's icon when a resource gets a pin.
- `Removed` (true): when a pin is removed because the resource is gone.
- `Regrow` (false): when a regrowing resource is picked or ready again.
- `Places` (true): when a place gets a pin and when it's looted.

### Sync

- `Enabled` (true): shares the mod's pins live with other players that have the mod. See [Sharing](#sharing).

### Debug

- `Log` (false): writes the index and pin changes to the log.

<details>
<summary>Default lists</summary>

- `Items`: `CopperOre, TinOre, IronScrap, SilverOre, Obsidian, BlackMarble, Softtissue, FlametalOreNew, Raspberry, Blueberries, Cloudberry, Mushroom, MushroomYellow, DragonEgg, Sap`
- `Dungeons`: `Crypt2, Crypt3, Crypt4, SunkenCrypt4, HalfBurried_ForestCrypt, TrollCave02, MountainCave02, BearCave, Mistlands_DvergrTownEntrance1, Mistlands_DvergrTownEntrance2, Mistlands_DvergrBossEntrance1, MorgenHole1, MorgenHole2, MorgenHole3, TheHole01, TheDarkestHole, FireHole, Hildir_crypt, Hildir_cave, Hildir_plainsfortress, CharredFortress`
- `Camps`: `GoblinCamp2, GoblinCamp2_1, Greydwarf_camp1, LumberCamp, Mistlands_Harbour1, Mistlands_Excavation1, Mistlands_Excavation2, Mistlands_Excavation3, Mistlands_Lighthouse1_new`
- `Bosses`: `Eikthyrnir, GDKing, Bonemass, Dragonqueen, GoblinKing, FaderLocation`
- `Traders`: `Vendor_BlackForest, Hildir_camp, BogWitch_Camp`

</details>

## Limits

- It only sees what your game has loaded, so a node gets a pin once you have been near it.
- Pins are saved in your character file like any other pin. The mod keeps its own list per world and character in `BepInEx/ResourcePins/<world>_<character>.json`.
- A dungeon's chests only exist after some player was close enough for the game to build the interior. Before that the mod don't know any chest and the pin has no X.
- If the game crashes before saving, pins added since the last save are lost. They come back when you pass by again.

## Sharing

Players that have the mod share its pins live: a pin someone adds shows on your map, a pin someone mines out goes away, and the X follows picked berries and looted places. This works on any server, nothing to install there: the server just pass the messages, and players without the mod never see them. When you join, you and every player already online with the mod swap everything you know, also what you found playing alone.

The only case where the server helps is when you play in groups that are not online at the same time (two players today, other two tomorrow). Without the mod on the server, pins only travel between players online together. With the mod also on a dedicated server, the server keeps every player's pins in `BepInEx/ResourcePins/<world>_server.json`, saved with the world, and sends them to whoever joins. The server needs BepInEx and the same `ResourcePins.dll` in `BepInEx/plugins`, nothing else.

A pin you delete by hand stays deleted only for you. Turn sharing off with `Enabled` under `[Sync]`.

The cartography table shares the mod's pins like any other pin, X included.

## Development

Build and copy the DLL to your game (close the game first):

```sh
dotnet build src/ResourcePins -c Release -p:Deploy=true
```

It copies `ResourcePins.dll` to `BepInEx/plugins` of the default Steam install, pass `-p:ValheimDir=...` for other path.

```sh
dotnet test tests/ResourcePins.Tests
```

### Release

Releases are built local because the build needs Valheim's assemblies. The version is `Version` in `src/ResourcePins/Plugin.cs`. Bump it, add a `CHANGELOG.md` entry, then:

```sh
./scripts/package.sh                      # -> dist/ResourcePins-<version>.zip
gh release create v<version> dist/ResourcePins-<version>.zip --generate-notes
```

## License

[MIT](LICENSE)
