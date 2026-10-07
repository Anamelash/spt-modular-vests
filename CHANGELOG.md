# Changelog

## 1.1.0

- Insuring a rig insures the pouches on it: a lost insured rig comes back with every pouch it
  carried, and the trader never keeps a pouch back.
- With APBS installed, bots wear a rig complete with its soft armor and plates, in the tiers
  where APBS carries them. The rigs follow APBS's modded equipment switch: with
  `enableModdedEquipment` off, bots do not wear them.

## 1.0.0

First release.

- Ten modular rigs, each in five colours — Coyote, Olive, MultiCam, Black and EMR — for
  fifty rigs in all: 6B45, IOTV Gen4 in the Full Protection, Assault and High Mobility kits,
  FORT Gladiator-S, 6B43 Zabralo-Sh, NFM THOR Integrated Carrier, MF-UNTAR, HighCom Trooper
  TFO and Interceptor OTV. A rig carries no pockets of its own and takes the plates and the
  soft armor of the body armor it is built on.
- Thirteen pouches in the same five colours: pistol magazine, grenade and Warrior grenade
  (one cell); gadget, open-top magazine, magazine, double magazine, narrow utility and bottle
  (two cells); admin (two cells wide); medical, utility and cargo (a whole cluster).
- Pouch cells come in 2x2 clusters — four on a rig with a cummerbund, two on a rig without.
  A pouch spreads right and down from its cell and blocks the cells it covers, the way a
  helmet blocks the headset slot.
- The rig window lays the grids of every attached pouch out in centred rows, pouches of the
  same kind together. Quick move, dragging a group of items and unloading fill them like the
  pockets of any rig, and what does not fit in one pouch goes on to the next.
- In raid the pouches are fixed to the rig and their contents work like rig pockets: quick
  reload, grenade selection and quick-slot binds.
- The line-up is sold by the mod's own trader, Anatoly, at loyalty level 1 for roubles; he
  buys it back along with the plates and panels that go into it. On the flea market and at
  Fence a rig comes assembled, with its soft armor, collar and plates.
- Bots wear the modular rigs, with a pouch loadout rolled per bot in a colour matched to the
  rig, and they reload, top their magazines up, throw grenades and heal out of those pouches.
- The rigs and the pouches turn up in loot: in the containers that hold the body armor, on
  dead scavs and PMCs, in weapon and wooden crates, and on the maps as rigs complete with a
  pouch loadout. Bots carry single pouches in their backpacks.
- With APBS installed, the rigs are weighed into its tiers against the body armor they are
  built on, tier by tier.
- Everything about bots and loot is a number in `mod-files/bots.jsonc`, down to
  `"enabled": false` to leave both alone.
- English, Russian, Chinese (Simplified), Spanish, Portuguese (Brazil), German, French,
  Japanese, Polish, Turkish and Korean; any other language the game ships falls back to
  English.
