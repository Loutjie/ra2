"""Exact twin weapons: `<weapon>.exact` with the base RA2 rules.ini values.

A twin weapon inherits its base weapon (art, sounds, effects, projectile type stay) and
overrides the balance values. Names use the mod's defined key casing
(exact.snapshot.weapon_definitions); the RA2 section is the rules.ini section of the same
name, looked up case-insensitively (exact.model.load_weapon).

Mapping (all conversions in exact.conversions)
  ROF (frames)               -> ReloadDelay = frames_to_ticks(ROF)
  Range (cells)              -> Range = cells_to_wdist(Range)
  Burst                      -> Burst
  Speed (leptons per frame)  -> Projectile.Speed = projectile_speed_to_openra(Speed), when the
                                mod projectile has a Speed field (Bullet, Missile). Arcing RA2
                                projectiles ignore the weapon's Speed= and fly at a fixed
                                ARCING_SPEED of 50 (ModEnc, "Arcing"), so 50 is converted.
                                Inviso (instant hit in RA2) and Vertical (dropped) projectiles
                                have no flight speed to convert: the base speed is kept. Every
                                Arcing/Inviso/Vertical case is listed as a "# Speed:" note.
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
  * a same-named section that no twinned RA2 actor fires (it belongs to another RA2 unit,
    e.g. MP5 while the Chrono Commando fires ChronoMP5);
  * RESTRUCTURED: the mod models the mechanic differently (charge/reload split, mind control,
    bombs, latching, beam splitting, erasure), so RA2 numbers do not map field by field;
  * the mod weapon has no damage warhead, several, one that is not SpreadDamage, or explicit
    Range steps (which would override Spread).
RA2 weapons fired by twinned actors that no twin armament names (e.g. Sapper, FakeC4,
MakeupKit, PrismSupport) have no mod counterpart and are listed, not generated.
RA2_COUNTERPARTS hints at the RA2 weapon a renamed mod weapon stands in for; the pairs are
linked both on the exception and in that list.
The exceptions head the generated file and render_weapons returns them as a report, which
exact.generate writes to tools/exact/weapon_exceptions.json for EXCEPTIONS.md (Task 8).
"""
from __future__ import annotations

from collections.abc import Callable, Collection, Iterable, Mapping

from exact import conversions
from exact.actors import SUFFIX
from exact.model import ARMOR_ORDER, ExactActor, ExactWeapon, load_weapon, load_weapons, parse_bool

# exact.model.ARMOR_ORDER -> the mod's Versus keys (mods/ra2/weapons/*.yaml).
VERSUS_KEYS = ("None", "Flak", "Plate", "Light", "Medium", "Heavy", "Wood", "Steel", "Concrete",
    "Drone", "Rocket")
if len(VERSUS_KEYS) != len(ARMOR_ORDER):
    raise RuntimeError("VERSUS_KEYS must map every exact.model.ARMOR_ORDER entry")

# Mod projectile types with a Speed field among those twin weapons use.
SPEED_PROJECTILES = frozenset({"Bullet", "Missile"})

# ModEnc, "Arcing": Arcing=yes projectiles fly at a fixed 50 leptons per frame; the
# weapon's Speed= is ignored.
ARCING_SPEED = 50

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

# Mod weapon -> the RA2 weapon(s) it stands in for under another name (hints for
# EXCEPTIONS.md; the mod weapon's own name is not fired by the twinned RA2 actor, or its
# mechanic is restructured).
RA2_COUNTERPARTS = {
    "MP5": "ChronoMP5",
    "MP5E": "ChronoMP5E",
    "DogJaw": "BadTeeth",
    "DemoTruckTargeting": "Demobomb, TerrorBomb",
    "SquidPunch": "SquidGrab",
}
_COUNTERPARTS = {k.lower(): v for k, v in RA2_COUNTERPARTS.items()}

NOT_FIRED = "rules.ini section not fired by the twinned RA2 actor"


def _type(node) -> str:
    return (node.get("_value", "") if isinstance(node, Mapping) else node or "").strip()


def damage_warheads(base: Mapping) -> list[str]:
    """Keys of the Warhead@* blocks of a resolved weapon that have a Damage field."""
    return [k for k, v in base.items()
        if k.split("@")[0] == "Warhead" and isinstance(v, Mapping) and "Damage" in v]


def exception_reason(name: str, w: ExactWeapon | None, base: Mapping,
        fired: Collection[str] | None = None) -> str | None:
    """Why the twin of mod weapon `name` stays an Inherits stub, or None to generate it.

    fired: lowercase IDs of the weapons the twinned RA2 actors fire (None: not checked).
    A same-named section that none of them fires belongs to another RA2 unit.
    """
    if w is None:
        return "no rules.ini section of that name"
    if fired is not None and name.lower() not in fired:
        return NOT_FIRED
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


