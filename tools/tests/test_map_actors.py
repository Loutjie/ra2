"""Static check for the OpenRA/ra2 #826 / #814 map-load crash.

"This frozen actor has no footprint" is thrown when an actor with
FrozenUnderFog is spawned whose whole footprint lies outside the map's
playable Bounds (or outside the map). This test parses every map.yaml and
flags such actors.

Off-map waypoints are allowed (never rendered or frozen).
"""
from __future__ import annotations

import re
from pathlib import Path

import pytest

from ra2data import resolved

MAPS_DIR = Path(__file__).resolve().parents[2] / "mods" / "ra2" / "maps"
MAP_YAMLS = sorted(MAPS_DIR.glob("*/map.yaml"))


def parse_map(path: Path):
    """-> (map_size, bounds, [(id, name, x, y)]) from a map.yaml."""
    map_size = bounds = None
    actors = []
    section = None
    current = None
    for line in path.read_text(encoding="utf-8").splitlines():
        if line and not line[0].isspace():
            section = line.split(":")[0]
            if section == "MapSize":
                map_size = tuple(int(v) for v in line.split(":")[1].split(","))
            elif section == "Bounds":
                bounds = tuple(int(v) for v in line.split(":")[1].split(","))
        elif section == "Actors":
            m = re.match(r"\t([^\t:]+):\s*(\S+)", line)
            if m:
                current = (m[1], m[2])
                continue
            m = re.match(r"\t\tLocation:\s*(-?\d+),\s*(-?\d+)", line)
            if m and current:
                actors.append((*current, int(m[1]), int(m[2])))
    return map_size, bounds, actors


def to_mpos(x: int, y: int) -> tuple[int, int]:
    """CPos.ToMPos for RectangularIsometric grids (engine/OpenRA.Game/CPos.cs)."""
    v = x + y
    return (v - (v & 1)) // 2 - y, v


def in_map(cell, map_size) -> bool:
    x, y = cell
    if x < y:  # invalid in the RectangularIsometric coordinate system
        return False
    u, v = to_mpos(x, y)
    return 0 <= u < map_size[0] and 0 <= v < map_size[1]


def in_bounds(cell, bounds) -> bool:
    u, v = to_mpos(*cell)
    return bounds[0] <= u < bounds[0] + bounds[2] and bounds[1] <= v < bounds[1] + bounds[3]


def footprint_cells(rules: dict, x: int, y: int):
    """Cells covered by an actor: Building.Footprint ('x' occupied) or one cell."""
    building = rules.get("Building")
    if not isinstance(building, dict) or "Footprint" not in building:
        return [(x, y)]
    rows = building["Footprint"].split()
    return [(x + cx, y + cy) for cy, row in enumerate(rows) for cx, c in enumerate(row) if c != "_"]


# Waypoints are never rendered or frozen, so some maps keep them off-map.
OFF_MAP_ALLOWED = {"waypoint"}


def outside_map_actors(path: Path):
    map_size, bounds, actors = parse_map(path)
    return [a for a in actors if a[1] not in OFF_MAP_ALLOWED and not in_map((a[2], a[3]), map_size)]


_RULES: dict[str, dict] = {}


def frozen_footprint_offenders(path: Path):
    """FrozenUnderFog actors with no footprint cell inside Bounds."""
    map_size, bounds, actors = parse_map(path)
    candidates = [a for a in actors if not in_bounds((a[2], a[3]), bounds)]
    rules = _RULES
    offenders = []
    for aid, name, x, y in candidates:
        if name not in rules:
            try:
                rules[name] = resolved.resolve_actor(name)
            except RuntimeError as e:
                if "Could not find actor" not in str(e):
                    raise
                rules[name] = {}  # not defined in rules: the engine would reject it elsewhere
        r = rules[name]
        if "FrozenUnderFog" not in r:
            continue
        if not any(in_map(c, map_size) and in_bounds(c, bounds) for c in footprint_cells(r, x, y)):
            offenders.append((aid, name, x, y))
    return offenders


MAP_IDS = [p.parent.name for p in MAP_YAMLS]


@pytest.mark.parametrize("path", MAP_YAMLS, ids=MAP_IDS)
def test_no_actors_outside_map(path):
    bad = outside_map_actors(path)
    assert not bad, f"{len(bad)} actor(s) outside the map: " + ", ".join(f"{a[0]}:{a[1]}@{a[2]},{a[3]}" for a in bad)


@pytest.mark.integration
@pytest.mark.parametrize("path", MAP_YAMLS, ids=MAP_IDS)
def test_no_frozen_actor_without_footprint(path):
    bad = frozen_footprint_offenders(path)
    assert not bad, f"{len(bad)} crash-prone actor(s): " + ", ".join(f"{a[0]}:{a[1]}@{a[2]},{a[3]}" for a in bad)


def test_to_mpos_matches_engine():
    # Alamo cafncp at 27,-27 is v=0 (above Bounds top=4).
    assert to_mpos(27, -27) == (27, 0)
    assert to_mpos(5, 3) == (1, 8)
    assert not in_map((3, 5), (60, 120))
