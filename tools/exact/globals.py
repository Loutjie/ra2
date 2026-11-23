"""Exact global and special rules: mods/ra2/rules/exact/globals.yaml.

RA2 keeps many balance rules in [General] and [CombatDamage] (veterancy, refunds, repair,
survivors, crew, superweapon timers); the mod keeps them on actors. This module writes them
onto the twins `<id>.exact` (merged with the blocks of exact.actors, so the file must load
after rules/exact/actors.yaml) and, for the neutral tech buildings, which are not twinned,
switches their balance traits on the owner's `balance.exact` prerequisite.

Per twin (only where the base actor has the trait; otherwise the value is listed as not applied)
  Veterancy (GainsExperience): thresholds VeteranRatio x100 and x200 percent of the actor's
    value for rank-veteran and rank-elite (the base thresholds are removed). The rank
    multipliers of ^GainsExperience (Firepower/Damage/ReloadDelay/Speed Multiplier @VETERAN,
    @ELITE) take VeteranCombat, 1 / VeteranArmor, VeteranROF and VeteranSpeed where the
    actor's abilities have FIREPOWER, STRONGER, ROF and FASTER, and 100 elsewhere. RA2's
    abilities are flags: an elite unit has its VeteranAbilities and its EliteAbilities, and a
    bonus in both lists applies once. RA2 has no accuracy bonus (InaccuracyMultiplier@ELITE
    100). Self-heal (ChangesHealth@ELITE) only with SELF_HEAL: from rank-veteran if it is a
    veteran ability, else from rank-elite; without it the trait is removed. RA2 SelfHealing=yes
    units heal at every rank through the base actor's own ChangesHealth, so their SELF_HEAL
    ability needs no trait.
  Sellable RefundPercent = RefundPercent.
  RepairableBuilding RepairPercent, RepairInterval, RepairStep = RepairPercent,
    minutes_to_ticks(RepairRate), RepairStep.
  RepairsUnits (RA2 UnitRepair=yes): ValuePercentage, Interval, HpPerStep = RepairPercent,
    minutes_to_ticks(URepairRate), RepairStep.
  ResourcePurifier Modifier = PurifierBonus.
  SpawnSurvivors (buildings killed): RA2 Crewed=yes buildings release
    floor(Cost x RefundPercent / divisor) crew, AlliedSurvivorDivisor with AlliedCrew and
    SovietSurvivorDivisor with SovietCrew; the side is that of the mod's survivors. Other
    actors in the mod's list (the construction yards' engineer) are kept. No crew and nothing
    else, or Crewed not yes: the trait is removed.
  EjectOnDeath (crew escape): RA2 Crewed=yes vehicles and aircraft; SuccessRate = CrewEscape;
    the crew is AlliedCrew/SovietCrew by the side of the RA2 owners (aircraft: Pilot).
  Superweapons: ChargeInterval = minutes_to_ticks(RechargeTime) of the RA2 SuperWeapon on the
    mod trait of its Type (SUPERWEAPON_TRAITS); paratroopers drop <ParaDropInf> x
    <ParaDropNum>; Iron Curtain Duration = frames_to_ticks(IronCurtainDuration); Lightning
    Storm BuildUpDelay/Duration/HitDelay/ScatterDelay from LightningDeferment/
    LightningStormDuration/HitDelay/ScatterDelay and
    its weapon twin from LightningDamage and LightningWarhead (weather_weapon).
  Iron Curtain kills infantry: infantry twins accept the `invulnerability` external condition
    and KillsSelf while they have it, unless the base actor already does (^Infantry).
  #514 death weapons: an RA2 actor with Explodes=yes and a DeathWeapon fires that weapon's
    twin from its FireWarheadsOnDeath (DEATH_WEAPON_TRAIT). Explodes=yes without a
    DeathWeapon (RA2's DefaultDeathWeapon, whose damage "is based on" the unit's strength by
    an undocumented formula) keeps the mod's death weapon.
  #803 Spy Satellite (SpySat=yes): the base RevealsMap (live map vision while powered) is kept,
    the closest equivalent with the fog of war on (exact.exceptions "spy-satellite").

Neutral tech buildings (exact.snapshot.NEUTRAL_TECH_ACTORS) get
`GrantConditionOnPrerequisite@BALANCE` (condition balance-exact on the owner's balance.exact)
and, for each balance trait (NEUTRAL_TRAITS) whose RA2 value differs from the mod's, `-X`, `X@REIMAGINED` (the
original plus RequiresCondition !balance-exact) and `X@EXACT` (with the RA2 values,
RequiresCondition balance-exact). exact.snapshot.strip_neutral folds them back. A support
power pair gets `<OrderName>Exact` on the Exact side: instances sharing an OrderName share one
charge timer.

Notes per exact.exceptions id are returned for EXCEPTIONS.md (exact.generate).
"""
from __future__ import annotations

