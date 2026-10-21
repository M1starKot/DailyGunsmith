namespace DailyGunsmith;

public class TierConfig
{
    public string Name { get; set; } = "Default";
    public int MinLevel { get; set; } = 1;

    /// <summary>Highest player level that can start this tier's quests (0 = no upper limit).</summary>
    public int MaxLevel { get; set; } = 0;

    /// <summary>How many quests of this tier are generated per refresh (default 1; 0 = tier disabled).</summary>
    public int QuestsPerDay { get; set; } = 1;
    public int Weight { get; set; } = 1;

    /// <summary>
    /// Weight multiplier applied per player level above MinLevel when ScaleTierWeightsByLevel is enabled.
    /// Positive values increase the tier's priority as the player levels up; negative values decrease it.
    /// </summary>
    public double LevelWeightScale { get; set; } = 0.0;

    /// <summary>
    /// Number of parts in the WHOLE required attachment tree (every part of a chain counts, e.g.
    /// receiver cover + adapter + mount + scope + collimator = 5). Random between min and max; max is a hard limit.
    /// </summary>
    public int MinMods { get; set; } = 3;
    public int MaxMods { get; set; } = 5;

    // ---- Requirements of this tier ----

    /// <summary>Chance (0..1) that the quest additionally requires a suppressor (any suppressor counts).</summary>
    public double SuppressorChance { get; set; } = 0.3;

    /// <summary>Chance (0..1) that the quest additionally requires a tactical foregrip (any foregrip counts).</summary>
    public double ForegripChance { get; set; } = 0.2;

    /// <summary>Chance (0..1) that the quest additionally requires a tactical device: flashlight, laser or combo (any counts).</summary>
    public double TacticalChance { get; set; } = 0.2;

    /// <summary>Chance (0..1) that the quest additionally requires a muzzle device: brake, compensator or flash hider (any counts, suppressors don't).</summary>
    public double MuzzleChance { get; set; } = 0.2;

    /// <summary>Chance (0..1) that the quest requires a specific magazine (a concrete magazine of the weapon is listed as a required part).</summary>
    public double MagazineChance { get; set; } = 0.25;

    /// <summary>
    /// Relative weights of the sight requirement. Only two kinds of sights can be required at the same time: a collimator and
    /// ONE magnified sight, and the magnified sights (assault scope / scope / special scope) exclude each other.
    /// Keys: none, collimator, assaultScope, scope, specialScope, collimatorAssaultScope, collimatorScope, collimatorSpecialScope.
    /// (Legacy keys: optic = assaultScope/scope, both = collimator + assaultScope/scope.)
    /// </summary>
    public Dictionary<string, double> SightModeWeights { get; set; } = new()
    {
        { "none", 2 }, { "collimator", 4 }, { "assaultScope", 1.5 }, { "scope", 1.5 }, { "specialScope", 0.5 },
        { "collimatorAssaultScope", 0.5 }, { "collimatorScope", 0.5 }, { "collimatorSpecialScope", 0.25 }
    };

    /// <summary>
    /// Relative weights of the weapon classes for this tier. Keys: pistol, smg, assaultRifle, marksmanRifle,
    /// sniperRifle, other. Classes with a low weight still appear, just less often. Missing keys use "other".
    /// </summary>
    public Dictionary<string, double> WeaponClassWeights { get; set; } = new()
    {
        { "pistol", 1 }, { "smg", 2 }, { "assaultRifle", 6 }, { "marksmanRifle", 6 },
        { "sniperRifle", 2 }, { "other", 1 }
    };

    // ---- Rewards of this tier ----

    /// <summary>Maximum armor class (including inserted armor plates) allowed for armor/helmet rewards on this tier (1..6).</summary>
    public int MaxArmorClass { get; set; } = 5;

    /// <summary>Relative weights of the loot reward categories: barter, armor, weapon, ammo, key.</summary>
    public Dictionary<string, double> RewardCategoryWeights { get; set; } = new()
    {
        { "barter", 6 }, { "armor", 2 }, { "weapon", 2 }, { "ammo", 2 }, { "key", 1 }
    };

    /// <summary>Multiplier applied to this tier's roubles and EXP (and to the value of the reward items).</summary>
    public double RewardMultiplier { get; set; } = 1.0;

    /// <summary>Base roubles reward before multipliers (0 = automatic, derived from minLevel).</summary>
    public int Roubles { get; set; } = 0;

    /// <summary>Base EXP reward before multipliers (0 = automatic, derived from minLevel).</summary>
    public int Experience { get; set; } = 0;

    /// <summary>Trader standing (reputation) reward. Not affected by multipliers.</summary>
    public double Standing { get; set; } = 0.02;
}
