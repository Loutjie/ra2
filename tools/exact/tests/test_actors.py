"""Tests for the Exact twin actors and the Reimagined overlay (fictional data)."""
from __future__ import annotations

import pytest

from exact import actors, conversions, generate
from exact.model import ExactActor
from exact.snapshot import HOST_FIELDS, is_buildable_trait, strip_ignored, weapon_definitions
from ra2data.resolved import REPO_ROOT, parse_miniyaml, resolve_actor


def make_actor(actor_id, kind="vehicle", **overrides):
    values = dict(id=actor_id, kind=kind, cost=900, strength=400, armor="heavy", speed=6, rot=5,
        sight=8, power=0, build_minutes=0.63, prerequisites=[], primary=None, secondary=None,
        elite_primary=None, elite_secondary=None, veteran_abilities=[], elite_abilities=[],
        death_weapon=None, superweapon=None)
    values.update(overrides)
    raw = {"Speed": str(values["speed"]), "ROT": str(values["rot"])}
    values.setdefault("raw", raw)
    return ExactActor(**values)


HTNK_BASE = {
    "Armament@primary": {"Weapon": "120mm", "LocalOffset": "768,0,512"},
    "Armament@elite": {"Weapon": "120mmE", "RequiresCondition": "rank-elite"},
    "Armor": {"Type": "Heavy"},
    "Buildable": {"Prerequisites": "~naweap", "Queue": "Vehicle"},
    "Health": {"HP": "300"},
    "MapEditorData": {"Categories": "Vehicle"},
    "Mobile": {"Speed": "75", "TurnSpeed": "16", "Locomotor": "heavytracked"},
    "RenderSprites": {},
    "RenderVoxels": {"Scale": "11.7"},
    "Repairable": {"RepairActors": "gadept, nadept, caoutp"},
    "RevealsShroud": {"Range": "6c0"},
    "Valued": {"Cost": "1000"},
}


def twin_of(actor, base, **kwargs):
    tree = parse_miniyaml(actors.render_twin(actor, base, **kwargs))
    assert list(tree) == [actor.id.lower() + ".exact"]
    return tree[actor.id.lower() + ".exact"]


def test_twin_inherits_and_overrides():
    twin = twin_of(make_actor("HTNK"), HTNK_BASE)
    assert twin["Inherits"] == "htnk"
    assert twin["Valued"] == {"Cost": "900"}
    assert twin["Health"] == {"HP": "400"}
    assert twin["Armor"] == {"Type": "Heavy"}
    assert twin["Mobile"] == {"Speed": "90", "TurnSpeed": "20"}
    assert twin["RevealsShroud"] == {"Range": "8c0"}
    assert twin["Buildable"] == {
        "Prerequisites": "~naweap, ~balance.exact",
        "BuildDuration": str(conversions.minutes_to_ticks(0.63)),
        "BuildDurationModifier": "100",
    }
    assert twin["-ReplaceOnPrerequisite@BALANCE"] == {}
    assert twin["-MapEditorData"] == {}
    assert twin["Armament@primary"] == {"Weapon": "120mm.exact"}
    assert twin["Armament@elite"] == {"Weapon": "120mmE.exact"}
    assert "Power" not in twin and "ProvidesPrerequisite@BASE" not in twin


def test_twin_prerequisites_drop_reimagined_token():
    base = {**HTNK_BASE, "Buildable": {"Prerequisites": "~naweap, ~balance.reimagined"}}
    assert twin_of(make_actor("HTNK"), base)["Buildable"]["Prerequisites"] == "~naweap, ~balance.exact"


def test_twin_weapon_names_use_defined_case():
    twin = twin_of(make_actor("HTNK"), HTNK_BASE, weapons={"120mme": "120mme", "120mm": "120mm"})
    assert twin["Armament@elite"] == {"Weapon": "120mme.exact"}


