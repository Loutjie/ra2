"""The ore growth rules in world.yaml mirror the original rules.ini."""
import re
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
WORLD_YAML = REPO_ROOT / "mods" / "ra2" / "rules" / "world.yaml"

TICKS_PER_SECOND = 25  # Timestep 40 ms


def _growth_traits():
    """{ResourceType: {field: int}} for each ResourceGrowth@ trait of the World actor."""
    result = {}
    actor = trait = None
    fields = {}
    for raw in WORLD_YAML.read_text(encoding="utf-8-sig").splitlines():
        if not raw.strip() or raw.lstrip().startswith("#"):
            continue
        depth = len(raw) - len(raw.lstrip("\t"))
        key, _, value = raw.strip().partition(":")
        value = value.split("#")[0].strip()
        if depth == 0:
            actor, trait = key, None
        elif depth == 1:
            trait = key if actor == "World" and key.startswith("ResourceGrowth") else None
            fields = {}
        elif depth == 2 and trait:
            fields[key] = value
            if "ResourceType" in fields:
                result[fields["ResourceType"]] = fields
    return result


def _per_mille(text):
    return round(float(text) * 1000)


def test_values_match_rules_ini(ra2ini):
    growth_minutes = int(re.match(r"\s*(\d+)", ra2ini["General"]["GrowthRate"]).group(1))
    interval = growth_minutes * 60 * TICKS_PER_SECOND
    # Ore is Tiberium Riparius, Gems is Tiberium Cruentus.
    expected = {"Ore": "Riparius", "Gems": "Cruentus"}
    traits = _growth_traits()
    assert set(traits) == set(expected)
    for resource, section in expected.items():
        t = traits[resource]
        assert int(t["Interval"]) == interval, resource
        assert int(t["GrowthChance"]) == _per_mille(ra2ini[section]["GrowthPercentage"].split(";")[0]), resource
        assert int(t["SpreadChance"]) == _per_mille(ra2ini[section]["SpreadPercentage"].split(";")[0]), resource
