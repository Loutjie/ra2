"""Guards that the Reimagined balance never changes (apart from two allowed additions)."""
from __future__ import annotations

import json

import pytest

from exact.snapshot import (NEUTRAL_TECH_ACTORS, SNAPSHOT_PATH, _blocks, _canonical, capture, strip_appended_twins,
    strip_ignored, strip_neutral, twinned_actors)

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


# --- neutral tech buildings: Exact/Reimagined trait pairs (fictional data) ---

NEUTRAL_ORIGINAL = {
    "Health": {"HP": "1000"},
    "CashTrickler": {"Interval": "100", "Amount": "20"},
    "Tooltip": {"Name": "Fake Derrick"},
}


def _neutral_with_pair(reimagined_trickler):
    return {
        "Health": {"HP": "1000"},
        "Tooltip": {"Name": "Fake Derrick"},
        "GrantConditionOnPrerequisite@BALANCE": {"Condition": "balance-exact", "Prerequisites": "balance.exact"},
        "CashTrickler@EXACT": {"Interval": "150", "Amount": "40", "RequiresCondition": "balance-exact"},
        "CashTrickler@REIMAGINED": reimagined_trickler,
    }


def test_neutral_unchanged_without_pairs():
    assert strip_neutral(NEUTRAL_ORIGINAL) == NEUTRAL_ORIGINAL


def test_neutral_reimagined_pair_equals_original():
    current = _neutral_with_pair({"Interval": "100", "Amount": "20", "RequiresCondition": "!balance-exact"})
    assert strip_neutral(current) == strip_neutral(NEUTRAL_ORIGINAL)


def test_neutral_reimagined_value_change_fails():
    current = _neutral_with_pair({"Interval": "100", "Amount": "25", "RequiresCondition": "!balance-exact"})
    assert strip_neutral(current) != strip_neutral(NEUTRAL_ORIGINAL)


def test_neutral_reimagined_without_exact_condition_fails():
    for condition in (None, "balance-exact", "!balance-exact && foo"):
        trickler = {"Interval": "100", "Amount": "20"}
        if condition:
            trickler["RequiresCondition"] = condition
        assert strip_neutral(_neutral_with_pair(trickler)) != strip_neutral(NEUTRAL_ORIGINAL), condition


def test_neutral_reimagined_beside_original_fails():
    current = _neutral_with_pair({"Interval": "100", "Amount": "20", "RequiresCondition": "!balance-exact"})
    current["CashTrickler"] = {"Interval": "100", "Amount": "20"}
    assert strip_neutral(current) != strip_neutral(NEUTRAL_ORIGINAL)


def test_neutral_removed_trait_fails():
    current = _neutral_with_pair({"Interval": "100", "Amount": "20", "RequiresCondition": "!balance-exact"})
    del current["Health"]
    assert strip_neutral(current) != strip_neutral(NEUTRAL_ORIGINAL)


def test_neutral_other_condition_grant_is_not_ignored():
    current = dict(NEUTRAL_ORIGINAL, **{"GrantConditionOnPrerequisite@OTHER": {"Condition": "x"}})
    assert strip_neutral(current) != strip_neutral(NEUTRAL_ORIGINAL)


def test_strip_neutral_does_not_mutate_input():
    current = _neutral_with_pair({"Interval": "100", "Amount": "20", "RequiresCondition": "!balance-exact"})
    before = json.dumps(current, sort_keys=True)
    strip_neutral(current)
    assert json.dumps(current, sort_keys=True) == before


# --- appended Exact twin names in repair/rearm host lists (fictional data) ---


FAKE_TWINS = {"fakedepot", "fakepad"}


def test_strip_removes_appended_twin_hosts():
    tree = {"Repairable": {"RepairActors": "fakedepot, fakeoutpost, fakedepot.exact", "Voice": "Move"},
        "Rearmable@x": {"RearmActors": "fakepad, fakepad.exact"}}
    assert strip_ignored(tree, FAKE_TWINS) == {"Repairable": {"RepairActors": "fakedepot, fakeoutpost", "Voice": "Move"},
        "Rearmable@x": {"RearmActors": "fakepad"}}


