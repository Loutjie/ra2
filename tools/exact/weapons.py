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

Stubs (the twin stays `Inherits: <weapon>` with a comment, so the twin armament still
resolves and keeps the Reimagined values; no RA2 values are invented):
  * no rules.ini section of that name (e.g. a mod-only targeting weapon);
  * a same-named section that no twinned RA2 actor fires (it belongs to another RA2 unit,
    e.g. MP5 while the Chrono Commando fires ChronoMP5);
  * the mod weapon has no damage warhead, several, one that is not SpreadDamage, or explicit
    Range steps (which would override Spread): structure_reason.
Where the mod models a mechanic differently (charge/reload split, mind control, bombs,
latching, beam splitting, erasure), exact.exceptions adjusts these twins after they are
computed: render_weapons takes an `adjust` callback that may change a twin's values, fill a
stub from another RA2 section (WeaponTwin.source) or turn a twin into a stub.
RA2 weapons fired by twinned actors that no twin uses (neither by name nor as its source,
e.g. Sapper, FakeC4, MakeupKit, PrismSupport) have no mod counterpart and are listed, not
generated. RA2_COUNTERPARTS hints at the RA2 weapon a renamed mod stub stands in for; the
pairs are linked both on the stub and in that list.
The stubs head the generated file and render_weapons returns them as a report, which
exact.generate writes to tools/exact/weapon_exceptions.json and to EXCEPTIONS.md.
"""
from __future__ import annotations

from collections.abc import Callable, Collection, Iterable, Mapping
from dataclasses import dataclass, field

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

# Mod stub -> the RA2 weapon(s) it stands in for under another name (hints for
# EXCEPTIONS.md, for stubs that exact.exceptions does not fill from that section).
RA2_COUNTERPARTS = {
    "DogJaw": "BadTeeth",
    "DemoTruckTargeting": "Demobomb, TerrorBomb",
}
_COUNTERPARTS = {k.lower(): v for k, v in RA2_COUNTERPARTS.items()}

NOT_FIRED = "rules.ini section not fired by the twinned RA2 actor"


@dataclass
class WeaponTwin:
    """The twin `<name>.exact` of mod weapon `name`: values to override, or a stub."""
    name: str
    values: dict | None = None      # MiniYaml fields of the twin block; None: Inherits stub
    reason: str | None = None       # why it is a stub
    note: str | None = None         # projectile speed decision
    source: str | None = None       # RA2 section the values come from, when not `name`
    exception: str | None = None    # exact.exceptions id that last changed it
    comments: list[str] = field(default_factory=list)   # rendered above the block

    @property
    def twin(self) -> str:
        return f"{self.name}{SUFFIX}"


def _type(node) -> str:
    return (node.get("_value", "") if isinstance(node, Mapping) else node or "").strip()


def damage_warheads(base: Mapping) -> list[str]:
    """Keys of the Warhead@* blocks of a resolved weapon that have a Damage field."""
    return [k for k, v in base.items()
        if k.split("@")[0] == "Warhead" and isinstance(v, Mapping) and "Damage" in v]


def structure_reason(base: Mapping) -> str | None:
    """Why RA2 values cannot be written onto the resolved mod weapon `base`, or None."""
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
    return structure_reason(base)


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


def twin_values(w: ExactWeapon, base: Mapping, projectile: Mapping[str, str] | None = None
        ) -> tuple[dict, str | None]:
    """Fields of the twin block with w's values, and the projectile speed note (or None).

    base: resolved tree of the mod weapon (its single SpreadDamage damage warhead gets the
    RA2 values; see structure_reason). projectile: the RA2 projectile section (flags decide
    the speed; see ra2_projectile_speed). The note is only given when the mod projectile has
    a Speed field, i.e. when the decision shows in the twin.
    """
    values: dict = {"ReloadDelay": conversions.frames_to_ticks(w.rof),
        "Range": conversions.cells_to_wdist(w.range), "Burst": w.burst}
    speed, note = ra2_projectile_speed(w, projectile)
    kind = _type(base.get("Projectile"))
    if kind not in SPEED_PROJECTILES:
        note = None
    elif speed is not None:
        values["Projectile"] = {"_value": kind,
            "Speed": conversions.projectile_speed_to_openra(speed)}
    key = damage_warheads(base)[0]
    spread = conversions.cells_to_wdist(w.cell_spread) if w.cell_spread > 0 else POINT_SPREAD
    # The lint's unknown-field check needs the warhead type on the overriding node too.
    values[key] = {"_value": _type(base[key]), "Damage": w.damage, "Spread": spread,
        "Falloff": f"100, {w.percent_at_max}", "Versus": dict(zip(VERSUS_KEYS, w.verses))}
    return values, note


def _lines(values: Mapping, depth: int) -> list[str]:
    out = []
    for key, value in values.items():
        if key == "_value":
            continue
        indent = "\t" * depth
        if isinstance(value, Mapping):
            out.append(f"{indent}{key}: {value['_value']}" if "_value" in value else f"{indent}{key}:")
            out += _lines(value, depth + 1)
        else:
            out.append(f"{indent}{key}: {value}")
    return out


def render_block(name: str, values: Mapping) -> str:
    """The `<name>.exact` block inheriting `name` and overriding `values`."""
    return "\n".join([f"{name}{SUFFIX}:", f"\tInherits: {name}", *_lines(values, 1)]) + "\n"


def render_twin_weapon(w: ExactWeapon, base: dict, *, name: str | None = None,
        projectile: Mapping[str, str] | None = None) -> str:
    """The `<name>.exact` block with w's values (name defaults to w.id); see twin_values."""
    return render_block(name or w.id, twin_values(w, base, projectile)[0])


