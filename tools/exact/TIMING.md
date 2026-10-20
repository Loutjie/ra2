# RA2 timing and unit conversions

Research behind `tools/exact/conversions.py`. Every Exact-mode number goes
through that module, so each constant is traced to a source here.

Researched 2026-10-20. Quotes are kept short; follow the links for context.

## Conclusion

| Constant | Value | Status |
|---|---|---|
| `OPENRA_TICKS_PER_SECOND` | 25 | Settled: `mods/ra2/mod.yaml`, `GameSpeeds` default `Timestep: 40` (ms). |
| `RA2_FRAMES_PER_SECOND` | 25 (fallback) | **Not settled.** Best-supported value is **30** at the default skirmish speed, but sources disagree (30 vs 45). The brief's fallback (one RA2 frame = one OpenRA tick, the mod's current behaviour) is used until the in-game measurement below is done. |
| RA2 frames per rules.ini minute | 900 | Settled (engine constant, see below). |
| `minutes_to_ticks` | wall-clock: 1 min = 1500 ticks (`MINUTES_ARE_FRAMES = False`) | Matches the mod today. Only consistent with the engine's 900 frames per minute if RA2 runs at 15 fps. **Open decision**, see "Minutes". |
| Speed | x15 | Kept (mod convention), scaled by `RA2_FRAMES_PER_SECOND / 25` (hypothesis, see section 6). |
| ROT | x4 | Kept (mod convention), scaled the same way (hypothesis). |
| `GameSpeedBias=1.6` | not applied separately | See "GameSpeedBias". |
| Distance | 1 cell = 256 leptons = 1024 WDist | Settled. |

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

Reading: the stock single-player/skirmish table appears to be 60 / GameSpeed
(MAX, 60, 30, 20, 15, 12, 10), the default skirmish setting appears to be
"Fast" = GameSpeed 2, which gives **30 frames per second**. But one forum
statement puts the same notch at 45, the unverified 2016 measurement suggests
10 FPS steps, the multiplayer table differs from the skirmish one, and the
CnCNet client (which many players use) can change the default. No source
measures stock RA2 skirmish directly. Per the brief, the rate is therefore not
taken as established: `RA2_FRAMES_PER_SECOND = OPENRA_TICKS_PER_SECOND` (25),
the mod's current 1:1 behaviour.

## 4. Measurement that would settle it (owner of the game)

Stock RA2 (not via the CnCNet client), Skirmish, leave the speed slider at its
default and note its position (count notches from the left, 1 to 7). Time
with a real stopwatch or phone, not the in-game countdown (that counts game
minutes of 900 frames, not real time).

1. **Iron Curtain duration** (`IronCurtainDuration=750` frames). Fire the Iron
   Curtain on your own tanks; time from the effect starting to it ending.
   fps = 750 / seconds. Expected: 25 s at 30 fps, 16.7 s at 45, 30 s at 25,
   50 s at 15.
2. **Iron Curtain recharge** (`[IronCurtainSpecial] RechargeTime=5` minutes =
   4500 frames if minutes are 900 frames). Time from the device finishing
   (or from firing) until it is ready again. Expected 150 s at 30 fps, 100 s
   at 45, 300 s at 15. This test decides the `MINUTES_ARE_FRAMES` switch in
   `conversions.py`: if it takes 4500 / measured-fps seconds (150 s at 30 fps),
   rules minutes are 900 frames and the switch should become `True` (subject
   to the balance decision in section 5); if it takes 300 s whatever the
   rate, minutes are wall-clock and the switch stays `False`.
3. Optional cross-check: Ivan's time bomb (`IvanTimedDelay=450` frames):
   15 s at 30 fps, 10 s at 45.
4. Optional: repeat test 1 one notch faster ("Faster") to tell 60 / GameSpeed
   (expect 12.5 s at 60 fps) from the 45-FPS table.

Once measured, set `RA2_FRAMES_PER_SECOND` in `conversions.py` and update the
literal expectations in `tests/test_conversions.py` (`test_speed_and_rot`).

## 5. Minutes (open decision)

The brief fixes `minutes_to_ticks(7) == 10500`, i.e. a rules minute is a
wall-clock minute at OpenRA's 25 ticks per second. That is also what the mod
does today (Iron Curtain `RechargeTime=5` -> `ChargeInterval: 7500`; Lightning
Storm 10 -> 15000). The module implements exactly that.

The engine, however, converts a rules minute to 900 frames (section 2). If RA2
runs at 30 frames per second, a "5-minute" recharge lasts 150 real seconds in
RA2 but 300 seconds in the mod. The engine-consistent formula is
`frames_to_ticks(minutes * 900)`, which equals the wall-clock formula only at
15 fps. `conversions.MINUTES_ARE_FRAMES` selects it (default `False`, the
brief's and the mod's behaviour); tests cover both settings. Changing it halves every minute-based timer (superweapons, repair and
reload rates, build time) at 30 fps, so it is a balance decision for the
user, not something to guess here.

## 6. Speed, ROT and GameSpeedBias

- The mod uses Speed x15 and ROT x4 (`GAP_STATS.md`: "Speed x15; ROT x4",
  checked against 42 and 22 units). Neither factor is documented as derived.
  ModEnc gives Speed's internal value as `min((min(Speed,100)*256/100),255)`
  (<https://modenc.renegadeprojects.com/Speed>) and no unit for ROT
  (<https://modenc.renegadeprojects.com/ROT>).
- **Hypothesis, to confirm with the frame-rate measurement:** Speed and ROT
  are per-frame quantities, like ROF. The module scales them by
  `RA2_FRAMES_PER_SECOND / 25`, so with the fallback they stay x15 / x4, and
  when the rate is set every per-frame quantity moves together (at 30 fps:
  Speed x18, ROT x4.8, ROF x25/30). Nothing shows x15 / x4 were calibrated
  against a 1:1 frame mapping; after measuring the rate, time one unit over a
  known distance in RA2 before relying on the scaling.
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
- Percentages: `"50%"` is a percentage; a bare number (`".5"`, `"1"`) is a
  fraction, as in RA2. Input must match `digits[.digits]` or `.digits` with an
  optional `%`; anything else (`1_0`, `1e2`, negatives) is rejected.
  Rounding is half away from zero on exact decimals.
