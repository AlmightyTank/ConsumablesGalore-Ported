# Adding a new consumable

This mod is data-driven. To add a new item you **do not touch any C# code** — you
just add a `.json` file to the `items/` folder. On server start the mod scans
`items/*.json` and loads everything it finds.

## Steps

1. Copy an existing file in `items/` that is closest to what you want (e.g. copy
   `AdrenalinePlus.json` for a stim, `Glucose.json` for a food/drink).
2. Rename it (the file name doesn't matter, only the contents do).
3. Change the fields (see below).
4. Deploy:
   - **Quick test:** put the file in `SPT/user/mods/ConsumablesGalore/items/` and
     restart the server.
   - **Keep it in the project:** put it in this repo's `items/` folder, run
     `dotnet build -c Release`, then copy `bin/Release/ConsumablesGalore/` to
     `SPT/user/mods/`.

If a file has a problem it is skipped and logged as
`[Consumables Galore] Failed to parse <file> ...` — the other items still load.

## Required fields

| Field           | Description |
|-----------------|-------------|
| `cloneOrigin`   | Tpl of the vanilla item to clone. Must be a real 24-char item id. Look items up at https://db.sp-tarkov.com |
| `id`            | The new item's tpl. **Must be a unique 24-character hex id** (see below). |
| `fleaPrice`     | Number > 0, or `"asOriginal"`. |
| `handBookPrice` | Number, or `"asOriginal"`. |
| `locales.en`    | `name`, `shortName`, `description`. Without a name the item is blank in-game. |

### The `id` must be a valid MongoId

`id` (and `craft._id`) must be **exactly 24 hexadecimal characters**, e.g.
`68328fb55ace8f9d24940178`. Made-up strings like `"myStim"` will throw an error.
Generate one with any "MongoDB ObjectId generator", or copy an existing id and
change several hex digits (keep it unique across all item files).

## Optional fields

| Field                           | Description |
|---------------------------------|-------------|
| `MaxResource`                   | Number of uses / hp resource. |
| `BackgroundColor`               | Inventory cell colour, e.g. `"red"`, `"green"`, `"blue"`, `"yellow"`, `"violet"`, `"grey"`, `"black"`, `"orange"`. Omit to keep the origin's colour. |
| `effects_health`                | Hydration/energy etc. changes. Keys: `Health`, `Hydration`, `Energy`, `Radiation`, `Temperature`, `Poisoning`. |
| `effects_damage`                | Negative side effects. Keys: `Pain`, `Contusion`, `HeavyBleeding`, `LightBleeding`, `Fracture`, `Intoxication`, `RadExposure`. |
| `Buffs`                         | Stimulator buffs (array). `BuffType` and `SkillName` must be valid game values — copy from an existing file. |
| `trader`                        | Sells the item. `traderId`, `loyaltyReq`, `price`, `amountForSale`, optional `questUnlock`. |
| `craft`                         | Hideout craft recipe. `_id` is its own unique 24-hex id; `endProduct` must equal the item `id`. |
| `includeInSameQuestsAsOrigin`   | `true` = add the item as a valid handover/find target in any quest that uses the origin. |
| `includeInQuestAssortAsOrigin`  | `true` = if the origin is quest-locked in `trader`'s assort (only purchasable after starting/completing/failing a quest), lock this item the same way. Requires `trader` to be set. |
| `includeInQuestRewardsAsOrigin` | `true` = wherever a quest gives the origin item as a reward, also give this item as a reward (in addition to, not instead of, the origin). |
| `questReward`                   | Gives this item as a reward on a **specific quest you name**, independent of the clone origin. `questId`, `state` (default `"Success"`), `count` (default `1`). |
| `addSpawnsInSamePlacesAsOrigin` | `true` = spawn in loose/static loot wherever the origin spawns. |
| `spawnWeightComparedToOrigin`   | Spawn chance relative to the origin (e.g. `0.5` = half as common). |

### `trader.questUnlock` — lock the item's assort entry behind a specific quest

Unlike `includeInQuestAssortAsOrigin` (which mirrors whatever quest already
locks the origin), `questUnlock` locks this item behind **any quest you
choose**, even one the origin has nothing to do with:

```json
"trader": {
  "traderId": "54cb50c76803fa8b248b4571",
  "loyaltyReq": 1,
  "price": 5000,
  "amountForSale": 5,
  "questUnlock": {
    "questId": "PUT_A_QUEST_ID_HERE",
    "state": "started"
  }
}
```

`state` is `"started"`, `"success"`, or `"fail"` (case-insensitive) — matches
the trader's `questassort.json` semantics: the item appears once the quest
reaches that state.

### `questReward` — give the item as a reward on a specific quest

```json
"questReward": {
  "questId": "PUT_A_QUEST_ID_HERE",
  "state": "Success",
  "count": 3
}
```

`state` matches the quest's internal reward-state key (`"Started"`,
`"AvailableForStart"`, `"Success"`, `"Fail"`, etc.) — `"Success"` (reward on
turn-in) is what you want in almost every case. The item is added alongside
whatever else that quest already rewards; nothing existing is removed.

