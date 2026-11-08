# USSEP CC Cleanup

Lets you run the [Unofficial Skyrim Special Edition Patch](https://www.nexusmods.com/skyrimspecialedition/mods/266)
(USSEP) without the free Creation Club content (Rare Curios, Survival Mode, Fishing, Saints &
Seducers and `_ResourcePack.esl`), while USSEP itself stays unmodified. That keeps lists
installable through Wabbajack and other tools that expect USSEP's original files.

USSEP lists the free CC plugins as masters. This mod has two parts:

* Five dummy masters: empty plugins with the CC file names, so USSEP loads.
* `USSEP CC Cleanup.esp`: an ESL-flagged plugin that removes what USSEP still points at the
  missing CC content.

Built for USSEP 4.3.9c. A different USSEP version needs a rebuilt plugin (see `BUILDING.md`).

This code was written with LLM assistance.

## Installation

* Remove the real free CC plugins, their BSAs and any patches that need them.
* Install this mod and load `USSEP CC Cleanup.esp` directly after USSEP.
* Start a new game. Saves that have seen the CC content are not supported.
* Report problems with this mod here, not to the USSEP team.

## What the cleanup does

* Disables 20 USSEP references to CC objects that would otherwise be left floating or empty,
  for example the Yellow Book of Riddles in two Hearthfire basements and a camp with moonstone
  in Eastmarch's volcanic tundra.
* Gives 46 CC references whose base object is CC (4 actors, 42 objects) an inert vanilla base
  and disables them. The engine still creates these references, and a USSEP-placed CC actor
  without a base crashed the game on save load in Riften.
* Switches two vanilla fishing rods that USSEP points at the CC fishing rod back to the vanilla
  one.
* Stops the Saints & Seducers Nerveshatter quest from starting with the game.
* Simplifies vanilla records as if Survival Mode were off: Elemental Fury, the diseases,
  Histskin, the Steed Stone, Quick Shot, the Flame Cloak scroll, the food health effects,
  Dawnguard's Blood Potion and two dialogue lines lose their Survival-only conditions and
  effects.
* Removes CC relations from four factions, CC keywords from six magic effects and CC entries
  from five locations (Dawnstar, Morthal, Riften, Markarth and Windstad Manor).
* Points dead salmon back at vanilla Salmon Meat.

USSEP's retroactive-update quests keep their CC properties, which come up as None, so Papyrus
may log None-property errors from `USSEPRetroactive` scripts. xEdit's Check for Errors still
lists USSEP's unresolved CC form IDs. Both are expected.

## License

Released under the MIT License (see `LICENSE`). The dummy masters only borrow the CC file
names; they contain no Creation Club or USSEP data.

Source: <https://github.com/hankinsrim/USSEPCCCleanup>
