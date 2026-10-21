using System.Text.RegularExpressions;

namespace DailyGunsmith;

/// <summary>Finds typos and impossible values in config.json (unknown item IDs etc.).</summary>
internal static class ConfigValidator
{
    public static readonly string[] SightModes =
        { "none", "collimator", "assaultScope", "scope", "specialScope", "collimatorAssaultScope", "collimatorScope", "collimatorSpecialScope" };
    public static readonly string[] LegacySightModes = { "optic", "both" };
    public static readonly string[] WeaponClassKeys = { "pistol", "smg", "assaultRifle", "marksmanRifle", "sniperRifle", "other" };
    public static readonly string[] RewardCategoryKeys = { "barter", "armor", "weapon", "ammo", "key" };

    public static List<string> Validate(ModConfig config, ItemCatalog catalog)
    {
        var warnings = new List<string>();

        void CheckIds(string section, string key, IEnumerable<string>? ids, bool weaponsOnly = false)
        {
            if (ids == null) return;
            foreach (var id in ids)
            {
                if (string.IsNullOrWhiteSpace(id)) continue;
                if (!catalog.Items.TryGetValue(id, out _))
                    warnings.Add($"{section}.{key}: unknown item ID '{id}'");
                else if (weaponsOnly && !catalog.IsDescendantOf(id, ItemCatalog.WeaponBase))
                    warnings.Add($"{section}.{key}: '{id}' is not a weapon");
            }
        }

        CheckIds("weapons", "blacklist", config.Weapons.Blacklist);
        CheckIds("weapons", "whitelist", config.Weapons.Whitelist, true);
        CheckIds("weapons", "extra", config.Weapons.Extra, true);
        CheckIds("weapons", "categoryBlacklist", config.Weapons.CategoryBlacklist);
        CheckIds("modules", "blacklist", config.Modules.Blacklist);
        CheckIds("modules", "whitelist", config.Modules.Whitelist);
        CheckIds("modules", "extra", config.Modules.Extra);
        CheckIds("modules", "categoryBlacklist", config.Modules.CategoryBlacklist);

        CheckIds("categoryGroups", "suppressor", config.CategoryGroups.Suppressor);
        CheckIds("categoryGroups", "muzzle", config.CategoryGroups.Muzzle);
        CheckIds("categoryGroups", "reflex", config.CategoryGroups.Reflex);
        CheckIds("categoryGroups", "assaultScope", config.CategoryGroups.AssaultScope);
        CheckIds("categoryGroups", "scope", config.CategoryGroups.Scope);
        CheckIds("categoryGroups", "specialScope", config.CategoryGroups.SpecialScope);
        CheckIds("categoryGroups", "foregrip", config.CategoryGroups.Foregrip);
        CheckIds("categoryGroups", "tactical", config.CategoryGroups.Tactical);

        if (!Regex.IsMatch(config.TraderId ?? "", "^[0-9a-fA-F]{24}$"))
            warnings.Add($"traderId: '{config.TraderId}' is not a valid 24-character ID, Mechanic is used instead");

        if (config.RefreshIntervalHours < 1 || config.RefreshIntervalHours > 24 * 365)
            warnings.Add($"refreshIntervalHours: {config.RefreshIntervalHours} is outside 1..8760 and will be clamped");

        if (config.WeightMarginMin < 1.0) warnings.Add($"weightMarginMin: {config.WeightMarginMin} is below 1.0 and will be raised to 1.0");
        if (config.WeightMarginMax < config.WeightMarginMin) warnings.Add($"weightMarginMax ({config.WeightMarginMax}) is lower than weightMarginMin ({config.WeightMarginMin}), weightMarginMin is used for both");
        if (config.Build.MaxDepth < 1) warnings.Add($"build.maxDepth: {config.Build.MaxDepth} is below 1 and will be treated as 1");
        if (config.Build.ChildSlotBoost < 0) warnings.Add($"build.childSlotBoost: {config.Build.ChildSlotBoost} is negative and will be treated as 0");

        foreach (var kv in config.Build.SlotWeights)
            if (kv.Value < 0) warnings.Add($"build.slotWeights.{kv.Key}: negative weight {kv.Value}");

        foreach (var t in config.Tiers)
        {
            if (t.MinMods > t.MaxMods) warnings.Add($"tiers.{t.Name}: minMods ({t.MinMods}) is greater than maxMods ({t.MaxMods})");
            if (t.QuestsPerDay < 0) warnings.Add($"tiers.{t.Name}: questsPerDay ({t.QuestsPerDay}) is negative, the tier is disabled");
            if (t.MaxLevel > 0 && t.MaxLevel < t.MinLevel) warnings.Add($"tiers.{t.Name}: maxLevel ({t.MaxLevel}) is lower than minLevel ({t.MinLevel}), the upper limit is ignored");
            if (t.Weight < 0) warnings.Add($"tiers.{t.Name}: negative weight");
            if (t.SuppressorChance < 0 || t.SuppressorChance > 1) warnings.Add($"tiers.{t.Name}: suppressorChance must be between 0 and 1");

            if (t.TacticalChance < 0 || t.TacticalChance > 1) warnings.Add($"tiers.{t.Name}: tacticalChance must be between 0 and 1");
            if (t.MuzzleChance < 0 || t.MuzzleChance > 1) warnings.Add($"tiers.{t.Name}: muzzleChance must be between 0 and 1");

            if (t.MagazineChance < 0 || t.MagazineChance > 1) warnings.Add($"tiers.{t.Name}: magazineChance must be between 0 and 1");
            foreach (var key in t.SightModeWeights.Keys)
                if (!SightModes.Contains(key) && !LegacySightModes.Contains(key)) warnings.Add($"tiers.{t.Name}.sightModeWeights: unknown key '{key}' (allowed: {string.Join(", ", SightModes)})");
            foreach (var kv in t.SightModeWeights.Concat(t.WeaponClassWeights).Concat(t.RewardCategoryWeights))
                if (kv.Value < 0) warnings.Add($"tiers.{t.Name}: negative weight for '{kv.Key}'");
            if (t.Standing < 0) warnings.Add($"tiers.{t.Name}.standing: negative value");
            foreach (var key in t.WeaponClassWeights.Keys)
                if (!WeaponClassKeys.Contains(key)) warnings.Add($"tiers.{t.Name}.weaponClassWeights: unknown key '{key}' (allowed: {string.Join(", ", WeaponClassKeys)})");
            if (t.MaxArmorClass < 0 || t.MaxArmorClass > 6) warnings.Add($"tiers.{t.Name}.maxArmorClass: {t.MaxArmorClass} is outside 0..6 (0 = automatic)");
            foreach (var key in t.RewardCategoryWeights.Keys)
                if (!RewardCategoryKeys.Contains(key)) warnings.Add($"tiers.{t.Name}.rewardCategoryWeights: unknown key '{key}' (allowed: {string.Join(", ", RewardCategoryKeys)})");
        }

        return warnings;
    }
}
