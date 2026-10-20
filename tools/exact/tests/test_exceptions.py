"""Exact conversion exceptions (exact.exceptions) and EXCEPTIONS.md. Fictional rules and trees,
except the checks on the committed generated files and the integration test."""
from __future__ import annotations

import json

import pytest

from exact import conversions, exceptions, generate
from exact.snapshot import weapon_definitions
from ra2data.resolved import REPO_ROOT, parse_miniyaml

AP = {"Verses": "10%,20%,30%,40%,50%,60%,70%,80%,90%,95%,99%", "CellSpread": "0", "PercentAtMax": "100%"}

RULES = {
    "General": {"BuildSpeed": ".5"},
    "InfantryTypes": {"1": "CCOMAND", "2": "YURI"},
    "BuildingTypes": {"1": "PRISM", "2": "COIL"},
    "PRISM": {"TechLevel": "1", "Owner": "Reds", "Primary": "PrismShot", "Secondary": "PrismSupport"},
    "COIL": {"TechLevel": "1", "Owner": "Reds", "Primary": "CoilBolt", "Secondary": "OPCoilBolt"},
    "CCOMAND": {"TechLevel": "1", "Owner": "Reds", "Primary": "ChronoMP5"},
    "YURI": {"TechLevel": "1", "Owner": "Reds", "Primary": "MindControl"},
    "PrismShot": {"Damage": "120", "ROF": "60", "Range": "8", "Warhead": "AP"},
    "PrismSupport": {"Damage": "1", "Warhead": "AP"},
    "CoilBolt": {"Damage": "200", "ROF": "120", "Range": "7", "Warhead": "AP"},
    "OPCoilBolt": {"Damage": "300", "ROF": "100", "Range": "8", "Warhead": "AP"},
    "ChronoMP5": {"Damage": "125", "ROF": "10", "Range": "6", "Warhead": "AP"},
    "MP5": {"Damage": "1", "ROF": "99", "Warhead": "AP"},
    "MindControl": {"Damage": "10", "ROF": "200", "Range": "7", "Warhead": "AP"},
    "AP": AP,
}

SPREAD = {"Projectile": {"_value": "InstantHit"},
    "Warhead@1Dam": {"_value": "SpreadDamage", "Spread": "42", "Damage": "1"}}
TARGET = {"Warhead@1Dam": {"_value": "TargetDamage", "Damage": "0"}}

BASES = {
    "prism": {"Buildable": {}, "Armament": {"Weapon": "PrismShot"}, "AttackTesla": {"ReloadDelay": "60"}},
    "coil": {"Buildable": {}, "Armament": {"Weapon": "CoilBolt"},
        "Armament@charged": {"Weapon": "OPCoilBolt"},
        "AttackTesla": {"ReloadDelay": "120", "MaxCharges": "3"}},
    "ccomand": {"Buildable": {}, "Armament": {"Weapon": "MP5"}},
    "yuri": {"Buildable": {}, "Armament": {"Weapon": "MindControl"}},
}
MOD_WEAPONS = {n.lower(): n for n in ("PrismShot", "CoilBolt", "OPCoilBolt", "MP5", "MindControl")}


def resolve(name):
    return TARGET if name == "MindControl" else SPREAD


def build(bases=None, resolve_weapon=resolve):
    return generate.build(RULES, bases or BASES, MOD_WEAPONS, resolve_base_weapon=resolve_weapon)


def trees(outputs):
    return (parse_miniyaml(outputs[generate.ACTORS_FILE]),
        parse_miniyaml(outputs[generate.WEAPONS_FILE]))


@pytest.mark.parametrize("fps", [25.0, 30.0])
def test_prism_total_delay_equals_ra2_rof(monkeypatch, fps):
    monkeypatch.setattr(conversions, "RA2_FRAMES_PER_SECOND", fps)
    actors_tree, weapons_tree = trees(build())
    charge = exceptions.ATTACK_TESLA_INITIAL_CHARGE_DELAY
    reload = int(actors_tree["prism.exact"]["AttackTesla"]["ReloadDelay"])
    assert charge + reload == conversions.frames_to_ticks(60)
    # The armament never holds the tower back: the trait sets the pace.
    assert int(weapons_tree["PrismShot.exact"]["ReloadDelay"]) == reload
    # RA2 values for the rest; MaxCharges already 1, so not written.
    assert weapons_tree["PrismShot.exact"]["Warhead@1Dam"]["Damage"] == "120"
    assert "MaxCharges" not in actors_tree["prism.exact"]["AttackTesla"]


def test_prism_uses_the_actors_own_charge_delay():
    bases = {**BASES, "prism": {**BASES["prism"],
        "AttackTesla": {"ReloadDelay": "60", "InitialChargeDelay": "10"}}}
    actors_tree, _ = trees(build(bases))
    assert int(actors_tree["prism.exact"]["AttackTesla"]["ReloadDelay"]) + 10 == conversions.frames_to_ticks(60)


def test_tesla_fires_each_weapon_once_per_its_ra2_rof():
    actors_tree, weapons_tree = trees(build())
    trait = actors_tree["coil.exact"]["AttackTesla"]
    charge = exceptions.ATTACK_TESLA_INITIAL_CHARGE_DELAY
    reload = int(trait["ReloadDelay"])
    assert trait["MaxCharges"] == "1"
    # Faster OPCoilBolt (ROF 100): the trait sets the pace, charge + reload == ROF.
    assert charge + reload == conversions.frames_to_ticks(100)
    assert int(weapons_tree["OPCoilBolt.exact"]["ReloadDelay"]) == reload
    # Slower CoilBolt (ROF 120): its own reload, longer than charge + reload, sets the pace.
    assert int(weapons_tree["CoilBolt.exact"]["ReloadDelay"]) == conversions.frames_to_ticks(120)


