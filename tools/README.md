# Python data tools

Helpers for generating data and asserting rules for the RA2 mod. Runtime code
uses the standard library only; tests need `pytest`.

- `ra2data.ini` - lenient parser for the original Westwood INI files.
- `ra2data.resolved` - `resolve_actor(name)` / `resolve_weapon(name)` run
  `utility.sh --resolved-rules` / `--resolved-weapons` (`utility.cmd` on
  Windows) from the repo root and parse the MiniYaml into nested dicts.
  Results are cached in `$TMPDIR/ra2-resolved-<hash>/`, keyed on the engine
  version and the paths, mtimes and sizes of `mods/**/*.yaml`.
  `cells("5c768")` converts a distance to cells (5.75).

Run the tests from the repo root with `python3 -m pytest tools -q` (or
`make unit-test`). Tests marked `integration` need a built mod.

## Original game data

The `ra2ini` fixture parses the original `rules.ini` from `~/Projects/ra2-reference/ini/`,
or from the folder named by `$RA2INI`, and skips the test when it is absent.
Game data is never committed to this repository; test fixtures are inline and fictional.
