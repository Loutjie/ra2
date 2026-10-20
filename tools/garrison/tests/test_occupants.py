"""Occupants (GI, Conscript) and their weapons, checked against base RA2 rules.ini."""
import re
from pathlib import Path

import pytest

from garrison import generate
from ra2data import ini
from ra2data.resolved import cells

REPO_ROOT = Path(__file__).resolve().parents[3]
RULES = sorted((REPO_ROOT / "mods" / "ra2" / "rules").glob("*.yaml"))
GARRISONERS = {"e1", "e2", "e1.exact", "e2.exact"}


def actor_blocks():
    """(actor name, [lines]) for every top-level node of every rules file."""
    for path in RULES:
        name, lines = None, []
        for line in path.read_text(encoding="utf-8").splitlines():
            if line and not line[0] in "\t #":
                if name:
                    yield name, lines
                name, lines = line.rstrip(":").strip(), []
            elif name:
                lines.append(line)
        if name:
            yield name, lines


def test_only_gi_and_conscript_are_garrisoners():
    found = {n for n, lines in actor_blocks() if any(re.match(r"\s*CargoType:\s*Garrisoner\s*$", l) for l in lines)}
    # a template (^name) carrying the type would show up here and fail the subset check
    assert found, "nobody can garrison"
    assert found <= GARRISONERS, f"unexpected garrisoners: {sorted(found - GARRISONERS)}"
    assert {"e1", "e2"} <= found
    # nothing the generator covers (the buildings) may also carry the passenger type
    assert not any(n in found for n in re.findall(r"^([a-z0-9_]+):$", generate.DEFAULT_OUT.read_text(encoding="utf-8"), re.M))


def test_only_gi_and_conscript_carry_the_garrison_armament():
    carriers = {n for n, lines in actor_blocks() if any(re.match(r"\s*Armament@GARRISON:", l) for l in lines)}
    assert {"e1", "e2"} <= carriers <= GARRISONERS


def transports():
    """Actors that define a (non-garrison) Cargo, plus everything inheriting from one."""
    blocks = dict(actor_blocks())
    found = {n for n, lines in blocks.items()
             if any(re.match(r"\tCargo:", l) for l in lines) and not n.startswith("^")}
    changed = True
    while changed:
        changed = False
        for n, lines in blocks.items():
            parents = {l.split(":", 1)[1].strip() for l in lines if re.match(r"\tInherits(@\w+)?:", l)}
            if n not in found and not n.startswith("^") and parents & found:
                found.add(n)
                changed = True
    return sorted(found)


@pytest.mark.integration
def test_all_transports_accept_garrisoners():
    from ra2data.resolved import resolve_actor

    names = transports()
    assert len(names) >= 8, names
    for name in names:
        # pdplane (paradrop plane) has no Types at all: it is preloaded, never boarded by order
        if "Types" not in resolve_actor(name)["Cargo"]:
            assert name == "pdplane", f"{name} has a Cargo with no Types"
            continue
        types = {t.strip() for t in resolve_actor(name)["Cargo"]["Types"].split(",")}
        assert "Infantry" in types, name
        assert "Garrisoner" in types, f"{name} cannot carry GIs/Conscripts: {sorted(types)}"


@pytest.mark.integration
def test_gi_and_conscript_resolve_as_garrisoners():
    from ra2data.resolved import resolve_actor

    for name, weapon in (("e1", "AlliedOccupyW"), ("e2", "SovietOccupyW")):
        rules = resolve_actor(name)
        assert rules["Passenger"]["CargoType"] == "Garrisoner"
        assert "EntersGarrisons" in rules
        assert rules["Armament@GARRISON"]["Name"] == "garrison"
        assert rules["Armament@GARRISON"]["Weapon"] == weapon


# rules.ini Verses order -> the mod's armour type names
ARMOUR_ORDER = ["None", "Flak", "Plate", "Light", "Medium", "Heavy", "Wood", "Steel", "Concrete", "Drone", "Rocket"]


@pytest.mark.integration
@pytest.mark.parametrize("weapon", ["AlliedOccupyW", "SovietOccupyW"])
def test_occupy_weapons_match_ra2(ra2ini, weapon):
    from ra2data.resolved import resolve_weapon

    src = ra2ini[weapon]
    warhead = ra2ini[src["Warhead"]]
    rules = resolve_weapon(weapon)
    # timings 1:1 (the mod's existing convention): ROF -> ReloadDelay, Range in cells
    assert int(rules["ReloadDelay"]) == int(src["ROF"])
    assert cells(rules["Range"]) == float(src["Range"])
    dam = rules["Warhead@1Dam"]
    assert int(dam["Damage"]) == int(src["Damage"])
    verses = [int(v.strip().rstrip("%")) for v in warhead["Verses"].split(",")]
    assert len(verses) == len(ARMOUR_ORDER)
    assert {k: int(v) for k, v in dam["Versus"].items()} == dict(zip(ARMOUR_ORDER, verses))
    prone = int(warhead["ProneDamage"].split(";")[0].strip().rstrip("%"))
    assert f"Prone{prone}Percent" in dam["DamageTypes"]


@pytest.mark.integration
def test_garrison_buildings_autotarget_and_are_not_repairable():
    from ra2data.resolved import resolve_actor

    names = re.findall(r"^([a-z0-9_]+):$", generate.DEFAULT_OUT.read_text(encoding="utf-8"), re.M)
    assert len(names) == 120
    for name in names:
        rules = resolve_actor(name)
        assert rules["Cargo@GARRISON"]["EjectOnDeath"].lower() == "true", name
        assert rules["Cargo@GARRISON"]["LoadedCondition"] == "garrisoned", name
        assert rules["AutoTarget@GARRISON"]["RequiresCondition"] == "garrisoned", name
        assert not any(t.split("@")[0] == "AutoTarget" for t in rules if t != "AutoTarget@GARRISON"), name
        for trait in ("RepairableBuilding", "WithBuildingRepairDecoration"):
            assert not any(t.split("@")[0] == trait for t in rules), f"{name} has {trait}"


def test_cawsh12_has_no_leftover_turret():
    text = (REPO_ROOT / "mods/ra2/rules/civilian-structures.yaml").read_text(encoding="utf-8")
    block = text[text.index("\ncawsh12:"):text.index("\ncawsh12.rubble:")]
    assert "Turreted" not in block and "QuantizedFacings" not in block


def test_cawsh12_is_not_garrisonable(ra2ini):
    # base rules.ini: CanBeOccupied=no, so the hand-written garrison block must be gone
    assert ra2ini["CAWSH12"]["CanBeOccupied"] == "no"
    text = (REPO_ROOT / "mods/ra2/rules/civilian-structures.yaml").read_text(encoding="utf-8")
    block = text[text.index("\ncawsh12:"):text.index("\ncawsh12.rubble:")]
    assert "Cargo" not in block and "AttackGarrisoned" not in block
