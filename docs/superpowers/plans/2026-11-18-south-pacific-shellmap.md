# South Pacific Shellmap Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the blank main-menu map with an endless, scripted Allied-versus-Soviet battle on South Pacific terrain that shows off the iconic RA2 units and superweapons.

**Architecture:** The deliverable is a new map, `mods/ra2/maps/south-pacific-shellmap/`. It copies South Pacific's terrain, places two unkillable bases owned by two scripted players, and runs a Lua script that loops waves of units, a two-minute superweapon cycle and a slowly moving camera. One small C# Lua property gives the script access to the mod's Weather Storm power. `blank-shellmap` loses its `Visibility: Shellmap`, so the engine always picks the new map.

**Tech Stack:** OpenRA MiniYAML (map, rules), Lua map scripting, C# (`OpenRA.Mods.RA2`), Python 3 (one throwaway helper).

**Spec:** `docs/superpowers/specs/2026-11-18-south-pacific-shellmap-design.md`. Read it first: the unit lists, the strike schedule and the "Settled during review" section are binding.

## Global Constraints

- Never copy, commit or redistribute original game files. Only mod files: maps, rules, Lua, C#.
- Commit identity is repo-local `Loutjie Coetzee <lccoetzee@gmail.com>`. End every commit message with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Code style follows `.editorconfig`: tabs, and the StyleCop and IDE rules enforced by `make check`.
- Players are `Allies` (faction `america`, blue) and `Soviets` (faction `russia`, red), enemies of each other, not AI. `Neutral` and `Creeps` stay as they are in South Pacific.
- Superweapons fire every 2 minutes, alternating nuke and Weather Storm, only into the battle zone (the land strip), never inside a base.
- At most about 60 mobile units alive across both sides; husks and wrecks gone within about 30 s.
- World sounds off (`MusicPlaylist: DisableWorldSounds: true`); otherwise music as in `blank-shellmap`.
- Base buildings are targetable but never die.
- Acceptance:
  - 30 minutes left alone on the menu: no slowdown, still busy, no base lost, no pile-up;
  - `make check` and `make test` clean;
  - the user approves it on screen.

## Review Focus

1. **The battle zone is empty when a strike is due.** The strike falls back to the strip's centre cell rather than failing or erroring, and the cycle keeps going. *Test: Task 5, Step 4.*
2. **Units chasing each other into a base.** Strike targets are clamped to the battle-zone rectangle, so nothing lands within 8 cells of a base building. *Test: Task 5, Step 4.*
3. **The Chronoshift destination is water or blocked.** Only cells reported as land, inside the battle zone, are used, and the jump is skipped if none are free. *Test: Task 5, Step 5.*
4. **Large windows and the zoomed-out "Far" setting.** The camera path never shows the map edge at the widest zoom (DefaultScale 0.7) in a 1710×1073 window. *Test: Task 6, Step 3.*
5. **Back to the menu after a game, or hours on the menu.** The shellmap restarts cleanly each time the menu opens, and unit counts stay under the cap after 30 minutes. *Test: Task 6, Step 4.*

---

### Task 1: Map skeleton on the menu

**Files:**
- Create: `mods/ra2/maps/south-pacific-shellmap/map.bin` (copy of `mods/ra2/maps/south-pacific/map.bin`)
- Create: `mods/ra2/maps/south-pacific-shellmap/map.png` (copy of `mods/ra2/maps/south-pacific/map.png`)
- Create: `mods/ra2/maps/south-pacific-shellmap/map.yaml`
- Create: `mods/ra2/maps/south-pacific-shellmap/rules.yaml`
- Create: `mods/ra2/maps/south-pacific-shellmap/shellmap.lua`
- Modify: `mods/ra2/maps/blank-shellmap/map.yaml`: remove the `Visibility: Shellmap` line and set `Visibility: Lobby`; keep `Categories: Shellmap`.

**Interfaces:**
- Produces:
  - players `Allies` and `Soviets`;
  - `shellmap.lua` with global `WorldLoaded` and `Tick` functions, which later tasks fill in;
  - `rules.yaml` with a `World` section that later tasks extend.

