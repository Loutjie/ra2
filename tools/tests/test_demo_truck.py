"""Demolition Truck can be force-attack ordered onto empty ground (OpenRA/ra2#338)."""
import pytest

from ra2data.resolved import resolve_weapon


def _targets(node):
    return {t.strip() for t in node["ValidTargets"].split(",")}


@pytest.mark.integration
def test_demo_truck_can_force_attack_ground():
    """Terrain only carries the Ground target type, so the targeting weapon must accept it."""
    weapon = resolve_weapon("DemoTruckTargeting")
    assert "Ground" in _targets(weapon)
    assert "Ground" in _targets(weapon["Warhead@1Dam"])
    # Ordinary right-click targets stay as before.
    assert "DetonateAttack" in _targets(weapon)
