namespace DailyGunsmith;

internal enum StatObjective
{
    Default,
    Lightest,
    Heaviest,
    BestErgo,
    WorstErgo,
    BestRecoil,
    WorstRecoil
}

internal sealed class AssembledPart
{
    public string Tpl = "";
    /// <summary>Index of the parent in the part list, -1 for the root item.</summary>
    public int ParentIndex = -1;
    public string SlotName = "";
}

/// <summary>
/// Assembles a complete item from the item database: every mandatory slot is filled (recursively), so the result is a
/// ready-to-use weapon (receiver, barrel, magazine, ...) or armor with all its plates/soft inserts.
/// Works for vanilla and modded items alike because it only relies on the slot data of the items themselves.
/// </summary>
internal sealed class PartAssembler
{
    private readonly ItemCatalog _catalog;
    private readonly HashSet<string> _blacklist;

    public PartAssembler(ItemCatalog catalog, HashSet<string> blacklist)
    {
        _catalog = catalog;
        _blacklist = blacklist;
    }

    public static double TotalWeight(ItemCatalog catalog, IEnumerable<AssembledPart> parts) =>
        parts.Sum(p => catalog.Items.TryGetValue(p.Tpl, out var it) ? it.Weight : 0);

    public static (double ergoDelta, double recoilPct, double weight) TotalModsStats(ItemCatalog catalog, IEnumerable<AssembledPart> parts)
    {
        double ergo = 0, recoil = 0, weight = 0;
        bool first = true;
        foreach (var p in parts)
        {
            if (catalog.Items.TryGetValue(p.Tpl, out var it))
            {
                weight += it.Weight;
                if (!first)
                {
                    ergo += it.Ergonomics;
                    recoil += it.Recoil;
                }
            }
            first = false;
        }
        return (ergo, recoil, weight);
    }

    /// <param name="armor">armor rule: every slot that is not a "mod_*" accessory slot (plates, soft armor, collar, ...) is filled.
    /// Otherwise only required slots and the magazine slot are filled.</param>
    /// <param name="rng">random choice of parts; null = deterministic.</param>
    /// <param name="lightest">prefer the lightest parts (used to estimate the minimum weight of a working weapon).</param>
    /// <returns>the parts (index 0 = root) or null if some mandatory slot cannot be filled.</returns>
    public List<AssembledPart>? Assemble(string rootTpl, bool armor, Random? rng, bool lightest, int maxArmorClass = 0) =>
        Assemble(rootTpl, armor, rng, lightest ? StatObjective.Lightest : StatObjective.Default, maxArmorClass);

    public List<AssembledPart>? Assemble(string rootTpl, bool armor, Random? rng, StatObjective objective, int maxArmorClass = 0)
    {
        if (!_catalog.Items.ContainsKey(rootTpl)) return null;
        if (armor && maxArmorClass > 0 && _catalog.Items[rootTpl].ArmorClass > maxArmorClass) return null;
        var parts = new List<AssembledPart> { new AssembledPart { Tpl = rootTpl } };
        return Fill(0, armor, rng, objective, parts, 0, maxArmorClass) ? parts : null;
    }

    /// <summary>
    /// Completes all remaining mandatory slots (+ magazine) around an already placed tree of parts (index 0 = root weapon).
    /// Returns the completed part list, or null if some mandatory slot conflicts or cannot be filled.
    /// </summary>
    public List<AssembledPart>? CompleteWeapon(List<AssembledPart> prePlaced, bool lightest, Random? rng = null) =>
        CompleteWeapon(prePlaced, lightest ? StatObjective.Lightest : StatObjective.Default, rng);

    public List<AssembledPart>? CompleteWeapon(List<AssembledPart> prePlaced, StatObjective objective, Random? rng = null)
    {
        if (prePlaced.Count == 0 || !_catalog.Items.ContainsKey(prePlaced[0].Tpl)) return null;
        var parts = prePlaced.Select(p => new AssembledPart { Tpl = p.Tpl, ParentIndex = p.ParentIndex, SlotName = p.SlotName }).ToList();
        if (HasConflict(_catalog, parts)) return null; // the placed parts themselves must not conflict
        if (!FillExisting(0, parts, objective, 0, rng)) return null;
        return HasConflict(_catalog, parts) ? null : parts;
    }

    private bool FillExisting(int index, List<AssembledPart> parts, StatObjective objective, int depth, Random? rng = null)
    {
        if (depth > 8) return false;
        if (!_catalog.Items.TryGetValue(parts[index].Tpl, out var owner)) return false;

        foreach (var slot in owner.Slots)
        {
            var existingIdx = parts.FindIndex(p => p.ParentIndex == index && string.Equals(p.SlotName, slot.Name, StringComparison.OrdinalIgnoreCase));
            if (existingIdx >= 0)
            {
                if (!FillExisting(existingIdx, parts, objective, depth + 1, rng)) return false;
                continue;
            }

            if (!MustFill(slot, false)) continue;

            var effRng = objective == StatObjective.Default ? rng : null;
            var candidates = Order(slot.Filter.Where(IsUsable).Where(c => !Conflicts(parts, c)), false, effRng, objective);
            bool filled = false;
            foreach (var candidate in candidates)
            {
                int mark = parts.Count;
                parts.Add(new AssembledPart { Tpl = candidate, ParentIndex = index, SlotName = slot.Name });
                if (Fill(parts.Count - 1, false, effRng, objective, parts, depth + 1)) { filled = true; break; }
                parts.RemoveRange(mark, parts.Count - mark);
            }

            if (!filled) return false;
        }
        return true;
    }

