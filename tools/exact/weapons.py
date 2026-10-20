"""Exact twin weapons: `<weapon>.exact` with the base RA2 rules.ini values.

A twin weapon inherits its base weapon (art, sounds, effects, projectile type stay) and
overrides the balance values. Names use the mod's defined key casing
(exact.snapshot.weapon_definitions); the RA2 section is the rules.ini section of the same
name, looked up case-insensitively (exact.model.load_weapon).

Mapping (all conversions in exact.conversions)
  ROF (frames)               -> ReloadDelay = frames_to_ticks(ROF)
  Range (cells)              -> Range = cells_to_wdist(Range)
  Burst                      -> Burst
  Speed (leptons per frame)  -> Projectile.Speed = projectile_speed_to_openra(Speed), only when
                                the mod projectile has a Speed field (Bullet, Missile) and the RA2
                                projectile actually flies at that speed: not Inviso (RA2 hits
                                instantly), not Arcing (RA2 derives the arc's speed from the
                                distance) and not Vertical (dropped bombs). Skipped cases are
                                listed as "Not applied" notes; the base speed is kept.
  Damage                     -> Damage of the damage warhead: the single Warhead@* block with
                                a Damage field.
  Warhead Verses (percent)   -> Versus, both in percent, in RA2 order
                                none, flak, plate, light, medium, heavy, wood, steel, concrete,
                                special_1, special_2 -> None, Flak, Plate, Light, Medium, Heavy,
                                Wood, Steel, Concrete, Drone, Rocket.
  CellSpread, PercentAtMax   -> SpreadDamage Spread = cells_to_wdist(CellSpread) and
                                Falloff = 100, PercentAtMax. With two Falloff steps at 0 and
                                Spread, OpenRA interpolates linearly from 100 % at the impact to
                                PercentAtMax at the edge and deals nothing beyond it, which is
                                RA2's linear CellSpread falloff. (OpenRA measures from the
                                victim's HitShape edge, RA2 from its centre.)
                                CellSpread 0 (RA2: only the object hit, no splash) cannot be
                                Spread 0 (OpenRA would deal no damage at all), so it becomes the
                                smallest spread, POINT_SPREAD, with Falloff 100, 100: full damage
                                to what the impact touches, nothing around it.

Exceptions (the twin stays `Inherits: <weapon>` with a comment, so the twin armament still
resolves and keeps the Reimagined values; no RA2 values are invented):
  * no rules.ini section of that name (e.g. a mod-only targeting weapon);
  * RESTRUCTURED: the mod models the mechanic differently (charge/reload split, mind control,
    bombs, latching, beam splitting, erasure), so RA2 numbers do not map field by field;
  * the mod weapon has no damage warhead, several, one that is not SpreadDamage, or explicit
    Range steps (which would override Spread).
RA2 weapons fired by twinned actors that no twin armament names (e.g. Sapper, FakeC4,
MakeupKit, PrismSupport) have no mod counterpart and are listed, not generated.
The exceptions head the generated file; Task 8 collects them into EXCEPTIONS.md.
"""
from __future__ import annotations

from collections.abc import Callable, Iterable, Mapping

from exact import conversions
from exact.model import ARMOR_ORDER, ExactActor, ExactWeapon, load_weapon, load_weapons, parse_bool

from exact.actors import SUFFIX  # noqa: E402  (same suffix as twin actors)

# exact.model.ARMOR_ORDER -> the mod's Versus keys (mods/ra2/weapons/*.yaml).
VERSUS_KEYS = ("None", "Flak", "Plate", "Light", "Medium", "Heavy", "Wood", "Steel", "Concrete",
    "Drone", "Rocket")
assert len(VERSUS_KEYS) == len(ARMOR_ORDER)

# Mod projectile types with a Speed field among those twin weapons use.
SPEED_PROJECTILES = frozenset({"Bullet", "Missile"})

# CellSpread 0: the smallest non-zero spread (1 WDist).
POINT_SPREAD = "0c1"

# Mod weapons whose mechanic is modelled differently, keyed by mod weapon name.
RESTRUCTURED = {
    "PrismShot": "Prism Tower charge/reload split (mod: ReloadDelay 1 plus a charge)",
    "CoilBolt": "Tesla Coil charge/reload split (mod: ReloadDelay 3 plus a charge)",
    "OPCoilBolt": "Tesla Coil charge/reload split (mod: ReloadDelay 3 plus a charge)",
    "Comet": "Prism Tank beam splitting modelled as area damage (mod: Spread 800)",
    "SuperComet": "Prism Tank beam splitting modelled as area damage (mod: Spread 800)",
    "MindControl": "mind control (mod: MindController, no damage)",
    "SuperMindControl": "mind control (mod: MindController, no damage)",
    "IvanBomber": "Crazy Ivan bomb (mod: attached delayed charge with its own falloff)",
    "IvanBomberE": "Crazy Ivan bomb (mod: attached delayed charge with its own falloff)",
    "DroneJump": "Terror Drone latch (mod: jump attack with target damage)",
    "SquidPunch": "Giant Squid grab (mod: restructured melee)",
    "NeutronRifle": "Chrono Legionnaire erasure (mod: separate erase mechanic)",
    "CRNeutronRifle": "Chrono Legionnaire erasure (mod: separate erase mechanic)",
}
_RESTRUCTURED = {k.lower(): v for k, v in RESTRUCTURED.items()}


def _type(node) -> str:
    return (node.get("_value", "") if isinstance(node, Mapping) else node or "").strip()


