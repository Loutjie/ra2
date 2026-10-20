"""Snapshot of the Reimagined balance (the guard that it never changes).

Reimagined snapshot (reimagined-snapshot.json, `--write`)
  Captured from the mod resolved WITHOUT the generated Exact files (exact_mod_entries, via
  ra2data.resolved.mods_without), so it records pure Reimagined.

  * Actors: every non-abstract top-level actor defined in the mod's rule files
    (mods/ra2/rules/*.yaml; names starting with '^', '-' or '@' are skipped) whose
    *resolved* rules contain a trait named `Buildable` or `Buildable@<x>`. The
    snapshot stores the whole resolved rule tree (all traits) of each actor.
  * Neutral tech buildings (NEUTRAL_TECH_ACTORS), whole, under "neutral".
  * Others: the World and Player actors and every other existing actor that the generated
    overlays (OVERLAY_PATHS: reimagined.yaml, globals.yaml, ai.yaml) change, e.g. hornet,
    asw, cdest, civilian vehicles, amcv.colorpicker; whole, under "others".
  * Weapons: every weapon named by a `Weapon`, `EmptyWeapon` or `MissileWeapon`
    field anywhere in a captured actor's tree (covers Armament*, Explodes*,
    FireWarheadsOnDeath-style traits, etc.). Comma-separated lists are split.
    Weapon names found in captured weapons' own trees are followed transitively.
    The whole resolved weapon definition is stored.
    References are matched case-insensitively (OpenRA lowercases weapon names) and
    stored under the defined key; capture raises if a referenced weapon is undefined.
    The neutral buildings' weapons are collected once their Exact traits are stripped
    (strip_neutral), so the twin weapons those traits name do not count.
  * SpawnActorOnDeath and similar actor references are not weapons and are not followed.

  The test resolves the same names from the whole mod and compares, ignoring only:
  * Buildable actors and weapons (`strip_ignored`): the `~balance.reimagined` entry of
    `Buildable*.Prerequisites`, traits keyed `ReplaceOnPrerequisite@BALANCE`, and twin names
    `<x>.exact` appended after `<x>` to the repair/rearm lists in HOST_FIELDS.
  * Neutral tech buildings (`strip_neutral`): `GrantConditionOnPrerequisite@BALANCE` (exactly
    NEUTRAL_GRANT) and every `*@EXACT` trait whose RequiresCondition is exactly `balance-exact`
    are ignored, and a `X@REIMAGINED` trait must equal the original `X` once its
    `RequiresCondition: !balance-exact` is removed (it may not sit beside `X`).
  * Others (`strip_other`): the appended host twins as above; on the Player, the
    BalanceProductionQueue rename and its Exact-only fields (strip_queues) and the AI twin
    additions (strip_ai_twins); on the World, the empty ReplaceOnPrerequisiteAtLoad
    (strip_world). Nothing else.

CLI (run from the tools/ directory, or from anywhere with tools/ on PYTHONPATH; needs a built mod):
    python3 -m exact.snapshot --write        # regenerate exact/reimagined-snapshot.json
"""
from __future__ import annotations

import argparse
import copy
import functools
import json
import re
import sys
from collections.abc import Collection
from pathlib import Path

from ra2data.resolved import REPO_ROOT, config_value, resolve_actor, resolve_weapon

SNAPSHOT_PATH = Path(__file__).resolve().parent / "reimagined-snapshot.json"
RULES_DIR = REPO_ROOT / "mods" / "ra2" / "rules"
WEAPONS_DIR = REPO_ROOT / "mods" / "ra2" / "weapons"
EXACT_ACTORS_PATH = RULES_DIR / "exact" / "actors.yaml"
MOD_YAML = REPO_ROOT / "mods" / "ra2" / "mod.yaml"
# Generated files that change existing (non-twin) actors; actors.yaml only defines twins.
OVERLAY_PATHS = tuple(RULES_DIR / "exact" / f for f in ("reimagined.yaml", "globals.yaml", "ai.yaml"))
SYSTEM_ACTORS = ("World", "Player")
# The Player's production queues: the Balance queue is a ClassicProductionQueue plus these
# Exact-only fields (BalanceProductionQueueInfo).
BALANCE_QUEUE = "BalanceProductionQueue"
CLASSIC_QUEUE = "ClassicProductionQueue"
BALANCE_QUEUE_FIELDS = ("ExactPrerequisite", "MultipleFactory", "MinLowPowerProductionSpeed",
    "MaxLowPowerProductionSpeed", "LowPowerPenaltyModifier")
