"""The Rocketeer must show exactly one infantry body per airborne state."""
import pytest

from ra2data.resolved import resolve_actor


@pytest.mark.integration
def test_jumpjet_has_one_body_per_airborne_state():
    rules = resolve_actor("jumpjet")
    conditions = {
        key: body.get("RequiresCondition")
        for key, body in rules.items()
        if key.split("@")[0] == "WithInfantryBody"
    }
    assert sorted(conditions.values(), key=str) == ["!airborne", "airborne"], conditions
