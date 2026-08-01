using System.Text.Json;
using System.Text.Json.Serialization;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Hideout;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Mod;

namespace ConsumablesGalore;

/// <summary>
/// Strongly typed representation of a single items/*.json definition.
/// Each file describes a consumable cloned from a vanilla item plus optional
/// trader sale, hideout craft, stimulator buffs, loot spawns and quest hookups.
/// </summary>
public record ConsumableItemConfig
{
    /// <summary>Tpl of the vanilla item to clone as a base.</summary>
    [JsonPropertyName("cloneOrigin")]
    public MongoId CloneOrigin { get; set; }

    /// <summary>Tpl the new cloned item will be given.</summary>
    [JsonPropertyName("id")]
    public MongoId Id { get; set; }

    /// <summary>
    /// Either the string "asOriginal", a multiplier (value &lt;= 10 of the origin's flea price),
    /// or an absolute rouble price (value &gt; 10).
    /// </summary>
    [JsonPropertyName("fleaPrice")]
    public JsonElement? FleaPrice { get; set; }

    /// <summary>Same rules as <see cref="FleaPrice"/> but for the handbook price.</summary>
    [JsonPropertyName("handBookPrice")]
    public JsonElement? HandBookPrice { get; set; }

    [JsonPropertyName("includeInSameQuestsAsOrigin")]
    public bool IncludeInSameQuestsAsOrigin { get; set; }

    [JsonPropertyName("addSpawnsInSamePlacesAsOrigin")]
    public bool AddSpawnsInSamePlacesAsOrigin { get; set; }

    [JsonPropertyName("spawnWeightComparedToOrigin")]
    public double SpawnWeightComparedToOrigin { get; set; } = 1;

    [JsonPropertyName("MaxResource")]
    public int? MaxResource { get; set; }

    [JsonPropertyName("BackgroundColor")]
    public string? BackgroundColor { get; set; }

    [JsonPropertyName("medUseTime")]
    public double? MedUseTime { get; set; }

    [JsonPropertyName("effects_health")]
    public Dictionary<HealthFactor, EffectsHealthProperties>? EffectsHealth { get; set; }

    [JsonPropertyName("effects_damage")]
    public Dictionary<DamageEffectType, EffectsDamageProperties>? EffectsDamage { get; set; }

    [JsonPropertyName("Buffs")]
    public List<Buff>? Buffs { get; set; }

    [JsonPropertyName("locales")]
    public Dictionary<string, LocaleDetails> Locales { get; set; } = new();

    [JsonPropertyName("trader")]
    public TraderSaleConfig? Trader { get; set; }

    [JsonPropertyName("craft")]
    public HideoutProduction? Craft { get; set; }
}

/// <summary>Optional block describing how the item is sold by a trader.</summary>
public record TraderSaleConfig
{
    [JsonPropertyName("traderId")]
    public MongoId TraderId { get; set; }

    [JsonPropertyName("loyaltyReq")]
    public int LoyaltyReq { get; set; }

    [JsonPropertyName("price")]
    public double Price { get; set; }

    [JsonPropertyName("amountForSale")]
    public int AmountForSale { get; set; }
}