    private bool IsUsable(string tpl) =>
        _catalog.Items.TryGetValue(tpl, out var it) && it.Type == "Item" && !it.QuestItem && !_blacklist.Contains(tpl);

    private bool Conflicts(List<AssembledPart> parts, string candidate)
    {
        foreach (var p in parts)
            if (_catalog.AreConflicting(p.Tpl, candidate)) return true;
        return false;
    }

    /// <summary>True if any two parts of the list (root included) conflict with each other.</summary>
    public static bool HasConflict(ItemCatalog catalog, IEnumerable<AssembledPart> parts) =>
        catalog.FindConflict(parts.Select(p => p.Tpl).ToList()) != null;

    private static bool MustFill(SlotInfo slot, bool armorRule)
    {
        if (slot.Required) return true;
        string n = slot.Name.ToLowerInvariant();
        if (armorRule) return !n.StartsWith("mod_");
        return n == "mod_magazine";
    }

    private bool Fill(int index, bool armor, Random? rng, StatObjective objective, List<AssembledPart> parts, int depth, int maxArmorClass = 0)
    {
        if (depth > 8) return false;
        if (!_catalog.Items.TryGetValue(parts[index].Tpl, out var owner)) return false;

        bool armorRule = armor && depth == 0;

        foreach (var slot in owner.Slots)
        {
            if (!MustFill(slot, armorRule)) continue;

            var allowed = slot.Filter.Where(IsUsable).Where(c => !Conflicts(parts, c));
            if (armor && maxArmorClass > 0)
                allowed = allowed.Where(c => !_catalog.Items.TryGetValue(c, out var ci) || ci.ArmorClass <= maxArmorClass);

            var candidates = Order(allowed, armorRule, rng, objective, maxArmorClass);

            bool filled = false;
            foreach (var candidate in candidates)
            {
                int mark = parts.Count;
                parts.Add(new AssembledPart { Tpl = candidate, ParentIndex = index, SlotName = slot.Name });
                if (Fill(parts.Count - 1, armor, rng, objective, parts, depth + 1, maxArmorClass)) { filled = true; break; }
                parts.RemoveRange(mark, parts.Count - mark); // roll back this candidate and everything below it
            }

            if (!filled) return false;
        }
        return true;
    }

    public static int EffectiveArmorClass(ItemCatalog catalog, IEnumerable<AssembledPart> parts)
    {
        int best = 0;
        foreach (var p in parts)
            if (catalog.Items.TryGetValue(p.Tpl, out var it) && (int)Math.Round(it.ArmorClass) > best)
                best = (int)Math.Round(it.ArmorClass);
        return best;
    }

    private double Price(string tpl) => _catalog.Prices.TryGetValue(tpl, out var p) ? p : 0;
    private double Weight(string tpl) => _catalog.Items.TryGetValue(tpl, out var it) ? it.Weight : 0;
    private double ErgoOf(string tpl) => _catalog.Items.TryGetValue(tpl, out var it) ? it.Ergonomics : 0;
    private double RecoilOf(string tpl) => _catalog.Items.TryGetValue(tpl, out var it) ? it.Recoil : 0;
    private int ArmorClassOf(string tpl) => _catalog.Items.TryGetValue(tpl, out var it) ? (int)Math.Round(it.ArmorClass) : 0;

    private List<string> Order(IEnumerable<string> candidates, bool armorRule, Random? rng, StatObjective objective, int maxArmorClass = 0)
    {
        var list = candidates.Distinct().ToList();

        switch (objective)
        {
            case StatObjective.Lightest:
                return list.OrderBy(Weight).ThenBy(c => c, StringComparer.Ordinal).ToList();
            case StatObjective.Heaviest:
                return list.OrderByDescending(Weight).ThenBy(c => c, StringComparer.Ordinal).ToList();
            case StatObjective.BestErgo:
                return list.OrderByDescending(ErgoOf).ThenBy(Weight).ThenBy(c => c, StringComparer.Ordinal).ToList();
            case StatObjective.WorstErgo:
                return list.OrderBy(ErgoOf).ThenByDescending(Weight).ThenBy(c => c, StringComparer.Ordinal).ToList();
            case StatObjective.BestRecoil:
                return list.OrderBy(RecoilOf).ThenBy(Weight).ThenBy(c => c, StringComparer.Ordinal).ToList();
            case StatObjective.WorstRecoil:
                return list.OrderByDescending(RecoilOf).ThenByDescending(Weight).ThenBy(c => c, StringComparer.Ordinal).ToList();
        }

        if (armorRule)
        {
            // Prioritize plates closest to maxArmorClass (when specified), then middle of the price range
            if (maxArmorClass > 0)
            {
                return list
                    .OrderByDescending(ArmorClassOf)
                    .ThenBy(c => Math.Abs(Price(c) - 35000))
                    .ThenBy(c => c, StringComparer.Ordinal)
                    .ToList();
            }
            var sorted = list.OrderBy(Price).ThenBy(c => c, StringComparer.Ordinal).ToList();
            int mid = sorted.Count / 2;
            return sorted.Select((c, i) => (c, d: Math.Abs(i - mid))).OrderBy(x => x.d).Select(x => x.c).ToList();
        }

        list = rng != null
            ? list.OrderBy(_ => rng.Next()).ToList()
            : list.OrderBy(c => c, StringComparer.Ordinal).ToList();

        // parts with a handbook price (i.e. obtainable ones) first; OrderBy is stable
        return list.OrderBy(c => _catalog.Prices.ContainsKey(c) ? 0 : 1).ToList();
    }
}
