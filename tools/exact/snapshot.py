"""Snapshot of the Reimagined balance: every buildable actor and every weapon they reference.

What is captured
  * Actors: every non-abstract top-level actor defined in the mod's rule files
    (mods/ra2/rules/*.yaml; names starting with '^', '-' or '@' are skipped) whose
    *resolved* rules contain a trait named `Buildable` or `Buildable@<x>`. The
    snapshot stores the whole resolved rule tree (all traits) of each actor.
  * Weapons: every weapon named by a `Weapon`, `EmptyWeapon` or `MissileWeapon`
    field anywhere in a captured actor's tree (covers Armament*, Explodes*,
    FireWarheadsOnDeath-style traits, etc.). Comma-separated lists are split.
    Weapon names found in captured weapons' own trees are followed transitively.
    The whole resolved weapon definition is stored.
    References are matched case-insensitively (OpenRA lowercases weapon names) and
    stored under the defined key; capture raises if a referenced weapon is undefined.
  * SpawnActorOnDeath and similar actor references are not weapons and are not followed.
  * Neutral tech buildings (NEUTRAL_TECH_ACTORS) are captured whole under "neutral",
    and the weapons they reference (once their Exact traits are stripped, see strip_neutral)
    are followed like those of the buildable actors.

Comparison
  * Buildable actors and weapons (`strip_ignored`) ignore only the `~balance.reimagined`
    entry of `Buildable*.Prerequisites`, traits keyed `ReplaceOnPrerequisite@BALANCE`, and
    twin names `<x>.exact` appended after `<x>` to the repair/rearm lists in HOST_FIELDS.
  * Neutral tech buildings (`strip_neutral`) are not replaced but switch traits by
    condition: `GrantConditionOnPrerequisite@BALANCE` (exactly NEUTRAL_GRANT) and every `*@EXACT` trait whose
    RequiresCondition is exactly `balance-exact` are ignored, and a `X@REIMAGINED` trait must
    equal the original `X` once its `RequiresCondition: !balance-exact` is removed (it may not
    sit beside `X`). Their weapons are collected after that stripping, so the twin weapons
    named by `*@EXACT` traits do not count as Reimagined weapons.

CLI (run from the tools/ directory, or from anywhere with tools/ on PYTHONPATH):
    python3 -m exact.snapshot --write     # regenerate exact/reimagined-snapshot.json
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

from ra2data.resolved import REPO_ROOT, resolve_actor, resolve_weapon

SNAPSHOT_PATH = Path(__file__).resolve().parent / "reimagined-snapshot.json"
RULES_DIR = REPO_ROOT / "mods" / "ra2" / "rules"
WEAPONS_DIR = REPO_ROOT / "mods" / "ra2" / "weapons"
EXACT_ACTORS_PATH = RULES_DIR / "exact" / "actors.yaml"
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


def strip_ignored(tree: dict, twins: Collection[str] | None = None) -> dict:
    """Copy of a resolved actor/weapon tree without the allowed Reimagined additions.

    twins: the twinned actors whose appended twin names are ignored (default: twinned_actors()).
    """
    out = copy.deepcopy(tree)
    out.pop(IGNORED_TRAIT, None)
    for key, trait in out.items():
        field = HOST_FIELDS.get(key.split("@")[0])
        if field and isinstance(trait, dict) and isinstance(trait.get(field), str):
            trait[field] = strip_appended_twins(trait[field], twins)
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


def _resolve(name: str) -> dict:
    try:
        return resolve_actor(name)
    except RuntimeError as e:
        raise RuntimeError(f"actor {name} no longer exists or cannot be resolved: {e}") from e


def capture(names: list[str], neutral: list[str] | tuple[str, ...] = ()) -> dict:
    """Resolve `names` (actors), keeping the buildable ones, and every `neutral` actor;
    plus all weapons they reference."""
    actors = {}
    for name in names:
        tree = _resolve(name)
        if has_buildable(tree):
            actors[name] = tree
    neutral_trees = {name: _resolve(name) for name in neutral}
    defs = weapon_definitions()
    weapons: dict = {}
    # Weapons of the neutral buildings' Exact traits (twin weapons) are not Reimagined weapons.
    trees = [*actors.values(), *(strip_neutral(t) for t in neutral_trees.values())]
    pending = _canonical(set().union(*(referenced_weapons(t) for t in trees)), defs) if trees else set()
    while pending:
        name = pending.pop()
        if name in weapons:
            continue
        weapons[name] = resolve_weapon(name)
        pending |= _canonical(referenced_weapons(weapons[name]), defs) - weapons.keys()
    return {"actors": actors, "neutral": neutral_trees, "weapons": weapons}


def dumps(data: dict) -> str:
    return json.dumps(data, indent="\t", sort_keys=True, ensure_ascii=False) + "\n"


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--write", action="store_true", help=f"regenerate {SNAPSHOT_PATH.name}")
    args = parser.parse_args(argv)
    if not args.write:
        parser.error("nothing to do; pass --write")
    data = capture(actor_names(), NEUTRAL_TECH_ACTORS)
    SNAPSHOT_PATH.write_text(dumps(data), encoding="utf-8")
    print(f"wrote {SNAPSHOT_PATH}: {len(data['actors'])} actors, {len(data['neutral'])} neutral actors,"
        f" {len(data['weapons'])} weapons")
    return 0


if __name__ == "__main__":
    sys.exit(main())