import math
from collections.abc import Callable, Collection, Mapping
from dataclasses import dataclass, field
from decimal import Decimal

from exact import conversions
from exact.actors import ARMOR_TYPES, SUFFIX, split_list, trait_type
from exact.model import ExactActor, load_actor, load_weapon, parse_bool
from exact.snapshot import EXACT_CONDITION, EXACT_SUFFIX, NEUTRAL_GRANT, NEUTRAL_IGNORED_TRAIT, \
    REIMAGINED_CONDITION, REIMAGINED_SUFFIX

VETERAN, ELITE = "rank-veteran", "rank-elite"
INVULNERABILITY = "invulnerability"
DEATH_WEAPON_TRAIT = "FireWarheadsOnDeath"

# (multiplier trait type, RA2 ability, [General] key, inverse): the rank traits of ^GainsExperience.
VETERAN_MULTIPLIERS = (
    ("FirepowerMultiplier", "FIREPOWER", "VeteranCombat", False),
    ("DamageMultiplier", "STRONGER", "VeteranArmor", True),
    ("ReloadDelayMultiplier", "ROF", "VeteranROF", False),
    ("SpeedMultiplier", "FASTER", "VeteranSpeed", False),
)
NO_BONUS_TRAITS = ("InaccuracyMultiplier@ELITE",)
SELF_HEAL_TRAIT = "ChangesHealth@ELITE"
MAPPED_ABILITIES = {"FIREPOWER", "STRONGER", "ROF", "FASTER", "SELF_HEAL"}

# RA2 superweapon Type -> mod support power trait type.
SUPERWEAPON_TRAITS = {
    "MultiMissile": "NukePower",
    "IronCurtain": "GrantExternalConditionPower",
    "LightningStorm": "WeatherControlSupportPower",
    "ChronoSphere": "ChronoshiftPower",
    "ParaDrop": "ParatroopersPower",
    "AmerParaDrop": "ParatroopersPower",
}
# Paratrooper drop [General] key prefix, by superweapon Type (and owner side for ParaDrop).
PARADROP_KEYS = {"AmerParaDrop": "AmerParaDrop", ("ParaDrop", "Allied"): "AllyParaDrop",
    ("ParaDrop", "Soviet"): "SovParaDrop"}
# Mod trait instance suffix -> side, for traits split by faction (SpawnActorsOnSell@allies, ...).
SIDE_SUFFIXES = {"allies": "Allied", "soviets": "Soviet"}

# Neutral tech building trait types switched by balance mode.
NEUTRAL_TRAITS = ("Armament", "Armor", "CashTrickler", "GivesCashOnCapture", "ParatroopersPower",
    "RepairsUnits", "RevealsShroud")


@dataclass
class Globals:
    blocks: dict[str, dict] = field(default_factory=dict)     # actor -> trait key -> fields
    notes: dict[str, list[str]] = field(default_factory=dict)  # exact.exceptions id -> lines
    weapons: set[str] = field(default_factory=set)            # mod weapons that need a twin

    def put(self, actor: str, key: str, fields: Mapping) -> None:
        self.blocks.setdefault(actor, {}).setdefault(key, {}).update(fields)

    def remove(self, actor: str, key: str) -> None:
        self.blocks.setdefault(actor, {})["-" + key] = None

    def note(self, exc: str, line: str) -> None:
        self.notes.setdefault(exc, []).append(line)