# The World trait that replaces map-load actors before the first tick (allowed only empty).
WORLD_TRAIT = "ReplaceOnPrerequisiteAtLoad"
SUPPORT_POWER_MODULE = "SupportPowerBotModule"
REIMAGINED_TOKEN = "~balance.reimagined"
IGNORED_TRAIT = "ReplaceOnPrerequisite@BALANCE"
NEUTRAL_TECH_ACTORS = ("caairp", "cahosp", "caoild", "caoutp", "cathosp")
NEUTRAL_IGNORED_TRAIT = "GrantConditionOnPrerequisite@BALANCE"
EXACT_SUFFIX = "@EXACT"
REIMAGINED_SUFFIX = "@REIMAGINED"
EXACT_CONDITION = "balance-exact"
REIMAGINED_CONDITION = "!balance-exact"
# The only allowed NEUTRAL_IGNORED_TRAIT.
NEUTRAL_GRANT = {"Condition": EXACT_CONDITION, "Prerequisites": "balance.exact"}
WEAPON_KEYS = ("Weapon", "EmptyWeapon", "MissileWeapon")
EXACT_SUFFIX_NAME = ".exact"
# trait type -> actor list that the Exact overlay may extend with twin names (repair and
# rearm hosts), so untwinned actors can use Exact facilities.
HOST_FIELDS = {
    "Repairable": "RepairActors",
    "RepairableNear": "RepairActors",
    "TransformsIntoRepairable": "RepairActors",
    "Rearmable": "RearmActors",
}


def is_buildable_trait(key: str) -> bool:
    return key == "Buildable" or key.startswith("Buildable@")


@functools.lru_cache(maxsize=4)
def _twinned_actors_cached(path: Path, mtime_ns: int) -> frozenset[str]:
    names = set()
    for line in path.read_text(encoding="utf-8").splitlines():
        m = re.match(r"([^\s#][^:]*):", line)
        if m and m.group(1).strip().endswith(EXACT_SUFFIX_NAME):
            names.add(m.group(1).strip()[: -len(EXACT_SUFFIX_NAME)])
    return frozenset(names)


def twinned_actors(path: Path = EXACT_ACTORS_PATH) -> frozenset[str]:
    """Base actors that have a twin: the top-level `<name>.exact` keys of the generated
    actors file, without the suffix (no game data needed). Empty if the file is absent."""
    if not path.is_file():
        return frozenset()
    return _twinned_actors_cached(path, path.stat().st_mtime_ns)


def strip_appended_twins(value: str, twins: Collection[str] | None = None) -> str:
    """An actor list without the trailing `<name>.exact` entries whose `<name>` comes earlier
    and is a twinned actor (`twins`, default: twinned_actors()).

    Only such appended twin names are allowed; a `.exact` entry elsewhere, one whose base is
    not in the list, or one whose base has no twin is kept so that the comparison fails.
    """
    if twins is None:
        twins = twinned_actors()
    names = [p.strip() for p in value.split(",") if p.strip()]
    while names and names[-1].endswith(EXACT_SUFFIX_NAME):
        base = names[-1][: -len(EXACT_SUFFIX_NAME)]
        if base not in twins or base not in names[:-1]:
            break
        names.pop()
    return ", ".join(names)


def _strip_hosts(tree: dict, twins: Collection[str] | None) -> None:
    """Remove the appended twin names from the HOST_FIELDS lists of `tree`, in place."""
    for key, trait in tree.items():
        field = HOST_FIELDS.get(key.split("@")[0])
        if field and isinstance(trait, dict) and isinstance(trait.get(field), str):
            trait[field] = strip_appended_twins(trait[field], twins)


def strip_ignored(tree: dict, twins: Collection[str] | None = None) -> dict:
    """Copy of a resolved actor/weapon tree without the allowed Reimagined additions.

    twins: the twinned actors whose appended twin names are ignored (default: twinned_actors()).
    """
    out = copy.deepcopy(tree)
    out.pop(IGNORED_TRAIT, None)
    _strip_hosts(out, twins)
    for key, trait in out.items():
        if not is_buildable_trait(key) or not isinstance(trait, dict):
            continue
        prereqs = trait.get("Prerequisites")
        if isinstance(prereqs, str):
            kept = [p.strip() for p in prereqs.split(",") if p.strip() != REIMAGINED_TOKEN]
            if kept:
                trait["Prerequisites"] = ", ".join(kept)
            else:
                del trait["Prerequisites"]
    return out


