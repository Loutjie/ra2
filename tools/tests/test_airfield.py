"""Air Force Command pads: four landing pads, aircraft production limited by free pads."""
import pytest

from ra2data import ini
from ra2data.resolved import resolve_actor

ART_FILE = "ra2.mix__local.mix__art.ini"
AIRFIELDS = ["gaairc", "amradr"]
PAD_AIRCRAFT = ["orca", "beag"]

# Westwood leptons are 256 per cell, OpenRA world units 1024 per cell.
LEPTON = 4


@pytest.fixture(scope="module")
def ra2art():
    folder = ini.ra2ini_dir()
    if folder is None or not (folder / ART_FILE).is_file():
        pytest.skip(f"{ART_FILE} not found (set $RA2INI)")
    return ini.parse_ini(str(folder / ART_FILE))


def _vec(value):
    return tuple(int(v) for v in value.split(","))


def _traits(actor, name):
    return [v for k, v in actor.items() if k == name or k.startswith(name + "@")]


@pytest.mark.integration
@pytest.mark.parametrize("name", AIRFIELDS)
def test_airforce_command_docks_four(name, ra2ini, ra2art):
    expected = int(ra2ini[name.upper()]["NumberOfDocks"])
    assert expected == 4

    actor = resolve_actor(name)
    pads = _traits(actor, "AircraftPads")
    assert len(pads) == 1, f"{name} must define exactly one AircraftPads trait"
    assert int(pads[0]["Count"]) == expected

    # One exit per pad, placed on RA2's DockingOffsetN (art.ini, leptons from the centre).
    image = ra2ini[name.upper()].get("Image", name.upper())
    art = ra2art[image]
    docks = sorted(tuple(c * LEPTON for c in _vec(art[f"DockingOffset{i}"])) for i in range(expected))
    exits = sorted(_vec(e["SpawnOffset"]) for e in _traits(actor, "Exit"))
    assert exits == docks


@pytest.mark.integration
def test_aircraft_queue_is_pad_limited():
    player = resolve_actor("Player")
    queues = [k for k in player if k.endswith("ProductionQueue@Aircraft")]
    assert queues == ["PadLimitedProductionQueue@Aircraft"]
    assert player[queues[0]]["Type"] == "Aircraft"


@pytest.mark.integration
@pytest.mark.parametrize("name", PAD_AIRCRAFT)
def test_aircraft_limited_by_pads(name, ra2ini):
    section = ra2ini[name.upper()]
    assert section.get("AirportBound", "").lower() == "yes"
    assert name.upper() in ra2ini["General"]["PadAircraft"].split(",")

    actor = resolve_actor(name)
    assert "PadBound" in actor, f"{name} must need a free Air Force Command pad to be built"

    # Balance is untouched by the pad limit.
    assert actor["Valued"]["Cost"] == section["Cost"]
    assert actor["AmmoPool"]["Ammo"] == section["Ammo"]
