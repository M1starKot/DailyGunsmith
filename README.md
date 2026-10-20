# Daily Gunsmith (SPT 4.1.x)

**Version 1.0.0** - author: m1starkot - mod id: `com.m1starkot.dailygunsmith`

Adds randomly generated **daily weapon-assembly quests** (Gunsmith style) to Mechanic.
Every day a new set of quests is generated:

* **Weapon** - picked at random from the live item database (vanilla + every mod loaded before this one).
* **Required parts** - a random but valid attachment *tree* built from the real slot filters: parts sit on parts, e.g.
  receiver cover -> adapter -> mount -> scope -> additional collimator. Every part of the chain is required.
  Sights, handguards, grips and muzzle devices are required most often (see `build` in the config).
* **Difficulty tiers** (Easy / Medium / Hard) control the required player level (5-20 / 21-35 / 36-80), how many parts are
  required, which weapon classes are likely, which category requirements appear and the rewards.
* **Limits** - ergonomics is a *minimum* ("at least"); recoil and **weight** are *maximums*. All three are calculated
  from the chosen parts, so they stay achievable.
* **Category requirements** - a quest may additionally require *any* suppressor and/or a sight type, and/or a
  specific magazine. Sight types: collimator, assault scope, scope, special scope. A build never holds more than two
  sights: a collimator plus ONE magnified sight - assault scope, scope and special scope exclude each other.
* **Rewards** - roubles, EXP, trader standing and 1-2 items: lootable barter items, jewelry, armor, weapons,
  ammo stacks and keys only. Weapons and armor are always handed out **fully assembled** (all mandatory weapon parts
  incl. magazine, all armor plates/soft inserts), vanilla and modded alike. Items that cannot be assembled
  completely are never given.
* With `deterministicPerDay: true` the same day always yields the same quests (same IDs), so restarting the
  server doesn't reset your progress. The set rolls over at midnight (server local time, or UTC if `useUtc`).

## Build

Requires the .NET 10 SDK.

    dotnet build -c Release

Copy the contents of `bin/Release/net10.0/` (`DailyGunsmith.dll`, `config/`, `locales/`) into

    <SPT>/user/mods/DailyGunsmith/

## Localization

Quest texts live in `locales/<language>.json`. English (`en.json`) is the fallback for every language;
Russian (`ru.json`) is included. Item names always come from the game's own locale of the player's language and are always the full names (not the short in-game abbreviations), so every required part can be found by its exact name.

To add a language, copy `en.json` to e.g. `de.json` (use the same language code SPT uses) and translate the values.
Placeholders: `title` {0}=weapon; `description` {0}=weapon, {1}=min ergonomics, {2}=max recoil, {3}=max weight (kg), {4}=modules, {5}=categories;
`condition` {0}=weapon.

## Config (`config/config.json`)

| key | meaning |
|---|---|
| `enabled` | turn the mod on/off |
| `refreshIntervalHours` | how often the quests are regenerated, in whole hours: `3`, `12`, `24` (default), `48`, any value from 1 |
| `useUtc`, `deterministicPerDay`, `seedSalt` | time zone of the rollover / seeding |
| `traderId` | quest giver and trader whose standing is rewarded (default `5a7c2eca46aef81a7ca2145d` = Mechanic) |
| `globalRewardMultiplier` | multiplier for the roubles/EXP rewards of all tiers |
| `requireHandbookEntry` | only use items that have a handbook price (skips unobtainable/dev items) |
| `minWeaponSlots` | a weapon needs at least this many attachment slots |
| `vanillaItemsPath` | optional path to the vanilla `items.json` (auto-detected in `SPT_Data/database/templates/`) |
| `weapons` / `modules` | filter rules, see below |
| `tiers[]` | one entry per difficulty tier, see below |

### Attachment trees (`build` section)

| key | meaning |
|---|---|
| `maxDepth` | how deep parts may be nested (default 6) |
| `childSlotBoost` | extra weight for slots of already placed parts - higher = longer chains (default 1.5) |
| `slotWeights` | priority per slot type: if a slot name contains the key (e.g. `scope` matches `mod_scope_000`), it gets this weight. Higher = required more often, `0` = never |
| `defaultSlotWeight` | weight of slots matching no key (default 1) |

