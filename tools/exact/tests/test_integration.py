"""Whole-mode checks of the Exact balance against the built mod (OpenRA.Utility --resolved-rules).

None of these needs the game's rules.ini: they read the committed generated files and the
mod as the engine resolves it. The sample values are RA2's own numbers, written out here.
"""
from __future__ import annotations

from functools import cache

import pytest

from exact import ai, generate
from exact.actors import SUFFIX, split_list, trait_type
from exact.snapshot import IGNORED_TRAIT
from ra2data.resolved import REPO_ROOT, parse_miniyaml, resolve_actor, resolve_weapon

pytestmark = pytest.mark.integration

# 83 RA2 buildables with a mod counterpart, plus both construction yards (RA2 does not list
# them as buildable; they are twinned because the MCVs deploy into them).
EXPECTED_TWINS = 85

# Mod buildables without a twin, with the reason (they are Reimagined only).
NOT_TWINNED = {
    "ghost": "RA2 defines GHOST but no player can build it",
}

# Prerequisites the tech-tree walk grants without any building, with the reason.
GRANTED_PREREQUISITES = {
    "techlevel.unrestricted": "Tech Level lobby option at its default (unrestricted)",
    "allies.tech.infiltrated": "infiltrating an Allied Battle Lab, not a building of the player",
    "soviets.tech.infiltrated": "infiltrating a Soviet Battle Lab, not a building of the player",
}
# Never granted: `disabled` (the construction yards are not built; the MCV deploys into them).

# StartingUnits actors that may stay the base actor in Exact, with the reason.
STARTING_UNITS_NOT_REPLACED: dict[str, str] = {}


def _twins() -> list[str]:
    return sorted(parse_miniyaml((REPO_ROOT / generate.ACTORS_FILE).read_text(encoding="utf-8")))


@cache
def _resolved(names: tuple[str, ...]) -> dict[str, dict]:
    return generate.resolve_all(list(names))


def _buildables(trees: dict[str, dict]) -> dict[str, list[str]]:
    """Actor -> prerequisites of its Buildable trait(s) (one list per trait, any may be met)."""
    out = {}
    for name, tree in trees.items():
        lists = [split_list(t.get("Prerequisites", "")) for k, t in tree.items()
            if trait_type(k) == "Buildable" and isinstance(t, dict)]
        if lists:
            out[name] = lists
    return out


def _met(prerequisites: list[str], provided: set[str]) -> bool:
    for p in prerequisites:
        name = p.lstrip("~")
        if name.startswith("!"):
            if name[1:] in provided:
                return False
        elif name not in provided:
            return False
    return True


def _provides(name: str, tree: dict, faction: str, provided: set[str]) -> set[str]:
    out = set()
    for key, trait in tree.items():
        if trait_type(key) != "ProvidesPrerequisite":
            continue
        trait = trait if isinstance(trait, dict) else {}
        factions = split_list(trait.get("Factions", ""))
        if factions and faction not in factions:
            continue
        if not _met(split_list(trait.get("RequiresPrerequisites", "")), provided):
            continue
        out.add(trait.get("Prerequisite") or name)
    return out


def reachable(trees: dict[str, dict], mcv: str, faction: str, granted: set[str]) -> set[str]:
    """Actors a player of `faction` gets from `mcv`: deploying (Transforms) and building what
    the prerequisites of the reached actors (plus `granted`) allow. A `!x` prerequisite is
    checked against what is provided so far; the conyard provides the faction ones first."""
    buildables = _buildables(trees)
    reached = {mcv}
    while True:
        provided = set(granted)
        for _ in range(2):  # ProvidesPrerequisite.RequiresPrerequisites may need a second pass
            for name in reached:
                provided |= _provides(name, trees[name], faction, provided)
        new = {t["IntoActor"] for n in reached for k, t in trees[n].items()
            if trait_type(k) == "Transforms" and isinstance(t, dict) and t.get("IntoActor") in trees}
        new |= {n for n, lists in buildables.items() if any(_met(p, provided) for p in lists)}
        if new <= reached:
            return reached
        reached |= new


