"""Tests for the Exact global and special rules (fictional data unless marked integration)."""
from __future__ import annotations

import json

import pytest

from exact import conversions, generate, globals as exact_globals
from exact.model import load_actor
from exact.snapshot import SNAPSHOT_PATH, strip_neutral
from ra2data.resolved import REPO_ROOT, parse_miniyaml, resolve_actor, resolve_weapon

GENERAL = {
    "BuildSpeed": ".5", "VeteranRatio": "2.5", "VeteranCombat": "1.2", "VeteranArmor": "2.0",
    "VeteranROF": "0.5", "VeteranSpeed": "1.5", "RefundPercent": "40%", "RepairPercent": "10%",
    "RepairRate": ".02", "RepairStep": "6", "URepairRate": ".04", "IRepairRate": ".002", "IRepairStep": "9",
    "PurifierBonus": ".3", "AlliedSurvivorDivisor": "300", "SovietSurvivorDivisor": "100",
    "AlliedCrew": "TROOP", "SovietCrew": "RED", "Pilot": "TROOP", "CrewEscape": "35%",
    "AllyParaDropInf": "TROOP", "AllyParaDropNum": "3", "SovParaDropInf": "RED", "SovParaDropNum": "2",
    "AmerParaDropInf": "TROOP", "AmerParaDropNum": "4",
    "LightningStormDuration": "90", "LightningHitDelay": "7", "LightningScatterDelay": "3",
    "LightningDamage": "77", "LightningWarhead": "ZapWH",
}

RULES = {
    "General": GENERAL,
    "CombatDamage": {"IronCurtainDuration": "300"},
    "Blues": {"Suffix": "Allied"}, "Reds": {"Suffix": "Soviet"},
    "InfantryTypes": {"1": "TROOP", "2": "RED"},
    "VehicleTypes": {"1": "TANK", "2": "VAN", "3": "BOOM"},
    "AircraftTypes": {"1": "JET"},
    "BuildingTypes": {"1": "SPHERE", "2": "CURTAIN", "3": "PLANT", "4": "EYE", "5": "WELL", "6": "DOC",
        "7": "PORT", "8": "STORM", "9": "AIRDROP"},
    "TROOP": {"TechLevel": "1", "Owner": "Blues", "Cost": "100", "Strength": "100",
        "VeteranAbilities": "STRONGER,FIREPOWER,SIGHT", "EliteAbilities": "SELF_HEAL,ROF"},
    "RED": {"TechLevel": "1", "Owner": "Reds", "Cost": "100", "Strength": "100"},
    "TANK": {"TechLevel": "2", "Owner": "Reds", "Cost": "900", "Strength": "400",
        "VeteranAbilities": "FASTER", "EliteAbilities": "FASTER"},
    "VAN": {"TechLevel": "2", "Owner": "Reds", "Cost": "900", "Strength": "400", "Crewed": "yes"},
    "BOOM": {"TechLevel": "2", "Owner": "Reds", "Cost": "500", "Explodes": "yes", "DeathWeapon": "Kaboom"},
    "JET": {"TechLevel": "2", "Owner": "Blues", "Cost": "1200", "Crewed": "yes"},
    "SPHERE": {"TechLevel": "9", "Owner": "Blues", "Cost": "2500", "SuperWeapon": "ChronoSpecial"},
    "CURTAIN": {"TechLevel": "9", "Owner": "Reds", "Cost": "2500", "SuperWeapon": "CurtainSpecial"},
    "STORM": {"TechLevel": "9", "Owner": "Blues", "Cost": "5000", "SuperWeapon": "StormSpecial"},
    "AIRDROP": {"TechLevel": "3", "Owner": "Blues", "Cost": "1000", "SuperWeapon": "AmerSpecial"},
    "PLANT": {"TechLevel": "1", "Owner": "Reds", "Cost": "1000", "Crewed": "yes"},
    "EYE": {"TechLevel": "8", "Owner": "Blues", "Cost": "1500", "SpySat": "yes"},
    "DOC": {"TechLevel": "3", "Owner": "Blues", "Cost": "800", "UnitRepair": "yes"},
    "WELL": {"TechLevel": "-1", "Strength": "1000", "Sight": "5", "Armor": "steel", "ProduceCashDelay": "150",
        "ProduceCashAmount": "30", "ProduceCashStartup": "750"},
    "PORT": {"TechLevel": "-1", "Strength": "800", "SuperWeapon": "ParaSpecial"},
    "ChronoSpecial": {"Type": "ChronoSphere", "RechargeTime": "7"},
    "CurtainSpecial": {"Type": "IronCurtain", "RechargeTime": "5"},
    "StormSpecial": {"Type": "LightningStorm", "RechargeTime": "10"},
    "ParaSpecial": {"Type": "ParaDrop", "RechargeTime": "4"},
    "AmerSpecial": {"Type": "AmerParaDrop", "RechargeTime": "3"},
    "Kaboom": {"Damage": "300", "Warhead": "BoomWH"},
    "BoomWH": {"CellSpread": "1"},
    "ZapWH": {"CellSpread": "2", "PercentAtMax": ".5", "Verses": "100%,90%"},
}

