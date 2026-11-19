# RA2 timing and unit conversions

Research behind `tools/exact/conversions.py`. Every Exact-mode number goes
through that module, so each constant is traced to a source here.

Researched 2026-10-20. Quotes are kept short; follow the links for context.

## Conclusion

| Constant | Value | Status |
|---|---|---|
| `OPENRA_TICKS_PER_SECOND` | 25 | Settled: `mods/ra2/mod.yaml`, `GameSpeeds` default `Timestep: 40` (ms). |
| `RA2_FRAMES_PER_SECOND` | **30** | **Measured** (section 4): RA2 speed slider 3 is about 20 fps, slider 4 about 30 fps. The user chose slider 4 as the reference for OpenRA Normal speed. One RA2 frame = 0.8333 OpenRA ticks. |
| RA2 frames per rules.ini minute | 900 | Settled (engine constant, see below). |
| `minutes_to_ticks` | engine frames: 1 min = 900 frames = 750 ticks (`MINUTES_ARE_FRAMES = True`) | **Measured** (section 4, section 5): wall-clock minutes (84 s for the refinery) are ruled out; the 4:22 Iron Curtain countdown is 3930 frames. |
| Speed | x15 | Kept (mod convention), scaled by `RA2_FRAMES_PER_SECOND / 25` = 1.2, i.e. x18 per RA2 speed unit (hypothesis, not separately measured, see section 6). |
| ROT | x4 | Kept (mod convention), scaled the same way: x4.8 (hypothesis). |
| `GameSpeedBias=1.6` | not applied separately | See "GameSpeedBias". |
| Distance | 1 cell = 256 leptons = 1024 WDist | Settled. |
| Projectile speed | leptons per frame x 4, scaled by `RA2_FRAMES_PER_SECOND / 25` | Unit settled (leptons per frame); follows the frame rate (x1.2). Mod's existing projectile speeds are not derived from it (section 7). |

## 1. Frames are game-logic steps, and their real length depends on the speed setting

