# South Pacific shellmap

The live battle running behind the RA2 mod's main menu.

## Intent

The menu currently runs `blank-shellmap`, which is empty terrain, and the user finds it boring. The new
shellmap should **show off the iconic RA2 units and superweapons**: an endless Allied-versus-Soviet battle in
the style of stock OpenRA Red Alert's `desert-shellmap`, watched while browsing the menus.

## Goals

- Each side shows its signature units fighting:
  - **Allied:** Prism Tank (`sref`), Mirage Tank (`mgtk`), Grizzly (`mtnk`), IFV (`fv`), GIs (`e1`),
    Harrier (`orca`), Black Eagle (`beag`), Aegis cruiser (`aegis`) and destroyer (`dest`) at sea.
  - **Soviet:** Rhino (`htnk`), Apocalypse (`apoc`), Tesla Tank (`ttnk`), Tesla Troopers (`shk`), Conscripts
    (`e2`), Kirov (`zep`), V3 (`v3`), Dreadnought (`dred`) at sea.
- **Superweapons fire every two minutes**, alternating the Soviet nuke (`namisl`) and the Allied Weather Storm
  (`gaweat`), aimed at units fighting in the middle, never at a base.
- The Chronosphere teleports an Allied group into the fight, and the Iron Curtain makes a Soviet group
  invulnerable for a short time, in the gaps between strikes.
- It runs forever: neither side wins, and the bases stay standing.

## Non-goals

- Not a playable map or mission; no player input.
- No AI players: everything is scripted.
- No new art, sounds or terrain painting.
- Not replacing `blank-shellmap` by deletion. It stays in the repo; only its `Visibility: Shellmap` is
  removed, so the engine always picks the new map.

## Anti-goals

These would make it a failure even if it technically works:

- Slowing the menus down, for example from hundreds of units or effects piling up.
- A base or superweapon being destroyed, or the battle stalling with nothing on screen.
- Superweapons hitting a base, or firing so often it becomes noise.
- Battle sounds behind the menu: only music plays (`MusicPlaylist: DisableWorldSounds: true`, as stock does).

## Constraints

- Terrain comes from the existing **South Pacific** map (`mods/ra2/maps/south-pacific`, 105×190, temperate):
  two large islands joined by a land strip in the middle, with open sea around them. The new map copies its
  `map.bin` and `map.png`; its 339 existing actors (spawns, trees, tech buildings) are kept or trimmed as needed.
- Original game assets are never copied into the repo. Only mod files are used (maps, rules, Lua).
- Superweapons are fired from Lua:
  - **Nuke:** has Lua support (`ActivateNukePower(cpos)`).
  - **Chronosphere:** has Lua support (`Chronoshift`).
  - **Weather Storm:** the RA2 mod's own `WeatherControlSupportPower` has none, so this needs a small new Lua
    property in `OpenRA.Mods.RA2`, modelled on `NukeProperties`.
  - **Iron Curtain:** done by granting its invulnerability condition from Lua, with the same visual as the power.
- The battle must hold up for hours: unit counts are capped and husks and debris don't pile up indefinitely.

## Approaches considered

1. **Build on the superweapon testbed (Little Big Lake terrain).** Bases and superweapons already exist and it's
   the fastest route, but the lake is too small for a naval battle.
2. **A larger coastal map, South Pacific (chosen).** Room for a land fight on the strip, a sea battle and air
   attacks crossing over. Bases and defences have to be laid out from scratch, and the action is more spread
   out. The user picked this for the naval scene.
3. **Two AI players.** The least scripting, but the user can't control which units appear; bases die and the AI
   fires superweapons at bases.

## The design

### Layout

- **Bases:** an Allied base on one island and a Soviet base on the other, each with a construction yard,
  power, a barracks, a war factory, a shipyard, an airfield or helipad (Allied) or Kirov pad (Soviet),
  defences, and its superweapons:
  - **Allied:** Weather Control Device and Chronosphere.
  - **Soviet:** Nuclear Missile Silo and Iron Curtain.
- **Bases can't be destroyed.** Every base building is invulnerable, through the stock shellmap's
  `unkillable` condition / `DamageMultiplier` pattern, so they can be hit but never fall.
