# Changelog

## 1.0.0 — colours, bots and loot

- Every rig now comes in the same five colours as the pouches - Coyote, Olive, MultiCam, Black
  and EMR - so a rig and the pouches on it match. Ten carriers, fifty rigs. Each wears a
  recoloured copy of its carrier's own model: the printed webbing, the hardware and the labels
  are recoloured with it rather than tinted over.
- **The rigs you already own change colour.** A modular rig keeps its id, its cells, the pouches
  in them and its place in your stash - only its look changes, and its name now says which colour
  it is. The ones that change: Interceptor OTV (UCP) is black, (Woodland) olive, (3 Color Desert)
  coyote, (Centre-Europe) EMR; MF-UNTAR is black, (DBDU) coyote, (MARPAT) EMR, (Wine Leaf) olive;
  FORT Gladiator-S is olive; THOR is coyote and (MAS Gray) black; 6B45 and 6B43 are EMR; the
  three IOTV Gen4 kits are MultiCam; HighCom Trooper TFO keeps MultiCam and Coyote.
- Couturier is no longer a dependency of any kind. The rigs that were its colourways are now
  ordinary rigs off the vanilla carrier, so they are there whether Couturier is installed or not,
  and removing Couturier no longer leaves them in your profile without a template. They also
  weigh what the rest of their carrier's colours weigh, rather than the lighter Couturier value.
- The colours of one carrier share the room a single rig had on bots and in loot: five colours
  of a carrier turn up as often together as one did before, not five times as often.

- Anatoly buys his own rigs back. He never could: a trader may only buy an item when it buys
  every part inside it too, and a rig always carries its soft armor panels and usually its
  plates. He now buys everything his rigs' slots accept - the panels, collars, shoulders, groin
  plates and every plate that fits - which also means he takes a bare plate off your hands.

- Bots wear the modular rigs, with pouches on them. A rig turns up as often as the armor vest it
  is built on: the bot types that wear the prototype, in the proportion they wear it. Its pouches
  are rolled, not placed one cell at a time - magazine pouches across the chest left to right,
  the rest of the chest either filled or left bare, each cummerbund empty, half full or full -
  and the colours are matched to the rig, with the odd pouch out.
- A bot in a modular rig carries its spare magazines, its loose rounds, its grenades and its
  medicine in those pouches, and reaches them in raid: it reloads, tops magazines up, throws
  and heals out of a pouch the same way the player does.
- The rigs and the pouches turn up in loot. Containers that hold the prototype now hold the rig,
  assembled with its panels and plates; dead scavs, dead PMCs, weapon crates and wooden crates
  hold single pouches; and the map itself offers rigs complete with a pouch loadout wherever the
  prototype lies around. Non-PMC bots carry single pouches in their backpacks, and so do PMCs.
- All of it is numbers in `mod-files/bots.jsonc`: how often a rig turns up, how many magazines a
  chest carries, how full a cummerbund is, how likely a pouch is to be the odd colour out, and
  the loot weights. A missing or broken file switches the bot and loot half off with a warning
  and leaves the rest of the mod working.
- With APBS installed, its tier tables are corrected after every import: our rigs are weighed
  against their prototypes tier by tier instead of at a flat weight in tiers 3 to 7, they leave
  the tiers their prototype is absent from, and the pouch cells are taken out of its mod tables
  so no user preset can roll a pouch into a cell that another pouch already covers.

- Every rig, pouch, colour and the trader now speak Chinese (Simplified), Spanish, Portuguese
  (Brazil), German, French, Japanese, Polish, Turkish and Korean as well as English and Russian.
  A language the game ships but the mod does not list still falls back to English.
- Names follow the game's own conventions instead of the mod's: Russian puts the kind of item
  first ("Модульная разгрузка IOTV Gen4"), colours are written the way the game writes them
  (Coyote and MultiCam stay Latin in Russian, EMR is "Флора Цифра"), and an empty pouch cell is
  labelled in upper case like the game's own cell labels.
- Short names are the donor item's own short name plus an " M" marker ("Gen4 Full M",
  "THOR IC M"). They are the abbreviations the player already reads on the vanilla item, they
  fit the item cell, and they still tell a modular rig apart from the plain one.
- The open-top magazine pouch is no longer called a Fastmag, which is a different product, and
  the "large utility pouch" is now the cargo pouch: it takes the same 2x2 and holds the same six
  cells as the utility pouch, so nothing about it was large.
- `items.jsonc` writes the colour names and the two descriptions once instead of once per item:
  a `colors` block and `vestDescription` / `pouchDescription`. A pouch variant without a colour
  of its own and an item without a description of its own take the shared text.
- `build/dump-strings.ps1` writes `localization/strings.json`: every string the mod shows a
  player, with its languages and what it is for. Generated from the config, not edited by hand.

- "Reload" and "Load ammo" in the context menu find magazines lying in pouches. Outside a raid
  that menu searches the player's containers one level deep - a rig's own grids, not what hangs
  on it - so a modular rig carrying every magazine in its pouches answered "Can't find any
  non-empty magazine", both worn and lying in the stash. In raid it always worked, that search
  is a different one.

- The character screen no longer lists a modular rig's pouch cells under "special slots". It
  lists every non-armor slot of an equipped container there, which for a modular rig meant eight
  cells hung next to the pouch grids as a block of noise; pouches are fitted on the Modding
  screen. A rig with real special slots as well keeps the panel.

- Mods that widen the same reachable-item searches keep working. Reaching into a pouch used to
  replace the game's search at the call site, which hid it from the other mod looking for it:
  with Use Items Anywhere installed, all of its patches were dropped and items in the slots it
  adds - a belt, for one - could no longer be bound to a quick slot, used or reloaded from. The
  game's search is now left where it stands and the pouches are added to what it found.