def render_stub(name: str, reason: str) -> str:
    return f"# Exception: {reason}\n{name}{SUFFIX}:\n\tInherits: {name}\n"


def make_twin(rules: dict, name: str, w: ExactWeapon, base: Mapping) -> WeaponTwin:
    """The twin of mod weapon `name` with the values of RA2 weapon w (a stub if base's
    structure does not take them)."""
    reason = structure_reason(base)
    source = w.id if w.id.lower() != name.lower() else None
    if reason is not None:
        return WeaponTwin(name, reason=reason)
    values, note = twin_values(w, base, rules.get(w.projectile, {}))
    return WeaponTwin(name, values=values, note=note, source=source)


def unmatched_ra2_weapons(ra2_weapons: Iterable[str], twin_weapons: Iterable[str]) -> list[str]:
    """RA2 weapon IDs that no twin weapon matches (case-insensitively), sorted."""
    names = {n.lower() for n in twin_weapons}
    return sorted({w for w in ra2_weapons if w.lower() not in names}, key=str.lower)


def _mod_twins_for(ra2_weapon: str, twins: Iterable[WeaponTwin]) -> list[str]:
    """Stubs whose RA2_COUNTERPARTS hint names `ra2_weapon`."""
    return sorted(t.twin for t in twins if t.values is None and ra2_weapon.lower()
        in (p.strip().lower() for p in _COUNTERPARTS.get(t.name.lower(), "").split(",")))


def weapon_twins(rules: dict, names: Iterable[str], twinned: Iterable[ExactActor],
        resolve: Callable[[str], dict]) -> tuple[dict[str, WeaponTwin], list[str]]:
    """Mod weapon name -> its twin, and the IDs of the RA2 weapons the twinned actors fire.

    resolve: mod weapon name -> resolved tree (only called for weapons the twinned actors fire).
    """
    fired_ids = list(load_weapons(rules, list(twinned)))
    fired = {k.lower() for k in fired_ids}
    twins = {}
    for name in sorted(set(names)):
        w = load_weapon(rules, name)
        if w is None or name.lower() not in fired:
            twins[name] = WeaponTwin(name, reason=exception_reason(name, w, {}, fired))
        else:
            twins[name] = make_twin(rules, name, w, resolve(name))
    return twins, fired_ids


def render_weapons(rules: dict, names: Iterable[str], twinned: Iterable[ExactActor],
        resolve: Callable[[str], dict],
        adjust: Callable[[dict[str, WeaponTwin]], None] | None = None) -> tuple[str, dict]:
    """Twin weapon file body for mod weapons `names`, and a machine-readable report.

    twinned: the RA2 actors that get twins. resolve: mod weapon name -> resolved tree.
    adjust: changes the twins in place before they are rendered (exact.exceptions).
    The report has
      "exceptions":   [{"twin", "reason", "ra2_counterpart"?, "exception"?}] (Inherits stubs),
      "sources":      [{"twin", "ra2", "exception"?}] (values from another RA2 section),
      "ra2_without_mod_counterpart": [{"weapon", "mod_twins"?}],
      "notes":        [{"twin", "note"}] (projectile speed decisions).
    """
    twins, fired_ids = weapon_twins(rules, names, twinned, resolve)
    if adjust is not None:
        adjust(twins)
    blocks, exceptions, sources, notes = [], [], [], []
    for name in sorted(twins):
        t = twins[name]
        comments = "".join(f"# {c}\n" for c in t.comments)
        if t.values is None:
            entry = {"twin": t.twin, "reason": t.reason}
            reason = t.reason
            hint = _COUNTERPARTS.get(name.lower())
            if hint:
                entry["ra2_counterpart"] = hint
                reason += f" (RA2 counterpart: {hint})"
            if t.exception:
                entry["exception"] = t.exception
            exceptions.append(entry)
            blocks.append(comments + render_stub(name, f"{t.twin}: {reason}"))
            continue
        if t.source:
            sources.append({"twin": t.twin, "ra2": t.source,
                **({"exception": t.exception} if t.exception else {})})
        if t.note:
            notes.append({"twin": t.twin, "note": t.note})
        blocks.append(comments + render_block(name, t.values))
    used = [*twins, *(t.source for t in twins.values() if t.values is not None and t.source)]
    unmatched = []
    for weapon in unmatched_ra2_weapons(fired_ids, used):
        entry = {"weapon": weapon}
        mod_twins = _mod_twins_for(weapon, twins.values())
        if mod_twins:
            entry["mod_twins"] = mod_twins
        unmatched.append(entry)
    shown = [e["weapon"] + (f" (mod: {', '.join(e['mod_twins'])})" if "mod_twins" in e else "")
        for e in unmatched]
    head = ("# RA2 weapons of twinned actors without a mod counterpart (not generated): "
        + (", ".join(shown) if shown else "(none)") + "\n"
        + "# Exceptions, kept as Inherits stubs: "
        + (", ".join(e["twin"] for e in exceptions) if exceptions else "(none)") + "\n"
        + "".join(f"# Values from RA2 {s['ra2']}: {s['twin']}\n" for s in sources)
        + "".join(f"# Speed: {n['twin']}: {n['note']}\n" for n in notes))
    report = {"exceptions": exceptions, "sources": sources, "ra2_without_mod_counterpart": unmatched,
        "notes": notes}
    return head + "\n" + "\n".join(blocks), report
