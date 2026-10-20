"""Audio configuration checks: sound names in the mod YAML against the original INI files."""
import re
from pathlib import Path

import pytest

from ra2data import ini

REPO_ROOT = Path(__file__).resolve().parents[2]
MOD = REPO_ROOT / "mods" / "ra2"

SOUND_FILE = "ra2.mix__local.mix__sound.ini"
_LINE = re.compile(r"^(?P<indent>\t*)(?P<key>[^\s:#][^:]*?):(?P<value>.*)$")


def load_yaml(path):
    """Parse a MiniYaml file into a nested dict of {key: (value, children)}."""
    root = {}
    stack = [(-1, root)]
    for raw in path.read_text(encoding="utf-8-sig").splitlines():
        if not raw.strip() or raw.lstrip().startswith("#"):
            continue
        m = _LINE.match(raw)
        if not m:
            continue
        depth = len(m["indent"])
        while stack[-1][0] >= depth:
            stack.pop()
        children = {}
        stack[-1][1][m["key"].strip()] = (m["value"].split("#")[0].strip(), children)
        stack.append((depth, children))
    return root


def rules(name):
    return load_yaml(MOD / "rules" / f"{name}.yaml")


def csv(value):
    return [v.strip() for v in value.split(",") if v.strip()]


@pytest.fixture(scope="module")
def sound_ini(ra2ini):
    """Parsed original sound.ini (skips together with ra2ini when game data is absent)."""
    path = ini.ra2ini_dir() / SOUND_FILE
    if not path.is_file():
        pytest.skip(f"{SOUND_FILE} not found")
    return ini.parse_ini(str(path))


def ini_sounds(sound_ini, section):
    """Lower-cased sample names of a sound.ini section."""
    return [s.lower() for s in sound_ini[section]["Sounds"].split()]


def test_nuke_has_launch_siren(sound_ini):
    siren = [s + ".wav" for s in ini_sounds(sound_ini, "NukeSiren")]
    nuke = rules("soviet-structures")["namisl"][1]["NukePower"][1]
    assert [nuke["LaunchSound"][0]] == siren
    assert [nuke["IncomingSound"][0]] == siren
