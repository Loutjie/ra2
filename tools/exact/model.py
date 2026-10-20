"""Exact data model: player-buildable actors and their weapons, read from
the base RA2 rules.ini (as parsed by ra2data.ini.parse_ini).

Later Exact-mode tasks generate twin actors and weapons from these objects,
so values stay in RA2 units here (frames, leptons-as-cells, minutes);
exact.conversions turns them into OpenRA units.

RA2 INI semantics followed
  * A TechnoType section only exists for the game if its ID is registered in
    [InfantryTypes], [VehicleTypes], [AircraftTypes] or [BuildingTypes];
    unregistered sections are ignored. The registering list gives `kind`.
  * Player-buildable = registered, TechLevel 1..10 (-1 = unbuildable) and a
    non-empty Owner= list.
  * Booleans are case-insensitive (yes/true/1 are true); lists are comma
    separated with optional spaces; a key repeated in a section keeps its last
    value (handled by parse_ini).
  * Verses live on the WARHEAD section named by the weapon's Warhead=.
    Order: none, flak, plate, light, medium, heavy, wood, steel, concrete,
    special_1, special_2. Percentages may have spacing ("50 %"), which is
    normalised here before exact.conversions.percent sees it.
  * RA2 base rules have no section inheritance, only engine defaults.

Engine defaults assumed when a key is absent (ModEnc, "Strength",
"Speed", "ROT", "Sight", "TechLevel", "Armor", "Weapons" pages):
  Strength 1, Armor none, Speed 0, ROT 0, Sight 1, Cost 0, Power 0,
  TechLevel -1 (unbuildable), BuildTimeMultiplier 1;
  weapon Damage 0, ROF 0, Range 0, Burst 1, Speed 100, projectile ""; warhead
  Verses 100% for all 11 armours, CellSpread 0, PercentAtMax 100%.
The real rules.ini always sets the keys that matter for the 87 buildables,
so the defaults only matter for incomplete (e.g. modded) data.

build_minutes = Cost / 1000 * [General]BuildSpeed * BuildTimeMultiplier, the
rules.ini definition of BuildSpeed ("minutes to produce a 1000 credit item").
Factory-count (MultipleFactory) and low-power effects are applied at runtime
and are deliberately not folded in.
"""
from __future__ import annotations

from dataclasses import dataclass, field

from exact import conversions

KIND_LISTS = {
    "InfantryTypes": "infantry",
    "VehicleTypes": "vehicle",
    "AircraftTypes": "aircraft",
    "BuildingTypes": "building",
}

ARMOR_ORDER = ("none", "flak", "plate", "light", "medium", "heavy", "wood",
               "steel", "concrete", "special_1", "special_2")

# Balance-relevant [General] keys, copied verbatim as strings.
GENERAL_KEYS = (
    "VeteranRatio", "VeteranCombat", "VeteranSpeed", "VeteranSight",
    "VeteranArmor", "VeteranROF", "VeteranCap", "InitialVeteran",
    "RefundPercent", "ReloadRate", "RepairPercent", "RepairRate", "RepairStep",
    "URepairRate", "IRepairRate", "IRepairStep", "BuildSpeed", "BuildupTime",
    "MultipleFactory", "LowPowerPenaltyModifier", "MinLowPowerProductionSpeed",
    "MaxLowPowerProductionSpeed", "WreckerKillsMultiplier", "GameSpeedBias",
    "SurvivorRate", "CloseEnough",
)


@dataclass
class ExactActor:
    id: str
    kind: str                  # infantry | vehicle | aircraft | building
    cost: int
    strength: int
    armor: str
    speed: int
    rot: int
    sight: int
    power: int                 # positive produces, negative drains
    build_minutes: float
    prerequisites: list[str]
    primary: str | None
    secondary: str | None
    elite_primary: str | None
    elite_secondary: str | None
    veteran_abilities: list[str]
    elite_abilities: list[str]
    death_weapon: str | None
    superweapon: str | None
    raw: dict = field(default_factory=dict)


@dataclass
class ExactWeapon:
    id: str
    damage: int
    rof: int                   # frames between shots
    range: float               # cells
    burst: int
    projectile: str
    speed: int                 # leptons per frame
    warhead: str
    verses: list[int]          # 11 integer percents in ARMOR_ORDER
    cell_spread: float         # cells
    percent_at_max: int        # integer percent


def parse_bool(v: str | None, default: bool = False) -> bool:
    if v is None or not v.strip():
        return default
    return v.strip().lower() in ("yes", "true", "1")


