using System.Reflection;
using System.Text.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services.Mod;

namespace ConsumablesGalore;

/// <summary>
/// Consumables Galore, ported from the original SPT 3.x TypeScript mod to the
/// SPT 4.0 C# server API. On load it reads every items/*.json definition and:
///   - clones a vanilla item into a new custom consumable
///   - applies stimulator buffs, health/damage effects and pricing
///   - optionally sells it at a trader, adds a hideout craft, injects it into the
///     same quests as its origin, and spawns it wherever the origin spawns.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 1)]
public class ConsumablesGalore(
    ISptLogger<ConsumablesGalore> logger,
    ModHelper modHelper,
    CustomItemService customItemService,
    DatabaseServer databaseServer
) : IOnLoad
{
    private const string ModName = "Consumables Galore";
    private const string RoublesTpl = "5449016a4bdc2d6f028b456f";

    // Map names (EFT keys) the original mod injected spawns into
    private static readonly string[] SpawnMaps =
    [
        "bigmap", "woods", "factory4_day", "factory4_night", "interchange",
        "laboratory", "lighthouse", "rezervbase", "shoreline", "tarkovstreets", "sandbox",
    ];

    private bool _debug;
    private bool _realDebug;

    public Task OnLoad()
    {
        logger.Info($"[{ModName}] started loading");

        var modPath = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());

        var config = modHelper.GetJsonDataFromFile<ModConfig>(System.IO.Path.Combine(modPath, "config"), "config.json");
        _debug = config.Debug;
        _realDebug = config.RealDebug;

        var itemsDir = System.IO.Path.Combine(modPath, "items");
        if (!Directory.Exists(itemsDir))
        {
            logger.Error($"[{ModName}] items folder not found at {itemsDir}, no items loaded");
            return Task.CompletedTask;
        }

        foreach (var file in Directory.GetFiles(itemsDir, "*.json", SearchOption.AllDirectories))
        {
            var fileName = System.IO.Path.GetFileName(file);
            if (_debug)
            {
                logger.Info($"[{ModName}] Processing file: {fileName}");
            }

            try
            {
                var itemConfig = modHelper.GetJsonDataFromFile<ConsumableItemConfig>(itemsDir, fileName);
                LoadConsumable(itemConfig);
            }
            catch (Exception ex)
            {
                logger.Error($"[{ModName}] Failed to parse {fileName}, item will not be loaded: {ex.Message}");
            }
        }

        logger.Success($"[{ModName}] finished loading");
        return Task.CompletedTask;
    }

    private void LoadConsumable(ConsumableItemConfig itemConfig)
    {
        var tables = databaseServer.GetTables();
        var origin = itemConfig.CloneOrigin;
        var newId = itemConfig.Id;

        if (!tables.Templates.Items.TryGetValue(origin, out var originItem))
        {
            logger.Error($"[{ModName}] Clone origin {origin} not found in item db, skipping {newId}");
            return;
        }

        // Resolve the origin's flea/handbook base values for "asOriginal"/multiplier support
        tables.Templates.Prices.TryGetValue(origin, out var originFleaPrice);
        var handbookEntry = tables.Templates.Handbook.Items.FirstOrDefault(i => i.Id == origin);
        var originHandbookPrice = handbookEntry?.Price;

        var fleaPrice = ResolvePrice(itemConfig.FleaPrice, originFleaPrice, originFleaPrice);
        var handbookPrice = ResolvePrice(itemConfig.HandBookPrice, originFleaPrice, originHandbookPrice);

        var overrides = new TemplateItemProperties
        {
            StimulatorBuffs = newId,
            EffectsHealth = itemConfig.EffectsHealth,
            EffectsDamage = itemConfig.EffectsDamage,
            MaxHpResource = itemConfig.MaxResource,
            MedUseTime = itemConfig.MedUseTime,
        };

        // BackgroundColor's setter runs string.Intern() which throws on null, so
        // only assign it when the item actually specifies one.
        if (itemConfig.BackgroundColor is not null)
        {
            overrides.BackgroundColor = itemConfig.BackgroundColor;
        }

        var cloneDetails = new NewItemFromCloneDetails
        {
            ItemTplToClone = origin,
            OverrideProperties = overrides,
            ParentId = originItem.Parent,
            NewId = newId,
            FleaPriceRoubles = fleaPrice,
            HandbookPriceRoubles = handbookPrice,
            HandbookParentId = handbookEntry?.ParentId.ToString(),
            Locales = itemConfig.Locales,
        };

        customItemService.CreateItemFromClone(cloneDetails);

        // Register stimulator buffs against the new item's buff key
        if (itemConfig.Buffs is not null)
        {
            tables.Globals.Configuration.Health.Effects.Stimulator.Buffs[newId] = itemConfig.Buffs;
        }

        if (itemConfig.IncludeInSameQuestsAsOrigin)
        {
            AddToOriginQuests(tables.Templates.Quests, origin, newId);
        }

        if (itemConfig.AddSpawnsInSamePlacesAsOrigin)
        {
            AddSpawns(tables.Locations, origin, newId, itemConfig.SpawnWeightComparedToOrigin);
        }

        if (itemConfig.Trader is not null)
        {
            AddToTrader(tables.Traders, newId, itemConfig.Trader);
        }

        if (itemConfig.Craft is not null)
        {
            tables.Hideout.Production.Recipes?.Add(itemConfig.Craft);
        }
    }

    /// <summary>
    /// Interprets the polymorphic price field: "asOriginal" uses the origin value,
    /// a value &lt;= 10 is a multiplier of the origin flea price, anything else is absolute.
    /// </summary>
    private static double ResolvePrice(JsonElement? price, double originFleaPrice, double? originValue)
    {
        if (price is null)
        {
            return originValue ?? originFleaPrice;
        }

        var element = price.Value;
        if (element.ValueKind == JsonValueKind.String)
        {
            // "asOriginal"
            return originValue ?? originFleaPrice;
        }

        if (element.ValueKind == JsonValueKind.Number)
        {
            var value = element.GetDouble();
            return value <= 10 ? originFleaPrice * value : value;
        }

        return originValue ?? originFleaPrice;
    }

    private void AddToOriginQuests(Dictionary<MongoId, Quest> quests, MongoId origin, MongoId newId)
    {
        foreach (var quest in quests.Values)
        {
            var conditions = quest.Conditions?.AvailableForFinish;
            if (conditions is null)
            {
                continue;
            }

            foreach (var condition in conditions)
            {
                var isItemCondition = condition.ConditionType is "HandoverItem" or "FindItem";
                if (!isItemCondition || condition.Target is not { IsList: true } target || target.List is null)
                {
                    continue;
                }

                if (target.List.Contains(origin) && !target.List.Contains(newId))
                {
                    if (_debug)
                    {
                        logger.Info($"[{ModName}] Adding {newId} to quest {quest.Id} ({quest.QuestName})");
                    }

                    target.List.Add(newId);
                }
            }
        }
    }

    private void AddToTrader(Dictionary<MongoId, Trader> traders, MongoId newId, TraderSaleConfig traderConfig)
    {
        if (!traders.TryGetValue(traderConfig.TraderId, out var trader))
        {
            logger.Warning($"[{ModName}] Trader {traderConfig.TraderId} not found, {newId} will not be sold");
            return;
        }

        trader.Assort.Items.Add(new Item
        {
            Id = newId,
            Template = newId,
            ParentId = "hideout",
            SlotId = "hideout",
            Upd = new Upd
            {
                UnlimitedCount = false,
                StackObjectsCount = traderConfig.AmountForSale,
            },
        });

        trader.Assort.BarterScheme[newId] =
        [
            [
                new BarterScheme
                {
                    Count = traderConfig.Price,
                    Template = RoublesTpl,
                },
            ],
        ];

        trader.Assort.LoyalLevelItems[newId] = traderConfig.LoyaltyReq;
    }

    private void AddSpawns(SPTarkov.Server.Core.Models.Spt.Server.Locations locations, MongoId origin, MongoId newId, double spawnWeight)
    {
        var dictionary = locations.GetDictionary();
        foreach (var mapName in SpawnMaps)
        {
            var mappedKey = locations.GetMappedKey(mapName);
            if (!dictionary.TryGetValue(mappedKey, out var location) || location is null)
            {
                continue;
            }

            AddLooseLootSpawns(location, origin, newId, spawnWeight);
            AddStaticLootSpawns(location, mapName, origin, newId, spawnWeight);
        }
    }

    private void AddLooseLootSpawns(Location location, MongoId origin, MongoId newId, double spawnWeight)
    {
        location.LooseLoot?.AddTransformer(looseLoot =>
        {
            if (looseLoot?.Spawnpoints is null)
            {
                return looseLoot;
            }

            foreach (var spawnpoint in looseLoot.Spawnpoints)
            {
                var items = spawnpoint.Template?.Items?.ToList();
                var distribution = spawnpoint.ItemDistribution?.ToList();
                if (items is null || distribution is null)
                {
                    continue;
                }

                var newItems = new List<SptLootItem>();
                var newDistribution = new List<LooseLootItemDistribution>();

                foreach (var item in items)
                {
                    if (item.Template != origin)
                    {
                        continue;
                    }

                    var originItemId = item.Id.ToString();
                    var originDist = distribution.FirstOrDefault(d => d.ComposedKey?.Key == originItemId);
                    if (originDist is null)
                    {
                        continue;
                    }

                    var newLootId = new MongoId();
                    newItems.Add(new SptLootItem
                    {
                        Id = newLootId,
                        Template = newId,
                    });
                    newDistribution.Add(new LooseLootItemDistribution
                    {
                        ComposedKey = new ComposedKey { Key = newLootId },
                        RelativeProbability = Math.Max(Math.Round((originDist.RelativeProbability ?? 0) * spawnWeight), 1),
                    });
                }

                if (newItems.Count > 0)
                {
                    spawnpoint.Template!.Items = items.Concat(newItems).ToList();
                    spawnpoint.ItemDistribution = distribution.Concat(newDistribution).ToList();
                }
            }

            return looseLoot;
        });
    }

    private void AddStaticLootSpawns(Location location, string mapName, MongoId origin, MongoId newId, double spawnWeight)
    {
        location.StaticLoot?.AddTransformer(staticLoot =>
        {
            if (staticLoot is null)
            {
                return staticLoot;
            }

            foreach (var container in staticLoot.Values)
            {
                var distribution = container.ItemDistribution?.ToList();
                if (distribution is null)
                {
                    continue;
                }

                var originDist = distribution.FirstOrDefault(d => d.Tpl == origin);
                if (originDist is null)
                {
                    continue;
                }

                distribution.Add(new ItemDistribution
                {
                    Tpl = newId,
                    RelativeProbability = (float)Math.Max(Math.Round((originDist.RelativeProbability ?? 0) * spawnWeight), 1),
                });
                container.ItemDistribution = distribution;

                if (_realDebug)
                {
                    logger.Warning($"[{ModName}] Added {newId} to a static container distribution on {mapName}");
                }
            }

            return staticLoot;
        });
    }
}
