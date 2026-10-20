"""Tests for exact.model against a fictional fixture and the real rules.ini."""
from __future__ import annotations

from pathlib import Path

import pytest

from exact import model
from ra2data import ini

FIXTURE = Path(__file__).parent / "fixtures" / "rules_sample.ini"


@pytest.fixture(scope="module")
def sample():
    return ini.parse_ini(str(FIXTURE))


@pytest.fixture(scope="module")
def actors(sample):
    return {a.id: a for a in model.load_actors(sample)}


def test_load_actors_filters_buildable(actors):
    # GHOST: TechLevel -1. UNLISTED: not in any registration list.
    # NOOWNER: no Owner. LABX: TechLevel 10 is the inclusive upper bound.
    assert sorted(actors) == ["LABX", "PLANT", "TANKY", "TROOP"]
    assert actors["TROOP"].kind == "infantry"
    assert actors["TANKY"].kind == "vehicle"
    assert actors["PLANT"].kind == "building"


def test_actor_fields(actors):
    troop = actors["TROOP"]
    assert (troop.cost, troop.strength, troop.armor) == (200, 100, "none")
    assert (troop.speed, troop.sight, troop.rot) == (4, 5, 0)
    assert troop.primary == "Rifle" and troop.secondary == "Pistol"
    assert troop.elite_primary == "RifleE" and troop.elite_secondary is None
    assert troop.prerequisites == ["PLANT", "LABX"]
    assert troop.veteran_abilities == ["STRONGER", "FIREPOWER"]
    assert troop.elite_abilities == ["SELF_HEAL"]
    assert troop.build_minutes == pytest.approx(0.1)
    assert actors["TANKY"].armor == "heavy"
    assert actors["TANKY"].rot == 5
    assert actors["TANKY"].death_weapon == "Boom"
    assert actors["PLANT"].power == 200 and actors["LABX"].power == -50
    assert actors["PLANT"].superweapon == "ZapSpecial"
    assert actors["PLANT"].raw["Armor"] == "wood"


def test_engine_defaults(actors):
    labx = actors["LABX"]
    assert (labx.strength, labx.sight, labx.speed, labx.rot) == (1, 1, 0, 0)
    assert labx.armor == "none" and labx.primary is None


def test_weapon_verses_parsed_to_ints(sample, actors):
    weapons = model.load_weapons(sample, list(actors.values()))
    assert set(weapons) == {"Rifle", "RifleE", "Pistol", "Cannon", "Boom"}
    rifle = weapons["Rifle"]
    assert rifle.verses == [100, 80, 70, 50, 25, 25, 75, 50, 25, 100, 100]
    assert (rifle.damage, rifle.rof, rifle.range, rifle.burst) == (15, 20, 4.0, 1)
    assert (rifle.projectile, rifle.speed, rifle.warhead) == ("Invisible", 100, "Light")
    assert (rifle.cell_spread, rifle.percent_at_max) == (0.0, 100)
    assert weapons["Pistol"].burst == 2 and weapons["Pistol"].speed == 100
    cannon = weapons["Cannon"]
    # "80 %" (spaced) is read as 80; spread and falloff come from the warhead.
    assert cannon.verses == [100, 90, 80, 70, 60, 50, 40, 30, 20, 10, 0]
    assert (cannon.cell_spread, cannon.percent_at_max, cannon.speed) == (1.5, 50, 40)
    assert len(cannon.verses) == 11


def test_general_keys(sample):
    general = model.load_general(sample)
    assert general["VeteranRatio"] == "2.5"
    assert general["MultipleFactory"] == ".8"
    assert general["BuildSpeed"] == ".5"
    assert "Name" not in general


def test_parse_bool_and_list():
    assert [model.parse_bool(v) for v in ("yes", "Yes", "TRUE", "1", "no", "", None)] == \
        [True, True, True, True, False, False, False]
    assert model.parse_list(" a , b,, c ") == ["a", "b", "c"]


def test_real_rules_has_87_buildables(ra2ini):
    actors = model.load_actors(ra2ini)
    assert len(actors) == 87, sorted(a.id for a in actors)
    weapons = model.load_weapons(ra2ini, actors)
    assert weapons
    assert all(len(w.verses) == 11 for w in weapons.values())
