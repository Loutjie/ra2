"""Lint checks over the mod's raw YAML rules and weapons."""
import re
from pathlib import Path

import pytest

REPO_ROOT = Path(__file__).resolve().parents[2]
RULES_DIR = REPO_ROOT / "mods" / "ra2" / "rules"
WEAPONS_DIR = REPO_ROOT / "mods" / "ra2" / "weapons"

_KEY = re.compile(r"^(?P<indent>\t*)(?P<key>[^\s:#][^:]*?):(?P<value>.*)$")


def _entries(path):
    """Yield (depth, key, value) for each MiniYaml line, skipping comments/blanks."""
    for raw in path.read_text(encoding="utf-8-sig").splitlines():
        if not raw.strip() or raw.lstrip().startswith("#"):
            continue
        m = _KEY.match(raw)
        if m:
            yield len(m["indent"]), m["key"].strip(), m["value"].split("#")[0].strip()


def armor_types():
    """Return {(file, actor, type)} for every Armor trait's Type field."""
    found = set()
    for path in sorted(RULES_DIR.glob("*.yaml")):
        actor = None
        in_armor = False
        for depth, key, value in _entries(path):
            if depth == 0:
                actor, in_armor = key, False
            elif depth == 1:
                in_armor = key == "Armor" or key.startswith("Armor@")
            elif depth == 2 and in_armor and key == "Type" and value:
                found.add((path.name, actor, value))
    return found


def versus_keys():
    """Return the set of every key used inside a Versus block of any weapon."""
    keys = set()
    for path in sorted(WEAPONS_DIR.glob("*.yaml")):
        versus_depth = None
        for depth, key, _ in _entries(path):
            if versus_depth is not None and depth <= versus_depth:
                versus_depth = None
            if versus_depth is not None:
                keys.add(key)
            elif key == "Versus":
                versus_depth = depth
    return keys


@pytest.mark.xfail(strict=True, reason="fixed in fix/armour-type-case")
def test_armor_types_match_versus_keys():
    keys = versus_keys()
    assert keys, "no Versus keys found; scanner or paths are wrong"
    bad = sorted(t for t in armor_types() if t[2] not in keys)
    assert not bad, (
        f"{len(bad)} armor types without an exact-case Versus key: {bad}"
    )
