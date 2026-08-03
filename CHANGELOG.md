# Changelog

## v3.0.0

**SPT 4.1 compatibility** - updated server and client for SPT 4.1.x
**Not backward compatible** - this version requires SPT 4.1.x and will not work with SPT 4.0.x or earlier.

### New

- **Quest-assort and quest-reward hookups**:
  - `includeInQuestAssortAsOrigin` - mirrors whatever quest already locks the origin item's trader assortment onto the new item
  - `includeInQuestRewardsAsOrigin` - mirrors any quest reward that gives the origin item onto the new item
  - `trader.questUnlock` - locks the new item's assortment entry behind a specific quest, independent of the origin
  - `questReward` - gives the new item as a reward on a specific quest, independent of the origin
  - Quest-assort locks now also add a matching `AssortmentUnlock` reward, so the quest correctly displays "unlocks trader assortment" in-game instead of only working mechanically
- **`LoadAdditionalItems` extension point** - other mods can now register their own item folders (including nested paths like `db/CustomItems/Consumables`) to be loaded through Consumables Galore's full pipeline (clone, buffs, trader, quest hookups, spawns), without editing this mod

### Fixed

- Consumables Galore is now registered as a DI singleton, so debug logging (and any other per-instance state) behaves consistently whether the item load is triggered by Consumables Galore itself or by another mod calling `LoadAdditionalItems`