def _factions_and_mcvs() -> dict[str, str]:
    """Playable faction -> its starting MCV (World's StartingUnits)."""
    world = resolve_actor("World")
    playable = {t["InternalName"] for k, t in world.items() if trait_type(k) == "Faction"
        and isinstance(t, dict) and t.get("Selectable", "True") != "False"}
    out = {}
    for key, trait in world.items():
        if trait_type(key) == "StartingUnits":
            for faction in split_list(trait.get("Factions", "")):
                if faction in playable:
                    out[faction] = trait["BaseActor"]
    return out


def test_every_buildable_has_twin():
    twins = _twins()
    assert len(twins) == EXPECTED_TWINS
    bases = generate.resolve_base_actors()
    assert set(NOT_TWINNED) <= set(bases)
    assert twins == sorted(n + SUFFIX for n in bases if n not in NOT_TWINNED)
    for name, tree in _resolved(tuple(twins)).items():
        assert any(trait_type(k) == "Buildable" for k in tree), name


def test_exact_tech_tree_reachable():
    """From each faction's MCV twin every Exact buildable is reachable, through what the Exact
    buildings provide (ProvidesPrerequisite@BASE names the base building) plus balance.exact,
    and each faction reaches the twins of exactly what it reaches in Reimagined."""
    twins = _twins()
    exact = _resolved(tuple(twins))
    bases = generate.resolve_base_actors()
    factions = _factions_and_mcvs()
    assert len(factions) == 9, factions
    union = set()
    for faction, mcv in sorted(factions.items()):
        got = reachable(exact, mcv + SUFFIX, faction, {"balance.exact", *GRANTED_PREREQUISITES})
        reimagined = reachable(bases, mcv, faction, {"balance.reimagined", *GRANTED_PREREQUISITES})
        assert got == {n + SUFFIX for n in reimagined if n not in NOT_TWINNED}, faction
        union |= got
    assert union == set(twins), sorted(set(twins) - union)


def test_sample_values():
    htnk = resolve_actor("htnk.exact")
    assert htnk["Valued"]["Cost"] == "900"
    assert htnk["Health"]["HP"] == "400"
    assert htnk["Armor"]["Type"] == "Heavy"
    assert htnk["Mobile"]["Speed"] == "72"
    e1 = resolve_actor("e1.exact")
    assert (e1["Valued"]["Cost"], e1["Health"]["HP"], e1["Armor"]["Type"], e1["Mobile"]["Speed"]) == \
        ("200", "125", "None", "48")
    assert e1["Buildable"]["Prerequisites"] == "~gapile, ~balance.exact"
    weapon = resolve_weapon("120mm.exact")
    damage = [w for k, w in weapon.items() if k.startswith("Warhead") and isinstance(w, dict)
        and w.get("_value") == "SpreadDamage"]
    assert len(damage) == 1
    assert damage[0]["Damage"] == "90"
    assert damage[0]["Versus"]["Wood"] == "65"


def test_starting_units_replaced_in_exact():
    world = resolve_actor("World")
    names = set()
    for key, trait in world.items():
        if trait_type(key) == "StartingUnits":
            names.add(trait["BaseActor"])
            names.update(split_list(trait.get("SupportActors", "")))
    assert names
    for name in sorted(names - set(STARTING_UNITS_NOT_REPLACED)):
        tree = resolve_actor(name)
        assert tree.get(IGNORED_TRAIT) == {"Prerequisite": "balance.exact", "IntoActor": name + SUFFIX}, name
        assert "Buildable" in resolve_actor(name + SUFFIX), name


def test_ai_lists_name_every_twin():
    """The resolved Player's bot modules list each twin beside its base (exact.ai)."""
    twins = {n[: -len(SUFFIX)] for n in _twins()}
    source = parse_miniyaml((REPO_ROOT / ai.AI_SOURCE).read_text(encoding="utf-8"))["Player"]
    player = resolve_actor("Player")
    checked = 0
    for key, trait in source.items():
        if not ai.is_bot_module(key) or not isinstance(trait, dict):
            continue
        for field, value in trait.items():
            names = split_list(value) if isinstance(value, str) else list(value) if isinstance(value, dict) else []
            for name in (n for n in names if n in twins):
                resolved = player[key][field]
                if isinstance(value, str):
                    assert name + SUFFIX in split_list(resolved), (key, field, name)
                else:
                    assert resolved[name + SUFFIX] == value[name], (key, field, name)
                checked += 1
    assert checked > 100
    assert {"amcv.exact", "smcv.exact"} <= set(split_list(player["McvManagerBotModule@test"]["McvTypes"]))
