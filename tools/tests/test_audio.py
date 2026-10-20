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


@pytest.mark.parametrize("actor, section, voiceset, multiplier", [
    ("asw", "OspreyDie", "OspreyVoice", 0.5),
    ("hornet", "HornetDie", "HornetVoice", 0.6),
])
def test_osprey_and_hornet_have_death_sounds(sound_ini, actor, section, voiceset, multiplier):
    traits = rules("aircraft")[actor][1]
    assert traits["Voiced"][1]["VoiceSet"][0] == voiceset
    assert "DeathSounds" in traits
    assert "-DeathSounds" not in traits
    assert float(traits["DeathSounds"][1]["VolumeMultiplier"][0]) == multiplier

    voices = load_yaml(MOD / "audio" / "voices.yaml")[voiceset][1]
    assert voices["DefaultVariant"][0] == ".wav"
    assert csv(voices["Voices"][1]["Die"][0]) == ini_sounds(sound_ini, section)
    assert multiplier == int(sound_ini[section]["Volume"]) / 100


def test_cash_tick_volume_lowered():
    sounds = load_yaml(MOD / "audio" / "notifications.yaml")["Sounds"][1]["Notifications"][1]
    for name in ("CashTickUp", "CashTickDown"):
        volume = float(sounds[name][1]["VolumeModifier"][0])
        assert 0 < volume < 1.0, name
    assert float(sounds["CashTickUp"][1]["VolumeModifier"][0]) == 0.6


def _descendants(root):
    """Names of all actors/templates whose Inherits chain reaches `root`."""
    parents = {}
    for path in sorted((MOD / "rules").glob("*.yaml")):
        for name, (_, traits) in load_yaml(path).items():
            parents[name] = [v for k, (v, _) in traits.items() if k == "Inherits" or k.startswith("Inherits@")]
    found = set()
    changed = True
    while changed:
        changed = False
        for name, ps in parents.items():
            if name not in found and any(p == root or p in found for p in ps):
                found.add(name)
                changed = True
    return found


def test_infantry_death_sounds_from_template():
    defaults = rules("defaults")
    infantry = defaults["^Infantry"][1]
    inherited = [v for k, (v, _) in infantry.items() if k.startswith("Inherits")]
    assert "^InfantryDeathSounds" in inherited

    # The template is the only place that configures infantry death sounds.
    template = defaults["^InfantryDeathSounds"][1]
    assert not [k for k in infantry if k.startswith("DeathSounds")]
    by_voice = {}
    for key, (_, trait) in template.items():
        assert key.startswith("DeathSounds"), key
        voice = trait["Voice"][0] if "Voice" in trait else "Die"
        by_voice.setdefault(voice, set()).update(csv(trait["DeathTypes"][0]))
    assert by_voice["Die"] >= {"ExplosionDeath", "BulletDeath", "FlameDeath"}
    assert by_voice["Zapped"] == {"ElectroDeath"}
    assert by_voice["Melted"] == {"RadiationDeath"}
    assert by_voice["PsyCrush"] == {"PsychicDeath"}

    # No infantry actor removes or replaces the template.
    for path in sorted((MOD / "rules").glob("*.yaml")):
        actors = load_yaml(path)
        for name in _descendants("^Infantry") & actors.keys():
            assert "-DeathSounds" not in actors[name][1], name


def test_voices_have_no_placeholder_burned_sound():
    death = load_yaml(MOD / "audio" / "voices.yaml")["^InfantryDeath"][1]["Voices"][1]
    assert "Burned" not in death
    assert "Burned" not in csv(death["DisablePrefixes"][0])
    assert "PsyCrush" in death