Adapters and mounts are never left empty if something can be placed on them. The tier's `maxMods` is a hard limit
for the whole tree. The ergonomics/recoil limits are calculated from the chosen parts. All parts of the tree are listed
in the quest condition's `containsItems`, so the quest is only completed by a weapon that contains every one of them.

**Conflicts:** no two parts of a required build may conflict with each other (`ConflictingItems`, checked in both directions, also against the weapon and against the mandatory parts that complete it, and including category-wide conflict entries). A part is never added if it conflicts with anything already in the tree; the finished build is verified once more, and a build or weapon that cannot be made conflict-free is discarded and re-rolled.

### Level requirement

Each quest only becomes available only for players whose level is within the tier's `minLevel`..`maxLevel` (defaults: Easy 5-20, Medium 21-35, Hard 36-80; `maxLevel` 0 = no upper limit). Every tier generates `questsPerDay` quests per refresh (default 1, set 0 to disable a tier), so each player gets that many quests of his own level range.

### Weapon classes per tier

`weaponClassWeights` of a tier sets how likely each class is. Keys: `pistol`, `smg`, `assaultRifle` (assault rifles and
carbines), `marksmanRifle` (semi-automatic rifles), `sniperRifle` (bolt-action / sniper rifles), `other` (shotguns, machine guns, anything else). A class
with a low weight still appears, just less often - every class can show up on every tier. Defaults:
Easy favors pistols and SMGs, Medium assault and semi-automatic rifles, Hard rifles, heavy rifles and assault rifles.

### Category requirements

Per tier: `suppressorChance` (0..1) and `magazineChance` (0..1, a concrete magazine of the weapon becomes a required part) and
`sightModeWeights` (`none`, `collimator`, `assaultScope`, `scope`, `specialScope`, `collimatorAssaultScope`,
`collimatorScope`, `collimatorSpecialScope`; the old keys `optic` / `both` still work).
Such a requirement is only added when the generated build itself contains a matching part (so the weapon is proven to
accept it). That part is not listed in `containsItems` - any item of the category completes the requirement - unless
other required parts sit on it. The category requirements use extended groups from `categoryGroups` - an item of ANY base class in the group counts:
`suppressor`, `reflex` (collimators incl. holographic sights + compact/micro collimators such as PK-06), `assaultScope`, `scope`, `specialScope`
(magnified sights, three separate mutually exclusive groups), `foregrip` (tactical foregrips) and `tactical` (flashlights, lasers, combo devices).
Per tier, `suppressorChance`, `foregripChance` and `tacticalChance` (0..1) set how often the quest requires them. Add or remove base-class IDs in those lists freely.
`emitAllMembers: true` writes the whole group into the quest condition; `false` writes only the class of the part the
generator picked (strict old behaviour - use it if the game rejects quests with several categories).
`autoDetect: true` extends the groups from the item database: every class (also from mods) named "silencer"/"suppressor"
joins `suppressor`, and the class of every sight with magnification joins `assaultScope` / `scope` / `specialScope`, so all magnified sights and all
suppressors (any caliber, vanilla or modded) count. The added classes are printed to the server log at startup.

### Limits (ergonomics / recoil / weight)

The three limits are derived from the **real stats of real modules**, not from a fixed guess. For every generated
quest the mod completes the required attachment tree into several full, working builds - the lightest possible
completion, the engine's deterministic "standard" completion, and a handful of genuinely randomized real
completions - using actual item data for whatever parts fill the remaining mandatory slots (magazine, sometimes
stock/pistol grip). Each completed build's ergonomics, recoil and weight are read straight from the item database;
the *worst* value across all of these real, playable builds (lowest ergonomics, highest recoil, heaviest weight)
becomes the basis for the limit. Only a small, mostly fixed safety margin is then added on top - a couple of
ergonomics points, ~3% recoil, and a `weightMarginMin`..`weightMarginMax` multiplier (defaults 1.05 - 1.25, rounded
up to 0.1 kg) - to cover rounding and the few mandatory-slot combinations the samples didn't happen to hit. This
keeps the numbers tied to the actual modules on the weapon (realistic) while still guaranteeing every generated
quest can be completed, which the dry run's self-check (below) verifies for every quest it generates.

