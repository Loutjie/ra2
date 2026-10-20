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
    The whole resolved weapon definition is stored; a weapon that is referenced but
    does not exist is stored as {"_missing": "true"}.
  * SpawnActorOnDeath and similar actor references are not weapons and are not followed.

Comparison (`strip_ignored`) ignores only the `~balance.reimagined` entry of
`Buildable*.Prerequisites` and traits keyed `ReplaceOnPrerequisite@BALANCE`.

CLI (run from the tools/ directory, or from anywhere with tools/ on PYTHONPATH):
    python3 -m exact.snapshot --write     # regenerate exact/reimagined-snapshot.json
"""
from __future__ import annotations

import argparse
import copy
import json
import re
import sys
from pathlib import Path

from ra2data.resolved import REPO_ROOT, resolve_actor, resolve_weapon

SNAPSHOT_PATH = Path(__file__).resolve().parent / "reimagined-snapshot.json"
RULES_DIR = REPO_ROOT / "mods" / "ra2" / "rules"
REIMAGINED_TOKEN = "~balance.reimagined"
IGNORED_TRAIT = "ReplaceOnPrerequisite@BALANCE"
MISSING_MARKER = "_missing"
WEAPON_KEYS = ("Weapon", "EmptyWeapon", "MissileWeapon")


def is_buildable_trait(key: str) -> bool:
    return key == "Buildable" or key.startswith("Buildable@")


def strip_ignored(tree: dict) -> dict:
    """Copy of a resolved actor/weapon tree without the allowed Reimagined additions."""
    out = copy.deepcopy(tree)
    out.pop(IGNORED_TRAIT, None)
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


def _blocks(rules_dir: Path) -> dict[str, str]:
    """Raw text of every top-level node (abstract ones included), concatenated across files."""
    blocks: dict[str, list[str]] = {}
    current = None
    for path in sorted(rules_dir.glob("*.yaml")):
        for line in path.read_text(encoding="utf-8").splitlines():
            m = re.match(r"([^\s#][^:]*):", line)
            if m:
                current = m.group(1).strip()
                blocks.setdefault(current, [])
            elif current is not None:
                blocks[current].append(line)
    return {name: "\n".join(lines) for name, lines in blocks.items()}


def actor_names(rules_dir: Path = RULES_DIR) -> list[str]:
    """Names of non-abstract actors that may be buildable, sorted.

    Cheap textual pre-filter (a superset of the buildable actors; `capture` makes the
    authoritative check on the resolved rules): an actor is kept if its own text, or
    that of any actor/template it transitively inherits, mentions `Buildable`.
    Names starting with '^', '-' or '@' are skipped.
    """
    blocks = _blocks(rules_dir)

    def maybe_buildable(name: str, seen: frozenset = frozenset()) -> bool:
        text = blocks.get(name, "")
        if re.search(r"^\s*Buildable\b", text, re.M):
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


def _resolve_weapon_or_missing(name: str) -> dict:
    """Resolved weapon, or a marker for a dangling reference (actors may name weapons that do not exist)."""
    try:
        return resolve_weapon(name)
    except RuntimeError as e:
        if "Could not find weapon" not in str(e):
            raise
        return {MISSING_MARKER: "true"}


def capture(names: list[str]) -> dict:
    """Resolve `names` (actors); keep the buildable ones plus all weapons they reference."""
    actors = {}
    for name in names:
        tree = resolve_actor(name)
        if has_buildable(tree):
            actors[name] = tree
    weapons: dict = {}
    pending = set().union(*(referenced_weapons(t) for t in actors.values())) if actors else set()
    while pending:
        name = pending.pop()
        if name in weapons:
            continue
        weapons[name] = _resolve_weapon_or_missing(name)
        pending |= referenced_weapons(weapons[name]) - weapons.keys()
    return {"actors": actors, "weapons": weapons}


def dumps(data: dict) -> str:
    return json.dumps(data, indent="\t", sort_keys=True, ensure_ascii=False) + "\n"


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--write", action="store_true", help=f"regenerate {SNAPSHOT_PATH.name}")
    args = parser.parse_args(argv)
    if not args.write:
        parser.error("nothing to do; pass --write")
    data = capture(actor_names())
    SNAPSHOT_PATH.write_text(dumps(data), encoding="utf-8")
    print(f"wrote {SNAPSHOT_PATH}: {len(data['actors'])} actors, {len(data['weapons'])} weapons")
    return 0


if __name__ == "__main__":
    sys.exit(main())
