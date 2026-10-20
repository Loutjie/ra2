"""Veterancy rules: only elite units self-heal."""
import pytest

from ra2data import resolved


@pytest.mark.integration
def test_elite_self_heal_requires_elite():
    t = resolved.resolve_actor("htnk")
    assert t["ChangesHealth@ELITE"]["RequiresCondition"] == "rank-elite"
