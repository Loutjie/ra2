"""Units smoke and buildings burn below half health, as in RA2 (ConditionYellow=50%)."""
from pathlib import Path

from ra2data.resolved import parse_miniyaml

MOD = Path(__file__).resolve().parents[2] / "mods" / "ra2"
DEFAULTS = parse_miniyaml((MOD / "rules" / "defaults.yaml").read_text(encoding="utf-8-sig"))


def _heavy_damage_condition(template):
    grant = DEFAULTS[template]["GrantConditionOnDamageState@HEAVYDAMAGE"]
    assert grant["Condition"] == "heavy-damage"
    # The engine's Heavy state starts below 50%, RA2's yellow health bar
    assert grant["ValidDamageStates"].replace(" ", "") == "Heavy,Critical"


def test_buildings_grant_heavy_damage_only_in_the_generated_fires():
    # Buildings without RA2 fire points would grant an unused condition (a lint warning).
    assert "GrantConditionOnDamageState@HEAVYDAMAGE" not in DEFAULTS["^Building"]


def test_damage_smoke_template_trails_smoke_only_when_heavily_damaged():
    _heavy_damage_condition("^DamageSmoke")
    smoke = DEFAULTS["^DamageSmoke"]["LeavesTrails@DAMAGESMOKE"]
    assert smoke["RequiresCondition"] == "heavy-damage"
    assert smoke["Image"] == "smallsmoke"
    assert smoke["Type"] == "CenterPosition"
    assert smoke["TrailWhileStationary"] == "true"


def test_vehicles_ships_and_aircraft_smoke_but_infantry_does_not():
    for template in ("^Vehicle", "^Ship", "^NeutralAircraft"):
        assert "^DamageSmoke" in DEFAULTS[template].values(), template
    assert "^DamageSmoke" not in DEFAULTS["^Infantry"].values()
