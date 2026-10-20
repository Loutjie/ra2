"""Checks on the raw sequence/rules YAML for the visual-polish fixes (OpenRA/ra2 #770, #760, #816)."""
from pathlib import Path

from ra2data.resolved import parse_miniyaml

MOD = Path(__file__).resolve().parents[2] / "mods" / "ra2"


def _load(kind, name):
    return parse_miniyaml((MOD / kind / name).read_text(encoding="utf-8-sig"))


def _zoffset(seq, default=0):
    return int(seq.get("ZOffset", default))


def test_pillbox_has_muzzle_overlay_and_sequence():
    gapill = _load("rules", "allied-structures.yaml")["gapill"]
    assert "WithMuzzleOverlay" in gapill
    assert gapill["Armament"]["MuzzleSequence"] == "muzzle"

    seqs = _load("sequences", "allied-structures.yaml")["gapill"]
    assert seqs["Inherits@Muzzle"] == "^WithMachineGunMuzzle"
    muzzle = seqs["muzzle"]
    # Defaults would otherwise give the flash the building's depth map and (0,-15,15) alignment offset.
    assert "DepthSprite" in muzzle and muzzle["DepthSprite"] == {}
    assert muzzle["Offset"].replace(" ", "") == "0,0,30"


def test_pillbox_muzzle_uses_art_flh():
    # art.ini GAPILL PrimaryFireFLH=90,0,40 (256 leptons/cell) -> 4x in OpenRA's 1024/cell
    x, y, z = (int(v) for v in _load("rules", "allied-structures.yaml")["gapill"]["Armament"]["LocalOffset"].split(","))
    assert (x, y, z) == (90 * 4, 0, 40 * 4)


def test_voxel_turret_structures_share_body_zoffset():
    """The sprite base of every voxel-turret defence sits at ZOffset -512 so the turret draws over it."""
    allied = _load("sequences", "allied-structures.yaml")
    soviet = _load("sequences", "soviet-structures.yaml")
    for seqs in (allied["nasam"], allied["gtgcan"], soviet["naflak"], soviet["nalasr"]):
        assert _zoffset(seqs["Defaults"]) == -512
        # the build-up animation is drawn above the (not yet present) turret
        assert _zoffset(seqs["make"]) == 0