def parse_list(v: str | None) -> list[str]:
    if v is None:
        return []
    return [p.strip() for p in v.split(",") if p.strip()]


def _num(section: dict, key: str, default, cast=float):
    v = section.get(key, "").strip()
    if not v:
        return default
    try:
        return cast(v)
    except ValueError:
        return cast(float(v))


def _int(section: dict, key: str, default: int) -> int:
    return _num(section, key, default, int)


def _flt(section: dict, key: str, default: float) -> float:
    v = section.get(key, "").strip()
    return float(v) if v else default


def _opt(section: dict, key: str) -> str | None:
    v = section.get(key, "").strip()
    return v or None


def _percent(v: str) -> int:
    return conversions.percent(v.replace(" ", ""))


def load_general(rules: dict) -> dict[str, str]:
    general = rules.get("General", {})
    return {k: general[k] for k in GENERAL_KEYS if k in general}


def _registered(rules: dict) -> dict[str, str]:
    """ID -> kind for every ID in the four type-registration lists."""
    kinds: dict[str, str] = {}
    for list_name, kind in KIND_LISTS.items():
        for value in rules.get(list_name, {}).values():
            if value.strip():
                kinds.setdefault(value.strip(), kind)
    return kinds


def _is_buildable(section: dict) -> bool:
    tech = _int(section, "TechLevel", -1)
    return 1 <= tech <= 10 and bool(parse_list(section.get("Owner")))


def load_actors(rules: dict) -> list[ExactActor]:
    """Player-buildable actors, in registration-list order."""
    build_speed = _flt(rules.get("General", {}), "BuildSpeed", 0.0)
    actors = []
    for actor_id, kind in _registered(rules).items():
        section = rules.get(actor_id)
        if section is None or not _is_buildable(section):
            continue
        cost = _int(section, "Cost", 0)
        multiplier = _flt(section, "BuildTimeMultiplier", 1.0)
        actors.append(ExactActor(
            id=actor_id,
            kind=kind,
            cost=cost,
            strength=_int(section, "Strength", 1),
            armor=section.get("Armor", "none").strip().lower() or "none",
            speed=_int(section, "Speed", 0),
            rot=_int(section, "ROT", 0),
            sight=_int(section, "Sight", 1),
            power=_int(section, "Power", 0),
            build_minutes=cost / 1000 * build_speed * multiplier,
            prerequisites=parse_list(section.get("Prerequisite")),
            primary=_opt(section, "Primary"),
            secondary=_opt(section, "Secondary"),
            elite_primary=_opt(section, "ElitePrimary"),
            elite_secondary=_opt(section, "EliteSecondary"),
            veteran_abilities=parse_list(section.get("VeteranAbilities")),
            elite_abilities=parse_list(section.get("EliteAbilities")),
            death_weapon=_opt(section, "DeathWeapon"),
            superweapon=_opt(section, "SuperWeapon"),
            raw=dict(section),
        ))
    return actors


def _verses(warhead: dict) -> list[int]:
    parts = parse_list(warhead.get("Verses"))
    values = [_percent(p) for p in parts[:len(ARMOR_ORDER)]]
    return values + [100] * (len(ARMOR_ORDER) - len(values))


def load_weapons(rules: dict, actors: list[ExactActor]) -> dict[str, ExactWeapon]:
    """Every weapon the actors can fire (primary, secondary, elite variants,
    death weapon), keyed by weapon ID. References to sections that do not
    exist are skipped."""
    weapons: dict[str, ExactWeapon] = {}
    for actor in actors:
        for wid in (actor.primary, actor.secondary, actor.elite_primary,
                    actor.elite_secondary, actor.death_weapon):
            if not wid or wid in weapons or wid not in rules:
                continue
            w = rules[wid]
            warhead_id = w.get("Warhead", "").strip()
            warhead = rules.get(warhead_id, {})
            spread = warhead.get("CellSpread", "").strip()
            at_max = warhead.get("PercentAtMax", "").strip()
            weapons[wid] = ExactWeapon(
                id=wid,
                damage=_int(w, "Damage", 0),
                rof=_int(w, "ROF", 0),
                range=_flt(w, "Range", 0.0),
                burst=_int(w, "Burst", 1),
                projectile=w.get("Projectile", "").strip(),
                speed=_int(w, "Speed", 100),
                warhead=warhead_id,
                verses=_verses(warhead),
                cell_spread=float(spread) if spread else 0.0,
                percent_at_max=_percent(at_max) if at_max else 100,
            )
    return weapons