def test_twins_keep_base_image():
    twin = twin_of(make_actor("HTNK"), HTNK_BASE)
    assert twin["RenderSprites"] == {"Image": "htnk"}
    assert twin["RenderVoxels"] == {"Image": "htnk"}

    mcv = {**HTNK_BASE, "RenderSprites": {"Image": "mcv"}, "RenderVoxels": {"Image": "mcvvxl"}}
    twin = twin_of(make_actor("AMCV"), mcv)
    assert twin["RenderSprites"] == {"Image": "mcv"}
    assert twin["RenderVoxels"] == {"Image": "mcvvxl"}

    # Traits whose image defaults to the actor's own name, not the RenderSprites image.
    infantry = {"RenderSprites": {"Image": "gi"}, "ChronoshiftableWithSpriteEffect": {},
        "WithDecoration@x": {"Image": "pips"}, "Valued": {"Cost": "1"}}
    twin = twin_of(make_actor("E1", kind="infantry"), infantry)
    assert twin["RenderSprites"] == {"Image": "gi"}
    assert twin["ChronoshiftableWithSpriteEffect"] == {"Image": "e1"}
    assert "WithDecoration@x" not in twin


def test_twin_without_editor_data_has_no_bogus_removal():
    base = {k: v for k, v in HTNK_BASE.items() if k != "MapEditorData"}
    assert "-MapEditorData" not in twin_of(make_actor("HTNK"), base)


def test_twin_without_replace_trait_has_no_removal():
    assert "-ReplaceOnPrerequisite@BALANCE" not in twin_of(make_actor("HTNK"), HTNK_BASE, replaced=False)


def test_infantry_without_rot_keeps_turnspeed():
    base = {"Mobile": {"Speed": "56", "TurnSpeed": "1023"}, "Valued": {"Cost": "1"}}
    actor = make_actor("E1", kind="infantry", speed=4, rot=0, raw={"Speed": "4"})
    assert twin_of(actor, base)["Mobile"] == {"Speed": "60"}


def test_aircraft_speed_and_turn():
    base = {"Aircraft": {"Speed": "200", "TurnSpeed": "8"}}
    actor = make_actor("ORCA", kind="aircraft", speed=14, rot=3)
    assert twin_of(actor, base)["Aircraft"] == {"Speed": "210", "TurnSpeed": "12"}


def test_armor_special_types():
    assert twin_of(make_actor("DRON", armor="special_1"), {"Armor": {"Type": "x"}})["Armor"] == {"Type": "Drone"}
    assert twin_of(make_actor("X", armor="special_2"), {"Armor": {"Type": "x"}})["Armor"] == {"Type": "Rocket"}


def test_buildings_provide_base_prerequisite():
    base = {"Buildable": {"Prerequisites": "garefn"}, "Power": {"Amount": "-20", "RequiresCondition": "x"},
        "ProvidesPrerequisite@default": {}}
    twin = twin_of(make_actor("GAWEAP", kind="building", power=-25, raw={}), base)
    assert twin["ProvidesPrerequisite@BASE"] == {"Prerequisite": "gaweap"}
    assert twin["Power"] == {"Amount": "-25"}
    assert "Mobile" not in twin
    assert "ProvidesPrerequisite@BASE" not in twin_of(make_actor("HTNK"), HTNK_BASE)


def test_transform_targets_rewritten():
    amcv = {"Transforms": {"IntoActor": "gacnst", "Facing": "384"}}
    twin = twin_of(make_actor("AMCV"), amcv, twins={"amcv", "gacnst"})
    assert twin["Transforms"] == {"IntoActor": "gacnst.exact"}
    assert "Transforms" not in twin_of(make_actor("AMCV"), amcv, twins={"amcv"})


def test_actor_lists_rewritten():
    base = {
        "FreeActor": {"Actor": "cmin", "Facing": "640"},
        "ParatroopersPower@paratroopers": {"UnitType": "pdplane", "DropItems": "e1, e1, e1, e2"},
        "CarrierParent": {"Actors": "hornet, hornet"},
        "SpawnActorsOnSell": {"ActorTypes": "e1, engineer"},
        "Rearmable": {"RearmActors": "gaairc, amradr"},
        "GrantConditionOnProduction@x": {"Actors": "shad", "Condition": "c"},
        "Repairable": {"RepairActors": "gadept, caoutp"},
    }
    twins = {"cmin", "e1", "gaairc", "shad", "gadept", "amradr"}
    twin = twin_of(make_actor("GAREFN", kind="building", raw={}), base, twins=twins)
    # Lists of actors to create: the twin replaces the base actor.
    assert twin["FreeActor"] == {"Actor": "cmin.exact"}
    assert twin["ParatroopersPower@paratroopers"] == {"DropItems": "e1.exact, e1.exact, e1.exact, e2"}
    assert twin["SpawnActorsOnSell"] == {"ActorTypes": "e1.exact, engineer"}
    assert "CarrierParent" not in twin
    # Lists of actors to match: the twin is added beside the base actor.
    assert twin["Rearmable"] == {"RearmActors": "gaairc, gaairc.exact, amradr, amradr.exact"}
    assert twin["GrantConditionOnProduction@x"] == {"Actors": "shad, shad.exact"}
    assert twin["Repairable"] == {"RepairActors": "gadept, gadept.exact, caoutp"}


