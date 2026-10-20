using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DailyGunsmith;

internal sealed class DryRunEntry
{
    public GeneratedQuest Quest = new();
    public bool Sample;
    public string Validity = "";
}

/// <summary>Writes generated quests as a readable text report plus a JSON file (for debugging and feedback).</summary>
internal static class DryRunExporter
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string Write(string folder, string period, IReadOnlyList<DryRunEntry> entries, Func<string, string> nameOf, Func<GeneratedQuest, string> descriptionOf, IReadOnlyList<string>? header = null)
    {
        System.IO.Directory.CreateDirectory(folder);
        string basePath = System.IO.Path.Combine(folder, "quests_" + period);

        System.IO.File.WriteAllText(basePath + ".txt", BuildText(entries, nameOf, descriptionOf, header), new UTF8Encoding(false));
        System.IO.File.WriteAllText(basePath + ".json", BuildJson(entries, nameOf).ToJsonString(Pretty), new UTF8Encoding(false));
        return basePath;
    }

    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private static string BuildText(IReadOnlyList<DryRunEntry> entries, Func<string, string> nameOf, Func<GeneratedQuest, string> descriptionOf, IReadOnlyList<string>? header)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Daily Gunsmith dry run - " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        sb.AppendLine("Quests: " + entries.Count);
        if (header != null) foreach (var h in header) sb.AppendLine(h);
        sb.AppendLine();

        int n = 0;
        foreach (var e in entries)
        {
            var q = e.Quest;
            n++;
            sb.AppendLine($"=== #{n} ({(e.Sample ? "sample" : "registered")}) | tier {q.TierName} | level {q.MinLevel} | json: {e.Validity} ===");
            sb.AppendLine($"Quest id : {q.Id}");
            sb.AppendLine($"Weapon   : {nameOf(q.WeaponTpl)} [{q.WeaponTpl}] class={q.WeaponClass}");
            sb.AppendLine($"Limits   : ergonomics >= {F(q.MinErgonomics)} | recoil <= {F(q.MaxRecoil)} | weight <= {F(q.MaxWeight)} kg");
            sb.AppendLine("Category : " + (q.Categories.Count == 0 ? "-" : string.Join(", ", q.Categories.Select(c => nameOf(c) + " [" + c + "]"))) + "  (any item of the category counts)");
            sb.AppendLine("Hand-in  : " + q.HandInCheck);
            sb.AppendLine($"Parts    : {q.Parts.Count(p => p.Listed)} required, {q.Parts.Count} in the tree");
            AppendTree(sb, q.Parts, -1, 1, nameOf);
            sb.AppendLine($"Rewards  : {q.RoublesReward} RUB, {q.ExpReward} EXP, standing {q.Standing}");
            foreach (var r in q.LootRewards)
            {
                sb.AppendLine($"           - {nameOf(r.Tpl)} x{r.Count} [{r.Tpl}]" + (r.Parts.Count > 1 ? $" assembled, {r.Parts.Count} parts" : ""));
                if (r.Parts.Count > 1) AppendTree(sb, r.Parts, -1, 3, nameOf);
            }
            sb.AppendLine("Text     : " + descriptionOf(q));
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static void AppendTree(StringBuilder sb, List<PartRecord> parts, int parentIndex, int indent, Func<string, string> nameOf)
    {
        for (int i = 0; i < parts.Count; i++)
        {
            var p = parts[i];
            if (p.ParentIndex != parentIndex) continue;
            string mark = p.ProofCategory != null && !p.Listed ? "  <- any item of this category is enough" : (p.Listed ? "" : "  (not listed)");
            sb.AppendLine(new string(' ', indent * 2) + $"{p.SlotName}: {nameOf(p.Tpl)} [{p.Tpl}]{mark}");
            AppendTree(sb, parts, i, indent + 1, nameOf);
        }
    }

    private static JsonArray PartsJson(List<PartRecord> parts, Func<string, string> nameOf)
    {
        var arr = new JsonArray();
        for (int i = 0; i < parts.Count; i++)
        {
            var p = parts[i];
            arr.Add(new JsonObject
            {
                ["index"] = i,
                ["parentIndex"] = p.ParentIndex,
                ["slot"] = p.SlotName,
                ["tpl"] = p.Tpl,
                ["name"] = nameOf(p.Tpl),
                ["listedInQuest"] = p.Listed,
                ["categoryProof"] = p.ProofCategory
            });
        }
        return arr;
    }

    private static JsonArray BuildJson(IReadOnlyList<DryRunEntry> entries, Func<string, string> nameOf)
    {
        var result = new JsonArray();
        foreach (var e in entries)
        {
            var q = e.Quest;
            JsonNode? questJson = null;
            try { questJson = JsonNode.Parse(q.Json); } catch { /* shown as null */ }

            var loot = new JsonArray();
            foreach (var r in q.LootRewards)
                loot.Add(new JsonObject { ["tpl"] = r.Tpl, ["name"] = nameOf(r.Tpl), ["count"] = r.Count, ["parts"] = PartsJson(r.Parts, nameOf) });

            var cats = new JsonArray();
            foreach (var c in q.Categories) cats.Add(new JsonObject { ["id"] = c, ["name"] = nameOf(c) });

            result.Add(new JsonObject
            {
                ["kind"] = e.Sample ? "sample" : "registered",
                ["jsonValidity"] = e.Validity,
                ["handInCheck"] = q.HandInCheck,
                ["questId"] = q.Id,
                ["tier"] = q.TierName,
                ["minLevel"] = q.MinLevel,
                ["weapon"] = new JsonObject { ["tpl"] = q.WeaponTpl, ["name"] = nameOf(q.WeaponTpl), ["class"] = q.WeaponClass },
                ["limits"] = new JsonObject { ["minErgonomics"] = q.MinErgonomics, ["maxRecoil"] = q.MaxRecoil, ["maxWeightKg"] = q.MaxWeight },
                ["requiredCategories"] = cats,
                ["parts"] = PartsJson(q.Parts, nameOf),
                ["rewards"] = new JsonObject { ["roubles"] = q.RoublesReward, ["experience"] = q.ExpReward, ["standing"] = q.Standing, ["items"] = loot },
                ["questJson"] = questJson
            });
        }
        return result;
    }
}
