"""Twin weapon generator (exact.weapons). Fictional rules and resolved trees."""
from __future__ import annotations

import pytest

from exact import conversions, generate, weapons
from exact.model import ExactWeapon, load_weapon
from ra2data.resolved import parse_miniyaml

VERSES = [10, 20, 30, 40, 50, 60, 70, 80, 90, 95, 99]


def make_weapon(**overrides) -> ExactWeapon:
    values = dict(id="Gun", damage=65, rof=60, range=5.75, burst=2, projectile="Shell", speed=40,
        warhead="AP", verses=list(VERSES), cell_spread=0.5, percent_at_max=25)
    values.update(overrides)
    return ExactWeapon(**values)


BASE = {
    "ReloadDelay": "99",
    "Range": "1c0",
    "Projectile": {"_value": "Bullet", "Speed": "682", "Image": "120mm"},
    "Warhead@1Dam": {"_value": "SpreadDamage", "Spread": "128", "Damage": "1",
        "Versus": {"None": "1", "Heavy": "1"}},
    "Warhead@2Eff": {"_value": "CreateEffect", "Explosions": "small"},
}


def twin(w=None, base=BASE, **kwargs) -> dict:
    tree = parse_miniyaml(weapons.render_twin_weapon(w or make_weapon(), base, **kwargs))
    assert list(tree) == ["Gun.exact"]
    return tree["Gun.exact"]


def test_versus_mapping_order():
    versus = twin()["Warhead@1Dam"]["Versus"]
    assert list(versus) == ["None", "Flak", "Plate", "Light", "Medium", "Heavy", "Wood", "Steel",
        "Concrete", "Drone", "Rocket"]
    assert list(versus.values()) == [str(v) for v in VERSES]


def test_reload_uses_frames_to_ticks(monkeypatch):
    monkeypatch.setattr(conversions, "RA2_FRAMES_PER_SECOND", 30.0)
    t = twin(make_weapon(rof=60))
    assert t["ReloadDelay"] == str(conversions.frames_to_ticks(60)) == "50"
    assert t["Inherits"] == "Gun"
    assert t["Range"] == "5c768"
    assert t["Burst"] == "2"


def test_damage_warhead_located_by_damage_field():
    base = {"Projectile": {"_value": "InstantHit"},
        "Warhead@Eff": {"_value": "CreateEffect", "Explosions": "small"},
        "Warhead@Hit": {"_value": "SpreadDamage", "Spread": "64", "Damage": "1"}}
    t = twin(base=base)
    assert "Warhead@Eff" not in t
    assert t["Warhead@Hit"]["_value"] == "SpreadDamage"
    assert t["Warhead@Hit"]["Damage"] == "65"
    # CellSpread 0.5 cells -> 0c512; linear falloff from 100 % to PercentAtMax at the edge.
    assert t["Warhead@Hit"]["Spread"] == "0c512"
    assert t["Warhead@Hit"]["Falloff"] == "100, 25"
    # InstantHit has no Speed field.
    assert "Projectile" not in t


def test_zero_cell_spread_is_point_damage():
    t = twin(make_weapon(cell_spread=0.0, percent_at_max=100))
    assert t["Warhead@1Dam"]["Spread"] == weapons.POINT_SPREAD
    assert t["Warhead@1Dam"]["Falloff"] == "100, 100"


def test_projectile_speed_converted_only_for_flying_ra2_projectiles():
    assert twin(projectile={"ROT": "20"})["Projectile"] == {"_value": "Bullet",
        "Speed": str(conversions.projectile_speed_to_openra(40))}
    for flags in ({"Inviso": "yes"}, {"Arcing": "true"}, {"Vertical": "yes"}):
        assert "Projectile" not in twin(projectile=flags)
    assert weapons.speed_note(make_weapon(), BASE, {"Arcing": "true"}) is not None
    assert weapons.speed_note(make_weapon(), BASE, {}) is None


def test_exception_reasons():
    w = make_weapon()
    assert weapons.exception_reason("Gun", None, BASE) == "no rules.ini section of that name"
    assert weapons.exception_reason("Gun", w, BASE) is None
    assert "PrismShot" in weapons.RESTRUCTURED
    assert weapons.exception_reason("prismshot", w, BASE) == weapons.RESTRUCTURED["PrismShot"]
    no_damage = {k: v for k, v in BASE.items() if k != "Warhead@1Dam"}
    assert weapons.exception_reason("Gun", w, no_damage) == "no damage warhead in the mod weapon"
    two = {**BASE, "Warhead@3Dam": {"_value": "SpreadDamage", "Damage": "5"}}
    assert weapons.exception_reason("Gun", w, two) == "2 damage warheads in the mod weapon"
    target = {**no_damage, "Warhead@1Dam": {"_value": "TargetDamage", "Damage": "5"}}
    assert "TargetDamage" in weapons.exception_reason("Gun", w, target)
    steps = {**no_damage, "Warhead@1Dam": {"_value": "SpreadDamage", "Damage": "5", "Range": "0, 32"}}
    assert "Range" in weapons.exception_reason("Gun", w, steps)