def test_tesla_with_a_stub_weapon_fails_loudly():
    def broken(name):
        return TARGET if name in ("MindControl", "PrismShot") else SPREAD
    with pytest.raises(ValueError, match="tesla-charge: prism fires PrismShot"):
        build(resolve_weapon=broken)


def test_mind_control_takes_ra2_timing_and_keeps_zero_damage():
    _, weapons_tree = trees(build())
    assert weapons_tree["MindControl.exact"] == {"Inherits": "MindControl",
        "ReloadDelay": str(conversions.frames_to_ticks(200)), "Range": "7c0", "Burst": "1"}


def test_mp5_filled_from_chrono_mp5():
    outputs = build()
    _, weapons_tree = trees(outputs)
    mp5 = weapons_tree["MP5.exact"]
    assert mp5["ReloadDelay"] == str(conversions.frames_to_ticks(10))
    assert mp5["Warhead@1Dam"]["Damage"] == "125"
    report = json.loads(outputs[generate.WEAPON_EXCEPTIONS_FILE])
    assert {"twin": "MP5.exact", "ra2": "ChronoMP5", "exception": "chrono-commando-mp5"} in report["sources"]
    # ChronoMP5 is used, so it is not listed as without a mod counterpart.
    assert [e["weapon"] for e in report["ra2_without_mod_counterpart"]] == ["PrismSupport"]


def test_alias_that_does_not_fit_keeps_the_stub():
    def target_mp5(name):
        return TARGET if name in ("MindControl", "MP5") else SPREAD
    outputs = build(resolve_weapon=target_mp5)
    _, weapons_tree = trees(outputs)
    assert weapons_tree["MP5.exact"] == {"Inherits": "MP5"}
    report = json.loads(outputs[generate.WEAPON_EXCEPTIONS_FILE])
    stub = next(e for e in report["exceptions"] if e["twin"] == "MP5.exact")
    assert stub["exception"] == "chrono-commando-mp5" and "TargetDamage" in stub["reason"]


def test_exceptions_are_well_formed():
    ids = [e.id for e in exceptions.EXCEPTIONS]
    assert len(ids) == len(set(ids))
    for e in exceptions.EXCEPTIONS:
        assert e.field and e.reason
        # A rule reports what it changed; a generator-made exception lists what it affected.
        assert e.rule is not None or e.listed is not None, e.id


def _assert_lists_everything(report_md: str, weapon_report: dict) -> None:
    for e in exceptions.EXCEPTIONS:
        assert f"\n## {e.id}\n" in report_md, e.id
        assert e.reason in report_md, e.id
    for stub in weapon_report["exceptions"]:
        assert stub["twin"] in report_md, stub
    for weapon in weapon_report["ra2_without_mod_counterpart"]:
        assert f"  - {weapon['weapon']}" in report_md, weapon
    for source in weapon_report["sources"]:
        assert source["twin"] in report_md, source


def test_every_exception_listed_in_report():
    outputs = build()
    report_md = outputs[exceptions.REPORT_FILE]
    assert report_md.startswith(generate.HEADER)
    _assert_lists_everything(report_md, json.loads(outputs[generate.WEAPON_EXCEPTIONS_FILE]))
    assert "  - prism.exact: AttackTesla ReloadDelay" in report_md


def test_every_exception_listed_in_committed_report():
    report_md = (REPO_ROOT / exceptions.REPORT_FILE).read_text(encoding="utf-8")
    weapon_report = json.loads((REPO_ROOT / generate.WEAPON_EXCEPTIONS_FILE).read_text(encoding="utf-8"))
    assert report_md.startswith(generate.HEADER)
    _assert_lists_everything(report_md, weapon_report)


def test_committed_prism_total_delay_equals_ra2_rof():
    """The committed twins (RA2 PrismShot ROF=60; atesla has no InitialChargeDelay)."""
    actors_tree = parse_miniyaml((REPO_ROOT / generate.ACTORS_FILE).read_text(encoding="utf-8"))
    weapons_tree = parse_miniyaml((REPO_ROOT / generate.WEAPONS_FILE).read_text(encoding="utf-8"))
    reload = int(actors_tree["atesla.exact"]["AttackTesla"]["ReloadDelay"])
    assert exceptions.ATTACK_TESLA_INITIAL_CHARGE_DELAY + reload == conversions.frames_to_ticks(60)
    assert int(weapons_tree["PrismShot.exact"]["ReloadDelay"]) == reload


def test_report_is_deterministic():
    assert build()[exceptions.REPORT_FILE] == build(dict(reversed(list(BASES.items()))))[exceptions.REPORT_FILE]


@pytest.mark.integration
def test_report_is_generated_and_up_to_date(ra2ini):
    outputs = generate.build(ra2ini, generate.resolve_base_actors(), weapon_definitions(),
        generate.resolve_host_actors())
    assert outputs[exceptions.REPORT_FILE] == (REPO_ROOT / exceptions.REPORT_FILE).read_text(encoding="utf-8"), \
        "regenerate with `python3 -m exact.generate`"
    again = generate.build(ra2ini, generate.resolve_base_actors(), weapon_definitions(),
        generate.resolve_host_actors())
    assert again[exceptions.REPORT_FILE] == outputs[exceptions.REPORT_FILE]
