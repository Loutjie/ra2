"""RA2 rules.ini units to OpenRA units.

Every Exact-mode number goes through this module. The research behind the
constants, with sources, is in tools/exact/TIMING.md.

Time
  RA2 runs one game-logic frame per rendered frame, so the length of a frame
  in real time depends on the game speed setting. RA2_FRAMES_PER_SECOND is the
  rate at the default skirmish speed. It is not settled yet (see TIMING.md),
  so it falls back to OPENRA_TICKS_PER_SECOND: one RA2 frame = one OpenRA
  tick, which is what the mod does today.

  Minutes in rules.ini are converted as wall-clock minutes at OpenRA's rate
  (1 minute = 1500 ticks), matching the mod's current superweapon timers.
  The RA2 engine itself turns a minute into 900 frames; the two agree only
  if RA2 runs at 15 frames per second. MINUTES_ARE_FRAMES switches to the
  engine's 900 frames per minute; TIMING.md explains the open decision.

Speed and turning
  Speed and ROT are per-frame quantities. The mod's conventions (Speed x15,
  ROT x4) are kept and scaled by RA2_FRAMES_PER_SECOND / OPENRA_TICKS_PER_SECOND
  so that they stay consistent with frames_to_ticks when the rate is settled
  (a hypothesis to confirm with the frame-rate measurement).
  GameSpeedBias is not applied separately (see TIMING.md).

Distance
  1 cell = 256 leptons = 1024 WDist.

Projectile speed
  Weapon Speed= is leptons per frame, converted exactly: x4 WDist per lepton,
  scaled by RA2_FRAMES_PER_SECOND / OPENRA_TICKS_PER_SECOND like every other
  per-frame quantity. The mod's existing projectile speeds are not derived
  from it (TIMING.md section 7).

Rounding is half away from zero on exact decimal values.
"""
from __future__ import annotations

import re
from decimal import ROUND_HALF_UP, Decimal

# mods/ra2/mod.yaml, GameSpeeds -> default -> Timestep: 40 ms per tick.
OPENRA_TICKS_PER_SECOND = 25

# Fallback: rate not established by sources (TIMING.md). Best-supported
# candidate is 30 frames per second; awaiting an in-game measurement.
RA2_FRAMES_PER_SECOND: float = float(OPENRA_TICKS_PER_SECOND)

# RA2 engine: a rules.ini minute is 900 frames (TIMING.md section 2).
RA2_FRAMES_PER_MINUTE = 900

# False: a rules.ini minute is a wall-clock minute (the mod today, the brief).
# True: a minute is RA2_FRAMES_PER_MINUTE frames through frames_to_ticks.
# The Iron Curtain recharge measurement in TIMING.md section 4 decides it.
MINUTES_ARE_FRAMES = False

# The mod's existing factors (GAP_STATS.md: "Speed x15; ROT x4").
SPEED_FACTOR = 15
ROT_FACTOR = 4

LEPTONS_PER_CELL = 256
WDIST_PER_CELL = 1024


def _round(value) -> int:
    return int(Decimal(str(value)).quantize(Decimal(1), rounding=ROUND_HALF_UP))


def _frame_scale() -> Decimal:
    return Decimal(str(RA2_FRAMES_PER_SECOND)) / OPENRA_TICKS_PER_SECOND


def frames_to_ticks(frames: float) -> int:
    """RA2 frames (ROF, IronCurtainDuration, ...) to OpenRA ticks."""
    if frames < 0:
        raise ValueError(f"negative frame count: {frames}")
    return _round(Decimal(str(frames)) / _frame_scale())


def minutes_to_ticks(minutes: float) -> int:
    """rules.ini minutes (RechargeTime, ...) to OpenRA ticks (see MINUTES_ARE_FRAMES)."""
    if minutes < 0:
        raise ValueError(f"negative duration: {minutes}")
    if MINUTES_ARE_FRAMES:
        return frames_to_ticks(Decimal(str(minutes)) * RA2_FRAMES_PER_MINUTE)
    return _round(Decimal(str(minutes)) * 60 * OPENRA_TICKS_PER_SECOND)


def speed_to_openra(ra2_speed: int) -> int:
    """RA2 Speed= to OpenRA Mobile/Aircraft Speed (WDist per tick)."""
    if ra2_speed < 0:
        raise ValueError(f"negative speed: {ra2_speed}")
    return _round(Decimal(str(ra2_speed)) * SPEED_FACTOR * _frame_scale())


def rot_to_turnspeed(rot: int) -> int:
    """RA2 ROT= to OpenRA TurnSpeed (WAngle per tick)."""
    if rot < 0:
        raise ValueError(f"negative ROT: {rot}")
    return _round(Decimal(str(rot)) * ROT_FACTOR * _frame_scale())


def projectile_speed_to_openra(ra2_speed: int) -> int:
    """RA2 weapon Speed= (leptons per frame) to OpenRA projectile Speed (WDist per tick)."""
    if ra2_speed < 0:
        raise ValueError(f"negative projectile speed: {ra2_speed}")
    return _round(Decimal(str(ra2_speed)) * WDIST_PER_CELL / LEPTONS_PER_CELL * _frame_scale())


def _wdist(units: int) -> str:
    # OpenRA's WDist.TryParse takes the sign from the cell part, so "-0c256"
    # would read as +256; a negative under one cell uses the plain form.
    if -WDIST_PER_CELL < units < 0:
        return str(units)
    sign = "-" if units < 0 else ""
    cells, rest = divmod(abs(units), WDIST_PER_CELL)
    return f"{sign}{cells}c{rest}"


def cells_to_wdist(cells: float) -> str:
    """Cells (Range=, Sight=, ...) to a WDist string: 5.75 -> "5c768"."""
    return _wdist(_round(Decimal(str(cells)) * WDIST_PER_CELL))


def leptons_to_wdist(leptons: int) -> str:
    """Leptons (256 per cell) to a WDist string: 384 -> "1c512"."""
    if isinstance(leptons, bool) or not isinstance(leptons, int):
        raise TypeError(f"leptons must be an int: {leptons!r}")
    return _wdist(_round(Decimal(str(leptons)) * WDIST_PER_CELL / LEPTONS_PER_CELL))


_PERCENT = re.compile(r"([0-9]+(?:\.[0-9]*)?|\.[0-9]+)(%?)")


def percent(v: str) -> int:
    """A rules.ini percentage to an integer percent: "50%" or ".5" -> 50.

    A value with a % sign is a percentage. A bare number is a fraction, as in
    RA2 (e.g. ".5", "1"). Anything else raises ValueError.
    """
    match = _PERCENT.fullmatch(v.strip())
    if not match:
        raise ValueError(f"not a percentage: {v!r}")
    value = Decimal(match.group(1))
    return _round(value if match.group(2) else value * 100)