RULES = {
    "General": {"BuildSpeed": ".5"},
    "InfantryTypes": {"1": "ENGINEER", "2": "SPY"},
    "VehicleTypes": {"1": "TANK"},
    "BuildingTypes": {"1": "PRISM"},
    "ENGINEER": {"TechLevel": "1", "Owner": "Reds", "Primary": "Sapper"},
    "SPY": {"TechLevel": "1", "Owner": "Reds", "Primary": "FakeC4", "Secondary": "MakeupKit"},
    "TANK": {"TechLevel": "1", "Owner": "Reds", "Primary": "Gun", "ElitePrimary": "GunE"},
    "PRISM": {"TechLevel": "1", "Owner": "Reds", "Primary": "PrismShot", "Secondary": "PrismSupport"},
    "Sapper": {"Damage": "1", "Warhead": "AP"},
    "FakeC4": {"Damage": "1", "Warhead": "AP"},
    "MakeupKit": {"Damage": "1", "Warhead": "AP"},
    "PrismSupport": {"Damage": "1", "Warhead": "AP"},
    "PrismShot": {"Damage": "100", "ROF": "60", "Warhead": "AP"},
    "Gun": {"Damage": "65", "ROF": "60", "Range": "5.75", "Projectile": "Shell", "Speed": "40",
        "Warhead": "AP"},
    "GunE": {"Damage": "80", "ROF": "50", "Range": "5.75", "Projectile": "Shell", "Warhead": "AP"},
    "Shell": {"Arcing": "true"},
    "AP": {"Verses": "10%,20%,30%,40%,50%,60%,70%,80%,90%,95%,99%", "CellSpread": ".5",
        "PercentAtMax": "25%"},
}

TANK_BASE = {"Buildable": {}, "Armament@primary": {"Weapon": "Gun"},
    "Armament@elite": {"Weapon": "GunE"}, "Armament@beam": {"Weapon": "Dogjaw"}}
PRISM_BASE = {"Buildable": {}, "Armament": {"Weapon": "PrismShot"}}
OTHER_BASES = {"engineer": {"Buildable": {}}, "spy": {"Buildable": {}}}
MOD_WEAPONS = {"gun": "Gun", "gune": "GunE", "dogjaw": "Dogjaw", "prismshot": "PrismShot"}


def resolve(name):
    return {"Gun": BASE, "GunE": BASE, "PrismShot": BASE}[name]


@pytest.fixture
def weapons_file():
    outputs = generate.build(RULES, {"tank": TANK_BASE, "prism": PRISM_BASE, **OTHER_BASES}, MOD_WEAPONS,
        resolve_base_weapon=resolve)
    return outputs[generate.WEAPONS_FILE]


def test_weapons_without_mod_counterpart_go_to_exceptions(weapons_file):
    assert ("# RA2 weapons of twinned actors without a mod counterpart (not generated): "
        "FakeC4, MakeupKit, PrismSupport, Sapper") in weapons_file
    assert "# Exception: Dogjaw.exact: no rules.ini section of that name" in weapons_file
    assert f"# Exception: PrismShot.exact: {weapons.RESTRUCTURED['PrismShot']}" in weapons_file
    tree = parse_miniyaml(weapons_file)
    assert sorted(tree) == ["Dogjaw.exact", "Gun.exact", "GunE.exact", "PrismShot.exact"]
    # Exceptions stay Inherits stubs: base values kept, nothing fabricated.
    assert tree["Dogjaw.exact"] == {"Inherits": "Dogjaw"}
    assert tree["PrismShot.exact"] == {"Inherits": "PrismShot"}
    assert tree["Gun.exact"]["Warhead@1Dam"]["Damage"] == "65"
    assert tree["GunE.exact"]["Warhead@1Dam"]["Damage"] == "80"
    assert "# Not applied: Gun.exact: Projectile Speed" in weapons_file


def test_weapons_file_is_deterministic(weapons_file):
    again = generate.build(RULES, {**OTHER_BASES, "prism": PRISM_BASE, "tank": TANK_BASE}, MOD_WEAPONS,
        resolve_base_weapon=resolve)[generate.WEAPONS_FILE]
    assert again == weapons_file
    assert weapons_file.startswith(generate.HEADER)


def test_load_weapon_is_case_insensitive():
    assert load_weapon(RULES, "gune").id == "GunE"
    assert load_weapon(RULES, "nope") is None
