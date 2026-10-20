"""Tests for the RA2 to OpenRA unit conversions (see tools/exact/TIMING.md)."""
from __future__ import annotations

import pytest

from exact import conversions
from exact.conversions import (
    OPENRA_TICKS_PER_SECOND,
    RA2_FRAMES_PER_SECOND,
    cells_to_wdist,
    frames_to_ticks,
    leptons_to_wdist,
    minutes_to_ticks,
    percent,
    projectile_speed_to_openra,
    rot_to_turnspeed,
    speed_to_openra,
)


def test_openra_rate_matches_default_timestep():
    # mods/ra2/mod.yaml: GameSpeeds -> default -> Timestep: 40 (ms per tick).
    assert OPENRA_TICKS_PER_SECOND == 1000 // 40


def test_measured_defaults():
    # Measured in game (TIMING.md section 4): slider position 3 runs at 20 fps,
    # and a rules.ini minute is 900 frames.
    assert RA2_FRAMES_PER_SECOND == 20
    assert conversions.MINUTES_ARE_FRAMES is True


def test_frames_to_ticks_uses_documented_rate():
    # Two seconds of RA2 game logic last two seconds in OpenRA: 40 frames, 50 ticks.
    assert frames_to_ticks(RA2_FRAMES_PER_SECOND * 2) == 50
    assert frames_to_ticks(20) == 25
    assert frames_to_ticks(0) == 0


def test_frames_to_ticks_at_one_frame_per_tick(monkeypatch):
    monkeypatch.setattr(conversions, "RA2_FRAMES_PER_SECOND", 25.0)
    assert frames_to_ticks(60) == 60


def test_frames_to_ticks_rejects_negative():
    with pytest.raises(ValueError):
        frames_to_ticks(-1)


def test_minutes_to_ticks():
    # 900 frames per minute at 20 fps = 45 s = 1125 ticks.
    assert minutes_to_ticks(7) == frames_to_ticks(7 * 900) == 7875
    assert minutes_to_ticks(0.5) == 563  # 562.5 rounds half up
    # The measured Ore Refinery: Cost 2000 x BuildSpeed .7 per 1000 = 1.4 minutes, 60 s.
    assert minutes_to_ticks(1.4) == 1575
    assert minutes_to_ticks(1.4) / OPENRA_TICKS_PER_SECOND == 63


def test_minutes_to_ticks_as_wall_clock(monkeypatch):
    monkeypatch.setattr(conversions, "MINUTES_ARE_FRAMES", False)
    assert minutes_to_ticks(7) == 10500
    assert minutes_to_ticks(0.5) == 750


def test_minutes_to_ticks_as_engine_frames_at_30_fps(monkeypatch):
    monkeypatch.setattr(conversions, "RA2_FRAMES_PER_SECOND", 30.0)
    assert minutes_to_ticks(7) == 5250  # 6300 frames at 30 fps = 210 s


def _parse_wdist(s):
    """Port of OpenRA.Game/WDist.cs TryParse (sign taken from the cell part)."""
    parts = s.replace("C", "c").split("c")
    if len(parts) == 2:
        cell, subcell = int(parts[0]), int(parts[1])
    elif len(parts) == 1:
        cell, subcell = 0, int(parts[0])
    else:
        raise ValueError(s)
    if cell < 0:
        subcell = -subcell
    return 1024 * cell + subcell


def test_cells_to_wdist():
    assert cells_to_wdist(5.75) == "5c768"
    assert cells_to_wdist(8) == "8c0"
    assert cells_to_wdist(0.5) == "0c512"
    assert cells_to_wdist(1.83) == "1c850"
    assert cells_to_wdist(-0.25) == "-256"
    assert cells_to_wdist(-1.25) == "-1c256"


@pytest.mark.parametrize("cells", [0, 0.25, -0.25, 0.999, -0.999, 1, -1, 1.25, -1.25, -8, 5.75])
def test_cells_to_wdist_round_trips_through_openra_parser(cells):
    assert _parse_wdist(cells_to_wdist(cells)) == round(cells * 1024)


def test_leptons_to_wdist():
    assert leptons_to_wdist(256) == "1c0"
    assert leptons_to_wdist(384) == "1c512"
    assert leptons_to_wdist(0) == "0c0"
    assert leptons_to_wdist(-64) == "-256"
    with pytest.raises(TypeError):
        leptons_to_wdist(1.5)


def test_speed_and_rot():
    # Speed x15 and ROT x4, the mod's existing convention (GAP_STATS.md).
    # Scaled by 20 fps / 25 ticks per second = 0.8.
    assert speed_to_openra(6) == 72
    assert speed_to_openra(0) == 0
    assert rot_to_turnspeed(5) == 16


def test_speed_and_rot_at_one_frame_per_tick(monkeypatch):
    monkeypatch.setattr(conversions, "RA2_FRAMES_PER_SECOND", 25.0)
    assert speed_to_openra(6) == 90
    assert rot_to_turnspeed(5) == 20


def test_projectile_speed_to_openra():
    # Weapon Speed= is leptons per frame: x4 WDist per lepton, 0.8 frames per tick.
    assert projectile_speed_to_openra(40) == 128
    assert projectile_speed_to_openra(100) == 320
    assert projectile_speed_to_openra(0) == 0
    with pytest.raises(ValueError):
        projectile_speed_to_openra(-1)


def test_projectile_speed_follows_frame_rate(monkeypatch):
    # 30 frames per second over 25 ticks per second: 1.2 frames per tick.
    monkeypatch.setattr(conversions, "RA2_FRAMES_PER_SECOND", 30.0)
    assert projectile_speed_to_openra(40) == 192
    assert projectile_speed_to_openra(1) == 5  # 4.8 rounds half away from zero


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
    for bad in ["nan%", "\u0665", "1_0", "1e2", "-5%", "50%%", "", "%", "."]:
        with pytest.raises(ValueError):
            percent(bad)