def test_twin_statistics_count_as_base():
    base = {"UpdatesPlayerStatistics": {"AddToArmyValue": "true"}}
    assert twin_of(make_actor("HTNK"), base)["UpdatesPlayerStatistics"] == {"OverrideActor": "htnk"}


def test_overlay_appends_hidden_prerequisite():
    bases = {
        "htnk": {"Buildable": {"Prerequisites": "~naweap", "Queue": "Vehicle"}},
        # Already overlaid (a second generator run reads the resolved overlay back).
        "gapowr": {"Buildable": {"Prerequisites": "~structures.allies, ~balance.reimagined"},
            "ReplaceOnPrerequisite@BALANCE": {"Prerequisite": "balance.exact", "IntoActor": "gapowr.exact"}},
        "e1": {"Buildable": {"Queue": "Infantry"}},
    }
    tree = parse_miniyaml(actors.render_reimagined_overlay(bases, no_replace={"e1"}))
    assert list(tree) == ["e1", "gapowr", "htnk"]
    assert tree["htnk"] == {
        "Buildable": {"Prerequisites": "~naweap, ~balance.reimagined"},
        "ReplaceOnPrerequisite@BALANCE": {"Prerequisite": "balance.exact", "IntoActor": "htnk.exact"},
    }
    assert tree["gapowr"]["Buildable"] == {"Prerequisites": "~structures.allies, ~balance.reimagined"}
    assert tree["e1"] == {"Buildable": {"Prerequisites": "~balance.reimagined"}}


def test_spawned_children_found():
    bases = {
        "carrier": {"CarrierParent": {"Actors": "hornet, hornet"}},
        "amradr": {"ParatroopersPower@p": {"UnitType": "pdplane", "DropItems": "e1"}},
        "x": {"AirstrikePower": {"UnitType": "plane2"}, "BaseSpawnerParent": {"Actors": "drone"}},
    }
    assert actors.spawned_children(bases) == {"hornet", "pdplane", "plane2", "drone"}


# --- generator (fictional rules and resolved trees) ---

RULES = {
    "General": {"BuildSpeed": ".5"},
    "InfantryTypes": {"1": "TROOP", "2": "SEAL"},
    "VehicleTypes": {"1": "MCV", "2": "LOST"},
    "BuildingTypes": {"1": "YARD", "2": "PLANT"},
    "TROOP": {"TechLevel": "1", "Owner": "Reds", "Cost": "200", "Strength": "100", "Speed": "4",
        "Sight": "5", "Primary": "Rifle"},
    "SEAL": {"TechLevel": "-1", "Owner": "Reds", "Cost": "1000", "Strength": "100"},
    "MCV": {"TechLevel": "10", "Owner": "Reds", "Cost": "3000", "Strength": "1000", "Speed": "4", "ROT": "5",
        "DeploysInto": "YARD"},
    "LOST": {"TechLevel": "2", "Owner": "Reds", "Cost": "500"},
    "YARD": {"TechLevel": "-1", "Owner": "Reds", "Cost": "3000", "Strength": "1000", "UndeploysInto": "MCV"},
    "PLANT": {"TechLevel": "1", "Owner": "Reds", "Cost": "800", "Strength": "750", "Power": "200"},
}