## 0.2.0 — pouch system

- Pouch slots come in 2x2 clusters; the 6B45 has four (chest left/right, cummerbund
  left/right): `mod_pouch_1..16`. The eight slots of 0.1.0 are clusters 1 and 2, unchanged:
  pouches attached to them stay where they are.
- IOTV Gen4 modular rigs in all three kits (Full Protection, Assault, High Mobility), four pouch
  clusters each. The kits share one cell layout: `bones.json` can point a rig at another rig's
  layout (`"tpl": "owner tpl"`).
- More modular rigs: FORT Gladiator-S (WTT-ContentBackport), 6B43 Zabralo-Sh, MF-UNTAR, NFM THOR
  Integrated Carrier (four clusters each), HighCom Trooper TFO (MultiCam, Coyote) and Interceptor
  OTV (UCP, Woodland) (two chest clusters, no cummerbund). With Couturier installed, its colours of
  the OTV, MF-UNTAR and THOR get modular versions as well: a rig may be `"optional"` in
  `items.jsonc`, and without its donor only that rig is left out.
- Pouch sizes on a rig (footprint): 1x1, 1x2, 2x1, 2x2. A cell accepts every pouch that fits
  from it; a larger pouch blocks the cells it covers through the game's own slot blocking
  (error message and highlighting of the pouch in the way), and no cell can be covered twice.
- A pouch spread over several cells sits in the middle of them, on the rig in the preview and
  on the character; the cells of a cluster follow one bone of the skeleton together.
- Pouches on a worn rig no longer push the character around: their solid colliders are switched
  off on the body, as the game does for everything hung on the item's own model.
- Pouches on a worn rig made of several parts (such as the IOTV Assault kit) stay on the chest
  instead of floating around the head: each cluster follows the nearest bone of the torso among
  all the rig's worn meshes, not the bones of its largest part (the shoulder pads). The cell
  layout on the item model carries over to the worn model exactly; the F12 "Worn layout" setting
  is gone.
- Pouch models are drawn with the game's item shader, set up like the game's IFAK, so they
  match the rest of the gear.
- The Modding screen hides the icons of cells covered by a larger pouch.
- Quick move (Ctrl+click), picking up and unloading fill the pouches of a worn modular rig
  before the pockets and the backpack, like the grids of any rig.
- UI Fixes: dragging several items at once into a pouch's grid works (each pouch's grids have a
  parent of their own in the rig window), and what does not fit spills over into the other
  pouches of the rig: a modular rig and its pouches are one storage, as a rig and its grids are.
  The spill-over follows the rig window: the next pouch to the right, then the rows below.
- A pouch taken off on the Modding screen goes to the inventory, then the stash, or stays with
  the game's "no free space" message; it is no longer moved onto another cell of the same rig.
- Empty pouch cells in the inspect window show a pouch picture instead of a blank white square,
  and a proper name (`pouchSlotName` in `items.jsonc`) instead of the slot id.
- `mounts.json`: how each pouch model sits on its seat.
- New pouches with their own models, each in Coyote, Olive, Multicam, Black and EMR Summer: pistol magazine and grenade (1x1),
  Fastmag (open top), flapped magazine, double magazine, narrow utility and bottle (1x2), admin
  (2x1), medical, utility and large utility (2x2), with one or two sections.
- The large utility pouch has one 2x3 section (was 2x2 + 2x1).
- The AFAK- and IFAK-type pouches of 0.1.0 are gone: take them off and empty them before updating.
- Gadget pouch (formerly the grenade pouch): two cells tall (1x2) with two 1x1 sections.
- Warrior grenade pouch: a 1x1 model in five colours.
- The rig window stacks the sections of a pouch in a column when none of them is taller than
  wide (admin, utility and gadget pouches).
- The rig window lays pouch grids out in centred rows no wider than a set width (F12, 6 cells):
  pouches of the same kind (same sections) share even rows, and different kinds share a row
  only when it stays symmetric — small pouches on both sides of larger ones. A pouch the rig
  carries only one of cannot be symmetric anyway: such pouches share a row, the widest in the
  middle, and that row comes after the symmetric ones.
- The bone editor moved into a separate development plugin (not part of the release).
- The trader screen shows the mod's pouches in order: by size, model and colour; the rigs by name,
  the kits and colours of a carrier together (the game ordered them by template id, i.e. at random).
- The line-up is sold by the mod's own trader, Anatoly (`mod-files/trader/trader.jsonc`:
  loyalty levels, refresh time, flea listing, buy-back rate), and no longer by Ragman. He buys
  back only what he sells. `traderId` in `items.jsonc` is gone (an old file still loads); a rig
  or a pouch may set `loyaltyLevel` (1-4).
- Every rig has a default preset with its soft armor, collar and default plates, as vanilla armor
  has: player offers on the flea market, Fence, loot and the handbook show the rig assembled
  instead of a bare carrier with "missing module", and Fence wears its plates down at random like
  any armor's. The trader sells rigs with their soft armor, without plates.

## 0.1.0 — prototype

- 6B45 modular rig (clone of the WTT-ContentBackport 6B45 as a chest rig): plate slots
  of the original, no pockets of its own, eight `mod_pouch` slots.
- AFAK-type (3x2) and IFAK-type (2x2) MOLLE pouches.
- Rig, pouches sold by Ragman (LL1, roubles).
- Rig window shows the grids of all attached pouches; rebuilt when a pouch is attached or
  removed while it is open.
- Pouch slot bones on the rig model: slot icons on the Modding screen, pouch models on the
  rig in the preview and on the character.
- Pouches locked in raid (configurable); pouch contents reachable for quick reload,
  grenade selection and quick-slot binds.
- In-game bone editor (off by default) writing `bones.json`.
