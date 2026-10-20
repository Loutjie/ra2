import pytest

from ra2data import resolved
from ra2data.resolved import cells, parse_miniyaml


def test_cells():
    assert cells("5c768") == 5.75
    assert cells("8c0") == 8.0
    assert cells("-1c512") == -1.5


def test_parse_miniyaml_nesting_and_suffixes():
    text = (
        "Valued:\n\tCost: 900\n"
        "Armament@PRIMARY:\n\tWeapon: 120mm\n"
        "Warhead@1Dam: SpreadDamage\n\tDamage: 50\n\tVersus:\n\t\tNone: 100\n\t\tHeavy: 80\n"
        "-Removed:\n"
    )
    t = parse_miniyaml(text)
    assert t["Valued"]["Cost"] == "900"
    assert t["Armament@PRIMARY"]["Weapon"] == "120mm"
    assert t["Warhead@1Dam"]["_value"] == "SpreadDamage"
    assert t["Warhead@1Dam"]["Versus"] == {"None": "100", "Heavy": "80"}
    assert "-Removed" in t


def test_cache_invalidates_on_yaml_change(monkeypatch):
    monkeypatch.setattr(resolved, "mods_hash", lambda: "aaa")
    first = resolved.cache_dir()
    monkeypatch.setattr(resolved, "mods_hash", lambda: "bbb")
    assert resolved.cache_dir() != first


@pytest.mark.integration
def test_resolve_actor_htnk():
    t = resolved.resolve_actor("htnk")
    assert t["Valued"]["Cost"] == "900"
    assert t["Health"]["HP"] == "400"
