"""Checks over the skirmish AI configuration on the resolved Player actor."""
import pytest

from ra2data.resolved import resolve_actor

pytestmark = pytest.mark.integration

SUPERWEAPONS = ("gaweat", "nairon", "namisl")

# Powers the AI deliberately has no decision for, with the reason.
NO_AI_DECISION = {
    "Chronoshift": "the engine SupportPowerBotModule orders a power at one cell with no source "
    "location (ExtraLocation), so the AI cannot pick units to chronoshift",
}

# Actor that owns each AI-relevant support power, by the power it grants.
POWER_OWNERS = {
    "Weather Control": ("gaweat", "WeatherControlSupportPower"),
    "Chronoshift": ("gacsph", "ChronoshiftPower"),
    "Iron Curtain": ("nairon", "GrantExternalConditionPower"),
    "Nuke": ("namisl", "NukePower"),
    "American paradrop": ("amradr", "ParatroopersPower"),
    "Allied paradrop": ("caairp", "ParatroopersPower@Allies"),
    "Soviet paradrop": ("caairp", "ParatroopersPower@Soviets"),
}


def _modules(prefix):
    """All Player traits named `prefix` or `prefix@<suffix>`."""
    player = resolve_actor("Player")
    return [v for k, v in player.items() if k.split("@")[0] == prefix and isinstance(v, dict)]


def _module(prefix):
    found = _modules(prefix)
    assert len(found) == 1, f"expected exactly one {prefix}, found {len(found)}"
    return found[0]


def _order_name(actor, trait):
    """The power's OrderName; the engine defaults it to the info class name + 'Order'."""
    traits = resolve_actor(actor)
    key = trait if "@" in trait else next((k for k in traits if k.split("@")[0] == trait), None)
    assert key in traits, f"{actor} has no {trait} trait"
    info = traits[key]
    return info.get("OrderName") or key.split("@")[0] + "InfoOrder"


def test_ai_builds_all_superweapons():
    fractions = _module("BaseBuilderBotModule")["BuildingFractions"]
    missing = [a for a in SUPERWEAPONS if int(fractions.get(a, "0")) <= 0]
    assert not missing, f"BuildingFractions has no entry for {missing}"


@pytest.mark.parametrize("power", [p for p in POWER_OWNERS if p not in NO_AI_DECISION])
def test_ai_has_decisions_for_all_powers(power):
    actor, trait = POWER_OWNERS[power]
    order = _order_name(actor, trait)
    decisions = _module("SupportPowerBotModule")["Decisions"]
    orders = {d["OrderName"] for d in decisions.values() if isinstance(d, dict)}
    assert order in orders, f"no SupportPowerBotModule decision for {power} ({order})"


def test_ai_does_not_build_chronosphere():
    # The AI cannot use the Chronosphere (see NO_AI_DECISION), so it must not build one.
    # Re-add gacsph once the engine bot can chronoshift with a source location.
    fractions = _module("BaseBuilderBotModule")["BuildingFractions"]
    assert "gacsph" not in fractions


@pytest.mark.parametrize("power", sorted(NO_AI_DECISION))
def test_ai_has_no_decision_for_unusable_powers(power):
    actor, trait = POWER_OWNERS[power]
    order = _order_name(actor, trait)
    decisions = _module("SupportPowerBotModule")["Decisions"]
    orders = {d["OrderName"] for d in decisions.values() if isinstance(d, dict)}
    assert order not in orders, f"{power}: {NO_AI_DECISION[power]}"


def test_ai_decision_scan_radii_are_positive():
    # A zero CoarseScanRadius/FineScanRadius makes the bot's map scan loop forever.
    for name, d in _module("SupportPowerBotModule")["Decisions"].items():
        for key in ("CoarseScanRadius", "FineScanRadius"):
            if key in d:
                assert int(d[key]) > 0, f"{name}.{key} must be positive"


def test_ai_replaces_harvesters():
    module = _module("HarvesterBotModule")
    types = {t.strip() for t in module.get("HarvesterTypes", "").split(",") if t.strip()}
    refineries = {t.strip() for t in module.get("RefineryTypes", "").split(",") if t.strip()}
    assert types == {"harv", "cmin"}
    assert refineries == {"garefn", "narefn"}
