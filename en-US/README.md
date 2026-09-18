# Boss Checklist (BossChecklist)

A boss checklist sidebar plus a browsable boss log book, in game.
The interface is a port of the tModLoader "Boss Checklist" mod.

## Features

- **Checklist sidebar** (default key `P`, shows while the inventory is open):
  bosses / mini-bosses / events with tick boxes, plus four filter buttons and a scrollbar
- **Boss log book** (default key `L`): a draggable book with side tabs
  - **Table of contents**: every entry in progression order, split into pre-hardmode / hardmode,
    with completion marks, progress bars and the "next up" highlight
  - **Entry pages**: boss portrait, how to summon it (summon item, recipe, crafting station,
    and whether you have the ingredients), loot & collectibles, and records
  - **Loot & collectibles**: treasure bag slot, the full drop table, a golden border around
    collectibles, a green slot plus checkmark for items you have already obtained,
    expert / master-only items marked when your world or difficulty cannot get them
  - **Records**: first victory, personal best, previous attempt and world record,
    tracked per player and per world (kills, deaths, attempt time, hits taken)
  - Right-click an entry to mark it as defeated, `Alt`+right-click to hide it;
    the hidden-list mode lets you toggle each entry's visibility
  - A chest icon appears next to entries whose loot (and collectibles) you have collected in full
- **Boss radar**: bosses off screen get their head and an arrow drawn at the screen edge
  (green = clear line of sight, red = blocked by terrain)
- Records, hidden / marked entries and collected drops are saved to
  `mods\boss-checklist\data\boss-checklist.txt`
- Every option lives in the F6 config menu (checklist, log, records, radar, ...)

## Notes

- "Defeated" is always read from the vanilla fields (`NPC.downedXxx`, `Main.hardMode`, ...),
  never from hardcoded values; the few event flags vanilla does not track (blood moon,
  frost moon, pumpkin moon, solar eclipse, ...) are filled in by this mod
- Drop tables come from the vanilla `Main.ItemDropsDB.GetRulesForNPCID`; corruption / crimson
  and expert-exclusive drops are filtered by the current world and difficulty. In a drunk world
  both evil biomes exist, so both sides count as obtainable
- Obtained loot is recorded from the item pickup event, so stashing a drop in a chest (or selling /
  trashing it) never loses the record; the inventory and equipment slots are also swept every few
  frames as a safety net
- Layout, colours and textures are copied from the tModLoader Boss Checklist;
  the `assets/*.rawimg` files come from the original mod, so check its licence before
  redistributing this port
- Both language builds share the same mod id (`boss-checklist`) and cannot be installed together