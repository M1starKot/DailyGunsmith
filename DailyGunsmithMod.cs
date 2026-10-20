using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Locales;
using SPTarkov.Server.Core.Utils;

namespace DailyGunsmith;

[Injectable(TypePriority = OnLoadOrder.PostLoad)]
public class DailyGunsmithMod(
    ISptLogger<DailyGunsmithMod> logger,
    TemplateTable templates,
    LocaleTable localeTable,
    LocaleService localeService,
    JsonUtil jsonUtil,
    ModHelper modHelper) : IOnLoad
{
    private static readonly string[] TemplateQuestIds = { "5ac23c6186f7741247042bad", "5ac2426c86f774138762edfe" };

    private readonly object _lock = new();
    private ModConfig _config = new();
    private readonly Dictionary<string, Dictionary<string, string>> _texts = new(StringComparer.OrdinalIgnoreCase);
    private volatile List<GeneratedQuest> _quests = new();
    private IDictionary<string, string>? _enNames;
    private HashSet<string>? _vanillaIds;
    private bool _vanillaLoaded;
    private List<string> _activeIds = new();
    private string _currentDay = "";
    private Timer? _timer;
    private bool _groupInfoLogged;
    private bool _poolLogged;
    private bool _configValidated;
    private string _modFolder = "";

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var folder = System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? AppContext.BaseDirectory;
        try
        {
            var helperFolder = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
            if (!string.IsNullOrWhiteSpace(helperFolder)) folder = helperFolder;
            var configPath = System.IO.Path.Combine(folder, "config", "config.json");
            if (System.IO.File.Exists(configPath))
            {
                // Sanitize non-breaking spaces (U+00A0) that break JSON parsers when copy-pasted
                var rawJson = System.IO.File.ReadAllText(configPath, Encoding.UTF8).Replace(' ', ' ');
                var opts = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                };
                _config = JsonSerializer.Deserialize<ModConfig>(rawJson, opts) ?? new ModConfig();
            }
            else
            {
                _config = modHelper.GetJsonDataFromFile<ModConfig>(System.IO.Path.Combine(folder, "config"), "config.json") ?? new ModConfig();
            }
        }
        catch (Exception ex)
        {
            logger.Warning($"[DailyGunsmith] Could not read config.json, using defaults: {ex.Message}");
        }

        _modFolder = folder;

        if (!_config.Enabled) return Task.CompletedTask;
        if (_config.Tiers.Count == 0) _config.Tiers.Add(new TierConfig { Name = "Standard", Weight = 1 });

        LoadTexts(folder);

        try { _enNames = localeService.GetLocaleDb("en"); }
        catch (Exception ex) { logger.Warning($"[DailyGunsmith] Could not read the English locale: {ex.Message}"); }

        foreach (var (lang, lazy) in localeTable.Global)
        {
            var code = lang;
            lazy.AddTransformer(d =>
            {
                if (d is null) return d;
                ApplyLocale(code, d);
                return d;
            });
        }

        Regenerate(pushToLoadedLocales: true);

        _timer = new Timer(_ => CheckRollover(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        return Task.CompletedTask;
    }

    // ---------------------------------------------------------------------------------------------
    // Localization: locales/<language>.json  (en is the fallback for every language)
    // ---------------------------------------------------------------------------------------------

    private void LoadTexts(string modFolder)
    {
        try
        {
            var dir = System.IO.Path.Combine(modFolder, "locales");
            if (!System.IO.Directory.Exists(dir))
            {
                logger.Warning("[DailyGunsmith] 'locales' folder not found, using built-in English texts.");
                return;
            }

            foreach (var file in System.IO.Directory.GetFiles(dir, "*.json"))
            {
                try
                {
                    var map = JsonSerializer.Deserialize<Dictionary<string, string>>(System.IO.File.ReadAllText(file, Encoding.UTF8));
                    if (map != null) _texts[System.IO.Path.GetFileNameWithoutExtension(file)] = map;
                }
                catch (Exception ex)
                {
                    logger.Warning($"[DailyGunsmith] Could not read locale file {System.IO.Path.GetFileName(file)}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            logger.Warning($"[DailyGunsmith] Could not read locales: {ex.Message}");
        }
    }

    private Dictionary<string, string> TextsFor(string lang)
    {
        var merged = new Dictionary<string, string>(QuestGenerator.DefaultTexts);
        if (_texts.TryGetValue("en", out var en)) foreach (var kv in en) merged[kv.Key] = kv.Value;
        if (!lang.Equals("en", StringComparison.OrdinalIgnoreCase) && _texts.TryGetValue(lang, out var own))
            foreach (var kv in own) merged[kv.Key] = kv.Value;
        return merged;
    }

    private static string? Lookup(IDictionary<string, string>? db, string id, string kind)
    {
        if (db == null) return null;
        if (db.TryGetValue($"{id} {kind}", out var v) && !string.IsNullOrWhiteSpace(v)) return v;
        if (kind != "Name" && db.TryGetValue($"{id} Name", out v) && !string.IsNullOrWhiteSpace(v)) return v;
        return null;
    }

    /// <summary>Writes the current daily quests' texts into one language's locale dictionary.</summary>
    private void ApplyLocale(string lang, IDictionary<string, string> dict)
    {
        var quests = _quests;
        if (quests.Count == 0) return;

        var texts = TextsFor(lang);
        string NameOf(string id, string kind) => Lookup(dict, id, kind) ?? Lookup(_enNames, id, kind) ?? id;

        foreach (var q in quests)
            foreach (var kv in QuestGenerator.BuildLocale(q, texts, NameOf))
                dict[kv.Key] = kv.Value;
    }

    // ---------------------------------------------------------------------------------------------
    // Vanilla item detection (used by weapons.includeModded / modules.includeModded = false)
    // ---------------------------------------------------------------------------------------------

    private HashSet<string>? LoadVanillaIds()
    {
        if (_vanillaLoaded) return _vanillaIds;
        _vanillaLoaded = true;

        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(_config.VanillaItemsPath)) candidates.Add(_config.VanillaItemsPath);
        foreach (var root in GetSptRoots())
        {
            candidates.Add(System.IO.Path.Combine(root, "SPT_Data", "database", "templates", "items.json"));
            candidates.Add(System.IO.Path.Combine(root, "SPT_Data", "Server", "database", "templates", "items.json"));
        }

        foreach (var path in candidates.Distinct())
        {
            try
            {
                if (!System.IO.File.Exists(path)) continue;
                using var doc = JsonDocument.Parse(System.IO.File.ReadAllBytes(path));
                var ids = new HashSet<string>();
                foreach (var p in doc.RootElement.EnumerateObject()) ids.Add(p.Name);
                _vanillaIds = ids;
                return ids;
            }
            catch (Exception ex)
            {
                logger.Warning($"[DailyGunsmith] Could not read {path}: {ex.Message}");
            }
        }

        logger.Warning("[DailyGunsmith] Vanilla items.json not found - the vanilla/modded split is unknown ('includeModded: false' is ignored). Set 'vanillaItemsPath' in config.json.");
        return null;
    }

    private IEnumerable<string> GetSptRoots()
    {
        var roots = new List<string> { AppContext.BaseDirectory, System.IO.Directory.GetCurrentDirectory() };
        if (!string.IsNullOrWhiteSpace(_modFolder))
        {
            var dir = new System.IO.DirectoryInfo(_modFolder);
            for (int i = 0; i < 4 && dir != null; i++, dir = dir.Parent)
                roots.Add(dir.FullName);
        }
        return roots.Distinct();
    }

    // ---------------------------------------------------------------------------------------------
    // Daily generation
    // ---------------------------------------------------------------------------------------------

    private void LogBlue(string message)
    {
        try
        {
            var colorType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => { try { return a.GetTypes(); } catch { return Type.EmptyTypes; } })
                .FirstOrDefault(t => t.IsEnum && t.Name == "LogTextColor");
            var method = logger.GetType().GetMethods().FirstOrDefault(m => m.Name == "LogWithColor");

            if (colorType != null && method != null && Enum.IsDefined(colorType, "Blue"))
            {
                var ps = method.GetParameters();
                var args = new object?[ps.Length];
                args[0] = message;
                for (var i = 1; i < ps.Length; i++)
                    args[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue : null;
                args[1] = Enum.Parse(colorType, "Blue");
                method.Invoke(logger, args);
                return;
            }
        }
        catch
        {
            // fall back below
        }

        logger.Info("\u001b[34m" + message + "\u001b[0m");
    }

    private void CheckRollover()
    {
        try
        {
            if (CurrentPeriod() != _currentDay) Regenerate(pushToLoadedLocales: _config.RefreshLoadedLocales);
        }
        catch (Exception ex)
        {
            logger.Error($"[DailyGunsmith] Daily refresh failed: {ex}");
        }
    }

    private static readonly DateTime PeriodAnchor = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

    private int IntervalHours => Math.Clamp(_config.RefreshIntervalHours, 1, 24 * 365);

    private string CurrentPeriod()
    {
        var now = _config.UseUtc ? DateTime.UtcNow : DateTime.Now;
        if (IntervalHours == 24) return now.ToString("yyyy-MM-dd");

        var hoursSinceAnchor = (long)Math.Floor((now - PeriodAnchor).TotalHours);
        return $"h{IntervalHours}-{hoursSinceAnchor / IntervalHours}";
    }

    private void Regenerate(bool pushToLoadedLocales)
    {
        lock (_lock)
        {
            var day = CurrentPeriod();
            var quests = templates.Quests;
            if (quests == null)
            {
                logger.Error("[DailyGunsmith] templates.Quests is null - cannot register quests.");
                return;
            }

            foreach (var oldId in _activeIds) quests.Remove(new MongoId(oldId));
            _activeIds = new List<string>();

            var catalog = ItemCatalog.Build(templates, jsonUtil);
            if (!_config.Weapons.IncludeModded || !_config.Modules.IncludeModded || !_groupInfoLogged)
                catalog.VanillaIds = LoadVanillaIds();

            if (!_configValidated)
            {
                _configValidated = true;
                foreach (var warning in ConfigValidator.Validate(_config, catalog))
                    logger.Warning($"[DailyGunsmith] config.json: {warning}");
            }

            var template = FindTemplate(quests);
            if (template == null)
            {
                logger.Error("[DailyGunsmith] No WeaponAssembly (Gunsmith) quest found to use as a template. No daily quests generated.");
                _currentDay = day;
                return;
            }
            var gunsmithIcons = CollectGunsmithIcons(quests);

            var seed = _config.DeterministicPerDay
                ? BitConverter.ToInt32(MD5.HashData(Encoding.UTF8.GetBytes(_config.SeedSalt + day)), 0)
                : Environment.TickCount;
            var rng = new Random(seed);

            var generator = new QuestGenerator(_config, catalog, rng, gunsmithIcons, template.Value.quest, template.Value.assembly);
            if (!_poolLogged)
            {
                _poolLogged = true;
                LogBlue($"[DailyGunsmith] Weapon pool: {generator.WeaponPoolSize} weapons (vanilla + modded), {catalog.Items.Count} items scanned.");
            }

            _groupInfoLogged = true; // the category group listing is no longer printed to the server log

            foreach (var kv in generator.RewardPoolSizes)
                if (kv.Value == 0)
                    logger.Warning($"[DailyGunsmith] No eligible items for the '{kv.Key}' reward category - it will not appear in quest rewards.");

            var generated = new List<GeneratedQuest>();
            var dryRunGenerated = new List<GeneratedQuest>();
            var usedWeapons = new HashSet<string>();

            // Каждая сложность даёт tier.QuestsPerDay квестов (по умолчанию 1) - редактируется в config.json.
            var tierPlan = BuildTierPlan();
            int plannedCount = tierPlan.Count;

            for (var i = 0; i < tierPlan.Count; i++)
            {
                var tier = tierPlan[i];
                // A quest whose own hand-in check fails can never be completed by the player: regenerate it (another weapon /
                // build) instead of registering it. Only if every attempt fails is the quest skipped.
                GeneratedQuest? gen = null;
                const int maxAttempts = 8;
                for (var attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    var candidate = generator.Generate(day, i, tier, usedWeapons);
                    if (candidate == null) break;
                    if (candidate.HandInCheck.StartsWith("ok", StringComparison.Ordinal)) { gen = candidate; break; }
                    logger.Warning($"[DailyGunsmith] Hand-in check failed for quest #{i + 1} ({tier.Name}), attempt {attempt}/{maxAttempts}: {candidate.HandInCheck} - regenerating.");
                }
                if (gen == null)
                {
                    logger.Warning($"[DailyGunsmith] Could not generate a completable daily quest #{i + 1} ({tier.Name}) - skipped.");
                    continue;
                }

                dryRunGenerated.Add(gen);

                Quest? quest = TryDeserialize(gen.Json);
                if (quest == null && gen.JsonWithoutStats != gen.Json)
                {
                    logger.Warning("[DailyGunsmith] Stat constraints were rejected by the deserializer; falling back to parts-only quest.");
                    quest = TryDeserialize(gen.JsonWithoutStats);
                }
                if (quest == null && !string.IsNullOrWhiteSpace(gen.LegacyJson))
                {
                    logger.Warning("[DailyGunsmith] Template-based quest JSON failed deserialization; falling back to legacy quest JSON.");
                    quest = TryDeserialize(gen.LegacyJson);
                }
                if (quest == null) continue;

                quests[new MongoId(gen.Id)] = quest;
                _activeIds.Add(gen.Id);
                generated.Add(gen);
            }

            _quests = generated;
            _currentDay = day;

            LogBlue($"[DailyGunsmith] Generated and registered {generated.Count}/{plannedCount} daily quests for period '{day}' ({string.Join(", ", _config.Tiers.Select(t => $"{t.Name}: {Math.Max(0, t.QuestsPerDay)}"))}).");

            if (_config.DryRun is { Enabled: true }) ExportDryRun(generator, dryRunGenerated, day);

            if (pushToLoadedLocales) PushToLoadedLocales();
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Dry run: report of the generated quests (+ extra samples) for testing / feedback
    // ---------------------------------------------------------------------------------------------

    private string CheckJson(GeneratedQuest q)
    {
        try { JsonNode.Parse(q.Json); }
        catch (Exception ex) { return "INVALID JSON: " + ex.Message; }

        try { if (jsonUtil.Deserialize<Quest>(q.Json) != null) return "ok"; }
        catch { /* try the fallback below */ }

        try { if (jsonUtil.Deserialize<Quest>(q.JsonWithoutStats) != null) return "ok without ergonomics/recoil block (rejected by the deserializer)"; }
        catch (Exception ex) { return "DESERIALIZATION FAILED: " + ex.Message; }

        return "DESERIALIZATION FAILED";
    }

    private void ExportDryRun(QuestGenerator generator, List<GeneratedQuest> generated, string period)
    {
        try
        {
            var entries = new List<DryRunEntry>();
            foreach (var q in generated)
                entries.Add(new DryRunEntry { Quest = q, Sample = false, Validity = CheckJson(q) });

            var used = new HashSet<string>();
            for (var i = 0; i < Math.Max(0, _config.DryRun.SampleCount); i++)
            {
                var gen = generator.Generate(period, 100 + i, _config.Tiers[i % _config.Tiers.Count], used);
                if (gen != null) entries.Add(new DryRunEntry { Quest = gen, Sample = true, Validity = CheckJson(gen) });
            }

            var texts = TextsFor("en");
            string NameOf(string id) => Lookup(_enNames, id, "Name") ?? id;
            string DescriptionOf(GeneratedQuest q) =>
                QuestGenerator.BuildLocale(q, texts, (id, kind) => Lookup(_enNames, id, kind) ?? id)[q.Id + " description"];

            var folder = System.IO.Path.IsPathRooted(_config.DryRun.Folder)
                ? _config.DryRun.Folder
                : System.IO.Path.Combine(_modFolder, _config.DryRun.Folder);

            var path = DryRunExporter.Write(folder, period, entries, NameOf, DescriptionOf, generator.CategoryGroupInfo.Concat(generator.DescribeGroups()).ToList());
            logger.Info($"[DailyGunsmith] Dry run: {entries.Count} quests written to {path}.txt and {path}.json");
        }
        catch (Exception ex)
        {
            logger.Warning($"[DailyGunsmith] Dry run export failed: {ex.Message}");
        }
    }

    private Quest? TryDeserialize(string json)
    {
        try { return jsonUtil.Deserialize<Quest>(json); }
        catch (Exception ex)
        {
            logger.Error($"[DailyGunsmith] Quest deserialization failed: {ex.Message}");
            return null;
        }
    }

    private (JsonObject quest, JsonObject assembly)? FindTemplate(Dictionary<MongoId, Quest> quests)
    {
        foreach (var id in TemplateQuestIds)
            if (quests.TryGetValue(new MongoId(id), out var q) && Extract(jsonUtil.Serialize(q, false)) is { } found)
                return found;

        foreach (var q in quests.Values)
        {
            var json = jsonUtil.Serialize(q, false);
            if (json == null || !json.Contains("WeaponAssembly", StringComparison.OrdinalIgnoreCase)) continue;
            if (Extract(json) is { } found) return found;
        }
        return null;
    }

    private List<string> CollectGunsmithIcons(Dictionary<MongoId, Quest> quests)
    {
        var icons = new HashSet<string>();
        foreach (var q in quests.Values)
        {
            var json = jsonUtil.Serialize(q, false);
            if (json == null || !json.Contains("WeaponAssembly", StringComparison.OrdinalIgnoreCase)) continue;
            if (JsonNode.Parse(json) is JsonObject qObj && qObj["image"] is JsonValue imgVal && imgVal.TryGetValue<string>(out var img) && !string.IsNullOrWhiteSpace(img))
            {
                icons.Add(img);
            }
        }
        return icons.ToList();
    }

    private static (JsonObject quest, JsonObject assembly)? Extract(string? json)
    {
        if (json == null || JsonNode.Parse(json) is not JsonObject quest) return null;
        var conditions = quest["conditions"] as JsonObject ?? quest["Conditions"] as JsonObject;
        var finish = conditions?["AvailableForFinish"] as JsonArray ?? conditions?["availableForFinish"] as JsonArray;
        if (finish == null) return null;
        foreach (var c in finish)
        {
            if (c is JsonObject co)
            {
                var cType = co["conditionType"]?.GetValue<string>() ?? co["ConditionType"]?.GetValue<string>();
                if (string.Equals(cType, "WeaponAssembly", StringComparison.OrdinalIgnoreCase))
                    return (quest, co);
            }
        }
        return null;
    }

    /// <summary>
    /// Detects the highest PMC level among active SPT profiles (checking user/profiles/*.json across all candidate SPT roots).
    /// Returns 0 if no profile is found yet.
    /// </summary>
    private int GetHighestPlayerLevel()
    {
        int bestLevel = 0;

        foreach (var root in GetSptRoots())
        {
            var profilesDir = System.IO.Path.Combine(root, "user", "profiles");
            if (!System.IO.Directory.Exists(profilesDir)) continue;

            try
            {
                foreach (var file in System.IO.Directory.GetFiles(profilesDir, "*.json"))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(System.IO.File.ReadAllBytes(file));
                        if (TryExtractPmcLevel(doc.RootElement, out int lvl) && lvl > bestLevel)
                            bestLevel = lvl;
                    }
                    catch
                    {
                        // ignore malformed/partial profile file
                    }
                }
            }
            catch
            {
                // ignore directory access error
            }
        }

        return bestLevel;
    }

    private static bool TryExtractPmcLevel(JsonElement root, out int level)
    {
        level = 0;
        if (!TryGetPropIgnoreCase(root, "characters", out var chars)) return false;
        if (!TryGetPropIgnoreCase(chars, "pmc", out var pmc)) return false;
        if (!TryGetPropIgnoreCase(pmc, "Info", out var info)) return false;
        if (!TryGetPropIgnoreCase(info, "Level", out var lvlNode)) return false;
        return lvlNode.TryGetInt32(out level);
    }

    private static bool TryGetPropIgnoreCase(JsonElement obj, string name, out JsonElement value)
    {
        if (obj.ValueKind == JsonValueKind.Object)
        {
            if (obj.TryGetProperty(name, out value)) return true;
            foreach (var p in obj.EnumerateObject())
            {
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = p.Value;
                    return true;
                }
            }
        }
        value = default;
        return false;
    }

    private double EffectiveTierWeight(TierConfig tier, int playerLevel)
    {
        double baseWeight = Math.Max(0, tier.Weight);
        if (baseWeight <= 0 || !_config.ScaleTierWeightsByLevel || playerLevel <= 0)
            return baseWeight;

        // If player has not reached the tier's MinLevel yet, strongly reduce its chance (unless all tiers are above playerLevel)
        if (playerLevel < tier.MinLevel)
        {
            bool anyUnlocked = _config.Tiers.Any(t => t.Weight > 0 && playerLevel >= t.MinLevel);
            if (anyUnlocked) return 0;
            return baseWeight;
        }

        int levelsAboveMin = Math.Max(0, playerLevel - tier.MinLevel);

        if (Math.Abs(tier.LevelWeightScale) > 1e-6)
        {
            double multiplier = Math.Clamp(1.0 + levelsAboveMin * tier.LevelWeightScale, 0.15, 6.0);
            return baseWeight * multiplier;
        }

        int maxTierMinLevel = _config.Tiers.Max(t => t.MinLevel);
        int minTierMinLevel = _config.Tiers.Min(t => t.MinLevel);
        if (maxTierMinLevel <= minTierMinLevel) return baseWeight;

        double difficulty = (double)(tier.MinLevel - minTierMinLevel) / (maxTierMinLevel - minTierMinLevel);
        double progress = Math.Clamp((playerLevel - minTierMinLevel) / 40.0, 0.0, 1.0);

        double autoMultiplier = (1.0 - progress) + progress * (0.25 + 3.55 * Math.Pow(difficulty, 1.3));
        return baseWeight * autoMultiplier;
    }

    /// <summary>
    /// Builds the list of tiers to generate: every tier appears QuestsPerDay times (default 1; 0 disables the tier).
    /// Quests are server-wide and gated by each tier's level range (minLevel..maxLevel), so a player
    /// sees the quests of the tier matching his level.
    /// </summary>
    private List<TierConfig> BuildTierPlan()
    {
        var plan = new List<TierConfig>();
        foreach (var t in _config.Tiers)
            for (int n = 0; n < Math.Max(0, t.QuestsPerDay); n++)
                plan.Add(t);
        return plan;
    }

    private TierConfig PickTier(Random rng, int playerLevel = 0)
    {
        var weights = _config.Tiers.Select(t => EffectiveTierWeight(t, playerLevel)).ToList();
        var total = weights.Sum();
        if (total <= 0)
        {
            weights = _config.Tiers.Select(t => (double)Math.Max(0, t.Weight)).ToList();
            total = weights.Sum();
            if (total <= 0) return _config.Tiers[0];
        }

        var roll = rng.NextDouble() * total;
        for (var i = 0; i < _config.Tiers.Count; i++)
        {
            roll -= weights[i];
            if (roll <= 0) return _config.Tiers[i];
        }
        return _config.Tiers[_config.Tiers.Count - 1];
    }

    private void PushToLoadedLocales()
    {
        try
        {
            foreach (var (lang, lazy) in localeTable.Global)
                if (lazy.Value is { } dict)
                    ApplyLocale(lang, dict);
        }
        catch (Exception ex)
        {
            logger.Warning($"[DailyGunsmith] Could not refresh loaded locales: {ex.Message}");
        }
    }
}
