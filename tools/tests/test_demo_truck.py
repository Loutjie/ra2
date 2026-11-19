"""Demolition Truck can be force-attack ordered onto empty ground (OpenRA/ra2#338).

Terrain is only targetable with Ctrl (AttackBase: TargetTerrainWithoutForceFire is false),
so making land terrain a valid target of the targeting weapon enables force-attack on
ground without changing what a plain right-click can target.
"""
from pathlib import Path

import pytest

from ra2data.resolved import REPO_ROOT, parse_miniyaml, resolve_actor, resolve_weapon

TILESETS = sorted((REPO_ROOT / "mods" / "ra2" / "tilesets").glob("*.yaml"))


def _types(value):
    return {t.strip() for t in value.split(",") if t.strip()}


def _terrain_target_types(path: Path):
    tree = parse_miniyaml(path.read_text(encoding="utf-8-sig"))
    return {k: _types(v["TargetTypes"]) for k, v in tree["Terrain"].items()
            if k.startswith("TerrainType@") and isinstance(v, dict) and "TargetTypes" in v}


def _actor_types(actor):
    out = set()
    for key, node in resolve_actor(actor).items():
        if (key == "Targetable" or key.startswith("Targetable@")) and isinstance(node, dict) and "TargetTypes" in node:
            out |= _types(node["TargetTypes"])
    return out


def _valid(weapon, types):
    valid = _types(weapon["ValidTargets"])
    invalid = _types(weapon.get("InvalidTargets", ""))
    return bool(valid & types) and not (invalid & types)


@pytest.mark.integration
def test_demo_truck_can_force_attack_ground():
    weapon = resolve_weapon("DemoTruckTargeting")
    for path in TILESETS:
        terrain = _terrain_target_types(path)
        land = {name: t for name, t in terrain.items() if "Ground" in t}
        assert land, path.name
        assert all(_valid(weapon, t) for t in land.values()), path.name
        water = {name: t for name, t in terrain.items() if "Water" in t and "Ground" not in t}
        assert not any(_valid(weapon, t) for t in water.values()), path.name


@pytest.mark.integration
def test_demo_truck_plain_click_targets_unchanged():
    """The weapon still accepts only DetonateAttack actors; ships and naval yards stay out."""
    weapon = resolve_weapon("DemoTruckTargeting")
    assert _types(weapon["ValidTargets"]) == {"DetonateAttack"}
    assert _types(weapon["Warhead@1Dam"]["ValidTargets"]) == {"DetonateAttack"}
    for actor in ("dest", "gayard", "nayard"):
        assert "Ground" in _actor_types(actor)
        assert not _valid(weapon, _actor_types(actor)), actor
    assert _valid(weapon, _actor_types("dog"))
