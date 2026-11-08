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
    assert sorted(actors) == ["LABX", "PLANT", "PRISM", "TANKY", "TROOP"]
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
    assert (labx.strength, labx.sight, labx.speed, labx.rot) == (0, 1, 0, 0)
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


def test_numbered_weapons(sample, actors):
    prism = actors["PRISM"]
    assert prism.primary is None
    assert prism.weapons == ["Rifle", "Pistol"]  # index order, undefined skipped
    assert prism.elite_weapons == ["RifleE"]
    assert "RifleE" in model.load_weapons(sample, [prism])


def test_resolve_prerequisite_aliases(sample):
    assert model.resolve_prerequisite_aliases(
        sample, ["power", "TECH", "LABX", "Missing"]) == [
        ["PLANT", "PLANT2"], ["LABX"], ["LABX"], ["Missing"]]


def test_duplicate_registration_raises():
    rules = {"InfantryTypes": {"1": "A"}, "VehicleTypes": {"1": "A"}}
    with pytest.raises(ValueError, match="registered as both"):
        model.load_actors(rules)


def test_build_minutes_known_unit(actors):
    # 800 credits at BuildSpeed .5 -> 0.4 minutes
    assert actors["PLANT"].build_minutes == pytest.approx(0.4)


def test_real_numbered_weapons(ra2ini):
    actors = model.load_actors(ra2ini)
    by_id = {a.id: a for a in actors}
    weapons = model.load_weapons(ra2ini, actors)
    assert "Comet" in by_id["SREF"].weapons
    assert "SuperComet" in by_id["SREF"].elite_weapons
    assert {"Comet", "SuperComet"} <= set(weapons)
    fv = by_id["FV"]
    assert len(fv.weapons) >= 10 and len(fv.elite_weapons) >= 10
    assert {"RepairBullet", "CRM60", "CRMP5", "CRNeutronRifle", "CowShot"} <= set(weapons)


def test_real_prerequisite_aliases_resolve(ra2ini):
    for actor in model.load_actors(ra2ini):
        for alts in model.resolve_prerequisite_aliases(ra2ini, actor.prerequisites):
            assert all(a in ra2ini for a in alts), (actor.id, alts)


def test_load_actor_ignores_buildability(sample):
    # GHOST is registered but TechLevel -1: load_actors skips it, load_actor does not.
    ghost = model.load_actor(sample, "GHOST")
    assert ghost is not None and ghost.id == "GHOST"
    assert model.load_actor(sample, "UNLISTED") is None
    assert model.load_actor(sample, "NOSUCH") is None


def test_load_actor_is_case_insensitive(sample):
    assert model.load_actor(sample, "ghost").id == "GHOST"


def test_rocket_launcher_takes_damage_and_warhead_from_the_globals():
    rules = {
        "V3Launcher": {"Damage": "1", "ROF": "150", "Range": "18", "Warhead": "Special"},
        "Special": {},
        "General": {"V3RocketDamage": "200"},
        "CombatDamage": {"V3Warhead": "V3WH"},
        "V3WH": {"CellSpread": "1", "PercentAtMax": ".25"},
    }
    weapon = model.load_weapon(rules, "V3Launcher")
    assert (weapon.damage, weapon.warhead, weapon.cell_spread, weapon.percent_at_max) == (200, "V3WH", 1.0, 25)
