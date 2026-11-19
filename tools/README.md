# Python data tools

Helpers for generating data and asserting rules for the RA2 mod. Runtime code
uses the standard library only; tests need `pytest`.

- `ra2data.ini` - lenient parser for the original Westwood INI files.
- `ra2data.resolved` - `resolve_actor(name)` / `resolve_weapon(name)` run
  `dotnet engine/bin/OpenRA.Utility.dll <MOD_ID> --resolved-rules|--resolved-weapons`
  directly (the same on every platform; `dotnet` must be on PATH and the engine
  built) and parse the MiniYaml into nested dicts. Each value is a string
  (leaf), an empty dict (no value, no children) or a dict; a node with both a
  value and children stores the value under `"_value"`.
  Results are cached in `$TMPDIR/ra2-resolved-<hash>/`, keyed on the engine
  version, the mod dll and the paths, mtimes and sizes of `mods/**/*.yaml`;
  file names are a sanitised prefix plus a sha1 of the exact name. Empty
  output is an error and is never cached.
  `cells("5c768")` converts a distance to cells (5.75).

Run the tests from the repo root with `python3 -m pytest tools -q` (or
`make unit-test`). Tests marked `integration` need a built mod; run `python3 -m pytest tools -q -m "not integration"` without one. Tests run with `--import-mode=importlib`, so test files in different `tools/*/tests/` folders may share names. The tools support Python 3.9+.

## Original game data

The `ra2ini` fixture parses the original `rules.ini` from `~/Projects/ra2-reference/ini/`,
or from the folder named by `$RA2INI`, and skips the test when it is absent.
Game data is never committed to this repository; test fixtures are inline and fictional.

## Exact balance

`exact` generates the Exact balance mode from the original `rules.ini`; see
[`exact/README.md`](exact/README.md).