def _general(rules: dict, key: str, section: str = "General") -> str | None:
    value = rules.get(section, {}).get(key, "").strip()
    return value or None


def _percent_of(value: str) -> int:
    return conversions.percent(value.replace(" ", ""))


def _round(value: Decimal) -> int:
    return conversions._round(value)


def _defined(weapon: str, weapons: Mapping[str, str]) -> str:
    return weapons.get(weapon.strip().lower(), weapon.strip())


def _twin(name: str, twins: Collection[str]) -> str:
    return name.lower() + SUFFIX if name.lower() in twins else name.lower()


def side_of(rules: dict, actor: ExactActor) -> str | None:
    """"Allied" or "Soviet" if every RA2 owner country of `actor` has that Suffix, else None."""
    sides = {rules.get(c, {}).get("Suffix", "").strip() for c in split_list(actor.raw.get("Owner", ""))}
    return sides.pop() if len(sides) == 1 and sides <= {"Allied", "Soviet"} else None


def crew_side(rules: dict, name: str) -> str | None:
    """The side whose crew (AlliedCrew/SovietCrew) is the infantry `name`, or None."""
    for side in ("Allied", "Soviet"):
        crew = _general(rules, f"{side}Crew")
        if crew and crew.lower() == name.lower().removesuffix(SUFFIX):
            return side
    return None


# --- per twin ---------------------------------------------------------------------------------

def veterancy(g: Globals, rules: dict, actor: ExactActor, base: Mapping, twin: str) -> None:
    if "GainsExperience" not in base:
        if actor.veteran_abilities or actor.elite_abilities:
            g.note("veterancy", f"{twin}: no GainsExperience trait, RA2 abilities not applied")
        return
    ratio = _general(rules, "VeteranRatio")
    if ratio is not None:
        levels = {VETERAN: str(_round(Decimal(ratio) * 100)), ELITE: str(_round(Decimal(ratio) * 200))}
        old = base["GainsExperience"].get("Conditions", {})
        if sorted(old.values()) != sorted(levels):
            raise ValueError(f"{twin}: GainsExperience conditions {old} are not {sorted(levels)}")
        conditions = {f"-{k}": None for k in old if k not in levels.values()}
        conditions.update({v: k for k, v in levels.items()})
        g.put(twin, "GainsExperience", {"Conditions": conditions})
    veteran = set(actor.veteran_abilities)
    elite = veteran | set(actor.elite_abilities)
    for kind, ability, key, inverse in VETERAN_MULTIPLIERS:
        value = _general(rules, key)
        if value is None:
            continue
        bonus = _round(Decimal(100) / Decimal(value) if inverse else Decimal(value) * 100)
        for rank, abilities in (("VETERAN", veteran), ("ELITE", elite)):
            trait = f"{kind}@{rank}"
            modifier = bonus if ability in abilities else 100
            if trait in base:
                g.put(twin, trait, {"Modifier": modifier})
            elif modifier != 100:
                g.note("veterancy", f"{twin}: {ability} has no {trait} trait to go to")
    for trait in NO_BONUS_TRAITS:
        if trait in base:
            g.put(twin, trait, {"Modifier": 100})
    if "SELF_HEAL" in elite and SELF_HEAL_TRAIT in base:
        g.put(twin, SELF_HEAL_TRAIT, {"RequiresCondition": VETERAN if "SELF_HEAL" in veteran else ELITE})
    elif SELF_HEAL_TRAIT in base:
        g.remove(twin, SELF_HEAL_TRAIT)
    elif "SELF_HEAL" in elite and not parse_bool(actor.raw.get("SelfHealing")):
        g.note("veterancy", f"{twin}: SELF_HEAL has no {SELF_HEAL_TRAIT} trait to go to")
    unmapped = sorted((veteran | elite) - MAPPED_ABILITIES)
    if unmapped:
        g.note("veterancy-abilities", f"{twin}: {', '.join(unmapped)}")