BASES = {
    "troop": {"Buildable": {"Prerequisites": "~barracks"}, "Armament": {"Weapon": "Rifle"},
        "Health": {"HP": "1"}, "Valued": {"Cost": "1"}, "RenderSprites": {}, "MapEditorData": {}},
    "seal": {"Buildable": {"Prerequisites": "~barracks"}, "Valued": {"Cost": "1"}},
    "mcv": {"Buildable": {"Prerequisites": "~factory"}, "Transforms": {"IntoActor": "yard"},
        "Mobile": {"Speed": "1"}, "Valued": {"Cost": "1"}},
    "yard": {"Buildable": {"Prerequisites": "~disabled"}, "Transforms": {"IntoActor": "mcv"},
        "Valued": {"Cost": "1"}},
    "plant": {"Buildable": {"Prerequisites": "~structures, ~balance.reimagined"}, "Power": {"Amount": "100"},
        "ReplaceOnPrerequisite@BALANCE": {"Prerequisite": "balance.exact", "IntoActor": "plant.exact"}},
    "pdplane": {"Buildable": {}},
    "drop": {"Buildable": {"Prerequisites": "~x"}, "ParatroopersPower": {"UnitType": "pdplane",
        "DropItems": "troop"}},
}


@pytest.fixture
def outputs():
    return generate.build(RULES, BASES, {"rifle": "Rifle"})


def test_generator_selects_twins(outputs):
    twins = parse_miniyaml(outputs[generate.ACTORS_FILE])
    assert sorted(twins) == ["mcv.exact", "plant.exact", "troop.exact", "yard.exact"]
    assert twins["mcv.exact"]["Transforms"] == {"IntoActor": "yard.exact"}
    assert twins["yard.exact"]["Transforms"] == {"IntoActor": "mcv.exact"}
    overlay = parse_miniyaml(outputs[generate.OVERLAY_FILE])
    # seal: RA2 cannot build it, so Reimagined only. drop and pdplane (no RA2 section): both modes.
    assert sorted(overlay) == ["mcv", "plant", "seal", "troop", "yard"]
    assert overlay["seal"] == {"Buildable": {"Prerequisites": "~barracks, ~balance.reimagined"}}
    text = outputs[generate.ACTORS_FILE]
    assert "# Mod buildables without a twin, Reimagined only (RA2 cannot build them): seal" in text
    assert "# Mod buildables without an RA2 counterpart (available in both modes): drop, pdplane" in text
    assert "# RA2 buildables without a mod actor (not generated): LOST" in text


def test_generator_appends_exact_hosts_for_untwinned_actors():
    hosts = {
        "hornet": {"Rearmable": {"RearmActors": "mcv"}},
        "civcar": {"Repairable": {"RepairActors": "plant, caoutp", "Voice": "Move"},
            "RepairableNear": {"RepairActors": "plant, plant.exact"}},
        "boat": {"Repairable": {"RepairActors": "caoutp"}},
    }
    bases = {**BASES, "drop": {**BASES["drop"], "Repairable": {"RepairActors": "plant"}},
        "mcv": {**BASES["mcv"], "Repairable": {"RepairActors": "plant"}}}
    overlay = parse_miniyaml(generate.build(RULES, bases, {}, hosts)[generate.OVERLAY_FILE])
    assert overlay["hornet"] == {"Rearmable": {"RearmActors": "mcv, mcv.exact"}}
    # RepairableNear already carried the twin (read back from a previous run): stripped and re-added once.
    assert overlay["civcar"] == {"Repairable": {"RepairActors": "plant, caoutp, plant.exact"},
        "RepairableNear": {"RepairActors": "plant, plant.exact"}}
    assert overlay["drop"] == {"Repairable": {"RepairActors": "plant, plant.exact"}}
    assert "boat" not in overlay
    # Twinned actors are not extended (their twins list both hosts).
    assert "Repairable" not in overlay["mcv"]


def test_generator_notes_dropped_values():
    rules = {**RULES, "PLANT": {**RULES["PLANT"], "ROT": "10", "Sight": "6"}}
    text = generate.build(rules, BASES, {})[generate.ACTORS_FILE]
    assert "# Not applied: plant: ROT=10 has no Mobile, Aircraft or Turreted trait to go to" in text
    assert "# Not applied: plant: Sight=6 has no RevealsShroud trait to go to" in text


def test_building_rot_turns_turret():
    base = {"Turreted": {"TurnSpeed": "1023", "Offset": "1,0,0"}, "RevealsShroud": {"Range": "1c0"}}
    actor = make_actor("GAPILL", kind="building", rot=10, raw={"ROT": "10"})
    traits, notes = actors.twin_traits(actor, base)
    assert traits["Turreted"] == {"TurnSpeed": "40"}
    assert not [n for n in notes if "ROT" in n]