## Pricing rules (`fleaPrice` / `handBookPrice`)

- `"asOriginal"` – use the origin item's price.
- a number `<= 10` – treat as a **multiplier** of the origin's flea price
  (e.g. `1.5` = 150% of origin).
- a number `> 10` – treat as an **absolute** rouble price.

## Minimal example

```json
{
  "cloneOrigin": "5c10c8fd86f7743d7d706df3",
  "id": "PUT_A_UNIQUE_24_HEX_ID_HERE",
  "fleaPrice": 50000,
  "handBookPrice": "asOriginal",
  "includeInSameQuestsAsOrigin": false,
  "addSpawnsInSamePlacesAsOrigin": true,
  "spawnWeightComparedToOrigin": 0.5,
  "locales": {
    "en": {
      "name": "My Custom Stim",
      "shortName": "MyStim",
      "description": "A custom stimulator."
    }
  }
}
```

## Full example (with buffs, trader and craft)

See any existing file such as `items/AdrenalinePlus.json` for a complete,
working reference that uses every feature.

## For other mod authors: adding items from your own mod

If you're making an addon/patch mod and want your own item JSON files run
through Consumables Galore's pipeline (clone, buffs, trader, quest hookups,
spawns) without editing this mod's `items/` folder, inject `ConsumablesGalore`
and call `LoadAdditionalItems` from your mod's `OnLoad`:

```csharp
[Injectable(TypePriority = OnLoadOrder.Preload + 2)] // after Consumables Galore's own items/ folder
public class YourAddonMod(ConsumablesGalore.ConsumablesGalore consumablesGalore) : IOnLoad
{
    public async Task OnLoadAsync(CancellationToken cancellationToken)
    {
        // Defaults to "items/" inside YOUR mod's own folder.
        await consumablesGalore.LoadAdditionalItems(Assembly.GetExecutingAssembly(), cancellationToken: cancellationToken);

        // Or point it at a nested folder, e.g. following the WTT db/CustomItems/ convention:
        await consumablesGalore.LoadAdditionalItems(
            Assembly.GetExecutingAssembly(),
            Path.Combine("db", "CustomItems", "Consumables"),
            cancellationToken);
    }
}
```

- The folder is resolved relative to **your** mod's directory (via the assembly you pass in),
  not Consumables Galore's.
- Give your mod's `[Injectable]` a later `TypePriority` than
  `OnLoadOrder.Preload + 1` (Consumables Galore's own priority) so your items load
  after the normal `items/` folder. `OnLoadOrder` is a static class of `int` constants
  (`Preload = 100000`, `GameCallbacks = 200000`, etc.), not an enum — pick any value
  greater than `100001` and less than the next stage you don't want to run before.
- The subfolder argument can be nested (`Path.Combine("db", "CustomItems", "Consumables")`).
  Keep your item `.json` files directly inside that folder — files in further subfolders
  underneath it won't resolve correctly.
