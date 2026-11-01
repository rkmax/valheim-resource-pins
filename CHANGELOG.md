# Changelog

## 0.3.0

Includes 0.2.1, which was never released on GitHub.

- **Breaking:** sync protocol 3. Every player and the server must update to share pins. Registry files are migrated on the first save and the old file is kept as `.v2.bak`; to go back to an older version, restore it.
- Large map: a filter panel on the left groups the filters by biome, and the minimap shows a legend. `FilterKey` (F) cycles the filter, Shift+F goes back; `FilterPanel` turns the list off.
- Dungeons and places use the names the game gives them. Places with no name are called after the creature they spawn, and creature spawners have their own `Spawners` list.
- Mod pins are no longer written to cartography tables.
- Pins are keyed by position. This fixes duplicate pins after server restarts and hand-deleted pins coming back; a deleted pin now hides that item around its spot.
- New default items: Tar, Fiddleheadfern, MushroomSmokePuff, QueenBee, SulfurStone, ProustitePowder, Vineberry. New default places: MorkBorg, three Mistlands camps and the Molten Core and Surtling Core stands. The `Blocklist` default no longer hides `dvergrprops_crate_ashlands`. Existing config files keep their values.
- `ResourcePins.ResourceApi.Query` returns the mod's pins near a point as JSON, for other mods.
- Dedicated server: writes the area explored on the cartography tables to `<world>_explored.bin`.

## 0.2.0

First release.

- Pins for resource nodes, removed when the resource is gone. X on picked berries while they regrow.
- Pins for dungeons, camps, boss altars and traders, with an X when looted.
- Messages in the top-left feed for new and removed pins.
- Share pins live with other players that have the mod.
- Optional server store: the mod on a dedicated server keeps the pins and sends them to who joins later.