def test_generator_weapon_stubs(outputs):
    stubs = parse_miniyaml(outputs[generate.WEAPONS_FILE])
    assert stubs == {"Rifle.exact": {"Inherits": "Rifle"}}


def test_generator_is_deterministic(outputs):
    again = generate.build(RULES, dict(reversed(list(BASES.items()))), {"rifle": "Rifle"})
    assert again == outputs
    for rel, text in outputs.items():
        if rel.endswith(".yaml"):
            assert text.startswith(generate.HEADER)


# --- integration tests against the built mod and the real rules.ini ---


@pytest.mark.integration
def test_generated_files_up_to_date(ra2ini):
    outputs = generate.build(ra2ini, generate.resolve_base_actors(), weapon_definitions(),
        generate.resolve_host_actors())
    stale = [rel for rel, text in outputs.items()
        if not (REPO_ROOT / rel).is_file() or (REPO_ROOT / rel).read_text(encoding="utf-8") != text]
    assert not stale, f"regenerate with `python3 -m exact.generate`: {stale}"


@pytest.mark.integration
def test_resolved_twin_and_base():
    twin = resolve_actor("htnk.exact")
    assert twin["RenderSprites"]["Image"] == "htnk" and twin["RenderVoxels"]["Image"] == "htnk"
    assert "MapEditorData" not in twin and "ReplaceOnPrerequisite@BALANCE" not in twin
    assert twin["Armament@primary"]["Weapon"] == "120mm.exact"
    base = resolve_actor("htnk")
    assert base["ReplaceOnPrerequisite@BALANCE"] == {"Prerequisite": "balance.exact", "IntoActor": "htnk.exact"}
    assert base["Buildable"]["Prerequisites"].endswith("~balance.reimagined")
    assert resolve_actor("amcv.exact")["Transforms"]["IntoActor"] == "gacnst.exact"


@pytest.mark.integration
def test_generated_lists_match_resolved_mod():
    """Staleness guard that needs the built mod but not the game's rules.ini: editing a base
    actor's prerequisites or repair/rearm hosts without regenerating fails here."""
    twins = parse_miniyaml((REPO_ROOT / generate.ACTORS_FILE).read_text(encoding="utf-8"))
    overlay = parse_miniyaml((REPO_ROOT / generate.OVERLAY_FILE).read_text(encoding="utf-8"))
    twinned = {name[: -len(actors.SUFFIX)] for name in twins}
    bases = {n: strip_ignored(t) for n, t in generate.resolve_all(sorted(twinned)).items()}
    for name, base in bases.items():
        for key, trait in base.items():
            if is_buildable_trait(key):
                kept = actors.split_list(trait.get("Prerequisites", ""))
                assert twins[name + actors.SUFFIX][key]["Prerequisites"] == ", ".join([*kept, "~balance.exact"]), name
                assert overlay[name][key]["Prerequisites"] == ", ".join([*kept, "~balance.reimagined"]), name
    untwinned = {n: strip_ignored(t) for n, t in {**generate.resolve_host_actors(),
        **generate.resolve_base_actors()}.items() if n not in twinned}
    for name, tree in untwinned.items():
        for key, trait in tree.items():
            field = HOST_FIELDS.get(actors.trait_type(key))
            if field and isinstance(trait, dict) and isinstance(trait.get(field), str):
                expected = actors.appended_twins(trait[field], twinned)
                if expected != ", ".join(actors.split_list(trait[field])):
                    assert overlay[name][key][field] == expected, name
    # No stray overlay blocks: every untwinned entry is a resolved actor and holds only
    # Reimagined-only prerequisites or the host lists checked above.
    for name in set(overlay) - twinned:
        assert name in untwinned, f"overlay block for unknown or no longer listed actor {name}"
        for key, value in overlay[name].items():
            if is_buildable_trait(key):
                kept = actors.split_list(untwinned[name][key].get("Prerequisites", ""))
                assert value == {"Prerequisites": ", ".join([*kept, "~balance.reimagined"])}, name
            else:
                field = HOST_FIELDS.get(actors.trait_type(key))
                assert field and value == {field: actors.appended_twins(untwinned[name][key][field], twinned)}, \
                    f"stray overlay entry {name}.{key}"