def economy(g: Globals, rules: dict, actor: ExactActor, base: Mapping, twin: str) -> None:
    refund, repair = _general(rules, "RefundPercent"), _general(rules, "RepairPercent")
    rate, step = _general(rules, "RepairRate"), _general(rules, "RepairStep")
    if refund and "Sellable" in base:
        g.put(twin, "Sellable", {"RefundPercent": _percent_of(refund)})
    if repair and rate and step and "RepairableBuilding" in base:
        g.put(twin, "RepairableBuilding", {"RepairPercent": _percent_of(repair),
            "RepairInterval": conversions.minutes_to_ticks(float(rate)), "RepairStep": int(step)})
    urate = _general(rules, "URepairRate")
    if parse_bool(actor.raw.get("UnitRepair")) and repair and urate and step:
        for key in sorted(k for k in base if trait_type(k) == "RepairsUnits"):
            g.put(twin, key, {"ValuePercentage": _percent_of(repair),
                "Interval": conversions.minutes_to_ticks(float(urate)), "HpPerStep": int(step)})
    bonus = _general(rules, "PurifierBonus")
    if bonus and "ResourcePurifier" in base:
        g.put(twin, "ResourcePurifier", {"Modifier": _percent_of(bonus)})


def survivors(g: Globals, rules: dict, actor: ExactActor, base: Mapping, twin: str,
        twins: Collection[str]) -> None:
    trait = base.get("SpawnSurvivors")
    refund = _general(rules, "RefundPercent")
    if not isinstance(trait, Mapping) or refund is None:
        return
    names = split_list(trait.get("Actors", ""))
    sides = {crew_side(rules, n) for n in names} - {None}
    if len(sides) != 1:
        g.note("survivors", f"{twin}: survivors {', '.join(names)} have no single RA2 crew side; kept")
        return
    side = sides.pop()
    crew, divisor = _general(rules, f"{side}Crew"), _general(rules, f"{side}SurvivorDivisor")
    if crew is None or divisor is None:
        return
    count = 0
    if parse_bool(actor.raw.get("Crewed")):
        count = math.floor(Decimal(actor.cost) * _percent_of(refund) / 100 / Decimal(divisor))
    others = [_twin(n, twins) for n in names if crew_side(rules, n) is None]
    result = [_twin(crew, twins)] * count + others
    if result:
        g.put(twin, "SpawnSurvivors", {"Actors": ", ".join(result)})
    else:
        g.remove(twin, "SpawnSurvivors")
    g.note("survivors", f"{twin}: {count} x {_twin(crew, twins)}"
        + (f" + {', '.join(others)}" if others else "")
        + ("" if parse_bool(actor.raw.get("Crewed")) else " (RA2 Crewed is not yes)")
        + f" (mod: {len(names)})")


def crew_escape(g: Globals, rules: dict, actor: ExactActor, base: Mapping, twin: str,
        twins: Collection[str]) -> None:
    chance = _general(rules, "CrewEscape")
    if actor.kind not in ("vehicle", "aircraft") or not parse_bool(actor.raw.get("Crewed")) or not chance:
        return
    airborne = "Aircraft" in base
    if actor.kind == "aircraft":
        crew = _general(rules, "Pilot")
    else:
        side = side_of(rules, actor)
        crew = _general(rules, f"{side}Crew") if side else None
    if crew is None:
        g.note("crew-escape", f"{twin}: no RA2 crew for its owners; not applied")
        return
    fields = {"PilotActor": _twin(crew, twins), "SuccessRate": _percent_of(chance), "EjectOnGround": "true"}
    if airborne:
        fields["EjectInAir"] = "true"
    g.put(twin, "EjectOnDeath", fields)
    g.note("crew-escape", f"{twin}: {fields['PilotActor']}, {fields['SuccessRate']}%")


def _superweapon(rules: dict, actor: ExactActor) -> tuple[str, dict] | None:
    section = rules.get(actor.superweapon or "", None)
    return (actor.superweapon, section) if section is not None else None


