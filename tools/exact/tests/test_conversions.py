"""Tests for the RA2 to OpenRA unit conversions (see tools/exact/TIMING.md)."""
from __future__ import annotations

import pytest

from exact.conversions import (
    OPENRA_TICKS_PER_SECOND,
    RA2_FRAMES_PER_SECOND,
    cells_to_wdist,
    frames_to_ticks,
    leptons_to_wdist,
    minutes_to_ticks,
    percent,
    rot_to_turnspeed,
    speed_to_openra,
)


def test_openra_rate_matches_default_timestep():
    # mods/ra2/mod.yaml: GameSpeeds -> default -> Timestep: 40 (ms per tick).
    assert OPENRA_TICKS_PER_SECOND == 1000 // 40


def test_frames_to_ticks_uses_documented_rate():
    # Two seconds of RA2 game logic last two seconds in OpenRA.
    assert frames_to_ticks(RA2_FRAMES_PER_SECOND * 2) == 50
    assert frames_to_ticks(0) == 0


def test_frames_to_ticks_rejects_negative():
    with pytest.raises(ValueError):
        frames_to_ticks(-1)


def test_minutes_to_ticks():
    assert minutes_to_ticks(7) == 10500
    assert minutes_to_ticks(0.5) == 750


def test_cells_to_wdist():
    assert cells_to_wdist(5.75) == "5c768"
    assert cells_to_wdist(8) == "8c0"
    assert cells_to_wdist(0.5) == "0c512"
    assert cells_to_wdist(1.83) == "1c850"
    assert cells_to_wdist(-0.25) == "-0c256"


def test_leptons_to_wdist():
    assert leptons_to_wdist(256) == "1c0"
    assert leptons_to_wdist(384) == "1c512"
    assert leptons_to_wdist(0) == "0c0"


def test_speed_and_rot():
    # Speed x15 and ROT x4, the mod's existing convention (GAP_STATS.md).
    assert speed_to_openra(6) == 90
    assert speed_to_openra(0) == 0
    assert rot_to_turnspeed(5) == 20


def test_percent():
    assert percent("50%") == 50
    assert percent(".5") == 50
    assert percent("150%") == 150
    assert percent("0.05") == 5
    assert percent("1") == 100
    assert percent(" 25% ") == 25
    assert percent(".333") == 33


def test_percent_rejects_garbage():
    with pytest.raises(ValueError):
        percent("half")
    with pytest.raises(ValueError):
        percent("nan%")