- **The battle zone** is the land strip in the middle. Units meet there and fight.
- **The sea:** naval units fight in the water next to the strip, where the camera passes over them.

### Waves

- Each side sends a mixed wave of its signature ground units from its war factory and barracks area towards
  the strip every 30–45 seconds, using `Reinforcements.Reinforce`, and sends them on an attack-move.
- **Air:** a Kirov leaves its pad and drifts towards the enemy base roughly every 90 seconds. Harriers and
  Black Eagles make strafing runs on the strip.
- **Sea:** Dreadnoughts versus Aegis cruisers and destroyers, topped up when one is lost.
- **Caps:** a new wave is skipped while a side already has more than a set number of units alive, so units
  can't pile up.

### Superweapons

Each strike targets the cell in the battle zone with the most units, which is never inside a base.

| Time | Event |
|---|---|
| 0:30 | first Chronosphere jump: an Allied group appears at the strip |
| 2:00 | nuke on the strip |
| 3:00 | Iron Curtain on a group of Soviet tanks |
| 4:00 | Weather Storm on the strip |
| repeating | the cycle repeats every four minutes: a strike every two minutes, alternating, with a Chronosphere jump or Iron Curtain in between |

### Camera

- Slow and continuous, using the engine's `CameraOvalMover` like stock does, centred on the land strip so
  the fight, the sea and the strikes pass through view.
- The camera never shows the map edge.
- Its position is set so the strike area is on screen when a strike lands.

### Sound and music

- `DisableWorldSounds: true`.
- Menu music is kept as it is now: the same starting track as `blank-shellmap`.

### Map settings

Following the stock shellmap's `rules.yaml`:
- no victory conditions, crates, autosave, starting units or start locations;
- no ore income;
- experience off;
- bridges and walls can't be destroyed;
- the `MenuPostProcessEffect` palette is removed.

Fog and shroud are off for the shellmap world.

## Settled during review

These are the designer's calls, made without asking the user; correct any of them.

- **Owners.** Two non-playable map players own everything:
  - `Allies`: faction `america`, colour blue.
  - `Soviets`: faction `russia`, colour red.

  Neither is an AI, and both are each other's enemies. `Neutral` and `Creeps` stay as in South Pacific.
- **The superweapon buildings are real and fire from Lua without charging.** `ActivateNukePower` fires
  regardless of charge. The Weather Storm Lua property does the same. Chronoshift is called on the Chronosphere
  building. The powers' own charge timers and ready notifications are irrelevant, because there is no player UI
  and sounds are off.
- **The bases are targetable but unkillable**, using the stock `unkillable` pattern applied to every base building
  through map rules. Units shooting at them is fine; it reads as a battle. Mobile units, naval units and aircraft
  can die and are replaced by the next wave.
- **Naval and air units.** The builder confirms in the map editor that both shipyards' water connects past the
  strip. If it doesn't, ships are placed in the shared water directly instead of coming from the shipyards.
  Aircraft don't rearm: they fly their attack and are replaced when lost or out of ammunition. Kirovs keep
  flying until shot down.
- **Performance.**
  - No more than about 60 mobile units alive across both sides; waves are skipped above each side's cap.
  - Husks and wrecks disappear within about 30 seconds.
  - The acceptance run is 30 minutes left alone on the menu: no slowdown, the battle still busy, no base lost,
    no growing pile of units or effects.
- **What counts as done.**
  - The user watches it on the menu and approves it.
  - The 30-minute soak passes.
  - The YAML and Lua checks are clean (`make test`).
  - The Weather Storm Lua property gets a unit test only if its logic goes beyond calling the power, which it
    shouldn't.
- **Iron Curtain:** grant the same external condition the Iron Curtain power grants, which brings its tint and
  invulnerability. That's enough; no separate effect actor. **Chronosphere:** use the real `Chronoshift` call,
  which brings its own effect.
- **Music:** copy `blank-shellmap`'s `MusicPlaylist` block as is, adding `DisableWorldSounds: true`.

## Left to the builder

- Exact building placement, defence layout, wave contents and sizes, unit caps, and the camera oval size.
  Tune them so the battle looks busy and stays smooth.
- Whether to trim South Pacific's existing tech buildings and trees, and which.
- How husks and wrecks are cleaned up if they pile up.
- Where the Weather Storm Lua property lives and how it is tested.
