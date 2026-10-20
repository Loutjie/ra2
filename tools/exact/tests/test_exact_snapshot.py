"""Guards that the Exact twins only change when exact-snapshot.json is regenerated on purpose."""
from __future__ import annotations

import json

import pytest

from exact import snapshot
from exact.snapshot import EXACT_SNAPSHOT_PATH, capture_exact, dumps, twin_weapons, twinned_actors

# --- unit tests (fictional data) ---


def test_twin_weapons_are_top_level_keys(tmp_path):
    path = tmp_path / "weapons.yaml"
    path.write_text("# GENERATED\n\nGun.exact:\n\tInherits: Gun\n\tReloadDelay: 5\n# Cut.exact: comment\n"
        "Boom.exact:\n\tInherits: Boom\n", encoding="utf-8")
    assert twin_weapons(path) == ["Boom.exact", "Gun.exact"]
    assert twin_weapons(tmp_path / "missing.yaml") == []


def test_capture_exact_resolves_every_twin(monkeypatch):
    trees = {"fakegun.exact": {"Health": {"HP": "1"}}, "fakepad.exact": {"Health": {"HP": "2"}}}
    weapons = {"Gun.exact": {"ReloadDelay": "5"}}
    monkeypatch.setattr(snapshot, "twinned_actors", lambda: frozenset({"fakepad", "fakegun"}))
    monkeypatch.setattr(snapshot, "twin_weapons", lambda: sorted(weapons))
    monkeypatch.setattr(snapshot, "_resolve", lambda name, without=(): trees[name])
    monkeypatch.setattr(snapshot, "resolve_weapon", lambda name, without=(): weapons[name])
    data = capture_exact()
    assert data == {"actors": trees, "weapons": weapons}
    # Deterministic bytes regardless of resolution order.
    assert dumps(data) == dumps({"weapons": weapons, "actors": dict(reversed(list(trees.items())))})


# --- drift guard against the built mod (no rules.ini needed) ---


@pytest.mark.integration
def test_exact_twins_unchanged():
    """A base actor or weapon edit that a twin inherits changes Exact: fail until the snapshot is
    regenerated on purpose (python3 -m exact.snapshot --write-exact, from tools/)."""
    recorded = json.loads(EXACT_SNAPSHOT_PATH.read_text(encoding="utf-8"))
    assert sorted(recorded["actors"]) == sorted(n + ".exact" for n in twinned_actors()), "twin actor set changed"
    assert sorted(recorded["weapons"]) == twin_weapons(), "twin weapon set changed"
    current = capture_exact()
    diffs = [("actor", n) for n in recorded["actors"] if current["actors"][n] != recorded["actors"][n]]
    diffs += [("weapon", n) for n in recorded["weapons"] if current["weapons"][n] != recorded["weapons"][n]]
    assert not diffs, f"Exact twins changed (regenerate exact-snapshot.json if intended): {diffs}"