def strip_neutral(tree: dict) -> dict:
    """Copy of a neutral tech building's tree with the Exact/Reimagined trait pairs folded back.

    `GrantConditionOnPrerequisite@BALANCE` is dropped only when it is exactly NEUTRAL_GRANT. A
    `X@EXACT` trait whose RequiresCondition is exactly `balance-exact` is dropped, and a
    `X@REIMAGINED` trait whose RequiresCondition is exactly `!balance-exact` becomes `X`
    without that condition. Anything else is left in place, so the comparison fails.
    """
    out = copy.deepcopy(tree)
    if out.get(NEUTRAL_IGNORED_TRAIT) == NEUTRAL_GRANT:
        del out[NEUTRAL_IGNORED_TRAIT]
    for key in [k for k in out if k.endswith(EXACT_SUFFIX)]:
        if isinstance(out[key], dict) and out[key].get("RequiresCondition") == EXACT_CONDITION:
            del out[key]
    for key in [k for k in out if k.endswith(REIMAGINED_SUFFIX)]:
        trait = out[key]
        base = key[: -len(REIMAGINED_SUFFIX)]
        if base in out or not isinstance(trait, dict) or trait.get("RequiresCondition") != REIMAGINED_CONDITION:
            continue
        del out[key]
        del trait["RequiresCondition"]
        out[base] = trait
    return out


def strip_beside_twins(value: str, twins: Collection[str] | None = None) -> str:
    """An AI actor list without each `<name>.exact` that directly follows its `<name>`, where
    `<name>` is a twinned actor (exact.ai writes each twin beside its base). Any other
    `.exact` entry is kept so that the comparison fails."""
    if twins is None:
        twins = twinned_actors()
    names = [p.strip() for p in value.split(",") if p.strip()]
    kept = [n for i, n in enumerate(names) if not (n.endswith(EXACT_SUFFIX_NAME) and i > 0
        and names[i - 1] == n[: -len(EXACT_SUFFIX_NAME)] and n[: -len(EXACT_SUFFIX_NAME)] in twins)]
    return ", ".join(kept)


def _is_bot_module(key: str) -> bool:
    kind = key.split("@")[0]
    return kind.endswith(("BotModule", "BotManager")) and kind != SUPPORT_POWER_MODULE


def strip_ai_twins(tree: dict, twins: Collection[str] | None = None) -> dict:
    """Copy of the Player tree without the AI twin additions of exact.ai:

      * in a `*BotModule`/`*BotManager` trait, a twin listed directly after its twinned base in
        a comma list, and a dictionary key `<name>.exact` of a twinned `<name>` that is also a
        key, with the same value;
      * in SupportPowerBotModule Decisions, a `<decision>.exact` that equals `<decision>` but
        for another OrderName.
    """
    if twins is None:
        twins = twinned_actors()
    out = copy.deepcopy(tree)
    for key, trait in out.items():
        if not isinstance(trait, dict):
            continue
        if key.split("@")[0] == SUPPORT_POWER_MODULE and isinstance(trait.get("Decisions"), dict):
            decisions = trait["Decisions"]
            for name in [n for n in decisions if n.endswith(EXACT_SUFFIX_NAME)]:
                base = decisions.get(name[: -len(EXACT_SUFFIX_NAME)])
                copied = decisions[name]
                if (isinstance(base, dict) and isinstance(copied, dict)
                        and copied.get("OrderName") != base.get("OrderName")
                        and {**copied, "OrderName": None} == {**base, "OrderName": None}):
                    del decisions[name]
            continue
        if not _is_bot_module(key):
            continue
        for field, value in trait.items():
            if isinstance(value, str):
                trait[field] = strip_beside_twins(value, twins)
            elif isinstance(value, dict) and "_value" not in value:
                for name in [n for n in value if n.endswith(EXACT_SUFFIX_NAME)]:
                    base = name[: -len(EXACT_SUFFIX_NAME)]
                    if base in twins and base in value and value[base] == value[name]:
                        del value[name]
    return out