def ra2_projectile_speed(w: ExactWeapon, projectile: Mapping[str, str] | None
        ) -> tuple[int | None, str | None]:
    """(RA2 speed in leptons per frame to convert, or None to keep the base speed; a note).

    Inviso projectiles hit instantly and Vertical ones drop, so their weapon Speed= has no
    OpenRA counterpart; Arcing projectiles fly at ARCING_SPEED whatever the weapon says.
    """
    flags = projectile or {}
    if parse_bool(flags.get("Inviso")):
        return None, (f"Projectile Speed={w.speed}: RA2 projectile {w.projectile} is Inviso "
            "(instant hit in RA2); base speed kept")
    if parse_bool(flags.get("Vertical")):
        return None, (f"Projectile Speed={w.speed}: RA2 projectile {w.projectile} is Vertical "
            "(dropped); base speed kept")
    if parse_bool(flags.get("Arcing")):
        return ARCING_SPEED, (f"Projectile Speed={w.speed}: RA2 projectile {w.projectile} is "
            f"Arcing, which flies at a fixed {ARCING_SPEED} (ModEnc); {ARCING_SPEED} used")
    return w.speed, None


def render_twin_weapon(w: ExactWeapon, base: dict, *, name: str | None = None,
        projectile: Mapping[str, str] | None = None) -> str:
    """The `<name>.exact` block with w's values (name defaults to w.id).

    base: resolved tree of the mod weapon (its single SpreadDamage damage warhead gets the
    RA2 values; see exception_reason). projectile: the RA2 projectile section (flags decide
    the speed; see ra2_projectile_speed).
    """
    name = name or w.id
    lines = [f"{name}{SUFFIX}:", f"\tInherits: {name}",
        f"\tReloadDelay: {conversions.frames_to_ticks(w.rof)}",
        f"\tRange: {conversions.cells_to_wdist(w.range)}",
        f"\tBurst: {w.burst}"]
    speed, _ = ra2_projectile_speed(w, projectile)
    if _type(base.get("Projectile")) in SPEED_PROJECTILES and speed is not None:
        lines += [f"\tProjectile: {_type(base['Projectile'])}",
            f"\t\tSpeed: {conversions.projectile_speed_to_openra(speed)}"]
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


def _mod_twins_for(ra2_weapon: str, names: Iterable[str]) -> list[str]:
    """Twins whose RA2_COUNTERPARTS hint names `ra2_weapon`."""
    return sorted(f"{n}{SUFFIX}" for n in names
        if ra2_weapon.lower() in (p.strip().lower() for p in _COUNTERPARTS.get(n.lower(), "").split(",")))


def render_weapons(rules: dict, names: Iterable[str], twinned: Iterable[ExactActor],
        resolve: Callable[[str], dict]) -> tuple[str, dict]:
    """Twin weapon file body for mod weapons `names`, and a machine-readable report.

    twinned: the RA2 actors that get twins. resolve: mod weapon name -> resolved tree (only
    called for weapons the twinned actors fire). The report has
      "exceptions":   [{"twin", "reason", "ra2_counterpart"?}] (Inherits stubs),
      "ra2_without_mod_counterpart": [{"weapon", "mod_twins"?}],
      "notes":        [{"twin", "note"}] (projectile speed decisions).
    """
    names = sorted(set(names))
    fired_ids = list(load_weapons(rules, list(twinned)))
    fired = {k.lower() for k in fired_ids}
    blocks, exceptions, notes = [], [], []
    for name in names:
        twin_id = f"{name}{SUFFIX}"
        w = load_weapon(rules, name)
        known = w is not None and name.lower() in fired
        base = resolve(name) if known else {}
        reason = exception_reason(name, w, base, fired)
        if reason is not None:
            entry = {"twin": twin_id, "reason": reason}
            hint = _COUNTERPARTS.get(name.lower())
            if hint:
                entry["ra2_counterpart"] = hint
                reason += f" (RA2 counterpart: {hint})"
            exceptions.append(entry)
            blocks.append(render_stub(name, f"{twin_id}: {reason}"))
            continue
        projectile = rules.get(w.projectile, {})
        _, note = ra2_projectile_speed(w, projectile)
        if note and _type(base.get("Projectile")) in SPEED_PROJECTILES:
            notes.append({"twin": twin_id, "note": note})
        blocks.append(render_twin_weapon(w, base, name=name, projectile=projectile))
    unmatched = []
    for weapon in unmatched_ra2_weapons(fired_ids, names):
        entry = {"weapon": weapon}
        mod_twins = _mod_twins_for(weapon, names)
        if mod_twins:
            entry["mod_twins"] = mod_twins
        unmatched.append(entry)
    shown = [e["weapon"] + (f" (mod: {', '.join(e['mod_twins'])})" if "mod_twins" in e else "")
        for e in unmatched]
    head = ("# RA2 weapons of twinned actors without a mod counterpart (not generated): "
        + (", ".join(shown) if shown else "(none)") + "\n"
        + "# Exceptions, kept as Inherits stubs: "
        + (", ".join(e["twin"] for e in exceptions) if exceptions else "(none)") + "\n"
        + "".join(f"# Speed: {n['twin']}: {n['note']}\n" for n in notes))
    report = {"exceptions": exceptions, "ra2_without_mod_counterpart": unmatched, "notes": notes}
    return head + "\n" + "\n".join(blocks), report
