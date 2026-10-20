"""Exact twin actors and the Reimagined prerequisite overlay, rendered as MiniYaml.

A twin `<id>.exact` inherits the mod's base actor `<id>` and overrides only the
values that RA2's rules.ini defines (exact.model), converted by exact.conversions:

  * Buildable: Prerequisites = the base prerequisites with `~balance.exact` in place
    of `~balance.reimagined`; BuildDuration = minutes_to_ticks(build_minutes);
    BuildDurationModifier 100 (the engine default of 60 would scale RA2's time).
  * Valued Cost, Health HP, Armor Type, Mobile/Aircraft Speed and TurnSpeed (TurnSpeed
    only when RA2 sets ROT=; RA2 infantry have none; a building's ROT= goes to its
    Turreted TurnSpeed), RevealsShroud Range, Power Amount.
    A value is only written to a trait the base actor has; generate.py lists what
    could not be applied.
  * Armament*: Weapon -> `<weapon>.exact`.
  * Buildings: `ProvidesPrerequisite@BASE: Prerequisite: <id>`, so prerequisites that
    name the base building (e.g. `naweap`) are met by the twin.
  * Actor names in actor lists point at twins where a twin exists. Lists of actors to
    create (SPAWN_FIELDS) swap the base for the twin; lists of actors to match
    (MATCH_FIELDS: repair and rearm hosts, production triggers) keep the base and add
    the twin beside it, so base hosts that can still exist in Exact (e.g. a captured
    map-placed depot) keep working.
  * Art: every trait whose image defaults to the actor's name gets the base image
    written out, so `htnk.exact` renders, animates and shows its icon from `htnk`.
    Voices come from Voiced.VoiceSet and tooltips from explicit fluent keys, which are
    inherited unchanged. UpdatesPlayerStatistics counts the twin as the base actor.
  * The twin removes `ReplaceOnPrerequisite@BALANCE` (it must not replace itself) and
    `MapEditorData` (twins do not appear in the map editor), when the base has them.

The overlay gives each twinned base actor `~balance.reimagined` in its Buildable
prerequisites and `ReplaceOnPrerequisite@BALANCE` into its twin, except for actors
that their creator configures after spawning them (spawner/carrier children and
support power aircraft), which never get the replacement trait. Mod buildables that
RA2 defines but cannot build get only `~balance.reimagined`. Actors without a twin
(e.g. hornet, civilian vehicles) get the twins of twinned repair/rearm hosts appended
to their HOST_FIELDS lists (exact.snapshot ignores exactly these appended names).
"""
from __future__ import annotations

from collections.abc import Collection, Mapping

from exact import conversions
from exact.model import ExactActor
from exact.snapshot import HOST_FIELDS, IGNORED_TRAIT, REIMAGINED_TOKEN, is_buildable_trait

SUFFIX = ".exact"
EXACT_TOKEN = "~balance.exact"
EXACT_PREREQUISITE = "balance.exact"
REMOVED_EDITOR_TRAIT = "MapEditorData"
BASE_PREREQUISITE_TRAIT = "ProvidesPrerequisite@BASE"

# RA2 armour (exact.model.ARMOR_ORDER) -> the mod's Armor Type.
ARMOR_TYPES = {
    "none": "None", "flak": "Flak", "plate": "Plate", "light": "Light", "medium": "Medium",
    "heavy": "Heavy", "wood": "Wood", "steel": "Steel", "concrete": "Concrete",
    "special_1": "Drone", "special_2": "Rocket",
}

# trait type -> fields holding actors that the trait creates: the twin replaces the base.
SPAWN_FIELDS = {
    "Transforms": ("IntoActor",),
    "DeploysTo": ("IntoActor",),
    "FreeActor": ("Actor",),
    "ParatroopersPower": ("DropItems",),
    "CarrierParent": ("Actors",),
    "BaseSpawnerParent": ("Actors",),
    "SpawnActorsOnSell": ("ActorTypes",),
    "SpawnSurvivors": ("Actors",),
}

# trait type -> fields holding actors to match against: the twin is added beside the base.
MATCH_FIELDS = {
    "Repairable": ("RepairActors",),
    "RepairableNear": ("RepairActors",),
    "TransformsIntoRepairable": ("RepairActors",),
    "Rearmable": ("RearmActors",),
    "GrantConditionOnProduction": ("Actors",),
}

# Traits whose Image defaults to the actor's own name (not to the RenderSprites image).
NAME_IMAGE_TRAITS = ("WithDecoration", "ChronoshiftableWithSpriteEffect",
    "ChronoResourceDelivery")


def trait_type(key: str) -> str:
    return key.split("@", 1)[0]