RANKS = {
    "GainsExperience": {"Conditions": {"500": "rank-veteran", "1000": "rank-elite"}},
    **{f"{kind}@{rank}": {"Modifier": "1", "RequiresCondition": "x"}
        for kind in ("FirepowerMultiplier", "DamageMultiplier", "ReloadDelayMultiplier", "SpeedMultiplier",
            "InaccuracyMultiplier") for rank in ("VETERAN", "ELITE")},
    "ChangesHealth@ELITE": {"Step": "2", "RequiresCondition": "rank-veteran && !rank-elite"},
}

BASES = {
    "troop": {**RANKS, "Parachutable": {}},
    "red": {"Parachutable": {}},
    "tank": {**RANKS, "Mobile": {}, "FireWarheadsOnDeath": {"Weapon": "TankBoom"}},
    "van": {"Mobile": {}},
    "boom": {"Mobile": {}, "FireWarheadsOnDeath": {"Weapon": "kaboom", "EmptyWeapon": "kaboom"}},
    "jet": {"Aircraft": {}},
    "sphere": {"ChronoshiftPower@chronoshift": {"ChargeInterval": "1"}, "Sellable": {}},
    "curtain": {"GrantExternalConditionPower@IRONCURTAIN": {"ChargeInterval": "1", "Duration": "1"}},
    "storm": {"WeatherControlSupportPower@LightningStorm": {"ChargeInterval": "1", "Weapon": "LightningStorm"}},
    "airdrop": {"ParatroopersPower@paratroopers": {"ChargeInterval": "1", "DropItems": "troop"}},
    "plant": {"Sellable": {}, "RepairableBuilding": {"RepairingNotification": "Repairing"},
        "SpawnSurvivors": {"Actors": "red, red, engineer"}, "ResourcePurifier": {"Modifier": "1"}},
    "eye": {"RevealsMap": {"RequiresCondition": "!lowpower"}},
    "doc": {"RepairsUnits": {"Interval": "1"}},
}

NEUTRAL = {
    "well": {
        "Health": {"HP": "1000"},
        "Armor": {"Type": "Steel"},
        "RevealsShroud": {"Range": "4c0"},
        "CashTrickler": {"Interval": "100", "Amount": "20"},
        "GivesCashOnCapture": {"Amount": "1000"},
        "Tooltip": {"Name": "Fake Well"},
    },
    "port": {
        "ParatroopersPower@Allies": {"OrderName": "AlliedDrop", "ChargeInterval": "9000", "DropItems": "troop"},
        "ParatroopersPower@Soviets": {"OrderName": "SovietDrop", "ChargeInterval": "9000", "DropItems": "red"},
    },
}