- RA2 durations such as ROF are counted in frames. ModEnc, ROF: "how many
  frames the firer cannot fire after the last shot"
  (<https://modenc.renegadeprojects.com/ROF>). IronCurtainDuration is
  "measured in frames" (<https://modenc.renegadeprojects.com/IronCurtainDuration>).
- The game speed setting is a single number. ModEnc, GameSpeed: values run
  "from 0 (fastest) to 6 (slowest)"
  (<https://modenc.renegadeprojects.com/GameSpeed>). ModEnc gives no FPS per
  value.
- In the engine family RA2 descends from, the speed setting is a frame-rate
  cap and one logic frame runs per frame. Red Alert 1 source released by EA,
  `REDALERT/QUEUE.CPP` lines 1423-1427:
  `specified_frame_rate = 60 / Options.GameSpeed;` (0 means 60 in network
  play) (<https://github.com/electronicarts/CnC_Remastered_Collection/blob/master/REDALERT/QUEUE.CPP>).
  This is RA1, not RA2; it is evidence for the mechanism, not for RA2's table.

## 2. The design basis is 15 frames per second ("900 frames = 1 minute")

- `rules.ini` line 693: `IronCurtainDuration=750 ;gs In frames 900 is a minute for 15fps`.
- `rules.ini` line 220: `SpyPowerBlackout=1000 ; Frame time ... (900 = 1 minute)`.
- `rules.ini` lines 46 and 249: `PlacementDelay=.05` (minutes) is described as
  "every 45 (PlacementDelay) frames", i.e. 0.05 x 900.
- ModEnc, Frames, infers the same from SpyPowerBlackout: 15 frames per in-game
  second (<https://modenc.renegadeprojects.com/Frames>).
- RA1 source, `REDALERT/DEFINES.H` lines 3123-3124: `TICKS_PER_SECOND 15`,
  `TICKS_PER_MINUTE (TICKS_PER_SECOND * 60)`, and rules minutes are multiplied
  by `TICKS_PER_MINUTE` (e.g. `BDATA.CPP:3127`, `CRATE.CPP:95`)
  (<https://github.com/electronicarts/CnC_Remastered_Collection/blob/master/REDALERT/DEFINES.H>).
- Counter-evidence that the designers also thought in 30 Hz: `rules.ini`
  lines 738-739 (radiation): "6000 frames -- something over 3 minutes at 30 Hz".

So a rules.ini "minute" is 900 frames, a fixed engine constant. It equals a
real minute only when the game runs at 15 frames per second.

## 3. What rate does the default skirmish speed run at?

| Source | Claim | Implied rate at default skirmish |
|---|---|---|
| CnCNet YR client, `ForcedOptions.md` (<https://github.com/CnCNet/cncnet-yr-client-package/blob/develop/ForcedOptions.md>) | Skirmish speed: "0 = MAX FPS, 1 = 60 FPS, 2 = 30 FPS"; starting value `cmbGameSpeedCapSkirmish=2` | 30 (if the default is GameSpeed 2) |
| Same file | Multiplayer: "0 = 60 FPS, 1 = 45 FPS, 2 = 30 FPS" | (a different table for online games) |
| CnCNet YR client, `MultiplayerGameLobby.ini` (<https://github.com/CnCNet/cncnet-yr-client-package/blob/develop/package/Resources/MultiplayerGameLobby.ini>) | `Items=60 FPS,45 FPS,30 FPS,20 FPS,15 FPS,12 FPS,10 FPS` | multiplayer table only |
| RA1 source (QUEUE.CPP above) | 60 / GameSpeed | GameSpeed 2 = 30 |
| CnCNet forum, "Game speed", Dasha, 2024-10-10 (<https://forums.cncnet.org/topic/12923-game-speed/>) | Fresh install: "5 (out of 7) is the default"; "5 means fast" | slider 5 of 7 = GameSpeed 2 |
| Same thread, Dasha | "4 is 30 and 5 is 45 as 6 is 60 fps" | **45** (contradicts the rows above) |
| CnCNet forum, "Game is running too fast", Ra2Nub, 2022-10-10 (<https://forums.cncnet.org/topic/12370-game-is-running-too-fast/>) | Single player: "Fastest" is uncapped and "Faster is 60 fps" | consistent with 60 / GameSpeed |
| CnCNet forum, "What's the obsession with highest game speed", 2018 (<https://forums.cncnet.org/topic/8878-whats-the-obsession-with-highest-game-speed/>) | "Fast 30fps?" (darbyjack, unsure); default called "Fast" (total_annihilationx666) | 30, weakly |
| CnCNet ts-patches, `src/gamespeed.asm` (<https://github.com/CnCNet/ts-patches/blob/master/src/gamespeed.asm>) | A *patch* for Tiberian Sun remapping single-player speeds, "speed 2 (Fast) = 45 FPS" | patched TS, not stock RA2 |
| Search-engine summary of a 2016 CnCNet post (SiRaLeX), not opened | measured caps "Game Speed 6 <= 59 FPS ... 3 <= 29 FPS" (10 FPS steps) | unverified, possibly 40 or 50 |

Reading (before the measurement): the stock single-player/skirmish table
appeared to be 60 / GameSpeed (MAX, 60, 30, 20, 15, 12, 10), but the sources
disagreed on which notch the default is (30 vs 45 fps), the multiplayer table
differs from the skirmish one, and no source measured stock RA2 skirmish. The
user's measurement in section 4 settles it: slider 3 is about 20 fps and slider 4 about 30 fps.

## 4. Measurements (done, 2026-10-20)

All by the user in original RA2, Skirmish.

| # | Speed slider | Observation | Frames | Implied fps |
|---|---|---|---|---|
| 1 | 3 | Ore Refinery (`Cost=2000`) built in about 60 s | 1260 | about 21 |
| 2 | 4 | Ore Refinery built in 40 s | 1260 | about 31.5 |
| 3 | 4 | Iron Curtain countdown 4:22 (in-game game-minutes of 900 frames, 262 s = 3930 frames) elapsed in 2:08 (128 s) | 3930 | about 30.7 |
| 4 | 4 | Iron Curtain effect (`IronCurtainDuration=750`) lasted 23.42 s | 750 | about 32.0 |

Slider to frame rate (community figures, section 3): 3 = 20 fps, 4 = 30 fps,
5 = 45 fps, 6 = 60 fps (Dasha, CnCNet forum: "4 is 30 and 5 is 45 as 6 is 60
fps", <https://forums.cncnet.org/topic/12923-game-speed/>; the same notches as
60 / GameSpeed for the lower values). The measurements agree: slider 3 is about
20 fps (measurement 1), slider 4 about 30 fps (measurements 2 to 4: 31.5, 30.7
and 32.0, a range of about 31 to 32 fps, 2 to 7 % above 30). The remaining few
percent are within stopwatch and build-up animation tolerance, and 30 is also
the community figure for slider 4, so the constant stays 30.

**Reasoning for minutes.** `[General] BuildSpeed=.7` is minutes per 1000
credits, so the refinery costs 2000 x .7 / 1000 = **1.4 rules.ini minutes**.

- If a rules minute were a real minute, the refinery would take 1.4 x 60 =
  **84 s** at any slider. Observed 60 s and 40 s: rejected. (This also drops
  the wall-clock reading that the mod used until now, `MINUTES_ARE_FRAMES =
  False`.)
- If a rules minute is the engine's 900 frames, the refinery takes 1.4 x 900 =
  **1260 frames**: 63 s at 20 fps (observed about 60) and 42 s at 30 fps
  (observed 40). Both fit.
- The Iron Curtain timer shows game-minutes of 900 frames: 4:22 = 262 game
  seconds = 3930 frames, which at 30 fps takes 131 s (observed 128).

**Ruling (user):** OpenRA Normal speed (25 ticks per second) corresponds to RA2
speed slider 4, so `RA2_FRAMES_PER_SECOND = 30` and `MINUTES_ARE_FRAMES = True`.
In Exact, a rules minute is 900 / 30 = 30 s = 750 ticks and one RA2 frame is
25 / 30 = 0.8333 ticks. (The first ruling used slider 3, 20 fps; it is replaced.)

**Iron Curtain cross-check (done, measurement 4):** `IronCurtainDuration=750`
frames predicts 25 s at 30 fps (625 ticks in Exact); the user timed 23.42 s,
about 32 fps. Optional further cross-checks: Ivan's time bomb
(`IvanTimedDelay=450` frames, 15 s at 30 fps); the Iron Curtain recharge
(`RechargeTime=5` minutes = 4500 frames = 150 s).

Sanity values after regeneration: `garefn.exact` BuildDuration 1050 ticks
(42 s), `htnk.exact` 473 ticks (0.63 min = 567 frames = 18.9 s), Iron Curtain
ChargeInterval 3750 and Duration 625, Chronosphere ChargeInterval 5250,
`120mm.exact` ReloadDelay 54 (ROF 65 frames).

## 5. Minutes (decided)

The engine converts a rules minute to 900 frames (section 2). The measurement
in section 4 shows RA2 behaves that way (60 s refinery, not 84 s), so
`conversions.MINUTES_ARE_FRAMES = True` and `minutes_to_ticks(m) =
frames_to_ticks(m * 900)`: 1 minute = 750 ticks at 30 fps. The old
wall-clock formula (1 minute = 1500 ticks, the mod's earlier superweapon
timers) is kept behind `MINUTES_ARE_FRAMES = False` for tests only. Every
minute-based timer in Exact (superweapons, repair and reload rates, build
time) is therefore 50 % shorter than the old wall-clock value (750 / 1500).
Reimagined is untouched.

## 6. Speed, ROT and GameSpeedBias

- The mod uses Speed x15 and ROT x4 (`GAP_STATS.md`: "Speed x15; ROT x4",
  checked against 42 and 22 units). Neither factor is documented as derived.
  ModEnc gives Speed's internal value as `min((min(Speed,100)*256/100),255)`
  (<https://modenc.renegadeprojects.com/Speed>) and no unit for ROT
  (<https://modenc.renegadeprojects.com/ROT>).
- **Hypothesis (frame rate now measured, scaling still unmeasured):** Speed
  and ROT are per-frame quantities, like ROF. The module scales the mod's
  x15 / x4 factors by `RA2_FRAMES_PER_SECOND / 25` = 1.2, so at 30 fps every
  per-frame quantity moves together: Speed x18, ROT x4.8, ROF x0.8333 ticks per
  frame (a Speed=6 tank: 90 -> 108 WDist per tick; `htnk.exact` TurnSpeed 24).
  Nothing shows x15 / x4 were calibrated against a 1:1 frame mapping, so this
  is reasoned, not measured. To confirm, time one unit over a known distance
  in RA2 at slider position 4.
- `GameSpeedBias=1.6` (`rules.ini` line 56, "multiplier to overall game object
  movement speed"). ModEnc: "a global multiplier to the speed of all units"
  and "does not affect the speed of projectiles or animations"; ModEnc also
  says it scales BuildSpeed and flags the logic as obsolete in RA2/YR without
  saying which part (<https://modenc.renegadeprojects.com/GameSpeedBias>).
  In RA1 (`REDALERT/HOUSE.CPP` lines 288-305) it multiplies the ground, air and
  build speed biases.
- **Handling: not applied separately.** It is one uniform factor on every unit,
  so it changes no relative balance, and x15 already appears to absorb it.
  Plausibility check under the RA1 movement model (`DRIVE.CPP` 663-670:
  about the internal speed in leptons per frame at full throttle):
  Speed 6 -> 15.36 x 1.6 = 24.6 leptons per frame = 98 WDist per frame,
  close to x15 = 90. Applying 1.6 on top of x15 would make every unit 60 %
  faster than the mod. That RA2 moves exactly like RA1 is not confirmed.
- Build time is not converted by this module. If RA2 also divides build time
  by GameSpeedBias (as RA1 does), the build-time task must account for it.

## 7. Distances and percentages

- 1 cell = 256 leptons. ModEnc, Leptons: "a single map cell is 256x256
  leptons" (<https://modenc.renegadeprojects.com/Leptons>). 1 cell = 1024
  WDist in OpenRA. WDist strings are `<cells>c<remainder>`; `5.75` -> `5c768`.
  OpenRA's `WDist.TryParse` (`engine/OpenRA.Game/WDist.cs`) takes the sign
  from the cell part, so `-0c256` would parse as +256; negatives under one
  cell are written as plain WDist units (`-256`).
- Projectile speed: a weapon's `Speed=` is in leptons per frame, so
  `projectile_speed_to_openra` converts it as leptons x 4 (WDist per lepton)
  x `RA2_FRAMES_PER_SECOND / 25` (frames per tick), with the same frame-rate
  constant as ROF and unit Speed. At 30 fps, `Speed=40` (120mm)
  -> 192 WDist per tick (160 at one frame per tick). GameSpeedBias does not
  apply (ModEnc: it "does not affect the speed of projectiles"). The mod's
  existing projectile speeds are not derived from this rule (many weapons use
  a hand-set `682`, against 160 for `Speed=40`); Exact values must come from
  the conversion, not be copied from the Reimagined weapons.
- Percentages: `"50%"` is a percentage; a bare number (`".5"`, `"1"`) is a
  fraction, as in RA2. Input must match `digits[.digits]` or `.digits` with an
  optional `%`; anything else (`1_0`, `1e2`, negatives) is rejected.
  Rounding is half away from zero on exact decimals.