def strip_queues(tree: dict) -> dict:
    """Copy of the Player tree with each `BalanceProductionQueue[@x]` turned back into the
    `ClassicProductionQueue[@x]` it extends: renamed, without BALANCE_QUEUE_FIELDS. A Balance
    queue beside a Classic queue of the same key is left in place, so the comparison fails."""
    out = copy.deepcopy(tree)
    for key in [k for k in out if k.split("@")[0] == BALANCE_QUEUE]:
        classic = CLASSIC_QUEUE + key[len(BALANCE_QUEUE):]
        trait = out[key]
        if classic in out or not isinstance(trait, dict):
            continue
        del out[key]
        out[classic] = {f: v for f, v in trait.items() if f not in BALANCE_QUEUE_FIELDS}
    return out


def strip_world(tree: dict) -> dict:
    """Copy of the World tree without the WORLD_TRAIT, which is allowed only empty (it has no fields)."""
    out = copy.deepcopy(tree)
    if out.get(WORLD_TRAIT) == {}:
        del out[WORLD_TRAIT]
    return out


def strip_other(name: str, tree: dict, twins: Collection[str] | None = None) -> dict:
    """Copy of a captured non-buildable actor (see capture's `others`) without the allowed additions.

    Every actor: twin names appended to its HOST_FIELDS lists (as strip_ignored). The Player
    also: the Balance production queues (strip_queues) and the AI twins (strip_ai_twins). The
    World also: the empty WORLD_TRAIT (strip_world). Nothing else is ignored.
    """
    out = copy.deepcopy(tree)
    _strip_hosts(out, twins)
    if name == "Player":
        out = strip_ai_twins(strip_queues(out), twins)
    elif name == "World":
        out = strip_world(out)
    return out


def _top_level_keys(path: Path) -> list[str]:
    keys = []
    for line in path.read_text(encoding="utf-8").splitlines():
        m = re.match(r"([^\s#][^:]*):", line)
        if m:
            keys.append(m.group(1).strip())
    return keys


def overlay_actors(paths: Collection[Path] = OVERLAY_PATHS) -> set[str]:
    """Existing actors that the generated overlay files change: their top-level keys other than twins."""
    return {k for p in paths if p.is_file() for k in _top_level_keys(p) if not k.endswith(EXACT_SUFFIX_NAME)}


def exact_mod_entries(mod_yaml: Path = MOD_YAML) -> tuple[str, ...]:
    """The generated Exact files as mod.yaml lists them (relative to the mod, e.g.
    "rules/exact/actors.yaml"), for resolving the mod without them (ra2data.resolved.mods_without)."""
    found = re.findall(r"^\s*[^\s|#]+\|((?:rules|weapons)/exact/\S+\.yaml)\s*$",
        mod_yaml.read_text(encoding="utf-8"), re.M)
    return tuple(found)


def _blocks(rules_dir: Path) -> dict[str, str]:
    """Raw text of every top-level node (abstract ones included), concatenated across files."""
    blocks: dict[str, list[str]] = {}
    current = None
    for path in sorted(rules_dir.glob("*.yaml")):
        for line in path.read_text(encoding="utf-8").splitlines():
            m = re.match(r"([^\s#][^:]*):", line)
            if m:
                current = m.group(1).strip()
                # Generated Exact twins (`<name>.exact`) are never base actors. They live in
                # rules/exact/, which the glob does not reach today; filter them anyway.
                if current.endswith(EXACT_SUFFIX_NAME):
                    current = None
                    continue
                blocks.setdefault(current, [])
            elif current is not None:
                blocks[current].append(line)
    return {name: "\n".join(lines) for name, lines in blocks.items()}


def actor_names(rules_dir: Path = RULES_DIR, field: str = "Buildable") -> list[str]:
    """Names of non-abstract actors that may be buildable (or may have `field`), sorted.

    Cheap textual pre-filter (a superset of the buildable actors; `capture` makes the
    authoritative check on the resolved rules): an actor is kept if its own text, or
    that of any actor/template it transitively inherits, has a line starting with `field`
    (a regular expression).
    Names starting with '^', '-' or '@' are skipped.
    """
    blocks = _blocks(rules_dir)

    def maybe_buildable(name: str, seen: frozenset = frozenset()) -> bool:
        text = blocks.get(name, "")
        if re.search(rf"^\s*(?:{field})\b", text, re.M):
            return True
        parents = [p.strip() for p in re.findall(r"^\s*Inherits(?:@\w+)?:\s*(\S.*)$", text, re.M)]
        return any(p not in seen and maybe_buildable(p, seen | {name}) for p in parents)

    return sorted(n for n in blocks if n[0] not in "^-@" and maybe_buildable(n))


