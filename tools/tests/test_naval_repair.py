"""Ships must be repairable at the naval yards (OpenRA/ra2#715)."""
import pytest

from ra2data.resolved import cells, resolve_actor

YARDS = ("gayard", "nayard")
SHIPS = ("lcrf", "dest", "aegis", "carrier", "tug", "cruise", "sapc", "sub", "hyd")


@pytest.mark.integration
@pytest.mark.parametrize("yard", YARDS)
def test_naval_yard_repairs_ships(yard):
    rules = resolve_actor(yard)
    assert "RepairsUnits" in rules, f"{yard} cannot repair"
    # An even-sized (4x4) building has its centre on a cell corner, so a ship (which
    # always sits at a cell centre) can never reach the 512-lepton range that
    # Repairable demands. Ships therefore have to use RepairableNear, which measures
    # range to the yard's hit shape instead of its centre.
    assert rules["Building"]["Dimensions"] == "4,4"
    assert rules["Building"]["TerrainTypes"] == "Water"
    for ship in SHIPS:
        ship_rules = resolve_actor(ship)
        assert "Repairable" not in ship_rules, f"{ship} uses centre-based Repairable"
        near = ship_rules.get("RepairableNear")
        assert near is not None, f"{ship} has no RepairableNear"
        assert yard in [a.strip() for a in near["RepairActors"].split(",")], f"{ship} cannot repair at {yard}"
        # Must reach from the water cell next to the yard to its hit shape.
        assert cells(near["CloseEnough"]) >= 1.0
