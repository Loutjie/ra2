"""Tech Outpost repair zone matches the original RA2 foundation (OpenRA/ra2#679).

Symptom (issue screenshot): the outpost's single repair pad sits on the building's
east edge, but the mod left a wider strip of the foundation walkable.

RA2 data: art.ini Foundation=4x3 and DockingOffset0=384,0,0 (1.5 cells east of the
foundation centre, i.e. x = 2.0 + 1.5 = 3.5, the centre of the last column), and
rules.ini NumberImpassableRows=3. ModEnc defines a row as a one-cell-wide line running
SW-NE, counted from the game west; that is a column of the OpenRA footprint (a line of
constant x). So the three west columns are blocked and the east column is the pad.
"""
import os
from pathlib import Path

import pytest

from ra2data.ini import parse_ini, ra2ini_dir
from ra2data.resolved import resolve_actor

ART_FILE = "ra2.mix__local.mix__art.ini"


def _int(value):
    return int(value.split(";")[0].strip())


@pytest.fixture(scope="module")
def art():
    folder = ra2ini_dir()
    if folder is None or not (Path(folder) / ART_FILE).is_file():
        pytest.skip("original art.ini not found")
    return parse_ini(os.path.join(folder, ART_FILE))


@pytest.mark.integration
def test_outpost_footprint_matches_ra2_blocked_columns_and_dock(ra2ini, art):
    width, height = (int(n) for n in art["CAOUTP"]["Foundation"].split("x"))
    blocked_columns = _int(ra2ini["CAOUTP"]["NumberImpassableRows"])
    dock_x = width / 2 + int(art["CAOUTP"]["DockingOffset0"].split(",")[0]) / 256
    dock_column = int(dock_x)

    building = resolve_actor("caoutp")["Building"]
    assert building["Dimensions"] == f"{width},{height}"
    rows = building["Footprint"].split()
    assert len(rows) == height and all(len(r) == width for r in rows)

    for row in rows:
        assert row[:blocked_columns] == "x" * blocked_columns
        assert set(row[blocked_columns:]) == {"="}
    # The dock (repair) cell lies in the walkable strip.
    assert dock_column >= blocked_columns