def referenced_weapons(tree) -> set[str]:
    found: set[str] = set()
    if isinstance(tree, dict):
        for key, value in tree.items():
            if key.split("@")[0] in WEAPON_KEYS and isinstance(value, str):
                found.update(w.strip() for w in value.split(",") if w.strip())
            else:
                found |= referenced_weapons(value)
    return found


def has_buildable(tree: dict) -> bool:
    return any(is_buildable_trait(k) for k in tree)


def weapon_definitions(weapons_dir: Path = WEAPONS_DIR) -> dict[str, str]:
    """Lowercased weapon name -> defined key, from the top-level keys of the weapon files.

    OpenRA lowercases weapon names on load, so rules may reference a weapon with
    different casing than its definition (`20mmE` vs `20mme:`).
    """
    defs: dict[str, str] = {}
    for path in sorted(weapons_dir.glob("*.yaml")):
        for line in path.read_text(encoding="utf-8").splitlines():
            m = re.match(r"([^\s#][^:]*):", line)
            if m:
                defs.setdefault(m.group(1).strip().lower(), m.group(1).strip())
    return defs


def _canonical(refs: set[str], defs: dict[str, str]) -> set[str]:
    """Map references to defined keys; fail if a referenced weapon has no definition."""
    missing = sorted(r for r in refs if r.lower() not in defs)
    if missing:
        raise RuntimeError(f"referenced weapons have no definition: {missing}")
    return {defs[r.lower()] for r in refs}


def _resolve(name: str, without: tuple[str, ...] = ()) -> dict:
    try:
        return resolve_actor(name, without) if without else resolve_actor(name)
    except RuntimeError as e:
        raise RuntimeError(f"actor {name} no longer exists or cannot be resolved: {e}") from e


def capture(names: list[str], neutral: list[str] | tuple[str, ...] = (), others: Collection[str] = (),
        without: tuple[str, ...] = ()) -> dict:
    """Resolve `names` (actors), keeping the buildable ones, every `neutral` actor and every one
    of `others` that is neither (non-buildable actors the overlays change, World, Player); plus
    all weapons they reference.

    without: mod.yaml files to leave out of the resolution (exact_mod_entries for pure Reimagined).
    """
    actors = {}
    for name in names:
        tree = _resolve(name, without)
        if has_buildable(tree):
            actors[name] = tree
    neutral_trees = {name: _resolve(name, without) for name in neutral}
    other_trees = {name: _resolve(name, without) for name in sorted(set(others))
        if name not in actors and name not in neutral_trees}
    defs = weapon_definitions()
    weapons: dict = {}
    # Weapons of the neutral buildings' Exact traits (twin weapons) are not Reimagined weapons.
    trees = [*actors.values(), *(strip_neutral(t) for t in neutral_trees.values()), *other_trees.values()]
    pending = _canonical(set().union(*(referenced_weapons(t) for t in trees)), defs) if trees else set()
    while pending:
        name = pending.pop()
        if name in weapons:
            continue
        weapons[name] = resolve_weapon(name, without) if without else resolve_weapon(name)
        pending |= _canonical(referenced_weapons(weapons[name]), defs) - weapons.keys()
    return {"actors": actors, "neutral": neutral_trees, "others": other_trees, "weapons": weapons}


def capture_reimagined() -> dict:
    """The Reimagined snapshot: the mod resolved without the generated Exact files."""
    return capture(actor_names(), NEUTRAL_TECH_ACTORS, (*SYSTEM_ACTORS, *sorted(overlay_actors())),
        without=exact_mod_entries())


def dumps(data: dict) -> str:
    return json.dumps(data, indent="\t", sort_keys=True, ensure_ascii=False) + "\n"


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--write", action="store_true", help=f"regenerate {SNAPSHOT_PATH.name}")
    args = parser.parse_args(argv)
    if not args.write:
        parser.error("nothing to do; pass --write")
    data = capture_reimagined()
    SNAPSHOT_PATH.write_text(dumps(data), encoding="utf-8")
    print(f"wrote {SNAPSHOT_PATH}: {len(data['actors'])} actors, {len(data['neutral'])} neutral actors,"
        f" {len(data['others'])} other actors, {len(data['weapons'])} weapons")
    return 0


if __name__ == "__main__":
    sys.exit(main())
