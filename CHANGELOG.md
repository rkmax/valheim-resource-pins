# Changelog

## Unreleased

- Large map: a key (`FilterKey`, F) cycles between all pins, only places and only one item; Shift+F goes back. A line under the biome name shows the active filter.
- `ResourcePins.ResourceApi.Query` returns the mod's pins near a point as JSON, for other mods.
- A place no longer gets one more pin each time the world loads; the extra copies go when you are near it again.

## 0.2.1

- Dedicated server: writes the area explored on the cartography tables to `<world>_explored.bin`, for tools outside the game.

## 0.2.0

First release.

- Pins for resource nodes, removed when the resource is gone. X on picked berries while they regrow.
- Pins for dungeons, camps, boss altars and traders, with an X when looted.
- Messages in the top-left feed for new and removed pins.
- Share pins live with other players that have the mod.
- Optional server store: the mod on a dedicated server keeps the pins and sends them to who joins later.