def actors_of(rules=RULES, names=None):
    names = names or BASES
    return {n: load_actor(rules, n) for n in names}


@pytest.fixture
def g():
    twins = set(BASES)
    return exact_globals.build_globals(RULES, actors_of(), BASES, twins, NEUTRAL,
        {"lightningstorm": "LightningStorm", "kaboom": "Kaboom"})


@pytest.fixture
def tree(g):
    return parse_miniyaml(exact_globals.render_globals(g))


def test_veterancy_thresholds_from_ratio(tree):
    conditions = tree["troop.exact"]["GainsExperience"]["Conditions"]
    # VeteranRatio 2.5: rank-veteran at 250 %, rank-elite at 500 % of the value; 1000 is removed.
    assert conditions == {"-1000": {}, "250": "rank-veteran", "500": "rank-elite"}


def test_veterancy_multipliers_from_abilities(tree):
    troop = tree["troop.exact"]
    # Veteran: STRONGER, FIREPOWER. Elite adds ROF; abilities are flags and do not stack.
    assert troop["FirepowerMultiplier@VETERAN"] == {"Modifier": "120"}
    assert troop["FirepowerMultiplier@ELITE"] == {"Modifier": "120"}
    assert troop["DamageMultiplier@VETERAN"] == {"Modifier": "50"}
    assert troop["ReloadDelayMultiplier@VETERAN"] == {"Modifier": "100"}
    assert troop["ReloadDelayMultiplier@ELITE"] == {"Modifier": "50"}
    assert troop["SpeedMultiplier@ELITE"] == {"Modifier": "100"}
    assert troop["InaccuracyMultiplier@ELITE"] == {"Modifier": "100"}
    assert tree["tank.exact"]["SpeedMultiplier@VETERAN"] == {"Modifier": "150"}


def test_self_heal_only_with_ability(tree):
    assert tree["troop.exact"]["ChangesHealth@ELITE"] == {"RequiresCondition": "rank-elite"}
    assert "-ChangesHealth@ELITE" in tree["tank.exact"]


def test_superweapon_charge_converted(tree):
    assert tree["sphere.exact"]["ChronoshiftPower@chronoshift"]["ChargeInterval"] == str(conversions.minutes_to_ticks(7))
    curtain = tree["curtain.exact"]["GrantExternalConditionPower@IRONCURTAIN"]
    assert curtain == {"ChargeInterval": str(conversions.minutes_to_ticks(5)),
        "Duration": str(conversions.frames_to_ticks(300))}
    storm = tree["storm.exact"]["WeatherControlSupportPower@LightningStorm"]
    assert storm == {"ChargeInterval": str(conversions.minutes_to_ticks(10)), "Duration": "90", "HitDelay": "7",
        "ScatterDelay": "3", "Weapon": "LightningStorm.exact"}
    drop = tree["airdrop.exact"]["ParatroopersPower@paratroopers"]
    assert drop == {"ChargeInterval": str(conversions.minutes_to_ticks(3)),
        "DropItems": "troop.exact,troop.exact,troop.exact,troop.exact"}


def test_superweapon_follows_frame_rate(monkeypatch):
    monkeypatch.setattr(conversions, "RA2_FRAMES_PER_SECOND", 30.0)
    g = exact_globals.build_globals(RULES, actors_of(names=["curtain"]), BASES, {"curtain"})
    assert g.blocks["curtain.exact"]["GrantExternalConditionPower@IRONCURTAIN"]["Duration"] == 250


def test_economy_rules(tree):
    assert tree["sphere.exact"]["Sellable"] == {"RefundPercent": "40"}
    plant = tree["plant.exact"]
    assert plant["RepairableBuilding"] == {"RepairPercent": "10", "RepairInterval": "30", "RepairStep": "6"}
    assert plant["ResourcePurifier"] == {"Modifier": "30"}
    assert tree["doc.exact"]["RepairsUnits"] == {"ValuePercentage": "10", "Interval": "60", "HpPerStep": "6"}


