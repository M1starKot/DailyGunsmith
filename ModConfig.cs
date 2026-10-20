namespace DailyGunsmith;

/// <summary>Filter rules for one kind of item (weapons or weapon modules/attachments).</summary>
public class ItemFilterConfig
{
    /// <summary>false = items added by other mods are never used (only vanilla items).</summary>
    public bool IncludeModded { get; set; } = true;

    /// <summary>Template IDs that are never used. Always wins over everything else.</summary>
    public List<string> Blacklist { get; set; } = new();

    /// <summary>If not empty, ONLY these template IDs are allowed (plus anything in "extra").</summary>
    public List<string> Whitelist { get; set; } = new();

    /// <summary>Template IDs that are always allowed, even without a handbook entry or inside a blacklisted category.</summary>
    public List<string> Extra { get; set; } = new();

    /// <summary>Base-class IDs whose whole subtree is skipped.</summary>
    public List<string> CategoryBlacklist { get; set; } = new();
}

/// <summary>Controls how the required attachment tree of a quest is built.</summary>
public class BuildConfig
{
    /// <summary>How deep attachments may be nested (receiver cover -> adapter -> mount -> scope -> collimator = 5).</summary>
    public int MaxDepth { get; set; } = 6;

    /// <summary>Weight multiplier for slots of already placed parts - higher values give longer attachment chains.</summary>
    public double ChildSlotBoost { get; set; } = 1.5;

    /// <summary>Weight of slots that match none of the keys below. 0 = such slots are never used.</summary>
    public double DefaultSlotWeight { get; set; } = 1;

    /// <summary>
    /// Priority of slots: when a slot name contains the key (case-insensitive), the slot gets this weight.
    /// The higher the weight, the more often parts of that slot are required. 0 disables a slot type.
    /// </summary>
    public Dictionary<string, double> SlotWeights { get; set; } = new()
    {
        { "scope", 6 }, { "sight", 6 }, { "handguard", 6 }, { "grip", 6 }, { "muzzle", 5 },
        { "mount", 3 }, { "reciever", 3 }, { "receiver", 3 },
        { "stock", 2 }, { "tactical", 2 }, { "magazine", 2 }, { "barrel", 2 }
    };
}

/// <summary>
/// Extended category groups for the "must contain an item of this category" requirements.
/// A requirement such as "reflex sight" is met by an item of ANY base class listed in its group
/// (e.g. reflex sight = collimators + compact collimators, which also covers holographic sights).
/// </summary>
public class CategoryGroupsConfig
{
    /// <summary>Suppressor group: base-class IDs, an item of any of them counts.</summary>
    public List<string> Suppressor { get; set; } = new() { "550aa4cd4bdc2dd8348b456c" };

    /// <summary>Reflex sight group: collimators (incl. holographic sights) + compact collimators (e.g. PK-06).</summary>
    public List<string> Reflex { get; set; } = new() { "55818ad54bdc2ddc698b4569", "55818acf4bdc2dde698b456b" };

    /// <summary>Tactical foregrips (vertical/angled foregrips); bipods are not included.</summary>
    public List<string> Foregrip { get; set; } = new() { "55818af64bdc2d5b648b4570" };

    /// <summary>Tactical devices: flashlights, laser designators and combined tactical devices.</summary>
    public List<string> Tactical { get; set; } = new() { "55818b084bdc2d5b648b4571", "55818b0e4bdc2dde698b456e", "55818b164bdc2ddc698b456c" };

    /// <summary>Assault scope group (low-magnification combat scopes).</summary>
    public List<string> AssaultScope { get; set; } = new() { "55818add4bdc2d5b648b456f" };

    /// <summary>Scope group (regular magnified scopes).</summary>
    public List<string> Scope { get; set; } = new() { "55818ae44bdc2dde698b456c" };

    /// <summary>Special scope group (night vision, thermal and other special purpose sights).</summary>
    public List<string> SpecialScope { get; set; } = new() { "55818aeb4bdc2ddc698b456a" };

    /// <summary>
    /// true  = the quest lists every base class of the group (any of them satisfies the quest).
    /// false = the quest lists only the base class of the part the generator placed (old behaviour, strict category).
    /// </summary>
    public bool EmitAllMembers { get; set; } = true;

    /// <summary>
    /// true = groups are extended automatically from the item database:
    /// suppressor: every item category (also from mods) whose name contains "silencer"/"suppressor";
    /// assaultScope / scope / specialScope: the class of every sight with magnification (Zooms above 1x) that is in none of the groups
    /// (night vision / thermal / special classes go to specialScope, classes named "assault" to assaultScope, the rest to scope);
    /// reflex: every sight class named collimator/reflex/holo (incl. micro collimators);
    /// foregrip / tactical: classes named foregrip / flashlight, laser, tactical.
    /// </summary>
    public bool AutoDetect { get; set; } = true;
}

public class DryRunConfig
{
    /// <summary>Write a report of the generated quests (plus extra samples) to disk. For testing and feedback.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>How many extra sample quests (spread evenly over the tiers) are generated for the report. They are NOT registered in the game.</summary>
    public int SampleCount { get; set; } = 30;

    /// <summary>Output folder (relative to the mod folder).</summary>
    public string Folder { get; set; } = "dryrun";
}

public class ModConfig
{
    public bool Enabled { get; set; } = true;
    public bool UseUtc { get; set; } = false;

    /// <summary>How often the quests are regenerated, in whole hours (1..8760). 24 = once a day at midnight.</summary>
    public int RefreshIntervalHours { get; set; } = 24;
    public bool DeterministicPerDay { get; set; } = true;
    public string SeedSalt { get; set; } = "daily-gunsmith";
    public bool RefreshLoadedLocales { get; set; } = true;

    /// <summary>
    /// When true, dynamically scales tier weights based on the player's current PMC level (detected from SPT profiles),
    /// giving higher priority to harder tiers as the player levels up.
    /// </summary>
    public bool ScaleTierWeightsByLevel { get; set; } = true;


    /// <summary>Quest giver and trader whose standing is rewarded. Default: Mechanic.</summary>
    public string TraderId { get; set; } = "5a7c2eca46aef81a7ca2145d";

    /// <summary>Multiplier for the roubles/EXP rewards of ALL tiers (on top of each tier's own rewardMultiplier).</summary>
    public double GlobalRewardMultiplier { get; set; } = 1.0;

    /// <summary>Only use items that have a handbook price (skips dev/unobtainable items).</summary>
    public bool RequireHandbookEntry { get; set; } = true;

    /// <summary>A weapon needs at least this many attachment slots to be picked.</summary>
    public int MinWeaponSlots { get; set; } = 3;

    /// <summary>Optional override: path to the vanilla items.json (used to tell vanilla items from modded ones).</summary>
    public string VanillaItemsPath { get; set; } = "";

    public ItemFilterConfig Weapons { get; set; } = new()
    {
        CategoryBlacklist = new List<string> { "5447bedf4bdc2d87278b4568", "5447bee84bdc2dc3278b4569" }
    };

    public ItemFilterConfig Modules { get; set; } = new();

    public BuildConfig Build { get; set; } = new();

    public CategoryGroupsConfig CategoryGroups { get; set; } = new();

    /// <summary>The weight limit is the weight of the lightest working build plus the required parts, times a random factor between these two values.</summary>
    public double WeightMarginMin { get; set; } = 1.15;
    public double WeightMarginMax { get; set; } = 1.35;

    public DryRunConfig DryRun { get; set; } = new();

    public List<TierConfig> Tiers { get; set; } = new();
}