def paradrop_items(rules: dict, sw_type: str, side: str | None, twins: Collection[str]) -> str | None:
    prefix = PARADROP_KEYS.get(sw_type) or PARADROP_KEYS.get((sw_type, side))
    infantry = _general(rules, f"{prefix}Inf") if prefix else None
    number = _general(rules, f"{prefix}Num") if prefix else None
    if infantry is None or number is None:
        return None
    return ",".join([_twin(infantry, twins)] * int(number))


def superweapon(g: Globals, rules: dict, actor: ExactActor, base: Mapping, twin: str,
        twins: Collection[str], weapons: Mapping[str, str]) -> None:
    found = _superweapon(rules, actor)
    if found is None:
        return
    sw_id, sw = found
    sw_type = sw.get("Type", "").strip()
    kind = SUPERWEAPON_TRAITS.get(sw_type)
    keys = sorted(k for k in base if trait_type(k) == kind)
    if not keys:
        g.note("superweapons", f"{twin}: RA2 {sw_id} has no {kind or sw_type} trait to go to")
        return
    recharge = sw.get("RechargeTime", "").strip()
    for key in keys:
        fields: dict = {}
        if recharge:
            fields["ChargeInterval"] = conversions.minutes_to_ticks(float(recharge))
        if kind == "ParatroopersPower":
            items = paradrop_items(rules, sw_type, side_of(rules, actor), twins)
            if items:
                fields["DropItems"] = items
        elif kind == "GrantExternalConditionPower":
            duration = _general(rules, "IronCurtainDuration", "CombatDamage")
            if duration:
                fields["Duration"] = conversions.frames_to_ticks(int(duration))
        elif kind == "WeatherControlSupportPower":
            for field_name, ini_key in (("BuildUpDelay", "LightningDeferment"),
                    ("Duration", "LightningStormDuration"),
                    ("HitDelay", "LightningHitDelay"), ("ScatterDelay", "LightningScatterDelay")):
                value = _general(rules, ini_key)
                if value:
                    fields[field_name] = conversions.frames_to_ticks(int(value))
            weapon = base[key].get("Weapon")
            if isinstance(weapon, str) and _general(rules, "LightningDamage"):
                weapon = _defined(weapon, weapons)
                fields["Weapon"] = weapon + SUFFIX
                g.weapons.add(weapon)
        g.put(twin, key, fields)
        g.note("superweapons", f"{twin}: {key} " + ", ".join(f"{k} {v}" for k, v in fields.items())
            + f" (RA2 {sw_id})")


def iron_curtain(g: Globals, actor: ExactActor, base: Mapping, twin: str) -> None:
    if actor.kind != "infantry" or "KillsSelf@IRONCURTAIN" in base:
        return  # the base mod's ^Infantry already dies under the Iron Curtain
    if not any(trait_type(k) == "ExternalCondition" and isinstance(t, Mapping)
            and t.get("Condition") == INVULNERABILITY for k, t in base.items()):
        g.put(twin, "ExternalCondition@INVULNERABILITY", {"Condition": INVULNERABILITY})
    g.put(twin, "KillsSelf@IRONCURTAIN", {"RequiresCondition": INVULNERABILITY})
    g.note("iron-curtain-kills-infantry", twin)


