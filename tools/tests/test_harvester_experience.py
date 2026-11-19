"""Harvester rank-up follows RA2's Trainable= flag (OpenRA/ra2#692)."""
import pytest

from ra2data.resolved import resolve_actor


def _trainable(ra2ini, section):
    return ra2ini[section].get("Trainable", "yes").split(";")[0].strip().lower() == "yes"


@pytest.mark.integration
def test_harvesters_cannot_gain_experience(ra2ini):
    """Level-up crates and kills must not promote harvesters RA2 marks Trainable=no (CMIN)."""
    assert not _trainable(ra2ini, "CMIN")
    cmin = resolve_actor("cmin")
    assert "GainsExperience" not in cmin
    assert not any(k.startswith("WithDecoration@VETERAN") for k in cmin)


@pytest.mark.integration
def test_war_miner_still_gains_experience(ra2ini):
    """RA2 leaves HARV at the default Trainable=yes, so it keeps ranking."""
    assert _trainable(ra2ini, "HARV")
    assert "GainsExperience" in resolve_actor("harv")