def twin_name(actor_id: str) -> str:
    return actor_id.lower() + SUFFIX


def split_list(value: str) -> list[str]:
    return [p.strip() for p in value.split(",") if p.strip()]


def spawned_children(base_actors: Mapping[str, dict]) -> set[str]:
    """Actors that a parent creates and then configures: spawner/carrier children and
    support power aircraft. ReplaceOnPrerequisite must never be put on them."""
    found: set[str] = set()
    for tree in base_actors.values():
        for key, trait in tree.items():
            if not isinstance(trait, dict):
                continue
            kind = trait_type(key)
            if kind in ("CarrierParent", "BaseSpawnerParent") and isinstance(trait.get("Actors"), str):
                found.update(split_list(trait["Actors"]))
            if kind.endswith("Power") and isinstance(trait.get("UnitType"), str):
                found.update(split_list(trait["UnitType"]))
    return found


def _prerequisites(trait: dict, token: str) -> str:
    value = trait.get("Prerequisites")
    kept = [p for p in split_list(value) if p != REIMAGINED_TOKEN] if isinstance(value, str) else []
    return ", ".join([*kept, token])


def _rewrite(value: str, twins: Collection[str], beside: bool) -> str:
    out = []
    for name in split_list(value):
        if name in twins:
            out.extend((name, name + SUFFIX) if beside else (name + SUFFIX,))
        else:
            out.append(name)
    return ", ".join(out)


def twin_traits(actor: ExactActor, base: Mapping, *, twins: Collection[str] = (),
        weapons: Mapping[str, str] | None = None) -> tuple[dict[str, dict[str, str]], list[str]]:
    """Trait overrides of the twin, and notes about RA2 values that have no trait to go to."""
    base_id = actor.id.lower()
    traits: dict[str, dict[str, str]] = {}
    notes: list[str] = []

    def put(key: str, field: str, value) -> None:
        traits.setdefault(key, {})[field] = str(value)

    def put_on(kind: str, field: str, value, label: str) -> None:
        if kind in base:
            put(kind, field, value)
        else:
            notes.append(f"{base_id}: {label} has no {kind} trait to go to")

    for key, trait in sorted(base.items()):
        if is_buildable_trait(key) and isinstance(trait, dict):
            put(key, "Prerequisites", _prerequisites(trait, EXACT_TOKEN))
            put(key, "BuildDuration", conversions.minutes_to_ticks(actor.build_minutes))
            put(key, "BuildDurationModifier", 100)

    put_on("Valued", "Cost", actor.cost, f"Cost={actor.cost}")
    put_on("Health", "HP", actor.strength, f"Strength={actor.strength}")
    put_on("Armor", "Type", ARMOR_TYPES[actor.armor], f"Armor={actor.armor}")
    locomotion = next((k for k in ("Mobile", "Aircraft") if k in base), None)
    for field, ini_key, convert in (("Speed", "Speed", conversions.speed_to_openra),
            ("TurnSpeed", "ROT", conversions.rot_to_turnspeed)):
        if ini_key not in actor.raw:
            continue
        value = convert(actor.speed if ini_key == "Speed" else actor.rot)
        turrets = sorted(k for k in base if trait_type(k) == "Turreted")
        if locomotion:
            put(locomotion, field, value)
        elif actor.kind == "building" and ini_key == "ROT" and turrets:
            # A building's ROT= turns its turret.
            for key in turrets:
                put(key, field, value)
        else:
            notes.append(f"{base_id}: {ini_key}={actor.raw[ini_key]} has no Mobile, Aircraft"
                + (" or Turreted" if actor.kind == "building" else "") + " trait to go to")
    put_on("RevealsShroud", "Range", conversions.cells_to_wdist(actor.sight), f"Sight={actor.sight}")
    if actor.power or "Power" in base:
        put_on("Power", "Amount", actor.power, f"Power={actor.power}")
    if actor.kind == "building":
        put(BASE_PREREQUISITE_TRAIT, "Prerequisite", base_id)

    for key, trait in sorted(base.items()):
        if not isinstance(trait, dict):
            continue
        kind = trait_type(key)
        if kind == "Armament" and isinstance(trait.get("Weapon"), str):
            weapon = trait["Weapon"].strip()
            put(key, "Weapon", (weapons or {}).get(weapon.lower(), weapon) + SUFFIX)
        for fields, beside in ((SPAWN_FIELDS.get(kind, ()), False), (MATCH_FIELDS.get(kind, ()), True)):
            for field in fields:
                value = trait.get(field)
                if isinstance(value, str) and _rewrite(value, twins, beside) != ", ".join(split_list(value)):
                    put(key, field, _rewrite(value, twins, beside))
        if kind in ("RenderSprites", "RenderVoxels"):
            put(key, "Image", trait.get("Image") or base_id)
        elif kind in NAME_IMAGE_TRAITS and not trait.get("Image"):
            put(key, "Image", base_id)
        elif kind == "UpdatesPlayerStatistics" and not trait.get("OverrideActor"):
            put(key, "OverrideActor", base_id)
    return traits, notes