def death_weapon(g: Globals, rules: dict, actor: ExactActor, base: Mapping, twin: str,
        weapons: Mapping[str, str]) -> None:
    wanted = (actor.death_weapon or "").lower()
    keys = [k for k, t in sorted(base.items()) if trait_type(k) == DEATH_WEAPON_TRAIT and isinstance(t, Mapping)
        and isinstance(t.get("Weapon"), str) and t["Weapon"].strip().lower() == wanted]
    if not parse_bool(actor.raw.get("Explodes")):
        for key in keys:
            g.note("death-weapons", f"{twin}: {key} {base[key]['Weapon'].strip()} kept: RA2 sets "
                f"DeathWeapon={actor.death_weapon} without Explodes=yes, and the mod fires it only "
                f"under {base[key].get('RequiresCondition', 'no condition')}")
        return
    if actor.death_weapon is None:
        g.note("death-weapon-strength", f"{twin}: RA2 Explodes=yes without DeathWeapon; mod death weapon kept")
        return
    if load_weapon(rules, actor.death_weapon) is None:
        g.note("death-weapons", f"{twin}: RA2 DeathWeapon {actor.death_weapon} has no rules.ini section; "
            "mod death weapon kept")
        return
    if not keys:
        g.note("death-weapons", f"{twin}: RA2 DeathWeapon {actor.death_weapon} has no {DEATH_WEAPON_TRAIT} "
            "firing it; not applied")
        return
    for key in keys:
        weapon = _defined(base[key]["Weapon"], weapons)
        fields = {f: weapon + SUFFIX for f in ("Weapon", "EmptyWeapon")
            if isinstance(base[key].get(f), str) and base[key][f].strip().lower() == wanted}
        g.put(twin, key, fields)
        g.weapons.add(weapon)
        g.note("death-weapons", f"{twin}: {key} {fields['Weapon']} (RA2 {actor.death_weapon})")


def spy_satellite(g: Globals, actor: ExactActor, base: Mapping, twin: str) -> None:
    if not parse_bool(actor.raw.get("SpySat")):
        return
    if "RevealsMap" not in base:
        g.note("spy-satellite", f"{twin}: no RevealsMap trait to go to")
        return
    g.note("spy-satellite", f"{twin}: the base RevealsMap is kept "
        f"(RequiresCondition {base['RevealsMap'].get('RequiresCondition', 'none')})")


# --- neutral tech buildings -------------------------------------------------------------------

def neutral_values(rules: dict, actor: ExactActor, tree: Mapping, twins: Collection[str],
        weapons: Mapping[str, str], twin_weapons: Collection[str]) -> dict[str, dict]:
    """Trait key -> the Exact field values of the neutral tech building `tree`, for the traits
    where they differ from the mod's."""
    raw = actor.raw
    out: dict[str, dict] = {}
    for key, trait in sorted(tree.items()):
        if not isinstance(trait, Mapping):
            continue
        kind = trait_type(key)
        values: dict = {}
        if kind == "CashTrickler" and raw.get("ProduceCashDelay") and raw.get("ProduceCashAmount"):
            values = {"Interval": conversions.frames_to_ticks(int(raw["ProduceCashDelay"])),
                "Amount": int(raw["ProduceCashAmount"])}
        elif kind == "GivesCashOnCapture" and raw.get("ProduceCashStartup"):
            values = {"Amount": int(raw["ProduceCashStartup"])}
        elif kind == "Armor" and raw.get("Armor"):
            values = {"Type": ARMOR_TYPES[actor.armor]}
        elif kind == "RevealsShroud" and raw.get("Sight"):
            values = {"Range": conversions.cells_to_wdist(actor.sight)}
        elif kind == "Armament" and isinstance(trait.get("Weapon"), str):
            weapon = _defined(trait["Weapon"], weapons)
            if weapon.lower() in {w.lower() for w in twin_weapons}:
                values = {"Weapon": weapon + SUFFIX}
        elif kind == "RepairsUnits":
            if parse_bool(raw.get("UnitRepair")):
                rate, step, cost = (_general(rules, k) for k in ("URepairRate", "RepairStep", "RepairPercent"))
                if rate and step and cost:
                    values = {"ValuePercentage": _percent_of(cost),
                        "Interval": conversions.minutes_to_ticks(float(rate)), "HpPerStep": int(step)}
            elif parse_bool(raw.get("Hospital")):
                rate, step = _general(rules, "IRepairRate"), _general(rules, "IRepairStep")
                if rate and step:
                    values = {"Interval": conversions.minutes_to_ticks(float(rate)), "HpPerStep": int(step)}
        elif kind == "ParatroopersPower":
            found = _superweapon(rules, actor)
            if found is not None:
                _, sw = found
                side = SIDE_SUFFIXES.get(key.partition("@")[2].lower())
                items = paradrop_items(rules, sw.get("Type", "").strip(), side, twins)
                if sw.get("RechargeTime", "").strip():
                    values["ChargeInterval"] = conversions.minutes_to_ticks(float(sw["RechargeTime"]))
                if items:
                    values["DropItems"] = items
        if not any(str(trait.get(k)) != str(v) for k, v in values.items()):
            continue  # RA2 and the mod agree: no pair
        if kind == "ParatroopersPower" and isinstance(trait.get("OrderName"), str):
            values["OrderName"] = trait["OrderName"] + "Exact"
        out[key] = values
    return out