def test_survivors_from_divisor(tree):
    # 1000 x 40 % / 100 (Soviet divisor) = 4 crew of the Soviet side; the engineer is kept.
    assert tree["plant.exact"]["SpawnSurvivors"] == {"Actors": "red.exact, red.exact, red.exact, red.exact, engineer"}


def test_survivors_removed_without_crew():
    rules = {**RULES, "PLANT": {**RULES["PLANT"], "Crewed": "no"}}
    bases = {"plant": {"SpawnSurvivors": {"Actors": "red, red"}}}
    g = exact_globals.build_globals(rules, actors_of(rules, ["plant"]), bases, {"plant", "red"})
    assert "-SpawnSurvivors" in g.blocks["plant.exact"]


def test_crew_escape(tree):
    assert tree["van.exact"]["EjectOnDeath"] == {"PilotActor": "red.exact", "SuccessRate": "35", "EjectOnGround": "true"}
    assert tree["jet.exact"]["EjectOnDeath"] == {"PilotActor": "troop.exact", "SuccessRate": "35",
        "EjectOnGround": "true", "EjectInAir": "true"}
    assert "EjectOnDeath" not in tree["tank.exact"]  # Crewed not set: RA2 default no


def test_iron_curtain_kills_infantry(tree):
    for name in ("troop.exact", "red.exact"):
        assert tree[name]["ExternalCondition@INVULNERABILITY"] == {"Condition": "invulnerability"}
        assert tree[name]["KillsSelf@IRONCURTAIN"] == {"RequiresCondition": "invulnerability"}
    assert "KillsSelf@IRONCURTAIN" not in tree["tank.exact"]


def test_death_weapon_twin(g, tree):
    assert tree["boom.exact"]["FireWarheadsOnDeath"] == {"Weapon": "Kaboom.exact", "EmptyWeapon": "Kaboom.exact"}
    assert "FireWarheadsOnDeath" not in tree["tank.exact"]  # RA2 TANK does not explode
    assert {"Kaboom", "LightningStorm"} <= g.weapons


def test_spy_satellite_keeps_base_reveal(g, tree):
    assert "eye.exact" not in tree or "RevealsMap" not in tree["eye.exact"]
    assert g.notes["spy-satellite"] == ["eye.exact: the base RevealsMap is kept (RequiresCondition !lowpower)"]


def test_death_weapon_messages():
    rules = {**RULES, "VehicleTypes": {"1": "IFV", "2": "DUD"},
        "IFV": {"TechLevel": "2", "Owner": "Reds", "Cost": "600", "DeathWeapon": "Kaboom"},
        "DUD": {"TechLevel": "2", "Owner": "Reds", "Cost": "600", "Explodes": "yes", "DeathWeapon": "Missing"}}
    bases = {"ifv": {"FireWarheadsOnDeath@demo": {"Weapon": "Kaboom", "RequiresCondition": "ifv-demo"}},
        "dud": {"FireWarheadsOnDeath": {"Weapon": "Missing"}}}
    g = exact_globals.build_globals(rules, actors_of(rules, ["ifv", "dud"]), bases, {"ifv", "dud"})
    assert "ifv.exact" not in g.blocks and "dud.exact" not in g.blocks
    assert g.notes["death-weapons"] == [
        "dud.exact: RA2 DeathWeapon Missing has no rules.ini section; mod death weapon kept",
        "ifv.exact: FireWarheadsOnDeath@demo Kaboom kept: RA2 sets DeathWeapon=Kaboom without Explodes=yes, "
        "and the mod fires it only under ifv-demo"]


