using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;

namespace DailyGunsmith
{
    /// <summary>One part of an attachment tree (for the report). ParentIndex -1 = attached to the weapon / root item.</summary>
    public class PartRecord
    {
        public string Tpl { get; set; } = string.Empty;
        public int ParentIndex { get; set; } = -1;
        public string SlotName { get; set; } = string.Empty;
        public int Depth { get; set; }
        /// <summary>false = not listed in containsItems (the part only proves that a category requirement is achievable).</summary>
        public bool Listed { get; set; } = true;
        public string? ProofCategory { get; set; }
    }

    public class LootRecord
    {
        public string Tpl { get; set; } = string.Empty;
        public int Count { get; set; } = 1;
        public List<PartRecord> Parts { get; set; } = new List<PartRecord>();
    }

    public class GeneratedQuest
    {
        public string Id { get; set; } = string.Empty;
        public string CondId { get; set; } = string.Empty;
        public string Json { get; set; } = string.Empty;
        public string JsonWithoutStats { get; set; } = string.Empty;
        public string LegacyJson { get; set; } = string.Empty;
        public string WeaponTpl { get; set; } = string.Empty;
        public string WeaponClass { get; set; } = string.Empty;
        public string TierName { get; set; } = string.Empty;
        public int MinLevel { get; set; }
        /// <summary>Parts listed in the quest's containsItems.</summary>
        public List<string> ModTpls { get; set; } = new List<string>();
        /// <summary>Category IDs of which the weapon must contain any item (hasItemFromCategory).</summary>
        public List<string> Categories { get; set; } = new List<string>();
        public List<PartRecord> Parts { get; set; } = new List<PartRecord>();
        public float MinErgonomics { get; set; }
        public float MaxRecoil { get; set; }
        public double MaxWeight { get; set; }
        public int ExpReward { get; set; }
        public int RoublesReward { get; set; }
        public string Standing { get; set; } = string.Empty;
        public List<LootRecord> LootRewards { get; set; } = new List<LootRecord>();
        /// <summary>Result of the internal hand-in simulation ("ok" or the list of problems).</summary>
        public string HandInCheck { get; set; } = string.Empty;
    }

    public class QuestGenerator
    {
        private readonly Random _random;
        private readonly string[] _icons;
        private readonly List<WeaponEntry> _weaponPool = new List<WeaponEntry>();
        private readonly PartAssembler _weightAssembler;
        private readonly PartAssembler _rewardAssembler;
        private const string MountBase = "55818b224bdc2dde698b456f";
        private readonly Dictionary<string, List<SlotOptions>> _childSlotCache = new Dictionary<string, List<SlotOptions>>();
        private const string MechanicId = "5a7c2eca46aef81a7ca2145d";
        private readonly string _traderId;
        private readonly ModConfig _config;
        private readonly ItemCatalog _catalog;
        private readonly JsonObject? _templateQuest;
        private readonly JsonObject? _templateAssembly;
        private readonly Dictionary<string, bool> _moduleCache = new Dictionary<string, bool>();
        private HashSet<string> _moduleBlacklist = new HashSet<string>();
        private HashSet<string> _moduleWhitelist = new HashSet<string>();
        private HashSet<string> _moduleExtra = new HashSet<string>();

        /// <summary>English fallback texts (used when locales/*.json is missing or lacks a key).</summary>
        public static readonly Dictionary<string, string> DefaultTexts = new Dictionary<string, string>
        {
            { "title", "Daily Gunsmith: {0}" },
            { "description", "Assemble a {0}. Minimum durability: 80%. Minimum ergonomics: {1}. Maximum recoil sum: {2}. Maximum weight: {3} kg. Required modules: {4}. Required categories: {5}." },
            { "noModules", "none" },
            { "noCategories", "none" },
            { "success", "Great build, exactly what the client asked for. Here is your payment." },
            { "accept", "I'll take the job." },
            { "decline", "Not now." },
            { "complete", "The weapon is ready, take it." },
            { "condition", "Assemble {0}" }
        };

        public int WeaponPoolSize => _weaponPool.Count;

        private static readonly string[] GunsmithIcons = new string[]
        {
            "/files/quest/icon/596b455186f77457cb50eccb.jpg",
            "/files/quest/icon/5ac242ab86f77412464f68b4.jpg",
            "/files/quest/icon/5ac2428686f77412450b42bf.jpg",
            "/files/quest/icon/5ac2426c86f774138762edfe.jpg",
            "/files/quest/icon/5ae4a7a786f7744537398b62.jpg",
            "/files/quest/icon/5ae4a7b886f77445383e888d.jpg",
            "/files/quest/icon/5ae4a7c886f77444fd07c646.jpg",
            "/files/quest/icon/5ae4a7d986f774453a46e2b0.jpg",
            "/files/quest/icon/5ae4a7e886f7744537398b66.jpg",
            "/files/quest/icon/5b478ff486f7744d184ecbbf.jpg",
            "/files/quest/icon/5b47926a86f7744d1b23c521.jpg",
            "/files/quest/icon/5b4794cb86f7744d184ecbc3.jpg",
            "/files/quest/icon/5b4795fb86f7744d1a094f82.jpg"
        };

        // Базовые классы предметов, из которых разрешено формировать награды-предметы.
        private const string BarterItemBase = "5448eb774bdc2d0a728b4567";
        private const string JewelryBase = "57864a3d24597754843f8721";
        private const string ArmorBase = "5448e54d4bdc2dcc718b4568";
        private const string HeadwearBase = "5a341c4086f77401f2541505";
        private const string AmmoBase = "5485a8684bdc2da71d8b4567";
        private const string KeyBase = "543be5e94bdc2df1348b4568";
        private const string GrenadeLauncherBase = "5447bedf4bdc2d87278b4568";
        private const string SpecialWeaponBase = "5447bee84bdc2dc3278b4569";

        private static readonly HashSet<string> ForbiddenCalibers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Caliber86x70",
            "Caliber127x55",
            "Caliber127x99",
            "Caliber127x108"
        };

        private static bool HasForbiddenCaliber(ItemInfo it) =>
            !string.IsNullOrWhiteSpace(it.AmmoCaliber) && ForbiddenCalibers.Contains(it.AmmoCaliber);

        private sealed class RewardCandidate
        {
            public string Tpl = "";
            public double Price;
            public int StackMax = 1;
            public int BaseArmorClass;
            public int MinEffectiveArmorClass;
            public int MaxEffectiveArmorClass;
        }

        private sealed class LootReward
        {
            public string Tpl = "";
            public int Count = 1;
            /// <summary>Complete assembled item (weapons, armor) - null for plain items.</summary>
            public List<AssembledPart>? Parts;
        }

        // категория -> отсортированный по цене список допустимых предметов
        private readonly Dictionary<string, List<RewardCandidate>> _rewardPools = new Dictionary<string, List<RewardCandidate>>();

        /// <summary>Number of eligible items per reward category (barter, armor, weapon, ammo, key).</summary>
        public Dictionary<string, int> RewardPoolSizes { get; } = new Dictionary<string, int>();

        public class SlotOptions
        {
            public string Name { get; set; } = string.Empty;
            public List<string> Candidates { get; set; } = new List<string>();
        }

        public class WeaponEntry
        {
            public string TemplateId { get; set; } = string.Empty;
            public float BaseErgonomics { get; set; } = 50f;
            public float BaseRecoil { get; set; } = 250f;
            public string ClassKey { get; set; } = "other";
            /// <summary>Weight of the lightest working build (all mandatory parts, lightest choice).</summary>
            public double MinimalWeight { get; set; }
            public List<SlotOptions> Slots { get; set; } = new List<SlotOptions>();
        }

        public class ModEntry
        {
            public string TemplateId { get; set; } = string.Empty;
        }

        public class GunsmithRequirements
        {
            public float MinErgonomics { get; set; }
            public float MaxRecoilSum { get; set; }
            public double MaxWeight { get; set; }
            public List<ModEntry> RequiredMods { get; set; } = new List<ModEntry>();
            public List<string> Categories { get; set; } = new List<string>();
            public List<PartRecord> Parts { get; set; } = new List<PartRecord>();
            /// <summary>True if two parts of the tree (or of the completed weapon) conflict with each other - such a build is never used.</summary>
            public bool HasConflict { get; set; }
            /// <summary>Stats of the generated (witness) build, used to verify that it satisfies the limits.</summary>
            public double WitnessWeight { get; set; }
            public float WitnessErgonomics { get; set; }
            public float WitnessRecoil { get; set; }
        }

        internal QuestGenerator(ModConfig config, ItemCatalog catalog, Random rng, List<string>? icons, JsonObject? templateQuest = null, JsonObject? templateAssembly = null)
        {
            _config = config;
            _traderId = System.Text.RegularExpressions.Regex.IsMatch(config.TraderId ?? "", "^[0-9a-fA-F]{24}$")
                ? config.TraderId!.ToLowerInvariant()
                : MechanicId;
            _catalog = catalog;
            _templateQuest = templateQuest;
            _templateAssembly = templateAssembly;
            _random = rng;
            _icons = icons != null && icons.Count > 0 ? icons.ToArray() : GunsmithIcons;
            _moduleBlacklist = new HashSet<string>(config.Modules.Blacklist ?? new List<string>());
            _moduleWhitelist = new HashSet<string>(config.Modules.Whitelist ?? new List<string>());
            _moduleExtra = new HashSet<string>(config.Modules.Extra ?? new List<string>());
            _weightAssembler = new PartAssembler(catalog, new HashSet<string>(config.Modules.Blacklist ?? new List<string>()));
            _rewardAssembler = new PartAssembler(catalog, new HashSet<string>(config.Modules.Blacklist ?? new List<string>()));
            _groupIds = BuildCategoryGroups(config, catalog, CategoryGroupInfo);
            BuildWeaponPool();
            BuildRewardPools(catalog);
        }

        private readonly Dictionary<string, List<string>> _groupIds;

        /// <summary>Human-readable lines about the category groups (incl. automatically detected classes) for the log.</summary>
        public List<string> CategoryGroupInfo { get; } = new List<string>();