def neutral_pairs(g: Globals, name: str, tree: Mapping, values: Mapping[str, Mapping],
        inherited: Mapping[str, Mapping] | None = None) -> None:
    """The Exact/Reimagined pairs of the neutral building `name` (its tree without pairs).

    inherited: the Exact values of a neutral building that `name` inherits, whose pairs it
    already has: only values that differ from them are written, onto the inherited `X@EXACT`.
    """
    inherited = inherited or {}
    for key in sorted(k for k in values if k in inherited and values[k] != inherited[k]):
        g.put(name, key + EXACT_SUFFIX, values[key])
        g.note("neutral-tech-buildings", f"{name}: {key} " + ", ".join(f"{k} {v}" for k, v in values[key].items()))
    values = {k: v for k, v in values.items() if k not in inherited}
    if not values:
        return
    g.put(name, NEUTRAL_IGNORED_TRAIT, NEUTRAL_GRANT)
    for key in sorted(values):
        original = tree[key]
        if "RequiresCondition" in original:
            raise ValueError(f"{name}: {key} already has a RequiresCondition; cannot pair it")
        g.remove(name, key)
        g.put(name, key + REIMAGINED_SUFFIX, {**original, "RequiresCondition": REIMAGINED_CONDITION})
        g.put(name, key + EXACT_SUFFIX, {**original, **values[key], "RequiresCondition": EXACT_CONDITION})
        g.note("neutral-tech-buildings", f"{name}: {key} " + ", ".join(f"{k} {v}" for k, v in values[key].items()))


# --- weapons ----------------------------------------------------------------------------------

def weather_weapon(rules: dict, base: Mapping) -> dict | None:
    """Twin values for the Lightning Storm weapon (its resolved mod tree `base`): Damage =
    LightningDamage with the LightningWarhead's Verses, CellSpread and PercentAtMax. The mod
    weapon's timing (ReloadDelay, Range, Burst) is kept. None without those values or when the
    mod weapon does not take them (exact.weapons.structure_reason)."""
    from exact.model import ExactWeapon, _verses
    from exact.weapons import structure_reason, twin_values
    damage, warhead_id = _general(rules, "LightningDamage"), _general(rules, "LightningWarhead")
    warhead = rules.get(warhead_id or "", None)
    if damage is None or warhead is None or structure_reason(base) is not None:
        return None
    spread = warhead.get("CellSpread", "").strip()
    at_max = warhead.get("PercentAtMax", "").strip()
    w = ExactWeapon(id="LightningStorm", damage=int(damage), rof=0, range=0.0, burst=1, projectile="",
        speed=0, warhead=warhead_id, verses=_verses(warhead), cell_spread=float(spread) if spread else 0.0,
        percent_at_max=_percent_of(at_max) if at_max else 100)
    values, _ = twin_values(w, base)
    return {k: v for k, v in values.items() if k not in ("ReloadDelay", "Range", "Burst", "Projectile")}


# --- build and render -------------------------------------------------------------------------

