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
| `trader`                        | Sells the item. `traderId`, `loyaltyReq`, `price`, `amountForSale`. |
| `craft`                         | Hideout craft recipe. `_id` is its own unique 24-hex id; `endProduct` must equal the item `id`. |
| `includeInSameQuestsAsOrigin`   | `true` = add the item as a valid handover/find target in any quest that uses the origin. |
| `addSpawnsInSamePlacesAsOrigin` | `true` = spawn in loose/static loot wherever the origin spawns. |
| `spawnWeightComparedToOrigin`   | Spawn chance relative to the origin (e.g. `0.5` = half as common). |

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