def test_strip_keeps_twin_name_without_base():
    assert strip_appended_twins("fakedepot, fakepad.exact", FAKE_TWINS) == "fakedepot, fakepad.exact"


def test_strip_keeps_twin_name_not_at_end():
    value = "fakedepot, fakedepot.exact, fakepad"
    assert strip_appended_twins(value, FAKE_TWINS) == value


def test_strip_keeps_appended_name_of_untwinned_actor():
    # otherdepot has no twin, so `otherdepot.exact` is not an allowed addition.
    old = {"Repairable": {"RepairActors": "fakedepot, otherdepot"}}
    new = {"Repairable": {"RepairActors": "fakedepot, otherdepot, otherdepot.exact"}}
    assert strip_appended_twins(new["Repairable"]["RepairActors"], FAKE_TWINS) == "fakedepot, otherdepot, otherdepot.exact"
    assert strip_ignored(old, FAKE_TWINS) != strip_ignored(new, FAKE_TWINS)
    new["Repairable"]["RepairActors"] = "fakedepot, otherdepot, fakedepot.exact"
    assert strip_ignored(old, FAKE_TWINS) == strip_ignored(new, FAKE_TWINS)


def test_twinned_actors_read_from_generated_file(tmp_path):
    path = tmp_path / "actors.yaml"
    path.write_text("# fakedepot.exact: a comment\n\nfakedepot.exact:\n\tInherits: fakedepot\n"
        "fakepad.exact:\n\tInherits: fakepad\n", encoding="utf-8")
    assert twinned_actors(path) == {"fakedepot", "fakepad"}
    assert twinned_actors(tmp_path / "missing.yaml") == frozenset()


def test_strip_ignores_twin_names_only_in_host_fields():
    tree = {"FreeActor": {"Actor": "fakeminer, fakeminer.exact"}}
    assert strip_ignored(tree) == tree


def test_appended_host_change_plus_real_change_fails():
    old = {"Repairable": {"RepairActors": "fakedepot"}}
    new = {"Repairable": {"RepairActors": "fakedepot, otherdepot, fakedepot.exact"}}
    assert strip_ignored(old, FAKE_TWINS) != strip_ignored(new, FAKE_TWINS)


def test_blocks_skip_exact_twins(tmp_path):
    (tmp_path / "a.yaml").write_text("fake:\n\tBuildable:\nfake.exact:\n\tInherits: fake\n", encoding="utf-8")
    assert _blocks(tmp_path) == {"fake": "\tBuildable:"}


# --- integration test against the built mod ---


@pytest.mark.integration
def test_reimagined_unchanged():
    snapshot = json.loads(SNAPSHOT_PATH.read_text(encoding="utf-8"))
    assert snapshot["actors"] and snapshot["weapons"]
    assert sorted(snapshot["neutral"]) == sorted(NEUTRAL_TECH_ACTORS)
    current = capture(sorted(snapshot["actors"]), sorted(snapshot["neutral"]))
    assert sorted(current["actors"]) == sorted(snapshot["actors"]), "buildable actor set changed"
    assert sorted(current["weapons"]) == sorted(snapshot["weapons"]), "referenced weapon set changed"
    diffs = [("actor", n) for n in snapshot["actors"]
        if strip_ignored(current["actors"][n]) != strip_ignored(snapshot["actors"][n])]
    diffs += [("weapon", n) for n in snapshot["weapons"]
        if strip_ignored(current["weapons"][n]) != strip_ignored(snapshot["weapons"][n])]
    diffs += [("neutral", n) for n in snapshot["neutral"]
        if strip_neutral(current["neutral"][n]) != strip_neutral(snapshot["neutral"][n])]
    assert not diffs, f"Reimagined balance changed for: {diffs}"