def damage_warheads(base: Mapping) -> list[str]:
    """Keys of the Warhead@* blocks of a resolved weapon that have a Damage field."""
    return [k for k, v in base.items()
        if k.split("@")[0] == "Warhead" and isinstance(v, Mapping) and "Damage" in v]


def exception_reason(name: str, w: ExactWeapon | None, base: Mapping) -> str | None:
    """Why the twin of mod weapon `name` stays an Inherits stub, or None to generate it."""
    if w is None:
        return "no rules.ini section of that name"
    if name.lower() in _RESTRUCTURED:
        return _RESTRUCTURED[name.lower()]
    found = damage_warheads(base)
    if not found:
        return "no damage warhead in the mod weapon"
    if len(found) > 1:
        return f"{len(found)} damage warheads in the mod weapon"
    warhead = base[found[0]]
    if _type(warhead) != "SpreadDamage":
        return f"damage warhead {found[0]} is {_type(warhead)}, not SpreadDamage"
    if "Range" in warhead:
        return f"damage warhead {found[0]} has explicit Range steps"
    return None


def speed_note(w: ExactWeapon, base: Mapping, projectile: Mapping[str, str] | None) -> str | None:
    """Why the projectile speed is not converted, if the mod projectile has a Speed field."""
    if _type(base.get("Projectile")) not in SPEED_PROJECTILES:
        return None
    flags = projectile or {}
    for flag, why in (("Inviso", "is Inviso (instant hit in RA2)"),
            ("Arcing", "is Arcing (RA2 derives the arc speed from the distance)"),
            ("Vertical", "is Vertical (dropped)")):
        if parse_bool(flags.get(flag)):
            return f"Projectile Speed={w.speed}: RA2 projectile {w.projectile} {why}; base speed kept"
    return None


def render_twin_weapon(w: ExactWeapon, base: dict, *, name: str | None = None,
        projectile: Mapping[str, str] | None = None) -> str:
    """The `<name>.exact` block with w's values (name defaults to w.id).

    base: resolved tree of the mod weapon (its single SpreadDamage damage warhead gets the
    RA2 values; see exception_reason). projectile: the RA2 projectile section (flags decide
    whether Speed applies; see speed_note).
    """
    name = name or w.id
    lines = [f"{name}{SUFFIX}:", f"\tInherits: {name}",
        f"\tReloadDelay: {conversions.frames_to_ticks(w.rof)}",
        f"\tRange: {conversions.cells_to_wdist(w.range)}",
        f"\tBurst: {w.burst}"]
    if _type(base.get("Projectile")) in SPEED_PROJECTILES and speed_note(w, base, projectile) is None:
        lines += [f"\tProjectile: {_type(base['Projectile'])}",
            f"\t\tSpeed: {conversions.projectile_speed_to_openra(w.speed)}"]
    key = damage_warheads(base)[0]
    spread = conversions.cells_to_wdist(w.cell_spread) if w.cell_spread > 0 else POINT_SPREAD
    # The lint's unknown-field check needs the warhead type on the overriding node too.
    lines += [f"\t{key}: {_type(base[key])}", f"\t\tDamage: {w.damage}", f"\t\tSpread: {spread}",
        f"\t\tFalloff: 100, {w.percent_at_max}", "\t\tVersus:"]
    lines += [f"\t\t\t{k}: {v}" for k, v in zip(VERSUS_KEYS, w.verses)]
    return "\n".join(lines) + "\n"


def render_stub(name: str, reason: str) -> str:
    return f"# Exception: {reason}\n{name}{SUFFIX}:\n\tInherits: {name}\n"


def unmatched_ra2_weapons(ra2_weapons: Iterable[str], twin_weapons: Iterable[str]) -> list[str]:
    """RA2 weapon IDs that no twin weapon matches (case-insensitively), sorted."""
    names = {n.lower() for n in twin_weapons}
    return sorted({w for w in ra2_weapons if w.lower() not in names}, key=str.lower)


def render_weapons(rules: dict, names: Iterable[str], twinned: Iterable[ExactActor],
        resolve: Callable[[str], dict]) -> tuple[str, list[tuple[str, str]]]:
    """Twin weapon file body for mod weapons `names`, and its (twin, reason) exceptions.

    twinned: the RA2 actors that get twins. resolve: mod weapon name -> resolved tree (only
    called for weapons with a rules.ini section).
    """
    blocks, notes, exceptions = [], [], []
    for name in sorted(set(names)):
        w = load_weapon(rules, name)
        base = resolve(name) if w is not None else {}
        reason = exception_reason(name, w, base)
        if reason is not None:
            exceptions.append((f"{name}{SUFFIX}", reason))
            blocks.append(render_stub(name, f"{name}{SUFFIX}: {reason}"))
            continue
        projectile = rules.get(w.projectile, {})
        note = speed_note(w, base, projectile)
        if note:
            notes.append(f"{name}{SUFFIX}: {note}")
        blocks.append(render_twin_weapon(w, base, name=name, projectile=projectile))
    unmatched = unmatched_ra2_weapons(load_weapons(rules, list(twinned)), names)
    head = ("# RA2 weapons of twinned actors without a mod counterpart (not generated): "
        + (", ".join(unmatched) if unmatched else "(none)") + "\n"
        + "# Exceptions, kept as Inherits stubs: "
        + (", ".join(t for t, _ in exceptions) if exceptions else "(none)") + "\n"
        + "".join(f"# Not applied: {n}\n" for n in notes))
    return head + "\n" + "\n".join(blocks), exceptions
