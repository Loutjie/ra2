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


def test_damage_smoke_rises_only_when_heavily_damaged():
    """RA2's SmallGreySSys smoke drifts upward, also while the unit stands still: a static trail
    sprite stays where it spawned, so the smoke is emitted as rising floating sprites (as in d2k)."""
    _heavy_damage_condition("^DamageSmoke")
    assert "LeavesTrails@DAMAGESMOKE" not in DEFAULTS["^DamageSmoke"]
    smoke = DEFAULTS["^DamageSmoke"]["FloatingSpriteEmitter@DAMAGESMOKE"]
    assert smoke["RequiresCondition"] == "heavy-damage"
    assert smoke["Image"] == "smallsmoke"
    assert smoke["Sequences"] == "idle"
    assert int(smoke["Gravity"]) > 0
    # Starts above the hull: at the centre the voxel model hides it.
    assert smoke["Offset"].replace(" ", "") == "0,0,256"


def test_vehicles_ships_and_aircraft_smoke_but_infantry_does_not():
    for template in ("^Vehicle", "^Ship", "^NeutralAircraft"):
        assert "^DamageSmoke" in DEFAULTS[template].values(), template
    assert "^DamageSmoke" not in DEFAULTS["^Infantry"].values()


def test_fire_and_smoke_sprites_draw_in_front_of_buildings():
    """Building sprites carry a depth map, so an effect at the building's position sorts behind its front
    and only shows on flat buildings. Like the explosions, the sprites need their own depth offset."""
    misc = parse_miniyaml((MOD / "sequences" / "misc.yaml").read_text(encoding="utf-8-sig"))
    for image in ("largefire", "mediumfire", "smallfire", "smallsmoke"):
        assert misc[image]["idle"]["Offset"].replace(" ", "") == "0,0,60", image


def test_damaged_vehicles_spark_when_hit():
    """RA2's SparkSys: each hit below the yellow health bar throws sparks (one flash, not a loop)."""
    sparks = DEFAULTS["^DamageSmoke"]["WithDamageOverlay@SPARKS"]
    assert sparks["Image"] == "explosion"
    assert sparks["LoopSequence"] == "piff"
    assert sparks["LoopCount"] == "1"
    assert sparks["MinimumDamageState"] == "Heavy"
    assert sparks["Palette"] == "effect"
    misc = parse_miniyaml((MOD / "sequences" / "misc.yaml").read_text(encoding="utf-8-sig"))
    assert "piff" in misc["explosion"]
