using System.Reflection;
using System.Text.Json;
using System.Threading;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Modding.Custom;
using SPTarkov.Server.Core.Utils.Json;

namespace ConsumablesGalore;

/// <summary>
/// Consumables Galore, ported from the original SPT 3.x TypeScript mod to the
/// SPT 4.1 C# server API. On load it reads every items/*.json definition and:
///   - clones a vanilla item into a new custom consumable
///   - applies stimulator buffs, health/damage effects and pricing
///   - optionally sells it at a trader, adds a hideout craft, injects it into the
///     same quests as its origin, and spawns it wherever the origin spawns.
/// </summary>
[Injectable(InjectionType.Singleton, TypePriority = OnLoadOrder.Preload + 1)]
public class ConsumablesGalore(
    ISptLogger<ConsumablesGalore> logger,
    ModHelper modHelper,
    CustomItemService customItemService,
    TemplateTable templateTable,
    TradersTable tradersTable,
    GlobalTable globalTable,
    LocationTable locationTable,
    HideoutTable hideoutTable
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

    public Task OnLoadAsync(CancellationToken cancellationToken)
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

        LoadItemsFromFolder(itemsDir, cancellationToken);

        logger.Success($"[{ModName}] finished loading");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Optional extension point for other mods: loads an additional folder of item definitions
    /// through the same pipeline as Consumables Galore's own items/ folder (clone, buffs, trader,
    /// quest hookups, spawns). Runs whenever it's called, so give your mod a later
    /// <see cref="OnLoadOrder"/> priority than Consumables Galore's
    /// (<c>OnLoadOrder.Preload + 1</c>) to have it run after the normal items/ folder.
    /// Pass your own assembly so the folder is resolved relative to your mod, not this one.
    /// </summary>
    /// <param name="callingAssembly">Your mod's assembly, e.g. <see cref="Assembly.GetExecutingAssembly"/>.</param>
    /// <param name="subFolder">Folder name (relative to your mod's root) to scan for *.json item definitions.</param>
    /// <param name="cancellationToken">Token observed while scanning; pass your own <c>OnLoadAsync</c> token.</param>
    public Task LoadAdditionalItems(Assembly callingAssembly, string subFolder = "items", CancellationToken cancellationToken = default)
    {
        var callingModPath = modHelper.GetAbsolutePathToModFolder(callingAssembly);
        var itemsDir = System.IO.Path.Combine(callingModPath, subFolder);

        if (!Directory.Exists(itemsDir))
        {
            logger.Warning($"[{ModName}] Additional items folder not found at {itemsDir}, nothing loaded for {callingAssembly.GetName().Name}");
            return Task.CompletedTask;
        }

        logger.Info($"[{ModName}] Loading additional items for {callingAssembly.GetName().Name} from {itemsDir}");
        LoadItemsFromFolder(itemsDir, cancellationToken);
        logger.Success($"[{ModName}] finished loading additional items for {callingAssembly.GetName().Name}");

        return Task.CompletedTask;
    }

    private void LoadItemsFromFolder(string itemsDir, CancellationToken cancellationToken)
    {
        foreach (var file in Directory.GetFiles(itemsDir, "*.json", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

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
    }

    private void LoadConsumable(ConsumableItemConfig itemConfig)
    {
        var origin = itemConfig.CloneOrigin;
        var newId = itemConfig.Id;

        if (!templateTable.Items.TryGetValue(origin, out var originItem))
        {
            logger.Error($"[{ModName}] Clone origin {origin} not found in item db, skipping {newId}");
            return;
        }

        // Resolve the origin's flea/handbook base values for "asOriginal"/multiplier support
        templateTable.Prices.TryGetValue(origin, out var originFleaPrice);
        var handbookEntry = templateTable.Handbook.Items.FirstOrDefault(i => i.Id == origin);
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
            NewItemName = BuildInternalItemName(itemConfig),
            FleaPriceRoubles = fleaPrice,
            HandbookPriceRoubles = handbookPrice,
            HandbookParentId = handbookEntry?.ParentId.ToString(),
            Locales = itemConfig.Locales,
        };

        customItemService.CreateItemFromClone(cloneDetails);

        // Register stimulator buffs against the new item's buff key
        if (itemConfig.Buffs is not null)
        {
            globalTable.Configuration.Health.Effects.Stimulator.Buffs[newId] = itemConfig.Buffs;
        }

        if (itemConfig.IncludeInSameQuestsAsOrigin)
        {
            AddToOriginQuests(templateTable.Quests, origin, newId);
        }

        if (itemConfig.AddSpawnsInSamePlacesAsOrigin)
        {
            AddSpawns(locationTable, origin, newId, itemConfig.SpawnWeightComparedToOrigin);
        }

        if (itemConfig.Trader is not null)
        {
            AddToTrader(tradersTable, newId, itemConfig.Trader);

            if (itemConfig.IncludeInQuestAssortAsOrigin)
            {
                AddToQuestAssort(tradersTable, templateTable.Quests, itemConfig.Trader.TraderId, origin, newId);
            }

            if (itemConfig.Trader.QuestUnlock is not null)
            {
                AddQuestAssortUnlock(tradersTable, templateTable.Quests, itemConfig.Trader.TraderId, newId, itemConfig.Trader.QuestUnlock);
            }
        }

        if (itemConfig.IncludeInQuestRewardsAsOrigin)
        {
            AddToQuestRewards(templateTable.Quests, origin, newId);
        }

        if (itemConfig.QuestReward is not null)
        {
            AddQuestReward(templateTable.Quests, newId, itemConfig.QuestReward);
        }

        if (itemConfig.Craft is not null)
        {
            hideoutTable.Production.Recipes?.Add(itemConfig.Craft);
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

    /// <summary>
    /// SPT 4.1 requires an internal (non-locale) name for cloned items. We don't have a
    /// dedicated field for this in items/*.json, so derive a slug from the English locale
    /// name, falling back to the new item's own id if no English name is set.
    /// </summary>
    private static string BuildInternalItemName(ConsumableItemConfig itemConfig)
    {
        var displayName = itemConfig.Locales.GetValueOrDefault("en")?.Name;
        return string.IsNullOrWhiteSpace(displayName)
            ? itemConfig.Id.ToString()
            : displayName.Trim().ToLowerInvariant().Replace(' ', '_');
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

    private void AddToQuestAssort(Dictionary<MongoId, Trader> traders, Dictionary<MongoId, Quest> quests, MongoId traderId, MongoId origin, MongoId newId)
    {
        if (!traders.TryGetValue(traderId, out var trader))
        {
            logger.Warning($"[{ModName}] Trader {traderId} not found, {newId} will not get quest assort unlocks");
            return;
        }

        var originAssortIds = trader.Assort.Items
            .Where(item => item.Template == origin)
            .Select(item => item.Id)
            .ToHashSet();

        if (originAssortIds.Count == 0)
        {
            return;
        }

        foreach (var (state, unlocks) in trader.QuestAssort)
        {
            var lockingQuestId = unlocks
                .Where(unlock => originAssortIds.Contains(unlock.Key))
                .Select(unlock => (MongoId?)unlock.Value)
                .FirstOrDefault();

            if (lockingQuestId is not { } questId)
            {
                continue;
            }

            unlocks[newId] = questId;

            if (_debug)
            {
                logger.Info($"[{ModName}] Locking {newId} behind quest {questId} ({GetQuestName(quests, questId)})");
            }

            AddAssortmentUnlockReward(quests, trader, traderId, newId, questId, state);
        }
    }

    private void AddQuestAssortUnlock(Dictionary<MongoId, Trader> traders, Dictionary<MongoId, Quest> quests, MongoId traderId, MongoId newId, QuestAssortUnlockConfig unlockConfig)
    {
        if (!traders.TryGetValue(traderId, out var trader))
        {
            logger.Warning($"[{ModName}] Trader {traderId} not found, {newId} will not get a quest assort unlock");
            return;
        }

        var state = unlockConfig.State.ToLowerInvariant();
        if (!trader.QuestAssort.TryGetValue(state, out var unlocks))
        {
            unlocks = new Dictionary<MongoId, MongoId>();
            trader.QuestAssort[state] = unlocks;
        }

        unlocks[newId] = unlockConfig.QuestId;

        if (_debug)
        {
            logger.Info($"[{ModName}] Locking {newId} behind quest {unlockConfig.QuestId} ({GetQuestName(quests, unlockConfig.QuestId)})");
        }

        AddAssortmentUnlockReward(quests, trader, traderId, newId, unlockConfig.QuestId, state);
    }

    /// <summary>
    /// Mirrors BSG's vanilla quest data: locking an assort entry behind a quest (trader.QuestAssort)
    /// only controls when the item can actually be bought. To have the quest itself display
    /// "unlocks trader assortment" in the client, the quest also needs an AssortmentUnlock reward
    /// pointing at the same assort entry, loyalty level and trader.
    /// </summary>
    private void AddAssortmentUnlockReward(Dictionary<MongoId, Quest> quests, Trader trader, MongoId traderId, MongoId newId, MongoId questId, string assortState)
    {
        if (!quests.TryGetValue(questId, out var quest))
        {
            return;
        }

        var assortItem = trader.Assort.Items.FirstOrDefault(item => item.Id == newId);
        if (assortItem is null)
        {
            return;
        }

        quest.Rewards ??= new Dictionary<string, List<Reward>>();
        var rewardStateKey = ToQuestRewardStateKey(assortState);
        if (!quest.Rewards.TryGetValue(rewardStateKey, out var rewards))
        {
            rewards = new List<Reward>();
            quest.Rewards[rewardStateKey] = rewards;
        }

        trader.Assort.LoyalLevelItems.TryGetValue(newId, out var loyaltyLevel);

        rewards.Add(new Reward
        {
            Id = new MongoId(),
            Type = RewardType.AssortmentUnlock,
            Index = rewards.Count,
            Target = newId.ToString(),
            TraderId = new StringOrInt(traderId.ToString(), null),
            LoyaltyLevel = loyaltyLevel,
            Items = [new Item { Id = assortItem.Id, Template = assortItem.Template }],
            IsHidden = false,
            Unknown = false,
        });

        if (_debug)
        {
            logger.Info($"[{ModName}] Adding {newId} to quest {questId} ({GetQuestName(quests, questId)}) as an assortment unlock");
        }
    }

    private static string ToQuestRewardStateKey(string assortState) => assortState.ToLowerInvariant() switch
    {
        "started" => "Started",
        "success" => "Success",
        "fail" => "Fail",
        _ => "Success",
    };

    private static string GetQuestName(Dictionary<MongoId, Quest> quests, MongoId questId)
    {
        return quests.TryGetValue(questId, out var quest) ? quest.QuestName ?? "unknown quest" : "unknown quest";
    }

    private void AddToQuestRewards(Dictionary<MongoId, Quest> quests, MongoId origin, MongoId newId)
    {
        foreach (var quest in quests.Values)
        {
            if (quest.Rewards is null)
            {
                continue;
            }

            foreach (var rewards in quest.Rewards.Values)
            {
                var originRewards = rewards
                    .Where(reward => reward.Type == RewardType.Item && reward.Items is { Count: 1 })
                    .Where(reward => reward.Items![0].Template == origin)
                    .ToList();

                foreach (var originReward in originRewards)
                {
                    var originItem = originReward.Items![0];
                    var newItemId = new MongoId();

                    rewards.Add(new Reward
                    {
                        Value = originReward.Value,
                        Id = new MongoId(),
                        Type = RewardType.Item,
                        Index = rewards.Count,
                        Target = newItemId.ToString(),
                        Items = [
                            new Item
                            {
                                Id = newItemId,
                                Template = newId,
                                ParentId = originItem.ParentId,
                                SlotId = originItem.SlotId,
                                Upd = originItem.Upd is null ? null : new Upd { StackObjectsCount = originItem.Upd.StackObjectsCount },
                            },
                        ],
                        LoyaltyLevel = originReward.LoyaltyLevel,
                        FindInRaid = originReward.FindInRaid,
                        GameMode = originReward.GameMode,
                        AvailableInGameEditions = originReward.AvailableInGameEditions,
                    });

                    if (_debug)
                    {
                        logger.Info($"[{ModName}] Adding {newId} to quest {quest.Id} ({quest.QuestName}) as a reward");
                    }
                }
            }
        }
    }

    private void AddQuestReward(Dictionary<MongoId, Quest> quests, MongoId newId, QuestRewardConfig rewardConfig)
    {
        if (!quests.TryGetValue(rewardConfig.QuestId, out var quest))
        {
            logger.Warning($"[{ModName}] Quest {rewardConfig.QuestId} not found, {newId} will not be added as a reward");
            return;
        }

        quest.Rewards ??= new Dictionary<string, List<Reward>>();
        if (!quest.Rewards.TryGetValue(rewardConfig.State, out var rewards))
        {
            rewards = new List<Reward>();
            quest.Rewards[rewardConfig.State] = rewards;
        }

        var newItemId = new MongoId();

        rewards.Add(new Reward
        {
            Value = rewardConfig.Count,
            Id = new MongoId(),
            Type = RewardType.Item,
            Index = rewards.Count,
            Target = newItemId.ToString(),
            Items = [
                new Item
                {
                    Id = newItemId,
                    Template = newId,
                    ParentId = "hideout",
                    SlotId = "hideout",
                    Upd = new Upd { StackObjectsCount = rewardConfig.Count },
                },
            ],
        });

        if (_debug)
        {
            logger.Info($"[{ModName}] Adding {newId} to quest {rewardConfig.QuestId} ({quest.QuestName}) as a {rewardConfig.State} reward");
        }
    }

    private void AddSpawns(LocationTable locations, MongoId origin, MongoId newId, double spawnWeight)
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