- [ ] **Step 1:** Copy terrain and write `map.yaml`. Use South Pacific's header (MapFormat, RequiresMod, Tileset, MapSize, Bounds), with:
  - `Title: South Pacific Shellmap`, `Visibility: Shellmap`, `Categories: Shellmap`;
  - players `Neutral` and `Creeps` copied from South Pacific, plus `PlayerReference@Allies` (`Faction: america`, `Color: 0000FF`, `Enemies: Soviets`) and `PlayerReference@Soviets` (`Faction: russia`, `Color: FF0000`, `Enemies: Allies`);
  - all actors except the `mpspawn` ones;
  - `Rules: rules.yaml`.
- [ ] **Step 2:** Write `rules.yaml`, mirroring the stock RA desert shellmap's rules (quoted in the spec's "Map settings"):
  - `Player:` `-ConquestVictoryConditions`, and `PlayerResources` with zero ore and gem values.
  - `World:` `-CrateSpawner`, `-AutoSave`, `-SpawnStartingUnits`, `-MapStartingLocations`, `-StartGameNotification`; `LuaScript: Scripts: shellmap.lua`; and `MusicPlaylist` copied from `blank-shellmap` plus `DisableWorldSounds: true`.
  - Shroud and fog disabled for the shellmap world.
  - `^Palettes: -MenuPostProcessEffect` if the mod defines it. Check with `grep -rn MenuPostProcessEffect mods/ra2`; if it isn't defined, leave the line out.
- [ ] **Step 3:** `shellmap.lua`: an empty `WorldLoaded` and `Tick`, plus a temporary terrain dump. In `WorldLoaded`, loop over every cell in `Map.Bounds` and `print` `x,y,Map.TerrainType(CPos.New(x, y))` in one line per cell.
- [ ] **Step 4:** Build and check. Copy the engine if it's missing: `cp -cR ../all-round1/engine ./engine`. Then:
  - Run `make all && ./utility.sh --check-yaml`. Expected: build succeeded, no errors, and the new map appears under "Testing map".
  - Launch `./launch-game.sh`. Expected: the menu shows South Pacific terrain with no sound effects.
  - Quit, then check `~/Library/Application Support/OpenRA/Logs/lua.log`. Expected: it contains the terrain dump.
- [ ] **Step 5:** Save the dump outside the repo, in the scratchpad (for example `terrain.csv`), for Tasks 3–6. Remove the dump code from `shellmap.lua`.
- [ ] **Step 6:** Commit. Message: `Add a South Pacific shellmap skeleton`.

### Task 2: Weather Storm from Lua

**Files:**
- Create: `OpenRA.Mods.RA2/Scripting/WeatherControlProperties.cs`

**Interfaces:**
- Produces: the Lua actor method `ActivateWeatherControl(CPos target)`, available on any actor with `WeatherControlSupportPower`, such as `gaweat`.

- [ ] **Step 1:** Implement `WeatherControlProperties : ScriptActorProperties, Requires<WeatherControlSupportPowerInfo>`, modelled on the engine's `OpenRA.Mods.Common/Scripting/Properties/NukeProperties.cs`:
  - `[ScriptPropertyGroup("Support Powers")]` and a `[Desc]` on the method.
  - `public void ActivateWeatherControl(CPos target)` builds `new Order(power.Info.OrderName, Self, Target.FromCell(Self.World, target), false)`, then calls `power.Activate(Self, order, Self.Owner.PlayerActor.Trait<SupportPowerManager>())`.
  - Then it notifies `INotifySupportPower.Activated`, as `NukeProperties` does.
  - No charge check: it fires regardless of charge, as the spec requires.
- [ ] **Step 2:** Run `make all && make check`. Expected: 0 errors.
- [ ] **Step 3:** Verify in game. Temporarily add a `gaweat` owned by `Allies` and call `ActivateWeatherControl` on a strip cell 5 s after load. Expected: storm clouds and lightning on that cell in the menu. Remove the temporary call.
- [ ] **Step 4:** Commit. Message: `Let Lua fire the Weather Storm`.

