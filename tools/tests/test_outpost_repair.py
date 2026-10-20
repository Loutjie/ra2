"""Tech Outpost repair zone matches the original RA2 foundation (OpenRA/ra2#679)."""
import pytest

from ra2data.resolved import resolve_actor


def _blocked_cells(footprint):
    return footprint.count("x") + footprint.count("X")


@pytest.mark.integration
def test_outpost_repair_range(ra2ini):
    """RA2 blocks NumberImpassableRows cells of the foundation; the rest is the repair pad."""
    original = int(ra2ini["CAOUTP"]["NumberImpassableRows"].split(";")[0])
    building = resolve_actor("caoutp")["Building"]
    footprint = building["Footprint"]
    assert building["Dimensions"] == "4,3"
    assert _blocked_cells(footprint) == original
    assert "=" in footprint
    # The Soviet service depot has the same 4x3 foundation and pad in RA2.
    assert footprint == resolve_actor("nadept")["Building"]["Footprint"]