def test_tech_buildings_get_condition_pairs(tree):
    well = tree["well"]
    assert well["GrantConditionOnPrerequisite@BALANCE"] == {"Condition": "balance-exact", "Prerequisites": "balance.exact"}
    assert well["CashTrickler@EXACT"] == {"Interval": "150", "Amount": "30", "RequiresCondition": "balance-exact"}
    assert well["CashTrickler@REIMAGINED"] == {"Interval": "100", "Amount": "20", "RequiresCondition": "!balance-exact"}
    assert well["GivesCashOnCapture@EXACT"]["Amount"] == "750"
    assert well["RevealsShroud@EXACT"]["Range"] == "5c0"
    assert "-CashTrickler" in well and "Health@EXACT" not in well
    # RA2 and the mod agree on the armour: no pair.
    assert "Armor@EXACT" not in well and "-Armor" not in well
    port = tree["port"]
    assert port["ParatroopersPower@Allies@EXACT"] == {"OrderName": "AlliedDropExact",
        "ChargeInterval": str(conversions.minutes_to_ticks(4)), "DropItems": "troop.exact,troop.exact,troop.exact",
        "RequiresCondition": "balance-exact"}
    assert port["ParatroopersPower@Soviets@EXACT"]["DropItems"] == "red.exact,red.exact"


def _apply(original: dict, overlay: dict) -> dict:
    """The original tree with the overlay's removals and additions (as the engine merges them)."""
    out = dict(original)
    for key, value in overlay.items():
        if key.startswith("-"):
            del out[key[1:]]
        else:
            out[key] = value
    return out


def test_tech_building_pairs_fold_back_to_original(tree):
    for name, original in NEUTRAL.items():
        assert strip_neutral(_apply(original, tree[name])) == original


def test_inheriting_tech_building_reuses_parent_pairs():
    rules = {**RULES, "BuildingTypes": {"1": "WELL", "2": "WELL2", "3": "WELL3"},
        "WELL2": dict(RULES["WELL"]), "WELL3": {**RULES["WELL"], "ProduceCashAmount": "45"}}
    neutral = {"well": NEUTRAL["well"], "well2": NEUTRAL["well"], "well3": NEUTRAL["well"]}
    g = exact_globals.build_globals(rules, {}, {}, set(), neutral,
        neutral_parents={"well2": "well", "well3": "well"})
    assert "well2" not in g.blocks
    assert g.blocks["well3"] == {"CashTrickler@EXACT": {"Interval": 150, "Amount": 45}}


def test_pairing_a_conditional_trait_fails():
    neutral = {"well": {"CashTrickler": {"Interval": "1", "Amount": "1", "RequiresCondition": "x"}}}
    with pytest.raises(ValueError, match="RequiresCondition"):
        exact_globals.build_globals(RULES, {}, {}, set(), neutral)


def test_weather_weapon_from_general():
    base = {"ReloadDelay": "0", "Warhead@2Dam": {"_value": "SpreadDamage", "Damage": "165", "Spread": "3c0"}}
    values = exact_globals.weather_weapon(RULES, base)
    assert values["Warhead@2Dam"]["Damage"] == 77
    assert values["Warhead@2Dam"]["Spread"] == "2c0" and values["Warhead@2Dam"]["Falloff"] == "100, 50"
    assert values["Warhead@2Dam"]["Versus"]["Flak"] == 90
    assert "ReloadDelay" not in values


def test_render_is_deterministic(g):
    assert exact_globals.render_globals(g) == exact_globals.render_globals(
        exact_globals.build_globals(RULES, actors_of(), dict(reversed(list(BASES.items()))), set(BASES),
            dict(reversed(list(NEUTRAL.items()))), {"lightningstorm": "LightningStorm", "kaboom": "Kaboom"}))