        private const string SightsBase = "5448fe7a4bdc2d6f028b456b";
        private const string IronSightBase = "55818ac54bdc2d5b648b456e";
        private const string AssaultScopeBase = "55818add4bdc2d5b648b456f";
        private const string ScopeBase = "55818ae44bdc2dde698b456c";
        private const string SpecialScopeBase = "55818aeb4bdc2ddc698b456a";

        /// <summary>Group keys of the sight types. A collimator ("reflex") may be combined with ONE of the magnified types, which exclude each other.</summary>
        private static readonly string[] MagnifiedSightGroups = { "assaultScope", "scope", "specialScope" };

        /// <summary>True for the "Special scope" class and everything like it (night vision / thermal / NVG devices, also from mods).</summary>
        private static bool IsSpecialSightClass(ItemCatalog catalog, string id)
        {
            if (catalog.IsDescendantOf(id, SpecialScopeBase)) return true;
            var current = id;
            for (var i = 0; i < 25 && current != ""; i++)
            {
                if (!catalog.Items.TryGetValue(current, out var it)) return false;
                var n = it.FallbackName ?? "";
                bool isClass = !string.Equals(it.Type, "Item", StringComparison.OrdinalIgnoreCase);
                // class names are matched broadly; item names only by "nvg" (real scopes such as "Nightforce" must stay allowed)
                if (n.IndexOf("nvg", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (isClass && (n.IndexOf("special", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("night", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("thermal", StringComparison.OrdinalIgnoreCase) >= 0)) return true;
                current = it.Parent;
            }
            return false;
        }

        private readonly Dictionary<string, string?> _sightKindCache = new Dictionary<string, string?>();

        /// <summary>
        /// Sight kind of an item: "reflex", "assaultScope", "scope", "specialScope", or null when it is not one of those sights.
        /// </summary>
        private string? SightKind(string id)
        {
            if (_sightKindCache.TryGetValue(id, out var cached)) return cached;
            string? kind = null;
            foreach (var key in new[] { "assaultScope", "scope", "specialScope", "reflex" })
                if (_groupIds.TryGetValue(key, out var ids) && _catalog.IsDescendantOfAny(id, ids)) { kind = key; break; }
            _sightKindCache[id] = kind;
            return kind;
        }

        private readonly Dictionary<string, bool> _bannedSightCache = new Dictionary<string, bool>();

        /// <summary>Sights that must never appear in a generated build: any sight (or night-vision style device) that belongs to none of the four sight types, except iron sights.</summary>
        private bool IsBannedSight(string id)
        {
            if (_bannedSightCache.TryGetValue(id, out var cached)) return cached;
            bool banned = false;
            if (SightKind(id) == null && !_catalog.IsDescendantOf(id, IronSightBase))
                banned = _catalog.IsDescendantOf(id, SightsBase) || IsSpecialSightClass(_catalog, id);
            _bannedSightCache[id] = banned;
            return banned;
        }

        // sight requirement modes: (collimator required, magnified sight group required)
        private static readonly Dictionary<string, (bool reflex, string? magnified)> SightModeDefs = new Dictionary<string, (bool, string?)>
        {
            ["none"] = (false, null),
            ["collimator"] = (true, null),
            ["assaultScope"] = (false, "assaultScope"),
            ["scope"] = (false, "scope"),
            ["specialScope"] = (false, "specialScope"),
            ["collimatorAssaultScope"] = (true, "assaultScope"),
            ["collimatorScope"] = (true, "scope"),
            ["collimatorSpecialScope"] = (true, "specialScope"),
        };

        private static Dictionary<string, List<string>> BuildCategoryGroups(ModConfig config, ItemCatalog catalog, List<string> info)
        {
            static List<string> Clean(List<string>? l) =>
                (l ?? new List<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();

            var g = new Dictionary<string, List<string>>
            {
                ["suppressor"] = Clean(config.CategoryGroups.Suppressor),
                ["reflex"] = Clean(config.CategoryGroups.Reflex),
                ["assaultScope"] = Clean(config.CategoryGroups.AssaultScope),
                ["scope"] = Clean(config.CategoryGroups.Scope),
                ["specialScope"] = Clean(config.CategoryGroups.SpecialScope),
                ["foregrip"] = Clean(config.CategoryGroups.Foregrip),
                ["tactical"] = Clean(config.CategoryGroups.Tactical)
            };

            if (config.CategoryGroups.AutoDetect)
            {
                // suppressor classes added by mods (or other classes) are found by the class name
                foreach (var it in catalog.Items.Values)
                {
                    if (string.Equals(it.Type, "Item", StringComparison.OrdinalIgnoreCase) || it.Id.Length != 24) continue;
                    if (!catalog.IsDescendantOf(it.Id, ItemCatalog.ModBase)) continue;
                    var n = it.FallbackName ?? "";
                    if (n.IndexOf("silenc", StringComparison.OrdinalIgnoreCase) < 0 &&
                        n.IndexOf("suppress", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (catalog.IsDescendantOfAny(it.Id, g["suppressor"])) continue;
                    g["suppressor"].Add(it.Id);
                    info.Add($"suppressor group: added class '{n}' [{it.Id}] (auto-detected)");
                }

                // classes added by mods are found by the class name
                void AddByName(string group, string[] words, string baseId)
                {
                    foreach (var it in catalog.Items.Values)
                    {
                        if (string.Equals(it.Type, "Item", StringComparison.OrdinalIgnoreCase) || it.Id.Length != 24) continue;
                        if (!catalog.IsDescendantOf(it.Id, baseId)) continue;
                        var n = it.FallbackName ?? "";
                        if (!words.Any(w => n.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                        if (catalog.IsDescendantOfAny(it.Id, g[group])) continue;
                        if (g.Where(kv => kv.Key != group).Any(kv => kv.Value.Contains(it.Id))) continue; // already belongs to another group
                        g[group].Add(it.Id);
                        info.Add($"{group} group: added class '{n}' [{it.Id}] (auto-detected)");
                    }
                }
                AddByName("reflex", new[] { "collimator", "reflex", "holo" }, SightsBase);
                AddByName("foregrip", new[] { "foregrip" }, ItemCatalog.ModBase);
                AddByName("tactical", new[] { "flashlight", "laser", "tactical" }, ItemCatalog.ModBase);

                // every sight with magnification belongs to one of the magnified sight groups: add the class it lives in
                foreach (var it in catalog.Items.Values)
                {
                    if (it.Type != "Item" || it.MaxZoom <= 1.0001) continue;
                    if (!catalog.IsDescendantOf(it.Id, SightsBase)) continue;
                    if (MagnifiedSightGroups.Any(k => catalog.IsDescendantOfAny(it.Id, g[k]))) continue;
                    if (catalog.IsDescendantOfAny(it.Id, g["reflex"])) continue;
                    if (catalog.IsDescendantOf(it.Id, IronSightBase)) continue;
                    if (it.Parent == "" || it.Parent == SightsBase) continue;
                    var cn = catalog.Items.TryGetValue(it.Parent, out var pc) ? pc.FallbackName ?? "?" : "?";
                    string key = IsSpecialSightClass(catalog, it.Id) ? "specialScope"
                        : cn.IndexOf("assault", StringComparison.OrdinalIgnoreCase) >= 0 ? "assaultScope" : "scope";
                    g[key].Add(it.Parent);
                    info.Add($"{key} group: added class '{cn}' [{it.Parent}] (contains magnified sight {it.FallbackName})");
                }
            }

            return g;
        }

        private static bool IsPlayerObtainable(ItemInfo it, ItemCatalog catalog, out double price)
        {
            price = 0;
            if (it.Id.Length != 24 || it.Type != "Item" || it.QuestItem || it.Unlootable) return false;
            // предмет должен иметь цену в справочнике - иначе он недоступен игроку (служебный/dev/квестовый)
            return catalog.Prices.TryGetValue(it.Id, out price) && price > 0;
        }

        private void BuildRewardPools(ItemCatalog catalog)
        {
            var pools = new Dictionary<string, List<RewardCandidate>>
            {
                { "barter", new List<RewardCandidate>() },
                { "armor", new List<RewardCandidate>() },
                { "weapon", new List<RewardCandidate>() },
                { "ammo", new List<RewardCandidate>() },
                { "key", new List<RewardCandidate>() }
            };

            foreach (var it in catalog.Items.Values)
            {
                if (!IsPlayerObtainable(it, catalog, out var price)) continue;
                if (HasForbiddenCaliber(it)) continue;

                string? category = null;
                if (catalog.IsDescendantOf(it.Id, BarterItemBase) || catalog.IsDescendantOf(it.Id, JewelryBase)) category = "barter";
                else if (catalog.IsDescendantOf(it.Id, ArmorBase)) category = "armor";
                else if (catalog.IsDescendantOf(it.Id, HeadwearBase) && it.ArmorClass > 0) category = "armor";
                else if (catalog.IsDescendantOf(it.Id, ItemCatalog.WeaponBase)
                         && !catalog.IsDescendantOf(it.Id, GrenadeLauncherBase)
                         && !catalog.IsDescendantOf(it.Id, SpecialWeaponBase)) category = "weapon";
                else if (catalog.IsDescendantOf(it.Id, AmmoBase)) category = "ammo";
                else if (catalog.IsDescendantOf(it.Id, KeyBase)) category = "key";

                if (category == null) continue;

                int baseArmor = (int)Math.Round(it.ArmorClass);
                int minEffArmor = baseArmor;
                int maxEffArmor = baseArmor;

                // Weapons and armor are only given fully assembled (all mandatory parts / all plates), vanilla and modded alike.
                // Items that cannot be assembled completely are never offered as a reward.
                if (category == "weapon" || category == "armor")
                {
                    var assembled = _rewardAssembler.Assemble(it.Id, category == "armor", null, false);
                    if (assembled == null || (category == "weapon" && assembled.Count < 2)) continue;

                    if (category == "armor")
                    {
                        // Determine minimum and maximum achievable armor class (with plates) for tier filtering
                        int foundMin = 0, foundMax = 0;
                        for (int ac = 1; ac <= 6; ac++)
                        {
                            var test = _rewardAssembler.Assemble(it.Id, true, null, false, ac);
                            if (test != null)
                            {
                                int eff = PartAssembler.EffectiveArmorClass(catalog, test);
                                if (foundMin == 0 || eff < foundMin) foundMin = eff;
                                if (eff > foundMax) foundMax = eff;
                            }
                        }
                        minEffArmor = foundMin > 0 ? foundMin : PartAssembler.EffectiveArmorClass(catalog, assembled);
                        maxEffArmor = foundMax > 0 ? foundMax : minEffArmor;
                    }
                }

                pools[category].Add(new RewardCandidate
                {
                    Tpl = it.Id,
                    Price = price,
                    StackMax = it.StackMaxSize,
                    BaseArmorClass = baseArmor,
                    MinEffectiveArmorClass = minEffArmor,
                    MaxEffectiveArmorClass = maxEffArmor
                });
            }

            foreach (var kv in pools)
            {
                RewardPoolSizes[kv.Key] = kv.Value.Count;
                if (kv.Value.Count == 0) continue;
                kv.Value.Sort((x, y) => x.Price.CompareTo(y.Price));
                _rewardPools[kv.Key] = kv.Value;
            }
        }

        private string? PickRewardCategory(TierConfig? tier, HashSet<string> usedCategories, int maxArmorClass, Random rng)
        {
            var available = _rewardPools.Keys
                .Where(k => !usedCategories.Contains(k))
                .Where(k => k != "armor" || _rewardPools[k].Any(c => c.MinEffectiveArmorClass <= maxArmorClass))
                .ToList();
            if (available.Count == 0) return null;

            double WeightOf(string key)
            {
                if (tier?.RewardCategoryWeights != null && tier.RewardCategoryWeights.TryGetValue(key, out var w))
                    return Math.Max(0, w);
                return key == "barter" ? 6 : 1;
            }

            double total = available.Sum(WeightOf);
            if (total <= 0) return available[rng.Next(available.Count)];

            double roll = rng.NextDouble() * total;
            foreach (var k in available)
            {
                roll -= WeightOf(k);
                if (roll <= 0) return k;
            }
            return available[available.Count - 1];
        }

        // Подбирает 1-2 предмета-награды с учётом тира (ограничение и приоритет класса брони до 4/5/6, повышенный шанс и стаки бартера).
        private List<LootReward> PickLootRewards(Random rng, TierConfig? tier, double targetValue)
        {
            var result = new List<LootReward>();
            if (_rewardPools.Count == 0) return result;

            int maxArmorClass = tier?.MaxArmorClass > 0 ? tier.MaxArmorClass : 5;
            string tierName = (tier?.Name ?? "").ToLowerInvariant();
            if (tier != null && tier.MaxArmorClass <= 0)
            {
                if (tierName.Contains("easy")) maxArmorClass = 4;
                else if (tierName.Contains("hard")) maxArmorClass = 6;
                else maxArmorClass = 5;
            }

            // Tier scaling for number of reward slots: Easy = 1-2, Medium = 2, Hard = 2-3
            int maxEntries = tierName.Contains("hard") ? rng.Next(2, 4) : (tierName.Contains("medium") ? 2 : rng.Next(1, 3));
            int entries = Math.Min(maxEntries, _rewardPools.Count);
            double perEntry = Math.Max(1000, targetValue / entries);

            var usedCategories = new HashSet<string>();
            for (int i = 0; i < entries; i++)
            {
                // Allow "barter" to be picked more than once when barter has the highest weight
                var blocked = new HashSet<string>(usedCategories.Where(c => c != "barter"));
                string? cat = PickRewardCategory(tier, blocked, maxArmorClass, rng);
                if (cat == null) break;
                usedCategories.Add(cat);

                var pool = _rewardPools[cat];
                RewardCandidate pick;
                List<AssembledPart>? parts = null;

                if (cat == "armor")
                {
                    // Strictly cap armor class by tier's maxArmorClass (Easy <= 4, Medium <= 5, Hard <= 6)
                    var eligibleArmor = pool.Where(c => c.MinEffectiveArmorClass > 0 && c.MinEffectiveArmorClass <= maxArmorClass).ToList();
                    if (eligibleArmor.Count == 0) continue;

                    // Weight armor candidates toward the tier's maxArmorClass (e.g. class 4 on Easy, 5 on Medium, 6 on Hard)
                    double ArmorWeight(RewardCandidate c)
                    {
                        int achievable = Math.Min(c.MaxEffectiveArmorClass, maxArmorClass);
                        int diff = Math.Max(0, maxArmorClass - achievable);
                        double classPriority = diff == 0 ? 6.0 : (diff == 1 ? 2.0 : 0.5);
                        bool priceFits = c.Price >= perEntry * 0.25 && c.Price <= perEntry * 2.0;
                        return classPriority * (priceFits ? 2.5 : 1.0);
                    }

                    double totalW = eligibleArmor.Sum(ArmorWeight);
                    double roll = rng.NextDouble() * totalW;
                    pick = eligibleArmor[eligibleArmor.Count - 1];
                    foreach (var cand in eligibleArmor)
                    {
                        roll -= ArmorWeight(cand);
                        if (roll <= 0) { pick = cand; break; }
                    }

                    parts = _rewardAssembler.Assemble(pick.Tpl, true, rng, false, maxArmorClass)
                            ?? _rewardAssembler.Assemble(pick.Tpl, true, null, false, maxArmorClass);
                    if (parts == null) continue;
                }
                else
                {
                    var fitting = pool.Where(c => c.Price >= perEntry * 0.25 && c.Price <= perEntry * 1.5).ToList();
                    if (fitting.Count > 0) pick = fitting[rng.Next(fitting.Count)];
                    else pick = pool.OrderBy(c => Math.Abs(c.Price - perEntry)).First();

                    if (cat == "weapon")
                    {
                        parts = _rewardAssembler.Assemble(pick.Tpl, false, rng, false)
                                ?? _rewardAssembler.Assemble(pick.Tpl, false, null, false);
                        if (parts == null) continue;
                    }
                }

                int count = 1;
                if (cat == "barter")
                {
                    // Allow multiple copies of lower-priced barter items to match the tier's target value (up to 5 per slot)
                    int maxBarterCount = tierName.Contains("hard") ? 5 : (tierName.Contains("medium") ? 4 : 3);
                    count = (int)Math.Clamp(Math.Round(perEntry / Math.Max(1, pick.Price)), 1, maxBarterCount);
                }
                else if (pick.StackMax > 1)
                {
                    count = (int)Math.Clamp(Math.Round(perEntry / Math.Max(1, pick.Price)), 1, pick.StackMax);
                }

                result.Add(new LootReward { Tpl = pick.Tpl, Count = count, Parts = parts });
            }
            return result;
        }

        private string BuildLootRewardJson(LootReward r, int index, string rewardId, string itemId, Random rng)
        {
            var sb = new StringBuilder();
            sb.Append("      {\n");
            sb.Append("        \"id\": \"" + rewardId + "\",\n");
            sb.Append("        \"type\": \"Item\",\n");
            sb.Append("        \"index\": " + index + ",\n");
            sb.Append("        \"target\": \"" + itemId + "\",\n");
            sb.Append("        \"value\": " + r.Count + ",\n");
            sb.Append("        \"findInRaid\": true,\n");
            sb.Append("        \"unknown\": false,\n");
            sb.Append("        \"items\": [\n");

            if (r.Parts == null || r.Parts.Count == 0)
            {
                sb.Append("          { \"_id\": \"" + itemId + "\", \"_tpl\": \"" + r.Tpl + "\", \"upd\": { \"StackObjectsCount\": " + r.Count + " } }\n");
            }
            else
            {
                // root item keeps the id the reward points to; every other part gets its own id and sits in its parent's slot
                var ids = new List<string> { itemId };
                for (int i = 1; i < r.Parts.Count; i++) ids.Add(NewMongoId(rng));

                for (int i = 0; i < r.Parts.Count; i++)
                {
                    var p = r.Parts[i];
                    var line = new StringBuilder();
                    line.Append("          { \"_id\": \"" + ids[i] + "\", \"_tpl\": \"" + p.Tpl + "\"");
                    if (i > 0) line.Append(", \"parentId\": \"" + ids[p.ParentIndex] + "\", \"slotId\": \"" + p.SlotName + "\"");
                    line.Append(", \"upd\": { \"StackObjectsCount\": 1" + RepairableJson(p.Tpl) + " } }");
                    sb.Append(line);
                    sb.Append(i < r.Parts.Count - 1 ? ",\n" : "\n");
                }
            }

            sb.Append("        ]\n");
            sb.Append("      },\n");
            return sb.ToString();
        }

        // Plates/weapons/parts with durability are handed out in perfect condition.
        private string RepairableJson(string tpl)
        {
            if (_catalog.Items.TryGetValue(tpl, out var it) && it.MaxDurability > 0)
            {
                string d = it.MaxDurability.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                return ", \"Repairable\": { \"Durability\": " + d + ", \"MaxDurability\": " + d + " }";
            }
            return "";
        }

        private string NewMongoId(Random rng)
        {
            byte[] bytes = new byte[12];
            rng.NextBytes(bytes);
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }

        // ---------------------------------------------------------------------------------------------
        // Filtering (config: weapons / modules sections)
        // ---------------------------------------------------------------------------------------------

        private bool PassesFilter(ItemFilterConfig f, HashSet<string> blacklist, HashSet<string> whitelist, HashSet<string> extra, string id)
        {
            if (blacklist.Contains(id)) return false;           // blacklist always wins
            if (extra.Contains(id)) return true;                // explicitly added items skip the checks below
            if (whitelist.Count > 0 && !whitelist.Contains(id)) return false;
            if (!f.IncludeModded && _catalog.IsModded(id)) return false;
            if (_config.RequireHandbookEntry && !_catalog.Prices.ContainsKey(id)) return false;
            if (f.CategoryBlacklist != null && f.CategoryBlacklist.Count > 0 && _catalog.IsDescendantOfAny(id, f.CategoryBlacklist)) return false;
            return true;
        }

        private bool IsAllowedModule(string id)
        {
            if (_moduleCache.TryGetValue(id, out var cached)) return cached;

            bool ok = false;
            if (_catalog.Items.TryGetValue(id, out var it) && it.Type == "Item" && !it.QuestItem && !_catalog.IsDescendantOf(id, AmmoBase))
                ok = PassesFilter(_config.Modules, _moduleBlacklist, _moduleWhitelist, _moduleExtra, id);

            _moduleCache[id] = ok;
            return ok;
        }

        private void BuildWeaponPool()
        {
            var wc = _config.Weapons;
            var blacklist = new HashSet<string>(wc.Blacklist ?? new List<string>());
            var whitelist = new HashSet<string>(wc.Whitelist ?? new List<string>());
            var extra = new HashSet<string>(wc.Extra ?? new List<string>());

            foreach (var it in _catalog.Items.Values)
            {
                if (it.Type != "Item" || it.QuestItem || !_catalog.IsDescendantOf(it.Id, ItemCatalog.WeaponBase)) continue;
                if (HasForbiddenCaliber(it)) continue;
                if (!PassesFilter(wc, blacklist, whitelist, extra, it.Id)) continue;
                if (!extra.Contains(it.Id) && it.Slots.Count < _config.MinWeaponSlots) continue;

                var slots = ChildSlots(it.Id);
                if (slots.Count == 0) continue;

                var minimalBuild = _weightAssembler.Assemble(it.Id, false, null, true);
                if (minimalBuild == null) continue; // skip weapons whose mandatory parts cannot be assembled
                double minimalWeight = PartAssembler.TotalWeight(_catalog, minimalBuild);

                // Base stats are taken exactly as the item database has them. They must NOT be clamped or replaced by guesses:
                // any distortion makes the generated limits differ from what the game really calculates (e.g. a Mosin whose
                // real recoil is far above 700 was capped at 700 and got an unreachable "max recoil 417").
                float erg = (float)Math.Max(0, it.Ergonomics);
                float recoil = (float)(it.RecoilForceUp + it.RecoilForceBack);
                if (recoil <= 0) continue; // no recoil data -> the limit cannot be calculated reliably, skip the weapon

                _weaponPool.Add(new WeaponEntry
                {
                    TemplateId = it.Id,
                    BaseErgonomics = erg,
                    BaseRecoil = recoil,
                    ClassKey = ClassKeyOf(it),
                    MinimalWeight = Math.Max(0.1, minimalWeight),
                    Slots = slots
                });
            }
        }

        private string ClassKeyOf(ItemInfo it)
        {
            switch ((it.WeapClass ?? "").ToLowerInvariant())
            {
                case "pistol":
                case "revolver": return "pistol";
                case "smg": return "smg";
                case "assaultrifle":
                case "assaultcarbine": return "assaultRifle";
                case "marksmanrifle": return "marksmanRifle";
                case "sniperrifle": return "sniperRifle";
                default: return "other";
            }
        }

        /// <summary>Picks a weapon: first a weapon class by the tier's weaponClassWeights, then a weapon of that class. Unused weapons first.</summary>
        private WeaponEntry? PickWeapon(TierConfig? tier, HashSet<string> used, Random rng)
        {
            var candidates = _weaponPool.Where(w => !used.Contains(w.TemplateId)).ToList();
            if (candidates.Count == 0) candidates = _weaponPool;
            if (candidates.Count == 0) return null;

            var weights = tier?.WeaponClassWeights;
            double WeightOf(string key)
            {
                if (weights == null) return 1;
                if (weights.TryGetValue(key, out var v)) return Math.Max(0, v);
                if (weights.TryGetValue("other", out var o)) return Math.Max(0, o);
                return 1;
            }

            var groups = candidates.GroupBy(w => w.ClassKey).ToList();
            double total = groups.Sum(g => WeightOf(g.Key));
            if (total <= 0) return candidates[rng.Next(candidates.Count)];

            double roll = rng.NextDouble() * total;
            foreach (var g in groups)
            {
                roll -= WeightOf(g.Key);
                if (roll <= 0)
                {
                    var list = g.ToList();
                    return list[rng.Next(list.Count)];
                }
            }
            var last = groups[groups.Count - 1].ToList();
            return last[rng.Next(last.Count)];
        }

        private bool AreConflicting(string a, string b) => _catalog.AreConflicting(a, b);

        /// <summary>Slots of an item that have at least one allowed module (cached). Works for weapons and for modules.</summary>
        private List<SlotOptions> ChildSlots(string itemId)
        {
            if (_childSlotCache.TryGetValue(itemId, out var cached)) return cached;

            var result = new List<SlotOptions>();
            if (_catalog.Items.TryGetValue(itemId, out var it))
            {
                foreach (var slot in it.Slots)
                {
                    var candidates = slot.Filter.Distinct().Where(IsAllowedModule).ToList();
                    if (candidates.Count > 0) result.Add(new SlotOptions { Name = slot.Name, Candidates = candidates });
                }
            }
            _childSlotCache[itemId] = result;
            return result;
        }

        private double SlotWeight(string slotName)
        {
            string n = slotName.ToLowerInvariant();
            double best = -1;
            foreach (var kv in _config.Build.SlotWeights)
                if (n.Contains(kv.Key.ToLowerInvariant()) && kv.Value > best) best = kv.Value;
            return best >= 0 ? best : _config.Build.DefaultSlotWeight;
        }

        private sealed class PlacedNode
        {
            public string Id = "";
            public string SlotName = "";
            public int Depth;
            public PlacedNode? Parent;
            public int Children;
            public bool MountLike;
            /// <summary>Set when the part was placed only to prove that a category requirement can be met.</summary>
            public string? ProofCategory;
        }

        private sealed class OpenSlot
        {
            public string Name = "";
            public List<string> Candidates = new List<string>();
            public int Depth;
            public PlacedNode? Parent;
        }

        private static string PickSightMode(TierConfig? tier, Random rng)
        {
            var weights = tier?.SightModeWeights;
            if (weights == null || weights.Count == 0) return "none";

            // legacy keys: optic -> assault scope / scope, both -> collimator + assault scope / scope
            var eff = new Dictionary<string, double>();
            void Add(string k, double v) => eff[k] = (eff.TryGetValue(k, out var o) ? o : 0) + Math.Max(0, v);
            foreach (var kv in weights)
            {
                if (kv.Key == "optic") { Add("assaultScope", kv.Value / 2); Add("scope", kv.Value / 2); }
                else if (kv.Key == "both") { Add("collimatorAssaultScope", kv.Value / 2); Add("collimatorScope", kv.Value / 2); }
                else if (SightModeDefs.ContainsKey(kv.Key)) Add(kv.Key, kv.Value);
            }

            double total = eff.Values.Sum();
            if (total <= 0) return "none";

            double roll = rng.NextDouble() * total;
            foreach (var m in SightModeDefs.Keys)
            {
                if (!eff.TryGetValue(m, out var v)) continue;
                roll -= v;
                if (roll <= 0) return m;
            }
            return "none";
        }

        /// <summary>
        /// Builds a random but valid attachment TREE: every part sits in a slot of a part that is itself part of the build
        /// (e.g. receiver cover -> adapter -> mount -> scope -> additional collimator). Slots are picked by weight
        /// (build.slotWeights): sights, handguards, grips and muzzle devices are required most often.
        /// The tier's maxMods is a hard limit for the number of parts in the whole tree.
        /// Category requirements (suppressor, collimator / assault scope / scope / special scope sight) are only emitted when the tree itself contains such a
        /// part, which proves that the weapon can take it; such a part is not listed in containsItems (any item of the
        /// category is accepted) unless other required parts sit on it.
        /// </summary>
        public GunsmithRequirements? GenerateRequirements(WeaponEntry weapon, TierConfig? tier, Random rng)
        {
            // Every part is checked against every other part while the tree grows; the finished tree is verified once more
            // and rejected (and rebuilt) if any two parts still conflict.
            for (var attempt = 0; attempt < 25; attempt++)
            {
                var result = BuildRequirements(weapon, tier, rng);
                if (!result.HasConflict) return result;
            }
            return null;
        }

        private GunsmithRequirements BuildRequirements(WeaponEntry weapon, TierConfig? tier, Random rng)
        {
            var reqs = new GunsmithRequirements();

            int minMods = Math.Max(1, tier?.MinMods ?? 1);
            int maxMods = Math.Max(minMods, tier?.MaxMods ?? 2);
            int wanted = rng.Next(minMods, maxMods + 1);
            int maxDepth = Math.Max(1, _config.Build.MaxDepth);

            var nodes = new List<PlacedNode>();
            var open = new List<OpenSlot>();
            foreach (var ws in weapon.Slots)
                open.Add(new OpenSlot { Name = ws.Name, Candidates = ws.Candidates, Depth = 0, Parent = null });

            var groups = _config.CategoryGroups;
            List<string> GroupIds(string group) => _groupIds.TryGetValue(group, out var l) ? l : new List<string>();
            bool InGroup(string id, string group) => _catalog.IsDescendantOfAny(id, GroupIds(group));
            bool IsPrimarySight(string id) => SightKind(id) != null;

            string sightMode = PickSightMode(tier, rng);
            var sightDef = SightModeDefs[sightMode];
            // at most two sights: a collimator plus ONE magnified sight (assault scope / scope / special scope, mutually exclusive)
            int maxPrimarySights = sightDef.reflex && sightDef.magnified != null ? 2 : 1;

            double Weight(OpenSlot o)
            {
                double w = SlotWeight(o.Name);
                if (o.Depth > 0) w *= Math.Max(0, _config.Build.ChildSlotBoost);
                // an adapter/mount without anything on it is pointless - strongly prefer finishing it
                if (o.Parent != null && o.Parent.MountLike && o.Parent.Children == 0) w *= 3;
                return w;
            }

            bool IsMountLike(string id, string slotName) =>
                slotName.ToLowerInvariant().Contains("mount") || _catalog.IsDescendantOf(id, MountBase);

            bool Compatible(string id)
            {
                if (AreConflicting(weapon.TemplateId, id)) return false;
                if (nodes.Any(n => n.Id == id || AreConflicting(n.Id, id))) return false;
                if (IsBannedSight(id)) return false;
                if (IsPrimarySight(id) && nodes.Count(n => IsPrimarySight(n.Id)) >= maxPrimarySights) return false;
                // the magnified sight types (assault scope / scope / special scope) exclude each other: only ONE of them per build;
                // together with at most one collimator that makes two sights at most
                var kind = SightKind(id);
                if (kind == "reflex" && nodes.Any(n => SightKind(n.Id) == "reflex")) return false;
                if (kind != null && kind != "reflex" && nodes.Any(n => { var k = SightKind(n.Id); return k != null && k != "reflex"; })) return false;
                return true;
            }

            List<AssembledPart> ToAssembledList(IEnumerable<PlacedNode> currentNodes)
            {
                var list = new List<AssembledPart> { new AssembledPart { Tpl = weapon.TemplateId, ParentIndex = -1, SlotName = "" } };
                var map = new Dictionary<PlacedNode, int>();
                foreach (var n in currentNodes)
                {
                    int pIdx = n.Parent != null && map.TryGetValue(n.Parent, out var pi) ? pi : 0;
                    map[n] = list.Count;
                    list.Add(new AssembledPart { Tpl = n.Id, ParentIndex = pIdx, SlotName = n.SlotName });
                }
                return list;
            }

            bool CanCompleteWeapon(PlacedNode candidateNode)
            {
                var testNodes = new List<PlacedNode>(nodes) { candidateNode };
                return _weightAssembler.CompleteWeapon(ToAssembledList(testNodes), true) != null;
            }

            PlacedNode? Place(OpenSlot target, string? forcedId = null)
            {
                if (nodes.Count >= maxMods) return null;
                // Do not attach children onto a proof-category item so it stays unlisted (any item of the category)
                if (target.Parent?.ProofCategory != null) return null;

                IEnumerable<string> source = target.Candidates.Where(Compatible);
                if (forcedId != null)
                {
                    source = source.Where(c => c == forcedId);
                }
                else
                {
                    // an adapter/mount is only placed if there is still room for something on it
                    bool roomForChild = nodes.Count + 2 <= maxMods;
                    source = source.Where(c => roomForChild || !IsMountLike(c, target.Name) || ChildSlots(c).Count == 0);
                }

                var pool = source.OrderBy(_ => rng.Next()).ToList();
                if (pool.Count == 0) return null;

                PlacedNode? chosenNode = null;
                foreach (var candId in pool)
                {
                    var trial = new PlacedNode
                    {
                        Id = candId,
                        SlotName = target.Name,
                        Depth = target.Depth,
                        Parent = target.Parent,
                        MountLike = IsMountLike(candId, target.Name)
                    };
                    if (CanCompleteWeapon(trial))
                    {
                        chosenNode = trial;
                        break;
                    }
                }

                if (chosenNode == null) return null;

                if (target.Parent != null) target.Parent.Children++;
                nodes.Add(chosenNode);
                open.Remove(target);

                if (target.Depth + 1 < maxDepth)
                    foreach (var cs in ChildSlots(chosenNode.Id))
                        open.Add(new OpenSlot { Name = cs.Name, Candidates = cs.Candidates, Depth = target.Depth + 1, Parent = chosenNode });
                return chosenNode;
            }

            void RollbackTo(int nodeMark)
            {
                while (nodes.Count > nodeMark)
                {
                    var rem = nodes[nodes.Count - 1];
                    nodes.RemoveAt(nodes.Count - 1);
                    if (rem.Parent != null) rem.Parent.Children--;
                    open.RemoveAll(o => o.Parent == rem);
                    var slotOpts = rem.Parent == null
                        ? weapon.Slots.FirstOrDefault(s => s.Name == rem.SlotName)
                        : ChildSlots(rem.Parent.Id).FirstOrDefault(s => s.Name == rem.SlotName);
                    if (slotOpts != null && !open.Any(o => o.Parent == rem.Parent && o.Name == rem.SlotName))
                        open.Add(new OpenSlot { Name = slotOpts.Name, Candidates = slotOpts.Candidates, Depth = rem.Depth, Parent = rem.Parent });
                }
            }

            // Shortest chain of parts (starting in any open slot) that ends in an item of the given category.
            (OpenSlot start, List<string> chain)? FindChain(string group, int maxLen)
            {
                var queue = new Queue<(OpenSlot start, List<string> chain)>();
                var visited = new HashSet<string>();

                foreach (var o in open.Where(x => x.Parent?.ProofCategory == null && SlotWeight(x.Name) > 0).OrderBy(_ => rng.Next()).ToList())
                {
                    foreach (var c in o.Candidates.OrderBy(_ => rng.Next()).ToList())
                    {
                        if (!Compatible(c)) continue;
                        if (InGroup(c, group)) return (o, new List<string> { c });
                        if (maxLen > 1 && o.Depth + 2 <= maxDepth && visited.Add(c)) queue.Enqueue((o, new List<string> { c }));
                    }
                }

                int steps = 0;
                while (queue.Count > 0 && steps++ < 4000)
                {
                    var (start, chain) = queue.Dequeue();
                    string last = chain[chain.Count - 1];

                    foreach (var cs in ChildSlots(last))
                    {
                        foreach (var c in cs.Candidates.OrderBy(_ => rng.Next()).ToList())
                        {
                            if (!Compatible(c) || chain.Contains(c) || chain.Any(prev => AreConflicting(prev, c))) continue;
                            var next = new List<string>(chain) { c };
                            if (InGroup(c, group)) return (start, next);
                            if (next.Count < maxLen && start.Depth + next.Count + 1 <= maxDepth && visited.Add(c)) queue.Enqueue((start, next));
                        }
                    }
                }
                return null;
            }

            // ---- 1. category requirements first (they need a proof part, possibly behind a chain of adapters/mounts) ----
            var needs = new List<string>();
            if (GroupIds("suppressor").Count > 0 && rng.NextDouble() < Math.Clamp(tier?.SuppressorChance ?? 0, 0, 1))
                needs.Add("suppressor");
            if (GroupIds("foregrip").Count > 0 && rng.NextDouble() < Math.Clamp(tier?.ForegripChance ?? 0, 0, 1))
                needs.Add("foregrip");
            if (GroupIds("tactical").Count > 0 && rng.NextDouble() < Math.Clamp(tier?.TacticalChance ?? 0, 0, 1))
                needs.Add("tactical");
            if (sightDef.magnified != null && GroupIds(sightDef.magnified).Count > 0) needs.Add(sightDef.magnified);
            if (sightDef.reflex && GroupIds("reflex").Count > 0) needs.Add("reflex");

            // magazine requirement: a concrete magazine of the weapon becomes a required (listed) part
            if (rng.NextDouble() < Math.Clamp(tier?.MagazineChance ?? 0, 0, 1))
            {
                var magSlot = open.FirstOrDefault(o => o.Parent == null && string.Equals(o.Name, "mod_magazine", StringComparison.OrdinalIgnoreCase));
                if (magSlot != null) Place(magSlot);
            }

            foreach (var need in needs)
            {
                int budget = maxMods - nodes.Count;
                if (budget <= 0) break;

                var found = FindChain(need, budget);
                if (found == null) continue;

                int mark = nodes.Count;
                OpenSlot? cursor = found.Value.start;
                var chain = found.Value.chain;
                PlacedNode? lastNode = null;
                bool ok = true;

                for (int i = 0; i < chain.Count; i++)
                {
                    if (cursor == null) { ok = false; break; }
                    lastNode = Place(cursor, chain[i]);
                    if (lastNode == null) { ok = false; break; }

                    if (i + 1 < chain.Count)
                    {
                        var placedNode = lastNode;
                        string nextTpl = chain[i + 1];
                        cursor = open.FirstOrDefault(o => o.Parent == placedNode && o.Candidates.Contains(nextTpl));
                    }
                }

                if (ok && lastNode != null)
                {
                    lastNode.ProofCategory = need; // group key: suppressor / reflex / assaultScope / scope / specialScope / foregrip / tactical
                    open.RemoveAll(o => o.Parent == lastNode); // prevent child placement on proof node
                }
                else
                {
                    RollbackTo(mark);
                }
            }

            // ---- 2. weighted growth of the tree up to the wanted number of parts ----
            int guard = 0;
            while (nodes.Count < wanted && open.Count > 0 && guard++ < 500)
            {
                var pickable = open.Where(o => Weight(o) > 0).ToList();
                if (pickable.Count == 0) break;

                double total = pickable.Sum(Weight);
                double roll = rng.NextDouble() * total;
                OpenSlot chosen = pickable[pickable.Count - 1];
                foreach (var o in pickable)
                {
                    roll -= Weight(o);
                    if (roll <= 0) { chosen = o; break; }
                }

                if (Place(chosen) == null) open.Remove(chosen);
            }

            // ---- 3. finish adapters/mounts that ended up empty (still within maxMods) ----
            while (nodes.Count < maxMods && guard++ < 1000)
            {
                var dangling = nodes.FirstOrDefault(n => n.MountLike && n.Children == 0 && open.Any(o => o.Parent == n && SlotWeight(o.Name) > 0));
                if (dangling == null) break;

                var options = open.Where(o => o.Parent == dangling && SlotWeight(o.Name) > 0).OrderByDescending(Weight).ToList();
                bool placed = false;
                foreach (var o in options)
                    if (Place(o) != null) { placed = true; break; }
                if (!placed) open.RemoveAll(o => o.Parent == dangling);
            }

            // ---- 4. limits calculated from several real completions of the build (worst case across genuinely
            // different real part choices for the remaining mandatory slots, e.g. magazine/stock/pistol grip) ----
            // Rather than one or two fixed completions plus a large arbitrary safety margin, we sample the
            // lightest completion, the deterministic "standard" one and several genuinely randomized real
            // completions, and take the worst value of each stat across all of them. That worst-case is what the
            // limits are built from, so the numbers track the actual modules on the weapon instead of a guess.
            var baseList = ToAssembledList(nodes);
            var variants = new List<List<AssembledPart>>();
            var completedBuild = _weightAssembler.CompleteWeapon(baseList, true);
            if (completedBuild != null) variants.Add(completedBuild);
            var standardBuild = _weightAssembler.CompleteWeapon(baseList, false);
            if (standardBuild != null) variants.Add(standardBuild);
            const int randomCompletionSamples = 4;
            for (var s = 0; s < randomCompletionSamples; s++)
            {
                var sample = _weightAssembler.CompleteWeapon(baseList, false, rng);
                if (sample != null) variants.Add(sample);
            }

            double ergSum, recoilPct, totalBuildWeight;
            // final safety net: no two parts of the whole build (weapon, required parts and completed mandatory parts) may conflict
            reqs.HasConflict = variants.Count == 0
                ? PartAssembler.HasConflict(_catalog, baseList)
                : variants.Any(v => PartAssembler.HasConflict(_catalog, v));

            if (variants.Count > 0)
            {
                var statsList = variants.Select(v => PartAssembler.TotalModsStats(_catalog, v)).ToList();
                ergSum = statsList.Min(s => s.ergoDelta);        // worst case: least ergonomics any real completion can give
                recoilPct = statsList.Max(s => s.recoilPct);     // worst case: most recoil any real completion can give
                totalBuildWeight = statsList.Max(s => s.weight); // worst case: heaviest any real completion can be
            }
            else
            {
                double treeWeight = 0, treeErgo = 0, treeRecoil = 0;
                foreach (var n in nodes)
                    if (_catalog.Items.TryGetValue(n.Id, out var it)) { treeErgo += it.Ergonomics; treeRecoil += it.Recoil; treeWeight += it.Weight; }
                ergSum = treeErgo;
                recoilPct = treeRecoil;
                totalBuildWeight = weapon.MinimalWeight + treeWeight;
            }

            float ergEstimate = (float)(weapon.BaseErgonomics + ergSum);
            float recoilEstimate = (float)(weapon.BaseRecoil * Math.Max(0.3, 1 + recoilPct / 100.0));

            // Ergonomics is a minimum ("at least"), recoil and weight are maximums.
            // The lower bound must never exceed what the witness build can actually reach: very heavy builds can have
            // ergonomics below 10 (even negative), and a hard floor of 10 made such quests impossible to hand in.
            // The game never reports negative ergonomics, so the requirement bottoms out at 0.
            reqs.MinErgonomics = Math.Clamp((float)Math.Floor(ergEstimate) - 2f, 0f, 85f);
            // No upper clamp: a hard cap (the old 800) lies below the real recoil of heavy-recoil weapons and makes the quest
            // impossible. A lower bound only loosens the requirement, so it is harmless.
            reqs.MaxRecoilSum = Math.Max(90f, (float)Math.Ceiling(recoilEstimate * 1.1f));

            // Weight: worst-case real completion weight x a (now much smaller) configurable margin, no extra flat buffer.
            double marginMin = Math.Max(1.0, _config.WeightMarginMin);
            double marginMax = Math.Max(marginMin, _config.WeightMarginMax);
            double margin = marginMin + rng.NextDouble() * (marginMax - marginMin);
            reqs.MaxWeight = Math.Max(1.0, Math.Ceiling(totalBuildWeight * margin * 10) / 10);

            reqs.WitnessWeight = totalBuildWeight;
            reqs.WitnessErgonomics = Math.Max(0f, ergEstimate);
            reqs.WitnessRecoil = recoilEstimate;

            // ---- 5. result: listed parts + the full tree for the report ----
            var indexOf = new Dictionary<PlacedNode, int>();
            for (int i = 0; i < nodes.Count; i++) indexOf[nodes[i]] = i;

            // The tier promises at least minMods parts: fewer means the layout failed -> retry.
            if (nodes.Count(n => !(n.ProofCategory != null && n.Children == 0)) < minMods) { reqs.HasConflict = true; return reqs; }

            foreach (var n in nodes)
            {
                bool listed = !(n.ProofCategory != null && n.Children == 0);
                if (listed) reqs.RequiredMods.Add(new ModEntry { TemplateId = n.Id });
                if (n.ProofCategory != null)
                {
                    var ids = groups.EmitAllMembers ? GroupIds(n.ProofCategory!) : GroupIds(n.ProofCategory!).Where(g => _catalog.IsDescendantOf(n.Id, g)).Take(1).ToList();
                    foreach (var id in ids) if (!reqs.Categories.Contains(id)) reqs.Categories.Add(id);
                }
                reqs.Parts.Add(new PartRecord { Tpl = n.Id, ParentIndex = n.Parent != null ? indexOf[n.Parent] : -1, SlotName = n.SlotName, Depth = n.Depth, Listed = listed, ProofCategory = n.ProofCategory });
            }

            // Multi-category quests need all-of semantics check: every listed category must be
            // satisfiable by ONE build (proof parts). Reject if witness covers only some.
            if (reqs.Categories.Count > 1)
            {
                var witnessTree = reqs.Parts.Select(p => p.Tpl).ToList();
                if (!reqs.Categories.All(c => witnessTree.Any(t => _catalog.IsDescendantOf(t, c)))) { reqs.HasConflict = true; return reqs; }
            }
            return reqs;
        }

        // ---------------------------------------------------------------------------------------------
        // Localization
        // ---------------------------------------------------------------------------------------------

        /// <summary>
        /// Builds the locale entries of one quest for one language.
        /// <paramref name="texts"/> are the templates from locales/{lang}.json, <paramref name="nameOf"/> resolves an
        /// item id + kind ("Name" / "ShortName") to the item's name in that language. Quest texts always use full names ("Name").
        /// </summary>
        public static Dictionary<string, string> BuildLocale(GeneratedQuest q, IReadOnlyDictionary<string, string> texts, Func<string, string, string> nameOf)
        {
            string Fmt(string key, params object[] args)
            {
                string template = texts.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : DefaultTexts[key];
                try { return string.Format(System.Globalization.CultureInfo.InvariantCulture, template, args); }
                catch (FormatException) { return string.Format(System.Globalization.CultureInfo.InvariantCulture, DefaultTexts[key], args); }
            }

            string weaponName = nameOf(q.WeaponTpl, "Name");
            var modNames = q.ModTpls.Select(id => nameOf(id, "Name")).ToList();
            string modsText = modNames.Count > 0 ? string.Join(", ", modNames) : Fmt("noModules");
            var categoryNames = q.Categories.Select(id => nameOf(id, "Name")).ToList();
            string categoriesText = categoryNames.Count > 0 ? string.Join(", ", categoryNames) : Fmt("noCategories");
            string weightText = q.MaxWeight.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            string description = Fmt("description", weaponName, q.MinErgonomics, q.MaxRecoil, weightText, modsText, categoriesText);

            return new Dictionary<string, string>
            {
                { q.Id + " name", Fmt("title", weaponName) },
                { q.Id + " description", description },
                { q.Id + " note", description },
                { q.Id + " startedMessageText", description },
                { q.Id + " successMessageText", Fmt("success") },
                { q.Id + " acceptPlayerMessage", Fmt("accept") },
                { q.Id + " declinePlayerMessage", Fmt("decline") },
                { q.Id + " completePlayerMessage", Fmt("complete") },
                { q.CondId, Fmt("condition", weaponName) }
            };
        }

        public GeneratedQuest? Generate(string day, int index, TierConfig? tier, HashSet<string> usedWeapons)
        {
            Random rng = _random;
            int questIndex = index + 1;

            // A weapon for which no conflict-free build can be generated is skipped and another one is picked.
            WeaponEntry? weapon = null;
            GunsmithRequirements? reqs = null;
            var triedWeapons = new HashSet<string>(usedWeapons);
            for (var tries = 0; tries < 10 && reqs == null; tries++)
            {
                weapon = PickWeapon(tier, triedWeapons, rng);
                if (weapon == null) return null;
                triedWeapons.Add(weapon.TemplateId);
                reqs = GenerateRequirements(weapon, tier, rng);
            }
            if (weapon == null || reqs == null) return null;
            usedWeapons.Add(weapon.TemplateId);

            string questId = NewMongoId(rng);
            string condId = NewMongoId(rng);
            string rewardExpId = NewMongoId(rng);
            string rewardMoneyId = NewMongoId(rng);
            string rewardRepId = NewMongoId(rng);
            string levelCondId = NewMongoId(rng);
            string maxLevelCondId = NewMongoId(rng);
            int minLevel = Math.Max(1, tier?.MinLevel ?? 1);
            int maxLevel = tier != null && tier.MaxLevel >= minLevel ? tier.MaxLevel : 0; // 0 = no upper limit

            string randomIcon = _icons[rng.Next(_icons.Length)];

            // Base rewards: taken from the tier's config; 0 means "automatic" (derived from the tier's minLevel).
            int baseExp = 8000 + (questIndex * 2500);
            int baseRoubles = 85000 + (questIndex * 35000);
            double standing = 0.02;
            double multiplier = Math.Max(0, _config.GlobalRewardMultiplier);

            if (tier != null)
            {
                baseExp = tier.Experience > 0 ? tier.Experience : Math.Max(5000, tier.MinLevel * 400);
                baseRoubles = tier.Roubles > 0 ? tier.Roubles : Math.Max(65000, tier.MinLevel * 4500);
                standing = Math.Max(0, tier.Standing);
                multiplier *= Math.Max(0, tier.RewardMultiplier);
            }

            int expReward = Math.Max(1, (int)Math.Round(baseExp * multiplier));
            int roublesReward = Math.Max(1, (int)Math.Round(baseRoubles * multiplier));
            string standingReward = standing.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);

            // Награды-предметы: только лутабельные бартерные предметы, броня, оружие, патроны, драгоценности и ключи.
            var lootRewards = PickLootRewards(rng, tier, roublesReward * 0.65);
            var lootJson = new StringBuilder();
            for (int i = 0; i < lootRewards.Count; i++)
                lootJson.Append(BuildLootRewardJson(lootRewards[i], 2 + i, NewMongoId(rng), NewMongoId(rng), rng));
            string lootRewardsJson = lootJson.ToString();
            int repIndex = 2 + lootRewards.Count;

            string rewardItemId = NewMongoId(rng);

            var modIdsJsonList = new List<string>();
            foreach (var m in reqs.RequiredMods)
            {
                modIdsJsonList.Add(string.Format("\"{0}\"", m.TemplateId));
            }
            string containsItemsJson = string.Join(", ", modIdsJsonList);

            var args = new QuestJsonArgs
            {
                QuestId = questId, CondId = condId, LevelCondId = levelCondId, MinLevel = minLevel, MaxLevelCondId = maxLevelCondId, MaxLevel = maxLevel,
                WeaponTpl = weapon.TemplateId, Icon = randomIcon,
                MinErgonomics = reqs.MinErgonomics, MaxRecoil = reqs.MaxRecoilSum, MaxWeight = reqs.MaxWeight,
                ContainsItemsJson = containsItemsJson, Categories = reqs.Categories,
                ExpId = rewardExpId, Exp = expReward, MoneyId = rewardMoneyId, MoneyItemId = rewardItemId, Roubles = roublesReward,
                LootRewardsJson = lootRewardsJson, RepIndex = repIndex, RepId = rewardRepId, Standing = standingReward
            };

            string questJsonWithStats = BuildTemplateQuestJson(args, true) ?? BuildQuestJson(args, true);
            string questJsonWithoutStats = BuildTemplateQuestJson(args, false) ?? BuildQuestJson(args, false);
            string legacyJson = BuildQuestJson(args, true);

            // loot rewards as records (for the report)
            var lootRecords = lootRewards.Select(r => new LootRecord
            {
                Tpl = r.Tpl,
                Count = r.Count,
                Parts = (r.Parts ?? new List<AssembledPart>()).Select(p => new PartRecord
                {
                    Tpl = p.Tpl, ParentIndex = p.ParentIndex, SlotName = p.SlotName
                }).ToList()
            }).ToList();

            return new GeneratedQuest
            {
                Id = questId,
                CondId = condId,
                Json = questJsonWithStats,
                JsonWithoutStats = questJsonWithoutStats,
                LegacyJson = legacyJson,
                WeaponTpl = weapon.TemplateId,
                WeaponClass = weapon.ClassKey,
                TierName = tier?.Name ?? "-",
                MinLevel = minLevel,
                ModTpls = reqs.RequiredMods.Select(m => m.TemplateId).ToList(),
                Categories = reqs.Categories,
                Parts = reqs.Parts,
                MinErgonomics = reqs.MinErgonomics,
                MaxRecoil = reqs.MaxRecoilSum,
                MaxWeight = reqs.MaxWeight,
                ExpReward = expReward,
                RoublesReward = roublesReward,
                Standing = standingReward,
                LootRewards = lootRecords,
                HandInCheck = VerifyHandIn(reqs, questJsonWithStats, minLevel, maxLevel)
            };
        }

        private sealed class QuestJsonArgs
        {
            public string QuestId = "", CondId = "", LevelCondId = "", MaxLevelCondId = "", WeaponTpl = "", Icon = "";
            public int MinLevel;
            public int MaxLevel;
            public float MinErgonomics, MaxRecoil;
            public double MaxWeight;
            public string ContainsItemsJson = "";
            public List<string> Categories = new List<string>();
            public string ExpId = "", MoneyId = "", MoneyItemId = "", LootRewardsJson = "", RepId = "", Standing = "";
            public int Exp, Roubles, RepIndex;
        }

        private static HashSet<string> ReadIdList(JsonNode? n)
        {
            var set = new HashSet<string>();
            JsonNode? arr = n is JsonObject o ? o["id"] : n;
            if (arr is JsonArray a)
                foreach (var e in a)
                {
                    var v = e?.GetValue<string>();
                    if (!string.IsNullOrEmpty(v)) set.Add(v);
                }
            return set;
        }

        private static double? ReadStat(JsonObject cond, string key) =>
            (cond[key] as JsonObject)?["value"]?.GetValue<double>();

        /// <summary>
        /// Simulates the hand-in against the quest JSON the server will really register: the generated (witness) build must
        /// contain every listed part, satisfy every emitted category and stay within the weight / ergonomics / recoil
        /// limits, and the level conditions must match the tier. Returns "ok" or the list of problems.
        /// </summary>
        private string VerifyHandIn(GunsmithRequirements reqs, string questJson, int minLevel, int maxLevel)
        {
            var problems = new List<string>();
            string note = "";
            try
            {
                var root = JsonNode.Parse(questJson) as JsonObject;
                var finish = root?["conditions"]?["AvailableForFinish"] as JsonArray;
                var cond = finish?.OfType<JsonObject>().FirstOrDefault(c => c["conditionType"]?.GetValue<string>() == "WeaponAssembly");
                if (cond == null) return "no WeaponAssembly condition in the quest JSON";

                var jsonItems = ReadIdList(cond["containsItems"]);
                var jsonCats = ReadIdList(cond["hasItemFromCategory"]);
                var tree = reqs.Parts.Select(p => p.Tpl).ToList();

                foreach (var m in reqs.RequiredMods)
                {
                    if (!jsonItems.Contains(m.TemplateId)) problems.Add($"required part {m.TemplateId} is missing in the quest JSON");
                    if (!tree.Contains(m.TemplateId)) problems.Add($"required part {m.TemplateId} is not part of the generated build");
                }
                if (jsonItems.Count != reqs.RequiredMods.Count) problems.Add("containsItems size differs from the required parts");

                foreach (var c in reqs.Categories)
                    if (!jsonCats.Contains(c)) problems.Add($"category {c} is missing in the quest JSON");

                foreach (var p in reqs.Parts.Where(x => !x.Listed && x.ProofCategory != null))
                    if (!_catalog.IsDescendantOfAny(p.Tpl, jsonCats))
                        problems.Add($"proof part {p.Tpl} ({p.ProofCategory}) is not accepted by the quest categories");

                // several categories in one condition only work if the game accepts "any of them"
                if (jsonCats.Count > 1 && !jsonCats.All(c => tree.Any(t => _catalog.IsDescendantOf(t, c))))
                    note = $" | note: {jsonCats.Count} categories listed, the generated build matches only some of them (requires 'any of' behaviour in the game)";

                var w = ReadStat(cond, "weight");
                if (w == null || reqs.WitnessWeight > w.Value + 1e-6) problems.Add($"weight limit {w} is below the generated build ({reqs.WitnessWeight:0.##} kg)");
                var e = ReadStat(cond, "ergonomics");
                if (e != null && reqs.WitnessErgonomics + 1e-6 < e.Value) problems.Add($"ergonomics limit {e} is above the generated build ({reqs.WitnessErgonomics:0.##})");
                var r = ReadStat(cond, "recoil");
                if (r != null && reqs.WitnessRecoil > r.Value + 1e-6) problems.Add($"recoil limit {r} is below the generated build ({reqs.WitnessRecoil:0.##})");

                var start = root?["conditions"]?["AvailableForStart"] as JsonArray;
                var levels = start?.OfType<JsonObject>().Where(c => c["conditionType"]?.GetValue<string>() == "Level").ToList() ?? new List<JsonObject>();
                bool HasLevel(string cmp, int v) => levels.Any(c => c["compareMethod"]?.GetValue<string>() == cmp && (c["value"]?.GetValue<double>() ?? -1) == v);
                if (minLevel > 1 && !HasLevel(">=", minLevel)) problems.Add($"level condition >= {minLevel} is missing");
                if (maxLevel > 0 && !HasLevel("<=", maxLevel)) problems.Add($"level condition <= {maxLevel} is missing");
            }
            catch (Exception ex)
            {
                problems.Add("check failed: " + ex.Message);
            }
            return problems.Count == 0 ? "ok" + note : string.Join("; ", problems) + note;
        }

        /// <summary>Report about the category groups: classes, number of items and how many of them come from mods.</summary>
        public List<string> DescribeGroups()
        {
            var lines = new List<string>();
            foreach (var kv in _groupIds)
            {
                var members = _catalog.Items.Values.Where(i => i.Type == "Item" && _catalog.IsDescendantOfAny(i.Id, kv.Value)).ToList();
                var modded = _catalog.VanillaIds == null ? null : members.Where(i => _catalog.IsModded(i.Id)).ToList();
                string moddedText = modded == null ? "modded/vanilla split unknown" : $"{modded.Count} from mods";
                lines.Add($"category group '{kv.Key}': {kv.Value.Count} classes, {members.Count} items ({moddedText})" + (members.Count == 0 ? "  <- EMPTY, requirement is never generated" : ""));
                if (modded != null && modded.Count > 0)
                    lines.Add("    modded items: " + string.Join(", ", modded.Take(12).Select(i => i.FallbackName)) + (modded.Count > 12 ? ", ..." : ""));
            }
            return lines;
        }

        private static void SetStatConstraint(JsonObject cond, string key, string compareMethod, double val)
        {
            if (cond[key] is JsonObject existing)
            {
                existing["compareMethod"] = compareMethod;
                existing["value"] = val;
            }
            else
            {
                cond[key] = new JsonObject
                {
                    ["compareMethod"] = compareMethod,
                    ["value"] = val
                };
            }
        }

        private string? BuildTemplateQuestJson(QuestJsonArgs a, bool includeStats)
        {
            try
            {
                if (_templateQuest == null || _templateAssembly == null) return null;
                var legacyNode = JsonNode.Parse(BuildQuestJson(a, includeStats)) as JsonObject;
                var qObj = JsonNode.Parse(_templateQuest.ToJsonString()) as JsonObject;
                var condObj = JsonNode.Parse(_templateAssembly.ToJsonString()) as JsonObject;
                if (legacyNode == null || qObj == null || condObj == null) return null;

                // Copy top-level fields from legacyNode while preserving any extra required SPT 4.1 properties on qObj
                foreach (var kv in legacyNode)
                {
                    if (string.Equals(kv.Key, "conditions", StringComparison.OrdinalIgnoreCase)) continue;
                    qObj[kv.Key] = kv.Value?.DeepClone();
                }

                // Configure WeaponAssembly condition from the native template
                condObj["id"] = a.CondId;
                condObj["index"] = 0;
                condObj["conditionType"] = "WeaponAssembly";
                condObj["parentId"] = "";
                condObj["dynamicLocale"] = false;
                condObj["value"] = 1;
                condObj["visibilityConditions"] = new JsonArray();

                if (condObj["target"] is JsonObject targetObj)
                    targetObj["id"] = new JsonArray(JsonValue.Create(a.WeaponTpl));
                else
                    condObj["target"] = new JsonArray(JsonValue.Create(a.WeaponTpl));

                var containsArr = new JsonArray();
                if (!string.IsNullOrWhiteSpace(a.ContainsItemsJson) && JsonNode.Parse("[" + a.ContainsItemsJson + "]") is JsonArray parsedContains)
                {
                    foreach (var item in parsedContains)
                        if (item != null) containsArr.Add(JsonValue.Create(item.GetValue<string>()));
                }
                if (condObj["containsItems"] is JsonObject containsObj)
                    containsObj["id"] = containsArr;
                else
                    condObj["containsItems"] = containsArr;

                var catArr = new JsonArray();
                foreach (var c in a.Categories) catArr.Add(JsonValue.Create(c));
                if (condObj["hasItemFromCategory"] is JsonObject catObj)
                    catObj["id"] = catArr;
                else
                    condObj["hasItemFromCategory"] = catArr;

                SetStatConstraint(condObj, "durability", ">=", 80);
                SetStatConstraint(condObj, "weight", "<=", Math.Round(a.MaxWeight, 1));

                if (includeStats)
                {
                    SetStatConstraint(condObj, "ergonomics", ">=", Math.Round(a.MinErgonomics));
                    SetStatConstraint(condObj, "recoil", "<=", Math.Round(a.MaxRecoil));
                }
                else
                {
                    SetStatConstraint(condObj, "ergonomics", ">=", 0);
                    SetStatConstraint(condObj, "recoil", "<=", 2000);
                }

                SetStatConstraint(condObj, "height", "<=", 20);
                SetStatConstraint(condObj, "width", "<=", 20);
                SetStatConstraint(condObj, "magazineCapacity", ">=", 0);
                SetStatConstraint(condObj, "muzzleVelocity", ">=", 0);
                SetStatConstraint(condObj, "effectiveDistance", ">=", 0);
                SetStatConstraint(condObj, "emptyTacticalSlot", ">=", 0);
                SetStatConstraint(condObj, "baseAccuracy", ">=", 0);

                var conditions = qObj["conditions"] as JsonObject ?? new JsonObject();
                qObj["conditions"] = conditions;
                conditions["AvailableForStart"] = legacyNode["conditions"]?["AvailableForStart"]?.DeepClone() ?? new JsonArray();
                conditions["AvailableForFinish"] = new JsonArray(condObj);
                conditions["Fail"] = new JsonArray();

                return qObj.ToJsonString();
            }
            catch
            {
                return null;
            }
        }

        private string BuildQuestJson(QuestJsonArgs a, bool includeStats)
        {
            string questId = a.QuestId, condId = a.CondId, weaponTplId = a.WeaponTpl, imageIcon = a.Icon;
            float minErgonomics = a.MinErgonomics, maxRecoil = a.MaxRecoil;
            string containsItemsJson = a.ContainsItemsJson;
            string rewardExpId = a.ExpId, rewardMoneyId = a.MoneyId, rewardItemId = a.MoneyItemId, rewardRepId = a.RepId;
            string lootRewardsJson = a.LootRewardsJson, standingReward = a.Standing;
            int expReward = a.Exp, roublesReward = a.Roubles, repIndex = a.RepIndex;

            // quest becomes available at the tier's level
            var levelParts = new List<string>();
            if (a.MinLevel > 1)
                levelParts.Add(
                  "\n      {\n" +
                  "        \"compareMethod\": \">=\",\n" +
                  "        \"conditionType\": \"Level\",\n" +
                  "        \"dynamicLocale\": false,\n" +
                  "        \"globalQuestCounter\": 0,\n" +
                  "        \"id\": \"" + a.LevelCondId + "\",\n" +
                  "        \"index\": 0,\n" +
                  "        \"parentId\": \"\",\n" +
                  "        \"value\": " + a.MinLevel + ",\n" +
                  "        \"visibilityConditions\": []\n" +
                  "      }");
            if (a.MaxLevel >= a.MinLevel && a.MaxLevel > 0)
                levelParts.Add(
                  "\n      {\n" +
                  "        \"compareMethod\": \"<=\",\n" +
                  "        \"conditionType\": \"Level\",\n" +
                  "        \"dynamicLocale\": false,\n" +
                  "        \"globalQuestCounter\": 0,\n" +
                  "        \"id\": \"" + a.MaxLevelCondId + "\",\n" +
                  "        \"index\": " + levelParts.Count + ",\n" +
                  "        \"parentId\": \"\",\n" +
                  "        \"value\": " + a.MaxLevel + ",\n" +
                  "        \"visibilityConditions\": []\n" +
                  "      }");
            string levelBlock = levelParts.Count > 0 ? string.Join(",", levelParts) + "\n    " : "";

            string categoriesJson = string.Join(", ", a.Categories.Select(c => "\"" + c + "\""));
            string weightText = a.MaxWeight.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

            string statsBlock = "";
            if (includeStats)
            {
                statsBlock = string.Format(System.Globalization.CultureInfo.InvariantCulture, @"
        ""ergonomics"": {{
          ""compareMethod"": "">="",
          ""value"": {0}
        }},
        ""recoil"": {{
          ""compareMethod"": ""<="",
          ""value"": {1}
        }},", minErgonomics, maxRecoil);
            }

            // The required parts are ALWAYS part of the condition (also in the fallback without stat limits).
            string containsBlock = "\n        \"containsItems\": [ " + containsItemsJson + " ],";

            return "{\n" +
                "  \"_id\": \"" + questId + "\",\n" +
                "  \"QuestName\": \"Оружейник: Ежедневный заказ\",\n" +
                "  \"canShowNotificationsInGame\": true,\n" +
                "  \"conditions\": {\n" +
                "    \"AvailableForStart\": [" + levelBlock + "],\n" +
                "    \"AvailableForFinish\": [\n" +
                "      {\n" +
                "        \"id\": \"" + condId + "\",\n" +
                "        \"index\": 0,\n" +
                "        \"conditionType\": \"WeaponAssembly\",\n" +
                "        \"parentId\": \"\",\n" +
                "        \"dynamicLocale\": false,\n" +
                "        \"value\": 1,\n" +
                "        \"visibilityConditions\": [],\n" +
                "        \"target\": [\n" +
                "          \"" + weaponTplId + "\"\n" +
                "        ]," + statsBlock + containsBlock + "\n" +
                "        \"durability\": {\n" +
                "          \"compareMethod\": \">=\",\n" +
                "          \"value\": 80\n" +
                "        },\n" +
                "        \"hasItemFromCategory\": [ " + categoriesJson + " ],\n" +
                "        \"height\": {\n" +
                "          \"compareMethod\": \"<=\",\n" +
                "          \"value\": 20\n" +
                "        },\n" +
                "        \"width\": {\n" +
                "          \"compareMethod\": \"<=\",\n" +
                "          \"value\": 20\n" +
                "        },\n" +
                "        \"magazineCapacity\": {\n" +
                "          \"compareMethod\": \">=\",\n" +
                "          \"value\": 0\n" +
                "        },\n" +
                "        \"muzzleVelocity\": {\n" +
                "          \"compareMethod\": \">=\",\n" +
                "          \"value\": 0\n" +
                "        },\n" +
                "        \"weight\": {\n" +
                "          \"compareMethod\": \"<=\",\n" +
                "          \"value\": " + weightText + "\n" +
                "        },\n" +
                "        \"effectiveDistance\": {\n" +
                "          \"compareMethod\": \">=\",\n" +
                "          \"value\": 0\n" +
                "        },\n" +
                "        \"emptyTacticalSlot\": {\n" +
                "          \"compareMethod\": \">=\",\n" +
                "          \"value\": 0\n" +
                "        },\n" +
                "        \"baseAccuracy\": {\n" +
                "          \"compareMethod\": \">=\",\n" +
                "          \"value\": 0\n" +
                "        }\n" +
                "      }\n" +
                "    ],\n" +
                "    \"Fail\": []\n" +
                "  },\n" +
                "  \"description\": \"" + questId + " description\",\n" +
                "  \"failMessageText\": \"" + questId + " failMessageText\",\n" +
                "  \"name\": \"" + questId + " name\",\n" +
                "  \"note\": \"" + questId + " note\",\n" +
                "  \"traderId\": \"" + _traderId + "\",\n" +
                "  \"location\": \"any\",\n" +
                "  \"image\": \"" + imageIcon + "\",\n" +
                "  \"type\": \"WeaponAssembly\",\n" +
                "  \"isKey\": false,\n" +
                "  \"restartable\": false,\n" +
                "  \"instantComplete\": false,\n" +
                "  \"secretQuest\": false,\n" +
                "  \"startedMessageText\": \"" + questId + " startedMessageText\",\n" +
                "  \"successMessageText\": \"" + questId + " successMessageText\",\n" +
                "  \"acceptPlayerMessage\": \"" + questId + " acceptPlayerMessage\",\n" +
                "  \"declinePlayerMessage\": \"" + questId + " declinePlayerMessage\",\n" +
                "  \"completePlayerMessage\": \"" + questId + " completePlayerMessage\",\n" +
                "  \"templateId\": \"" + questId + "\",\n" +
                "  \"rewards\": {\n" +
                "    \"Started\": [],\n" +
                "    \"Success\": [\n" +
                "      {\n" +
                "        \"id\": \"" + rewardExpId + "\",\n" +
                "        \"type\": \"Experience\",\n" +
                "        \"index\": 0,\n" +
                "        \"value\": " + expReward + ",\n" +
                "        \"unknown\": false\n" +
                "      },\n" +
                "      {\n" +
                "        \"id\": \"" + rewardMoneyId + "\",\n" +
                "        \"type\": \"Item\",\n" +
                "        \"index\": 1,\n" +
                "        \"target\": \"" + rewardItemId + "\",\n" +
                "        \"value\": " + roublesReward + ",\n" +
                "        \"findInRaid\": true,\n" +
                "        \"unknown\": false,\n" +
                "        \"items\": [\n" +
                "          {\n" +
                "            \"_id\": \"" + rewardItemId + "\",\n" +
                "            \"_tpl\": \"5449016a4bdc2d6f028b456f\",\n" +
                "            \"upd\": {\n" +
                "              \"StackObjectsCount\": " + roublesReward + "\n" +
                "            }\n" +
                "          }\n" +
                "        ]\n" +
                "      },\n" +
                lootRewardsJson +
                "      {\n" +
                "        \"id\": \"" + rewardRepId + "\",\n" +
                "        \"type\": \"TraderStanding\",\n" +
                "        \"index\": " + repIndex + ",\n" +
                "        \"target\": \"" + _traderId + "\",\n" +
                "        \"value\": " + standingReward + ",\n" +
                "        \"unknown\": false\n" +
                "      }\n" +
                "    ],\n" +
                "    \"Fail\": []\n" +
                "  },\n" +
                "  \"side\": \"Pmc\",\n" +
                "  \"status\": 0,\n" +
                "  \"KeyId\": \"\",\n" +
                "  \"changeQuestMessageText\": \"\"\n" +
                "}";
        }
    }
}