Base weapon stats (ergonomics, recoil) are used exactly as stored in the item database - they are never clamped or
replaced by default values, and the limits have no artificial caps that could fall below what the weapon can really
reach (the old 700 / 800 recoil caps made heavy-recoil weapons such as the Mosin impossible). Weapons without recoil
data are not used. A quest that fails its own hand-in check is regenerated (up to 8 attempts) and is skipped rather than
registered if it still fails.

### Dry run

Set `dryRun.enabled` to `true` to get a report of the generated quests in `<mod folder>/dryrun/`:
`quests_<period>.txt` (readable: weapon, limits, categories, attachment tree, rewards, assembled reward items, quest text)
and `quests_<period>.json` (same data plus the complete quest JSON). Besides the real quests it generates
`dryRun.sampleCount` extra sample quests, spread evenly over the tiers; samples are **not** registered in the game.
Each quest is also checked for valid JSON / deserialization. Send these files to get quests reviewed.

### Config check

On startup the config is checked: unknown item IDs in `blacklist` / `whitelist` / `extra` / `categoryBlacklist` and in
`categoryGroups`, non-weapons in the weapon lists, invalid `traderId`, `minMods` greater than `maxMods`, unknown keys in
the tier weight tables and similar mistakes are reported as `[DailyGunsmith] config.json: ...` warnings.

### Tiers and rewards

Each of the three tiers (Easy / Medium / Hard) can be edited separately:

| key | meaning |
|---|---|
| `weight` | how likely the tier is picked for a daily quest |
| `questsPerDay` | how many quests of this tier are generated per refresh (default 1, 0 = tier off) |
| `maxLevel` | highest player level that can start the quest (0 = no limit) |
| `minLevel` | player level required to start the quest (also used for automatic rewards when `roubles`/`experience` are 0) |
| `minMods` / `maxMods` | number of parts in the whole required tree - chain parts count, `maxMods` is a hard limit (defaults: Easy 3-4, Medium 5-7, Hard 8-10) |
| `rewardMultiplier` | multiplier for this tier's roubles and EXP; also scales the value of the reward items |
| `roubles` | base roubles reward before multipliers (0 = automatic) |
| `experience` | base EXP reward before multipliers (0 = automatic) |
| `standing` | trader standing reward (not multiplied) |

Final roubles/EXP = base value x `rewardMultiplier` x `globalRewardMultiplier`.
Example: Hard with `roubles: 180000` and `rewardMultiplier: 1.5` pays 270000 roubles.

### `weapons` and `modules` sections

Both sections have the same keys; `weapons` controls which guns can be requested, `modules` controls which
attachments can be required.

| key | meaning |
|---|---|
| `includeModded` | `false` disables every item added by other mods (only vanilla items remain) |
| `blacklist` | template IDs that are never used - always wins |
| `whitelist` | if not empty, only these template IDs are allowed (plus `extra`) |
| `extra` | template IDs that are always allowed, even without a handbook entry or inside a blacklisted category |
| `categoryBlacklist` | base-class IDs whose whole subtree is skipped (weapons default: grenade launchers, special weapons) |

Examples:

* Disable all modded weapons but keep modded attachments: `"weapons": { "includeModded": false }`.
* Never ask for one specific modded scope: put its template ID into `modules.blacklist`.
* Force a modded weapon that has no handbook price into the pool: put its ID into `weapons.extra`.

An `extra` module is only offered for weapons that actually accept it in one of their slots.

## Refresh interval

`refreshIntervalHours` sets how long one set of quests lasts. Periods are counted from 2000-01-01 00:00
(server local time, or UTC with `useUtc`), so values that divide 24 (1, 2, 3, 4, 6, 8, 12, 24) always roll over at
fixed clock times - e.g. `12` at 00:00 and 12:00, `3` at 00:00, 03:00, 06:00 ... Longer values such as `48` change
every 48 hours. The server checks once a minute. With `deterministicPerDay: true` the same period always yields the
same quests, so restarting the server doesn't reset your progress.

## Notes

* Old daily quests are removed from the quest database at rollover; a quest you started but didn't finish before
  the end of the refresh period will disappear.
* Reward weapons are assembled from their mandatory slots (plus the magazine); armor gets plates/soft inserts from the
  middle of the price range. Parts come from the item database, so modded gear works too.