def test_generator_writes_globals():
    rules = {**RULES, "InfantryTypes": {"1": "TROOP"}, "VehicleTypes": {}, "AircraftTypes": {}, "BuildingTypes": {"1": "WELL"}}
    bases = {"troop": {"Buildable": {"Prerequisites": "~x"}, **RANKS}}
    out = generate.build(rules, bases, {}, neutral_actors={"well": NEUTRAL["well"]})
    tree = parse_miniyaml(out[generate.GLOBALS_FILE])
    assert out[generate.GLOBALS_FILE].startswith(generate.HEADER)
    assert tree["troop.exact"]["GainsExperience"]["Conditions"]["250"] == "rank-veteran"
    assert "CashTrickler@EXACT" in tree["well"]


# --- integration tests against the built mod ---


@pytest.mark.integration
def test_resolved_twin_veterancy_and_superweapons(ra2ini):
    ratio = float(ra2ini["General"]["VeteranRatio"])
    e1 = resolve_actor("e1.exact")
    assert e1["GainsExperience"]["Conditions"] == {str(round(ratio * 100)): "rank-veteran",
        str(round(ratio * 200)): "rank-elite"}
    assert e1["KillsSelf@IRONCURTAIN"] == {"RequiresCondition": "invulnerability"}
    minutes = float(ra2ini["ChronoSphereSpecial"]["RechargeTime"])
    assert resolve_actor("gacsph.exact")["ChronoshiftPower@chronoshift"]["ChargeInterval"] == str(
        conversions.minutes_to_ticks(minutes))
    assert resolve_actor("terror.exact")["FireWarheadsOnDeath"]["Weapon"] == "TerrorBomb.exact"
    assert resolve_weapon("TerrorBomb.exact")["Warhead@1Dam"]["Damage"] == ra2ini["TerrorBomb"]["Damage"]


@pytest.mark.integration
def test_resolved_tech_buildings_fold_back_to_snapshot():
    snapshot = json.loads(SNAPSHOT_PATH.read_text(encoding="utf-8"))
    for name, original in snapshot["neutral"].items():
        current = resolve_actor(name)
        assert strip_neutral(current) == strip_neutral(original), name
    assert "ParatroopersPower@Allies@EXACT" in resolve_actor("caairp")


@pytest.mark.integration
def test_reimagined_pairs_match_source_files():
    """Staleness guard: each committed X@REIMAGINED equals X as the mod's rule files define it
    (resolved without globals.yaml), so an edit of a tech building needs a regeneration."""
    committed = parse_miniyaml((REPO_ROOT / generate.GLOBALS_FILE).read_text(encoding="utf-8"))
    source = generate.resolve_neutral_actors()
    checked = 0
    for name, tree in source.items():
        assert not any(k.endswith(("@EXACT", "@REIMAGINED")) for k in tree), name
        for key, trait in committed.get(name, {}).items():
            if key.endswith("@REIMAGINED"):
                original = tree[key[: -len("@REIMAGINED")]]
                assert trait == {**original, "RequiresCondition": "!balance-exact"}, (name, key)
                checked += 1
    assert checked


# The BalanceProductionQueue fields in player.yaml are RA2's [General] production rules.
QUEUE_GENERAL_KEYS = ("MultipleFactory", "MinLowPowerProductionSpeed", "MaxLowPowerProductionSpeed",
    "LowPowerPenaltyModifier")


def test_player_queues_match_rules_ini(ra2ini):
    player = parse_miniyaml((REPO_ROOT / "mods/ra2/rules/player.yaml").read_text(encoding="utf-8"))["Player"]
    queues = {k: v for k, v in player.items() if k.split("@")[0] == "BalanceProductionQueue"}
    assert len(queues) == 6
    expected = {key: str(conversions.percent(ra2ini["General"][key])) for key in QUEUE_GENERAL_KEYS}
    assert expected == {"MultipleFactory": "80", "MinLowPowerProductionSpeed": "50",
        "MaxLowPowerProductionSpeed": "80", "LowPowerPenaltyModifier": "100"}
    for name, queue in queues.items():
        assert {key: queue.get(key) for key in QUEUE_GENERAL_KEYS} == expected, name
