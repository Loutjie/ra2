"""exact.ai: the Exact twins beside every actor name of the AI bot modules (fictional data)."""
from __future__ import annotations

from exact import ai, generate
from ra2data.resolved import parse_miniyaml

TWINS = ["mcv", "yard", "plant", "tank", "boat"]

SOURCE = {
    "Player": {
        "ModularBot@fake": {"Name": "bot-name", "Type": "fake"},
        "GrantConditionOnBotOwner@fake": {"Condition": "enable-fake", "Bots": "fake"},
        "BaseBuilderBotModule@fake": {
            "RequiresCondition": "enable-fake",
            "DefenseQueues": "Support",
            "ConstructionYardTypes": "yard",
            "PowerTypes": "plant, oldplant",
            "BuildingLimits": {"plant": "8", "oldplant": "2"},
            "BuildingFractions": {"yard": "1", "plant": "35"},
        },
        "UnitBuilderBotModule@fake": {"UnitsToBuild": {"tank": "50", "ghost": "5"}, "UnitLimits": {"ghost": "1"}},
        "SquadManagerBotModule@fake": {"NavalUnitsTypes": "boat", "AirUnitsTypes": "ghost"},
        "McvManagerBotModule@fake": {"McvTypes": "mcv", "ConstructionYardTypes": "yard"},
        "HarvesterBotModule": {"RequiresCondition": "enable-fake"},
        "SupportPowerBotModule@fake": {"Decisions": {
            "drop": {"OrderName": "Drop", "MinimumAttractiveness": "5",
                "Consideration@1": {"Against": "Enemy", "Types": "Structure"}},
            "nuke": {"OrderName": "Nuke"},
        }},
    },
}


def overrides(renamed=None):
    return parse_miniyaml(ai.render_ai(SOURCE, TWINS, renamed or {}))


def test_lists_get_twins_beside_each_name():
    player = overrides()["Player"]
    assert player["BaseBuilderBotModule@fake"]["ConstructionYardTypes"] == "yard, yard.exact"
    assert player["BaseBuilderBotModule@fake"]["PowerTypes"] == "plant, plant.exact, oldplant"
    assert player["SquadManagerBotModule@fake"] == {"NavalUnitsTypes": "boat, boat.exact"}
    # The AI deploys its first MCV in place only if McvTypes names it.
    assert player["McvManagerBotModule@fake"] == {"McvTypes": "mcv, mcv.exact",
        "ConstructionYardTypes": "yard, yard.exact"}


def test_dictionaries_get_twin_keys_with_the_base_value():
    player = overrides()["Player"]
    assert player["BaseBuilderBotModule@fake"]["BuildingLimits"] == {"plant.exact": "8"}
    assert player["BaseBuilderBotModule@fake"]["BuildingFractions"] == {"yard.exact": "1", "plant.exact": "35"}
    assert player["UnitBuilderBotModule@fake"] == {"UnitsToBuild": {"tank.exact": "50"}}


def test_untouched_traits_and_fields_are_not_written():
    player = overrides()["Player"]
    assert "ModularBot@fake" not in player and "GrantConditionOnBotOwner@fake" not in player
    assert "HarvesterBotModule" not in player and "SupportPowerBotModule@fake" not in player
    assert "RequiresCondition" not in player["BaseBuilderBotModule@fake"]
    assert "DefenseQueues" not in player["BaseBuilderBotModule@fake"]


def test_decision_copied_for_a_renamed_exact_power():
    decisions = overrides({"Drop": "DropExact"})["Player"]["SupportPowerBotModule@fake"]["Decisions"]
    assert decisions == {"drop.exact": {"OrderName": "DropExact", "MinimumAttractiveness": "5",
        "Consideration@1": {"Against": "Enemy", "Types": "Structure"}}}


def test_nothing_to_add_gives_header_only():
    assert ai.render_ai(SOURCE, ["other"], {}) == ""


def test_renamed_orders_from_twins_and_neutral_pairs():
    generated = {
        "tower.exact": {"NukePower": {"OrderName": "NukeExact"}, "Health": {"HP": "1"}},
        "radar.exact": {"ParatroopersPower@p": {"DropItems": "a.exact"}},
        "airport": {
            "ParatroopersPower@a@EXACT": {"OrderName": "AlliedExact", "RequiresCondition": "balance-exact"},
            "ParatroopersPower@a@REIMAGINED": {"OrderName": "Allied", "RequiresCondition": "!balance-exact"},
            "ParatroopersPower@b@EXACT": {"OrderName": "Same"},
            "ParatroopersPower@b@REIMAGINED": {"OrderName": "Same"},
        },
    }
    bases = {"tower": {"NukePower": {"OrderName": "Nuke"}}, "radar": {"ParatroopersPower@p": {"OrderName": "Drop"}}}
    assert ai.renamed_orders(generated, bases) == {"Nuke": "NukeExact", "Allied": "AlliedExact"}


def test_generator_writes_ai_file():
    outputs = generate.build({}, {}, {})
    assert outputs[generate.AI_FILE].startswith(generate.HEADER)

