"""Tesla Troopers charge a friendly Tesla Coil only when ordered to, as in RA2.

AutoTargetPriority accepts allied targets by default, and the trooper's charge armament may fire at
allies, so on attack-move every trooper stopped at the first friendly coil it passed and charged it
for ever. The menu map collected some 25 troopers around each of two coils in 16 minutes.
"""
from pathlib import Path

from ra2data.resolved import parse_miniyaml

MOD = Path(__file__).resolve().parents[2] / "mods" / "ra2"


def test_tesla_trooper_never_picks_allies_on_its_own():
    shk = parse_miniyaml((MOD / "rules" / "soviet-infantry.yaml").read_text(encoding="utf-8-sig"))["shk"]
    assert shk["Armament@charge"]["TargetRelationships"] == "Ally"
    for priority in ("AutoTargetPriority@DEFAULT", "AutoTargetPriority@ATTACKANYTHING"):
        relationships = {r.strip() for r in shk[priority]["ValidRelationships"].split(",")}
        assert "Ally" not in relationships, priority
        assert "Enemy" in relationships, priority