### Task 3: Bases

**Files:**
- Modify: `mods/ra2/maps/south-pacific-shellmap/map.yaml` (Actors)
- Modify: `mods/ra2/maps/south-pacific-shellmap/rules.yaml`

**Interfaces:**
- Consumes: the `terrain.csv` dump from Task 1.
- Produces: named actors that Tasks 4–5 reference by name:

  | Allies | Soviets | Purpose |
  |---|---|---|
  | `AlliedFactory` (`gaweap`) | `SovietFactory` (`naweap`) | ground unit entry |
  | `AlliedBarracks` (`gapile`) | `SovietBarracks` (`nahand`) | infantry entry |
  | `AlliedYard` (`gayard`) | `SovietYard` (`nayard`) | naval entry, if its water connects |
  | `AlliedAirfield` (`gaairc`) | `SovietPad` (the mod's Kirov pad) | air entry |
  | `WeatherDevice` (`gaweat`), `Chronosphere` (`gacsph`) | `NukeSilo` (`namisl`), `IronCurtain` (`nairon`) | superweapons |

  Also the waypoints `BattleZoneTopLeft` and `BattleZoneBottomRight` (the strip rectangle), `StripCenter`, and `AlliedSeaEntry` / `SovietSeaEntry` when the shipyards' water doesn't connect.

- [ ] **Step 1:** Using `terrain.csv`, mark on an ASCII map:
  - each island's flat land for a base;
  - the land strip, which is the battle zone;
  - where both shipyards can reach the same water.

  Choose base positions at least 12 cells from the strip.
- [ ] **Step 2:** Place both bases: construction yard, power, barracks, war factory, shipyard, airfield or pad, 2–4 defences, and the superweapons from the table above. Add the waypoints.
- [ ] **Step 3:** In `rules.yaml`, make every base building unkillable with the stock pattern: on the building templates used, `DamageMultiplier@UNKILLABLE: Modifier: 0`. Walls and bridges get `DamageMultiplier: Modifier: 0`. Mobile units stay killable.
- [ ] **Step 4:** Run `./utility.sh --check-yaml` and launch. Expected:
  - no lint errors;
  - both bases visible and intact, with no actor stuck on water or cliffs. Take a Ctrl+P screenshot of each base.
- [ ] **Step 5:** Commit. Message: `Place the shellmap bases`.

### Task 4: Waves

**Files:**
- Modify: `mods/ra2/maps/south-pacific-shellmap/shellmap.lua`
- Modify: `mods/ra2/maps/south-pacific-shellmap/rules.yaml` (husk lifetime)

**Interfaces:**
- Consumes: the named actors and waypoints from Task 3.
- Produces: `AliveUnits(player) -> int`. Task 5 uses it to read the battle state.

- [ ] **Step 1:** Ground waves. Every 30–45 s, randomised with `Utils.RandomInteger`, each side sends a mixed wave via `Reinforcements.Reinforce` from its factory and barracks, then `AttackMove` to `StripCenter`:
  - **Allies:** `sref`, `mgtk`, `mtnk`, `fv`, `e1`.
  - **Soviets:** `htnk`, `apoc`, `ttnk`, `shk`, `e2`, plus a `v3` every other wave.

  A wave is skipped while that side's `AliveUnits` is above 30.
- [ ] **Step 2:** Air.
  - About every 90 s, a `zep` leaves `SovietPad` and attack-moves towards `AlliedFactory`.
  - Every 60–90 s, an `orca` or `beag` strafes the strip, then is removed when idle or out of ammunition.
- [ ] **Step 3:** Sea. Keep 2 `dred` (Soviets) and 2 `aegis` + 1 `dest` (Allies) alive. Top up when one is lost, from the shipyards or from the `*SeaEntry` waypoints.
- [ ] **Step 4:** Husks. In `rules.yaml`, give the husk templates the mod uses (check `^Husk` and its descendants in `mods/ra2/rules/defaults.yaml`) a lifetime of 750 ticks, about 30 s, or remove them on creation if the mod has no lifetime trait.
- [ ] **Step 5:** Launch and watch 5 minutes. Expected:
  - waves meet on the strip and fight;
  - ships fight at sea;
  - a Kirov crosses;
  - no errors in `lua.log`.
- [ ] **Step 6:** Commit. Message: `Send waves of signature units into the shellmap battle`.

### Task 5: Superweapon cycle

**Files:**
- Modify: `mods/ra2/maps/south-pacific-shellmap/shellmap.lua`

**Interfaces:**
- Consumes:
  - `ActivateWeatherControl` (Task 2);
  - `ActivateNukePower`, `Chronoshift`, `GrantCondition` (engine Lua);
  - the named actors from Task 3;
  - `AliveUnits` from Task 4.
- Produces: `StrikeTarget() -> CPos`, always inside the battle zone.

- [ ] **Step 1:** `StrikeTarget()` returns the battle-zone cell with the most units within 3 cells, considering only units inside the rectangle from `BattleZoneTopLeft` to `BattleZoneBottomRight`. If there are none, it returns `StripCenter.Location`.
- [ ] **Step 2:** The schedule, repeating every 4 minutes from load (spec table):

  | Time in cycle | Event |
  |---|---|
  | 0:30 | Chronoshift up to 4 Allied units onto free land cells in the battle zone |
  | 2:00 | `NukeSilo.ActivateNukePower(StrikeTarget())` |
  | 3:00 | `GrantCondition("invulnerability", 500)` on up to 4 Soviet tanks in the battle zone |
  | 4:00 | `WeatherDevice.ActivateWeatherControl(StrikeTarget())` |

  Use `Trigger.AfterDelay(DateTime.Minutes(n))`, re-armed each cycle.
- [ ] **Step 3:** Run `make check-scripts`, or `luac -p` on the file. Expected: no syntax errors.
- [ ] **Step 4:** Shorten the cycle to 20 s temporarily, with an empty battle zone at the first strike. Expected:
  - the strike lands on `StripCenter`;
  - every strike lands inside the rectangle, and none within 8 cells of a base building (check with screenshots);
  - the cycle continues. Then restore the 4-minute timings.
- [ ] **Step 5:** With the same short cycle, force a Chronoshift when the only candidate cells are water or occupied. Expected: the jump is skipped and there's no error in `lua.log`.
- [ ] **Step 6:** Commit. Message: `Fire the superweapon cycle in the shellmap`.

### Task 6: Camera, soak and hand-over

**Files:**
- Modify: `mods/ra2/maps/south-pacific-shellmap/shellmap.lua`

**Interfaces:**
- Consumes: `StripCenter`, plus the strike schedule from Task 5.

- [ ] **Step 1:** In `Tick`, move `Camera.Position` on a slow ellipse around `StripCenter`.
  - The radii are chosen so the strip, some sea and part of each base pass through view.
  - Speed is about 3° per second, like the engine's `CameraOvalMover`.
  - Five seconds before each strike, ease the camera to the strike cell, and resume the ellipse 10 s after it.
  - Use Lua, not the `CameraOvalMover` trait, which centres on the viewport's starting position.
- [ ] **Step 2:** Launch and watch one full 4-minute cycle. Expected: all four events happen on screen and the camera movement is smooth.
- [ ] **Step 3:** Set the in-game zoom to its widest setting in a 1710×1073 window. Expected: no black map edge is visible at any point on the ellipse. If it is, shrink the radii.
- [ ] **Step 4:** Soak test: leave the menu alone for 30 minutes, checking the alive-unit count in `lua.log` (log `AliveUnits` for both sides every minute). Then start a skirmish, quit to the menu and confirm the shellmap restarts. Expected:
  - the unit counts stay at or below the cap;
  - the bases are intact;
  - no slowdown;
  - no errors.

  Remove the per-minute logging afterwards.
- [ ] **Step 5:** Run `make check && make test`. Expected: 0 errors.
- [ ] **Step 6:** Commit. Message: `Move the shellmap camera with the battle`. Then ask the user to review it on screen.
