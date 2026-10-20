"""Guards that the Reimagined balance never changes (apart from two allowed additions)."""
from __future__ import annotations

import json

import pytest

from exact.snapshot import SNAPSHOT_PATH, _canonical, capture, strip_ignored

# --- unit tests of the ignore logic (fictional data) ---


def test_strip_removes_reimagined_prerequisite_token():
    tree = {"Buildable": {"Prerequisites": "~fakepile, ~balance.reimagined", "Queue": "Q"}}
    assert strip_ignored(tree) == {"Buildable": {"Prerequisites": "~fakepile", "Queue": "Q"}}


def test_strip_keeps_prerequisites_without_token():
    tree = {"Buildable@X": {"Prerequisites": "~fakepile, fakeradar"}}
    assert strip_ignored(tree) == tree


def test_strip_unspaced_and_mid_list_token():
    tree = {"Buildable": {"Prerequisites": "~a,~balance.reimagined,~b"}}
    assert strip_ignored(tree) == {"Buildable": {"Prerequisites": "~a, ~b"}}


def test_canonical_weapon_names_case_insensitive():
    defs = {"20mme": "20mme", "awpe": "AWPE"}
    assert _canonical({"20mmE", "awpe", "AWPE"}, defs) == {"20mme", "AWPE"}


def test_undefined_weapon_fails():
    with pytest.raises(RuntimeError, match="nope"):
        _canonical({"nope"}, {"x": "x"})


def test_strip_drops_prerequisites_if_token_was_only_entry():
    assert strip_ignored({"Buildable": {"Prerequisites": "~balance.reimagined"}}) == {"Buildable": {}}


def test_strip_removes_balance_replace_trait_only():
    tree = {"ReplaceOnPrerequisite@BALANCE": {"Foo": "1"}, "ReplaceOnPrerequisite@OTHER": {"Foo": "1"}}
    assert strip_ignored(tree) == {"ReplaceOnPrerequisite@OTHER": {"Foo": "1"}}


def test_strip_does_not_mutate_input():
    tree = {"Buildable": {"Prerequisites": "a, ~balance.reimagined"}}
    strip_ignored(tree)
    assert tree["Buildable"]["Prerequisites"] == "a, ~balance.reimagined"


def test_real_difference_still_fails():
    old = {"Buildable": {"Prerequisites": "~fakepile"}, "Health": {"HP": "100"}}
    new = {"Buildable": {"Prerequisites": "~fakepile, ~balance.reimagined"}, "Health": {"HP": "101"},
        "ReplaceOnPrerequisite@BALANCE": {"X": "1"}}
    assert strip_ignored(old) != strip_ignored(new)
    new["Health"]["HP"] = "100"
    assert strip_ignored(old) == strip_ignored(new)


# --- integration test against the built mod ---


@pytest.mark.integration
def test_reimagined_unchanged():
    snapshot = json.loads(SNAPSHOT_PATH.read_text(encoding="utf-8"))
    assert snapshot["actors"] and snapshot["weapons"]
    current = capture(sorted(snapshot["actors"]))
    assert sorted(current["actors"]) == sorted(snapshot["actors"]), "buildable actor set changed"
    assert sorted(current["weapons"]) == sorted(snapshot["weapons"]), "referenced weapon set changed"
    diffs = [("actor", n) for n in snapshot["actors"]
        if strip_ignored(current["actors"][n]) != strip_ignored(snapshot["actors"][n])]
    diffs += [("weapon", n) for n in snapshot["weapons"]
        if strip_ignored(current["weapons"][n]) != strip_ignored(snapshot["weapons"][n])]
    assert not diffs, f"Reimagined balance changed for: {diffs}"