def render_twin(actor: ExactActor, base: dict, *, twins: Collection[str] = (),
        weapons: Mapping[str, str] | None = None, replaced: bool = True,
        traits: Mapping[str, Mapping[str, str]] | None = None) -> str:
    """MiniYaml of the twin `<id>.exact` of `actor` over its resolved base actor `base`.

    twins: base IDs (lowercase) that have a twin, for rewriting actor references.
    weapons: lowercase weapon name -> defined name (OpenRA weapon names are case-insensitive).
    replaced: whether the base carries ReplaceOnPrerequisite@BALANCE (the twin removes it).
    traits: the trait overrides to write (default: twin_traits; exact.generate passes them
    after exact.exceptions has adjusted them).
    """
    if traits is None:
        traits, _ = twin_traits(actor, base, twins=twins, weapons=weapons)
    lines = [f"{twin_name(actor.id)}:", f"\tInherits: {actor.id.lower()}"]
    if replaced:
        lines.append(f"\t-{IGNORED_TRAIT}:")
    if REMOVED_EDITOR_TRAIT in base:
        lines.append(f"\t-{REMOVED_EDITOR_TRAIT}:")
    for key in sorted(traits):
        lines.append(f"\t{key}:")
        lines.extend(f"\t\t{field}: {value}" for field, value in traits[key].items())
    return "\n".join(lines) + "\n"


def appended_twins(value: str, twins: Collection[str]) -> str:
    """An actor list with the twin of every twinned entry appended at the end."""
    names = split_list(value)
    extra = []
    for name in names:
        if name in twins and name + SUFFIX not in names and name + SUFFIX not in extra:
            extra.append(name + SUFFIX)
    return ", ".join([*names, *extra])


def appended_host_fields(tree: Mapping, twins: Collection[str]) -> list[tuple[str, str, str]]:
    """(trait key, field, new list) for each HOST_FIELDS list of `tree` that appended_twins
    changes, in trait key order."""
    out = []
    for key, trait in sorted(tree.items()):
        field = HOST_FIELDS.get(trait_type(key))
        value = trait.get(field) if field and isinstance(trait, dict) else None
        if isinstance(value, str) and appended_twins(value, twins) != ", ".join(split_list(value)):
            out.append((key, field, appended_twins(value, twins)))
    return out


def render_reimagined_overlay(base_actors: dict, *, twins: Collection[str] | None = None,
        no_replace: Collection[str] = (), reimagined_only: Collection[str] = ()) -> str:
    """MiniYaml limiting base actors to Reimagined and letting untwinned actors use Exact hosts.

    base_actors: actor ID -> resolved tree (without a previous overlay: see strip_ignored).
    twins: the IDs that have a twin; default: every actor in base_actors not in reimagined_only.
      A twinned actor gets `~balance.reimagined` in its Buildable prerequisites and
      ReplaceOnPrerequisite@BALANCE into its twin (unless listed in no_replace).
    reimagined_only: untwinned actors that only get `~balance.reimagined`.
    Every untwinned actor gets the twins of twinned hosts appended to its HOST_FIELDS lists,
    so it can repair and rearm at Exact facilities. Actors with nothing to add are left out.
    """
    if twins is None:
        twins = set(base_actors) - set(reimagined_only)
    blocks = []
    for name in sorted(base_actors):
        lines = [f"{name}:"]
        tree = base_actors[name]
        if name in twins or name in reimagined_only:
            for key, trait in sorted(tree.items()):
                if is_buildable_trait(key) and isinstance(trait, dict):
                    lines += [f"\t{key}:", f"\t\tPrerequisites: {_prerequisites(trait, REIMAGINED_TOKEN)}"]
        if name in twins:
            if name not in no_replace:
                lines += [f"\t{IGNORED_TRAIT}:", f"\t\tPrerequisite: {EXACT_PREREQUISITE}",
                    f"\t\tIntoActor: {twin_name(name)}"]
        else:
            for key, field, value in appended_host_fields(tree, twins):
                lines += [f"\t{key}:", f"\t\t{field}: {value}"]
        if len(lines) > 1:
            blocks.append("\n".join(lines) + "\n")
    return "\n".join(blocks)
