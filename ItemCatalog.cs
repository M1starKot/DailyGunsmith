using System.Text.Json.Nodes;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Utils;

namespace DailyGunsmith;

internal sealed class SlotInfo
{
    public string Name = "";
    public bool Required;
    public List<string> Filter = new();
}

internal sealed class ItemInfo
{
    public string Id = "";
    public string Parent = "";
    public string Type = "";
    public string FallbackName = "";
    public bool QuestItem;
    public bool Unlootable;
    public int StackMaxSize = 1;
    public double ArmorClass;
    public double Ergonomics;
    public double RecoilForceUp;
    public double RecoilForceBack;
    public double Recoil; // Процент изменения отдачи на модулях
    public double Weight;
    public double Durability;
    public double MaxDurability;
    public string WeapClass = "";
    public string AmmoCaliber = "";
    /// <summary>Highest magnification of a sight (max value of props.Zooms); 0 when the item has none.</summary>
    public double MaxZoom;
    public List<SlotInfo> Slots = new();
    public HashSet<string> Conflicts = new();
    /// <summary>Entries of ConflictingItems that are item categories (nodes), not concrete items: they match a whole subtree.</summary>
    public List<string> ConflictBases = new();
}

internal sealed class ItemCatalog
{
    public const string WeaponBase = "5422acb9af1c889c16000029";
    public const string ModBase = "5448fe124bdc2da5018b4567";
    public const string KnifeBase = "5447e1d04bdc2dff2f8b4567";
    public const string ThrowableBase = "543be6564bdc2df4348b4568";

    public readonly Dictionary<string, ItemInfo> Items = new();
    public readonly Dictionary<string, double> Prices = new();

    /// <summary>IDs of items shipped with the game (null if the vanilla items.json could not be found).</summary>
    public HashSet<string>? VanillaIds;

    /// <summary>True only when the vanilla list is known and the item is not part of it.</summary>
    public bool IsModded(string id) => VanillaIds != null && !VanillaIds.Contains(id);

    public static ItemCatalog Build(TemplateTable templates, JsonUtil json)
    {
        var catalog = new ItemCatalog();

        foreach (var (key, item) in templates.Items)
        {
            if (JsonNode.Parse(json.Serialize(item, false) ?? "{}") is not JsonObject node) continue;

            var info = new ItemInfo
            {
                Id = key.ToString(),
                Parent = Str(Get(node, "_parent")),
                Type = Str(Get(node, "_type")),
                FallbackName = Str(Get(node, "_name")),
            };

            var props = Get(node, "_props");
            if (props != null)
            {
                info.QuestItem = Bool(Get(props, "QuestItem"));
                info.Unlootable = Bool(Get(props, "Unlootable"));
                info.StackMaxSize = Math.Max(1, (int)Num(Get(props, "StackMaxSize")));
                info.ArmorClass = Num(Get(props, "ArmorClass"));
                info.Ergonomics = Num(Get(props, "Ergonomics"));
                info.RecoilForceUp = Num(Get(props, "RecoilForceUp"));
                info.RecoilForceBack = Num(Get(props, "RecoilForceBack"));
                info.Recoil = Num(Get(props, "Recoil"));
                info.Weight = Num(Get(props, "Weight"));
                info.Durability = Num(Get(props, "Durability"));
                info.MaxDurability = Num(Get(props, "MaxDurability"));
                info.WeapClass = Str(Get(props, "weapClass"));
                info.AmmoCaliber = Str(Get(props, "ammoCaliber"));
                info.MaxZoom = MaxNumber(Get(props, "Zooms"));

                if (Get(props, "ConflictingItems") is JsonArray conflicts)
                    foreach (var c in conflicts) { var s = Str(c); if (s != "") info.Conflicts.Add(s); }

                if (Get(props, "Slots") is JsonArray slots)
                {
                    foreach (var slotNode in slots)
                    {
                        var slot = new SlotInfo
                        {
                            Name = Str(Get(slotNode, "_name")),
                            Required = Bool(Get(slotNode, "_required"))
                        };
                        if (Get(Get(slotNode, "_props"), "filters") is JsonArray filters)
                            foreach (var f in filters)
                                if (Get(f, "Filter") is JsonArray ids)
                                    foreach (var id in ids) { var s = Str(id); if (s != "") slot.Filter.Add(s); }
                        info.Slots.Add(slot);
                    }
                }
            }
            catalog.Items[info.Id] = info;
        }

        // ConflictingItems may (rarely, mostly in modded items) name a category instead of a concrete item.
        foreach (var it in catalog.Items.Values)
            foreach (var c in it.Conflicts)
                if (catalog.Items.TryGetValue(c, out var target) && !string.Equals(target.Type, "Item", StringComparison.OrdinalIgnoreCase))
                    it.ConflictBases.Add(c);

        if (JsonNode.Parse(json.Serialize(templates.Handbook, false) ?? "{}") is JsonObject hb && Get(hb, "Items") is JsonArray hbItems)
        {
            foreach (var h in hbItems)
            {
                var id = Str(Get(h, "Id"));
                if (id != "") catalog.Prices[id] = Num(Get(h, "Price"));
            }
        }

        return catalog;
    }

    /// <summary>True if the two items cannot be installed on the same weapon (checked in both directions, incl. category entries).</summary>
    public bool AreConflicting(string a, string b)
    {
        if (a == b) return false;
        return ConflictsWith(a, b) || ConflictsWith(b, a);
    }

    private bool ConflictsWith(string owner, string other)
    {
        if (!Items.TryGetValue(owner, out var o)) return false;
        if (o.Conflicts.Contains(other)) return true;
        foreach (var b in o.ConflictBases)
            if (IsDescendantOf(other, b)) return true;
        return false;
    }

    /// <summary>Returns the first pair of conflicting items in the list (the weapon itself included), or null if there is none.</summary>
    public (string a, string b)? FindConflict(IReadOnlyList<string> tpls)
    {
        for (var i = 0; i < tpls.Count; i++)
            for (var j = i + 1; j < tpls.Count; j++)
                if (AreConflicting(tpls[i], tpls[j])) return (tpls[i], tpls[j]);
        return null;
    }

    public bool IsDescendantOf(string tpl, string baseId)
    {
        var current = tpl;
        for (var i = 0; i < 100 && current != ""; i++)
        {
            if (current == baseId) return true;
            if (!Items.TryGetValue(current, out var it)) return false;
            current = it.Parent;
        }
        return false;
    }

    public bool IsDescendantOfAny(string tpl, IEnumerable<string> bases) => bases.Any(b => IsDescendantOf(tpl, b));

    private static JsonNode? Get(JsonNode? n, string name)
    {
        if (n is not JsonObject o) return null;
        foreach (var kv in o)
            if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)) return kv.Value;
        return null;
    }

    private static string Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
    private static bool Bool(JsonNode? n) => n is JsonValue v && v.TryGetValue<bool>(out var b) && b;
    /// <summary>Largest number found anywhere inside a (nested) JSON array, 0 if none.</summary>
    private static double MaxNumber(JsonNode? n)
    {
        if (n is JsonArray arr)
        {
            double best = 0;
            foreach (var e in arr) best = Math.Max(best, MaxNumber(e));
            return best;
        }
        return Num(n);
    }

    private static double Num(JsonNode? n)
    {
        if (n is not JsonValue v) return 0;
        if (v.TryGetValue<double>(out var d)) return d;
        if (v.TryGetValue<string>(out var str) &&
            double.TryParse(str, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed)) return parsed;
        return 0;
    }
}