"""Every light-source building of the original rules.ini must exist in the mod."""
import pytest

from ra2data.resolved import cells, resolve_actor

TINTS = {
    "LightIntensity": "Intensity",
    "LightRedTint": "RedTint",
    "LightGreenTint": "GreenTint",
    "LightBlueTint": "BlueTint",
}


def _number(text):
    # Some original values use "," as the decimal separator (e.g. "0,01").
    return float(text.replace(",", "."))


def _lamps(ra2ini):
    return [name for name in ra2ini["BuildingTypes"].values() if "LightIntensity" in ra2ini.get(name, {})]


@pytest.mark.integration
def test_every_ra2_lamp_has_light_source(ra2ini):
    lamps = _lamps(ra2ini)
    assert lamps, "no lamps found in rules.ini"
    problems = []
    for lamp in lamps:
        section = ra2ini[lamp]
        # The mod names actors in either case (e.g. galite, INGALITE); lookup is case-sensitive here.
        try:
            actor = resolve_actor(lamp)
        except Exception:
            try:
                actor = resolve_actor(lamp.lower())
            except Exception:
                problems.append(f"{lamp}: missing from the mod")
                continue
        light = actor.get("TerrainLightSource")
        if light is None:
            problems.append(f"{lamp}: no TerrainLightSource")
            continue
        if abs(cells(light["Range"]) * 1024 - int(section["LightVisibility"]) * 4) > 1:
            problems.append(f"{lamp}: Range {light['Range']} != {section['LightVisibility']} leptons * 4")
        for ini_key, field in TINTS.items():
            if abs(_number(light[field]) - _number(section[ini_key])) > 1e-6:
                problems.append(f"{lamp}: {field} {light[field]} != {section[ini_key]}")
    assert not problems, "\n".join(problems)