def build_globals(rules: dict, ra2: Mapping[str, ExactActor], bases: Mapping[str, Mapping],
        twins: Collection[str], neutral: Mapping[str, Mapping] | None = None,
        weapons: Mapping[str, str] | None = None, twin_weapons: Collection[str] = (),
        neutral_parents: Mapping[str, str] | None = None) -> Globals:
    """The global rules of every twin (the `twins`, base IDs with a twin, that have an RA2 actor
    in `ra2` and a resolved base tree in `bases`) and of the neutral tech buildings (`neutral`: name -> resolved tree
    without previous pairs, see exact.snapshot.strip_neutral).

    weapons: lowercase weapon name -> defined name. twin_weapons: mod weapons with a twin.
    neutral_parents: neutral building -> the neutral building it inherits (and its pairs)."""
    g = Globals()
    for name in sorted(n for n in twins if n in ra2 and n in bases):
        actor, base, twin = ra2[name], bases[name], name + SUFFIX
        veterancy(g, rules, actor, base, twin)
        economy(g, rules, actor, base, twin)
        survivors(g, rules, actor, base, twin, twins)
        crew_escape(g, rules, actor, base, twin, twins)
        superweapon(g, rules, actor, base, twin, twins, weapons or {})
        iron_curtain(g, actor, base, twin)
        death_weapon(g, rules, actor, base, twin, weapons or {})
        spy_satellite(g, actor, base, twin)
    ranked = sum(1 for n in twins if n in ra2 and n in bases and "GainsExperience" in bases[n])
    g.note("veterancy", f"{ranked} twins with GainsExperience")
    found: dict[str, dict] = {}
    for name, tree in sorted((neutral or {}).items()):
        actor = load_actor(rules, name)
        if actor is None:
            g.note("neutral-tech-buildings", f"{name}: no RA2 section; unchanged")
            continue
        found[name] = neutral_values(rules, actor, tree, twins, weapons or {}, twin_weapons)
        if parse_bool(actor.raw.get("Explodes")) and actor.death_weapon:
            fired = sorted(t["Weapon"].strip() for k, t in tree.items() if trait_type(k) == DEATH_WEAPON_TRAIT
                and isinstance(t, Mapping) and isinstance(t.get("Weapon"), str))
            g.note("death-weapons", f"{name}: RA2 DeathWeapon {actor.death_weapon} not mapped; mod "
                f"{', '.join(fired) or 'nothing'} kept (neutral buildings are not twinned)")
    parents = neutral_parents or {}
    for name in sorted(found):
        if parents.get(parents.get(name, "")) in found:
            raise ValueError(f"{name}: neutral buildings inherit more than one level deep")
        neutral_pairs(g, name, neutral[name], found[name], found.get(parents.get(name, "")))
    return g


def _lines(fields: Mapping, depth: int) -> list[str]:
    out = []
    indent = "\t" * depth
    for key, value in fields.items():
        if key == "_value":
            continue
        if isinstance(value, Mapping):
            head = f"{indent}{key}: {value['_value']}" if "_value" in value else f"{indent}{key}:"
            out += [head, *_lines(value, depth + 1)]
        elif value is None:
            out.append(f"{indent}{key}:")
        else:
            out.append(f"{indent}{key}: {value}")
    return out


def render_globals(g: Globals) -> str:
    """MiniYaml of the global rules, one block per actor, traits in key order (removals first)."""
    blocks = []
    for name in sorted(g.blocks):
        traits = g.blocks[name]
        keys = sorted(traits, key=lambda k: (not k.startswith("-"), k))
        blocks.append("\n".join([f"{name}:", *_lines({k: traits[k] for k in keys}, 1)]) + "\n")
    return "\n".join(blocks)


def fill_weather_weapons(g: Globals, rules: dict, weapon_twins: dict, resolve: Callable[[str], dict]) -> list[str]:
    """Give the Lightning Storm weapon twins (stubs: RA2 has no weapon section) their values."""
    lines = []
    for name in sorted(g.weapons):
        t = next((t for n, t in weapon_twins.items() if n.lower() == name.lower()), None)
        if t is None or t.values is not None or load_weapon(rules, name) is not None:
            continue
        values = weather_weapon(rules, resolve(t.name))
        if values is None:
            continue
        t.values, t.reason, t.source = values, None, None
        t.exception = "lightning-storm"
        t.comments.append("Exception lightning-storm: Damage from [General] LightningDamage, "
            "warhead values from LightningWarhead")
        lines.append(f"{t.twin}: from [General] LightningDamage, LightningWarhead")
    return lines
