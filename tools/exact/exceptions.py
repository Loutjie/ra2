"""Exact conversion exceptions: every place the Exact files are not a field-by-field copy of
the base RA2 rules.ini, with the reason.

Where the mod models a mechanic differently, Exact keeps RA2's behaviour rather than a literal
copy of its numbers, and each such case is an entry of EXCEPTIONS:
  * with a `rule`: exact.generate computes every twin first, then calls each rule in list
    order with the exception and the Generated values; the rule adjusts them in place (a
    weapon twin's values, a stub filled from another RA2 section, a twin turned into a stub,
    an actor twin's trait overrides) and returns lines saying what it changed;
  * without a rule: the generator itself makes the deviation (exact.conversions,
    exact.weapons, exact.actors, exact.generate); `listed` names what it affected.
render_report turns both into mods/ra2/rules/exact/EXCEPTIONS.md.
"""
from __future__ import annotations

from collections.abc import Callable, Mapping
from dataclasses import dataclass

from exact import conversions
from exact.actors import ARMAMENT_TRAITS, SUFFIX, trait_type
from exact.model import load_weapon
from exact.weapons import ARCING_SPEED, NOT_FIRED, POINT_SPREAD, WeaponTwin, make_twin

REPORT_FILE = "mods/ra2/rules/exact/EXCEPTIONS.md"

# engine/OpenRA.Mods.Cnc/Traits/Attack/AttackTesla.cs, AttackTeslaInfo.InitialChargeDelay.
ATTACK_TESLA_INITIAL_CHARGE_DELAY = 22


@dataclass
class Generated:
    """The values exact.generate computed, which rules adjust in place."""
    rules: dict                                     # parsed rules.ini
    bases: Mapping[str, dict]                       # twinned base actor ID -> resolved tree
    traits: dict[str, dict[str, dict[str, str]]]    # twinned base actor ID -> twin trait overrides
    weapons: dict[str, WeaponTwin]                  # mod weapon name -> twin
    resolve_weapon: Callable[[str], dict]           # mod weapon name -> resolved tree

    def weapon(self, name: str) -> WeaponTwin | None:
        return next((t for n, t in self.weapons.items() if n.lower() == name.lower()), None)


@dataclass(frozen=True)
class ConversionException:
    id: str
    field: str
    reason: str
    # (exception, generated values) -> lines describing the change; None: made by the generator.
    rule: Callable[[ConversionException, Generated], list[str]] | None = None
    # report facts -> the affected items, for exceptions without a rule.
    listed: Callable[[Mapping], list[str]] | None = None


# --- rules ----------------------------------------------------------------------------------

def tesla_charge(exc: ConversionException, gen: Generated) -> list[str]:
    """Keep the time between shots of every AttackTesla twin equal to RA2's ROF.

    AttackTesla waits InitialChargeDelay before each volley and refills its charges
    ReloadDelay ticks after the last shot; the armament also waits its weapon's ReloadDelay.
    One shot every max(trait ReloadDelay + charge, weapon ReloadDelay) ticks. With
    reload = (fastest weapon's RA2 ROF in ticks) - charge on the trait, the fastest weapon
    gets ReloadDelay = reload (the trait sets the pace) and a slower one keeps its converted
    ROF (the armament sets the pace), so each fires once per its own RA2 ROF. MaxCharges 1:
    RA2 fires one bolt per ROF, not a volley.
    """
    lines = []
    adjusted: dict[str, int] = {}
    for base_id, tree in sorted(gen.bases.items()):
        keys = [k for k in tree if trait_type(k) == "AttackTesla" and isinstance(tree[k], Mapping)]
        if not keys:
            continue
        twins = []
        for key, trait in sorted(tree.items()):
            if trait_type(key) in ARMAMENT_TRAITS and isinstance(trait, Mapping) and isinstance(trait.get("Weapon"), str):
                t = gen.weapon(trait["Weapon"].strip())
                if t is None or t.values is None:
                    raise ValueError(f"{exc.id}: {base_id} fires {trait['Weapon']}, whose twin is a stub: "
                        "the charge timing cannot be kept")
                twins.append(t)
        fastest = min(int(t.values["ReloadDelay"]) for t in twins)
        for key in keys:
            charge = int(tree[key].get("InitialChargeDelay", ATTACK_TESLA_INITIAL_CHARGE_DELAY))
            reload = fastest - charge
            if reload < 1:
                raise ValueError(f"{exc.id}: {base_id}: RA2 ROF of {fastest} ticks is shorter than "
                    f"the {charge}-tick charge")
            override = {"ReloadDelay": str(reload)}
            if tree[key].get("MaxCharges", "1").strip() != "1":
                override["MaxCharges"] = "1"
            gen.traits.setdefault(base_id, {}).setdefault(key, {}).update(override)
            lines.append(f"{base_id}{SUFFIX}: {key} " + ", ".join(f"{f} {v}" for f, v in override.items())
                + f"; charge {charge} + reload {reload} = {fastest} ticks")
        for t in sorted(twins, key=lambda t: t.name.lower()):
            ticks = int(t.values["ReloadDelay"])
            new = reload if ticks == fastest else ticks
            if adjusted.setdefault(t.name, new) != new:
                raise ValueError(f"{exc.id}: {t.name} is fired by AttackTesla actors with different timings")
            if new != ticks:
                t.values["ReloadDelay"] = new
                t.exception = exc.id
                t.comments.append(f"Exception {exc.id}: ReloadDelay {new} = RA2 ROF {ticks} ticks "
                    f"- {charge}-tick charge")
                lines.append(f"{t.twin}: ReloadDelay {new} (RA2 ROF {ticks} ticks - charge {charge})")
            else:
                lines.append(f"{t.twin}: ReloadDelay {ticks} (RA2 ROF; the armament sets the pace)")
    return lines


def _fill_from(pairs: Mapping[str, str]):
    """A rule filling each stub `name` from RA2 section pairs[name], if the mod weapon takes it."""
    def rule(exc: ConversionException, gen: Generated) -> list[str]:
        lines = []
        for name, section in pairs.items():
            t, w = gen.weapon(name), load_weapon(gen.rules, section)
            if t is None or w is None:
                continue
            new = make_twin(gen.rules, t.name, w, gen.resolve_weapon(t.name))
            t.exception = exc.id
            if new.values is None:
                t.reason = f"{exc.id}: RA2 {w.id} does not fit: {new.reason}"
                lines.append(f"{t.twin}: kept as a stub ({new.reason})")
                continue
            t.values, t.note, t.source, t.reason = new.values, new.note, w.id, None
            t.comments.append(f"Exception {exc.id}: values from RA2 {w.id}")
            lines.append(f"{t.twin}: values from RA2 {w.id}")
        return lines
    rule.__name__ = "fill_from"
    return rule


def ivan_bomb(exc: ConversionException, gen: Generated) -> list[str]:
    """RA2 timing and range for the bomb-planting weapons, and the bomb timer from
    [CombatDamage] IvanTimedDelay. The explosion (IvanDeath, IvanDeathE) keeps the mod's
    falloff; its 400 damage is RA2's IvanDamage."""
    timed = gen.rules.get("CombatDamage", {}).get("IvanTimedDelay", "").strip()
    lines = []
    for name in ("IvanBomber", "IvanBomberE"):
        t, w = gen.weapon(name), load_weapon(gen.rules, name)
        if t is None or w is None or t.reason == NOT_FIRED or not timed:
            continue
        delay = conversions.frames_to_ticks(float(timed))
        t.values = {"ReloadDelay": conversions.frames_to_ticks(w.rof),
            "Range": conversions.cells_to_wdist(w.range),
            "Warhead@bomb": {"_value": "AttachIvanBomb", "Timer": delay}}
        t.reason, t.exception = None, exc.id
        t.comments.append(f"Exception {exc.id}: RA2 timing and range; bomb timer from IvanTimedDelay")
        lines.append(f"{t.twin}: ReloadDelay, Range from RA2 {w.id}; bomb Timer {delay} (IvanTimedDelay={timed})")
    return lines


def chrono_erasure(exc: ConversionException, gen: Generated) -> list[str]:
    """RA2's temporal weapons: range from RA2, and the erase rate (ChronoErase Warp, warp
    points per tick) from RA2's Damage, which a temporal beam applies per frame."""
    lines = []
    ticks_per_1000_frames = conversions.frames_to_ticks(1000)
    for name in ("NeutronRifle", "NeutronRifleE", "CRNeutronRifle"):
        t, w = gen.weapon(name), load_weapon(gen.rules, name)
        if t is None or w is None or t.reason == NOT_FIRED:
            continue
        warp = round(w.damage * 1000 / ticks_per_1000_frames)
        t.values = {"Range": conversions.cells_to_wdist(w.range),
            "Warhead@Erase": {"_value": "ChronoErase", "Warp": warp}}
        t.reason, t.exception = None, exc.id
        t.comments.append(f"Exception {exc.id}: RA2 range; erase rate from Damage per frame")
        lines.append(f"{t.twin}: Range from RA2 {w.id}; Warp {warp} per tick (Damage={w.damage} per frame)")
    return lines


def mind_control(exc: ConversionException, gen: Generated) -> list[str]:
    """Timing and range from RA2; the damage stays 0 (RA2's Damage= on a mind control
    warhead takes control of the target and does not damage it)."""
    lines = []
    for name in ("MindControl", "SuperMindControl"):
        t, w = gen.weapon(name), load_weapon(gen.rules, name)
        if t is None or w is None or t.reason == NOT_FIRED:
            continue
        t.values = {"ReloadDelay": conversions.frames_to_ticks(w.rof),
            "Range": conversions.cells_to_wdist(w.range), "Burst": w.burst}
        t.reason, t.exception = None, exc.id
        t.comments.append(f"Exception {exc.id}: RA2 timing and range; mod damage (0) kept")
        lines.append(f"{t.twin}: ReloadDelay, Range, Burst from RA2 {w.id}; RA2 Damage={w.damage} not applied")
    return lines


def _stub(*names: str):
    """A rule keeping each twin `names` an Inherits stub (Reimagined values)."""
    def rule(exc: ConversionException, gen: Generated) -> list[str]:
        lines = []
        for name in names:
            t = gen.weapon(name)
            if t is None:
                continue
            t.values, t.note, t.source = None, None, None
            t.reason, t.exception = f"{exc.id} (see EXCEPTIONS.md)", exc.id
            lines.append(f"{t.twin}: Inherits stub")
        return lines
    rule.__name__ = "stub"
    return rule


# --- listings for generator-made exceptions --------------------------------------------------

def _stubs_without_rule(facts: Mapping) -> list[str]:
    return [f"{e['twin']}: {e['reason']}" + (f" (RA2 counterpart: {e['ra2_counterpart']})"
        if "ra2_counterpart" in e else "") for e in facts["exceptions"] if "exception" not in e]


def _no_counterpart(facts: Mapping) -> list[str]:
    return [e["weapon"] + (f" (stands in: {', '.join(e['mod_twins'])})" if "mod_twins" in e else "")
        for e in facts["ra2_without_mod_counterpart"]]


def _notes(word: str):
    def listed(facts: Mapping) -> list[str]:
        return [f"{n['twin']}: {n['note']}" for n in facts["notes"] if f" is {word}" in n["note"]]
    return listed


def _frame_rate(_facts: Mapping) -> list[str]:
    return [f"RA2_FRAMES_PER_SECOND = {conversions.RA2_FRAMES_PER_SECOND:g} "
        f"(OPENRA_TICKS_PER_SECOND = {conversions.OPENRA_TICKS_PER_SECOND})",
        f"MINUTES_ARE_FRAMES = {conversions.MINUTES_ARE_FRAMES}"]


def _globals(exc_id: str):
    """The lines exact.globals noted under `exc_id`."""
    def listed(facts: Mapping) -> list[str]:
        return list(facts.get("globals", {}).get(exc_id, []))
    return listed


def _fact(key: str):
    def listed(facts: Mapping) -> list[str]:
        return list(facts.get(key, []))
    return listed


EXCEPTIONS: list[ConversionException] = [
    ConversionException("tesla-charge",
        "AttackTesla ReloadDelay (and MaxCharges) of twins with AttackTesla (atesla.exact, "
        "tesla.exact); ReloadDelay of their weapons (PrismShot.exact, CoilBolt.exact, "
        "OPCoilBolt.exact)",
        "The mod splits RA2's ROF into a charge-up (AttackTesla InitialChargeDelay) and a reload "
        "(AttackTesla ReloadDelay, with a short weapon ReloadDelay), and the Tesla Coil fires "
        "volleys of three bolts. Exact keeps charge + reload equal to RA2's ROF and fires one "
        "bolt per ROF, as RA2 does. The Tesla Coil's overpowered OPCoilBolt (RA2 ROF 100) and "
        "normal CoilBolt (ROF 120) share one AttackTesla: the trait is timed for the faster "
        "weapon and the slower one's own ReloadDelay holds it to its ROF. RA2 values are used "
        "for everything else (damage, range, Versus).",
        rule=tesla_charge),
    ConversionException("mind-control",
        "MindControl.exact, SuperMindControl.exact: ReloadDelay, Range, Burst only",
        "RA2's Damage=10 sits on the Controller warhead, which takes control of the target "
        "instead of damaging it; the mod's MindController trait does that and the weapon's "
        "TargetDamage stays at 0. Exact takes RA2's ROF and Range and keeps 0 damage.",
        rule=mind_control),
    ConversionException("chrono-commando-mp5",
        "MP5.exact <- RA2 ChronoMP5, MP5E.exact <- RA2 ChronoMP5E",
        "The mod's Chrono Commando fires MP5/MP5E; RA2's CCOMAND fires ChronoMP5/ChronoMP5E "
        "(RA2's MP5 belongs to another unit). The values come from the sections the RA2 unit "
        "fires.",
        rule=_fill_from({"MP5": "ChronoMP5", "MP5E": "ChronoMP5E"})),
    ConversionException("demo-truck-targeting",
        "DemoTruckTargeting.exact (Terrorist, Demolition Truck)",
        "RA2's Terrorist and Demolition Truck fire TerrorBomb/Demobomb, suicide weapons whose "
        "damage is the explosion. The mod's actors fire DemoTruckTargeting, a no-damage "
        "targeting weapon that triggers KillsSelf, and the explosion is their "
        "FireWarheadsOnDeath weapon (TerrorBomb, demobomb). The targeting twin stays a stub; "
        "the explosions are twinned as RA2 death weapons (see death-weapons).",
        rule=_stub("DemoTruckTargeting")),
    ConversionException("attack-dog",
        "DogJaw.exact",
        "RA2's BadTeeth (ParasiteDog warhead, Damage 30, ROF 30, Range 1.5) is a leap that "
        "latches onto infantry; the mod's DogJaw is an instant TargetDamage hit (Damage 200). "
        "TargetDamage takes neither Spread nor Falloff, and the latch has no field to map, so "
        "the twin stays a stub.",
        rule=_stub("DogJaw")),
    ConversionException("dog-adog",
        "dog.exact",
        "The mod has one dog actor for both factions; RA2 has DOG (Soviet) and ADOG (Allied), "
        "whose balance values differ only in their weapon (BadTeeth, GoodTeeth). dog.exact takes DOG's values; "
        "ADOG has no mod actor (listed under ra2-buildables-without-mod-actor).",
        listed=lambda facts: ["dog.exact <- RA2 DOG"] if "dog" in facts.get("twins", ()) else []),
    ConversionException("terror-drone",
        "DroneJump.exact",
        "RA2's DroneJump puts the Terror Drone inside the target vehicle, where it deals damage "
        "over time; the mod models it as a jump attack with target damage (inheriting DogJaw). "
        "The twin stays a stub.",
        rule=_stub("DroneJump")),
    ConversionException("giant-squid",
        "SquidPunch.exact; RA2 SquidGrab",
        "RA2's Giant Squid grabs a ship (SquidGrab, ParasitePlus latch) and punches "
        "(SquidPunch). The mod's squid only punches: SquidPunch.exact takes RA2's SquidPunch "
        "values, and SquidGrab has no mod counterpart.",
        listed=lambda facts: ["SquidPunch.exact <- RA2 SquidPunch"]
            if "SquidPunch.exact" in facts.get("generated_weapons", ()) else []),
    ConversionException("crazy-ivan-bomb",
        "IvanBomber.exact, IvanBomberE.exact",
        "RA2's Crazy Ivan plants a bomb (IvanBomb) whose explosion is set in [CombatDamage] "
        "(IvanDamage, IvanTimedDelay), not on IvanBomber. The twins take RA2's ROF and range and "
        "the bomb timer; the explosion keeps the mod's falloff.",
        rule=ivan_bomb),
    ConversionException("prism-tank-beam",
        "Comet.exact, SuperComet.exact",
        "RA2's Prism Tank beam splits into secondary beams; the mod models the split as area "
        "damage (Spread 800). Writing RA2's CellSpread would remove it. The twins stay stubs.",
        rule=_stub("Comet", "SuperComet")),
    ConversionException("chrono-erasure",
        "NeutronRifle.exact, NeutronRifleE.exact, CRNeutronRifle.exact",
        "RA2's temporal beam (Temporal=yes) erases its target at Damage warp points per frame "
        "against Strength x 10; its Damage is not damage, and the beam holds until it stops, so ROF "
        "does not set the pace. The twins take RA2's range and the ChronoErase Warp converted to ticks.",
        rule=chrono_erasure),
    ConversionException("weapon-structure-stubs",
        "Twin weapons kept as Inherits stubs by exact.weapons",
        "No RA2 section of that name, a section no twinned RA2 actor fires, or a mod weapon "
        "whose damage warheads do not take RA2's values (none, several, not SpreadDamage, or "
        "explicit Range steps). Nothing is invented: the stub keeps Reimagined values.",
        listed=_stubs_without_rule),
    ConversionException("no-mod-counterpart",
        "RA2 weapons of twinned actors that no twin uses",
        "Weapons the mod's actors have no armament for (e.g. Sapper, FakeC4, MakeupKit, "
        "PrismSupport, the elite variants the mod does not split). They are not generated.",
        listed=_no_counterpart),
    ConversionException("arcing-projectile-speed",
        "Projectile Speed of twins whose RA2 projectile is Arcing",
        f"RA2 Arcing projectiles ignore the weapon's Speed= and fly at a fixed {ARCING_SPEED} "
        "leptons per frame (ModEnc, \"Arcing\"), so that speed is converted.",
        listed=_notes("Arcing")),
    ConversionException("inviso-vertical-projectile-speed",
        "Projectile Speed of twins whose RA2 projectile is Inviso or Vertical",
        "Inviso projectiles hit instantly and Vertical ones drop, so the weapon's Speed= has no "
        "flight speed to convert: the mod projectile's speed is kept.",
        listed=lambda facts: _notes("Inviso")(facts) + _notes("Vertical")(facts)),
    ConversionException("cellspread-zero",
        f"Damage warhead Spread of twins with RA2 CellSpread 0 ({POINT_SPREAD}, Falloff 100, 100)",
        "RA2 CellSpread 0 damages only the object hit. OpenRA Spread 0 would deal no damage, "
        f"so the twin gets the smallest spread, {POINT_SPREAD}, at full damage: point damage.",
        listed=_fact("point_spread")),
    ConversionException("dropped-actor-fields",
        "RA2 actor values without a trait to go to",
        "A value is only written to a trait the base actor has; these RA2 values are dropped "
        "(e.g. a wall's Sight= with no RevealsShroud).",
        listed=_fact("dropped")),
    ConversionException("construction-yards",
        "gacnst.exact, nacnst.exact",
        "RA2 does not list its construction yards as buildable, but its MCVs deploy into them "
        "(DeploysInto/UndeploysInto), so they are twinned too: the twins are the RA2 buildables "
        "with a mod actor plus what they deploy into or undeploy from.",
        listed=_fact("deploy_added")),
    ConversionException("reimagined-only-actors",
        "Mod buildables that RA2 defines but cannot build",
        "No twin is generated; the base actor gets ~balance.reimagined and is Reimagined only "
        "(e.g. ghost).",
        listed=_fact("reimagined_only")),
    ConversionException("mod-only-actors",
        "Mod buildables without an RA2 section",
        "Nothing to convert: no twin is generated and the actor stays buildable in both modes "
        "with its Reimagined values.",
        listed=_fact("both_modes")),
    ConversionException("ra2-buildables-without-mod-actor",
        "RA2 buildables the mod has no actor for",
        "Nothing to twin; they are not generated.",
        listed=_fact("missing")),
    ConversionException("untwinned-actors-use-exact-hosts",
        "Repair and rearm host lists of actors without a twin (reimagined.yaml)",
        "Actors without a twin (e.g. hornet, civilian vehicles) can exist in Exact games, so "
        "the twins of their twinned repair/rearm hosts are appended to their host lists.",
        listed=_fact("exact_host_users")),
    ConversionException("veterancy",
        "GainsExperience Conditions and the rank multipliers of twins with GainsExperience "
        "(globals.yaml)",
        "RA2 promotes at VeteranRatio x the unit's cost of kills (x2 for elite) and grants each "
        "rank's abilities as flags: an elite unit has its VeteranAbilities plus its "
        "EliteAbilities, and an ability in both applies once. The mod's ^GainsExperience grows "
        "every bonus at each rank. Exact sets the thresholds to VeteranRatio x100 and x200 % of "
        "the value, each @VETERAN/@ELITE multiplier to VeteranCombat (FIREPOWER), "
        "1 / VeteranArmor (STRONGER), VeteranROF (ROF) or VeteranSpeed (FASTER) when the rank "
        "has that ability and 100 otherwise, InaccuracyMultiplier@ELITE to 100 (RA2 has no "
        "accuracy bonus), and keeps the self-heal (ChangesHealth@ELITE, at the mod's rate: RA2 "
        "states none) only with SELF_HEAL, from the first rank that has it.",
        listed=_globals("veterancy")),
    ConversionException("veterancy-abilities",
        "RA2 veteran/elite abilities without a mod mechanic",
        "SIGHT (VeteranSight=0.0, no sight change), SCATTER and other abilities have no rank "
        "trait in the mod; they are not applied.",
        listed=_globals("veterancy-abilities")),
    ConversionException("survivors",
        "SpawnSurvivors of twins (destroyed buildings)",
        "RA2: a Crewed=yes building releases its sell value (Cost x RefundPercent) divided by "
        "AlliedSurvivorDivisor or SovietSurvivorDivisor crew (AlliedCrew/SovietCrew), rounded "
        "down. The mod's lists are fixed per building. Exact writes that count of the crew of "
        "the side the mod's list uses (its other entries, e.g. the construction yards' "
        "engineer, are kept) and removes the trait when nothing is left. Survivors of a sold "
        "building (SpawnActorsOnSell) keep the mod's lists.",
        listed=_globals("survivors")),
    ConversionException("crew-escape",
        "EjectOnDeath on twins of RA2 Crewed=yes vehicles and aircraft",
        "RA2 lets the crew of a destroyed Crewed=yes vehicle escape with CrewEscape chance; the "
        "mod has no crew. Exact adds EjectOnDeath with SuccessRate = CrewEscape and the crew of "
        "the RA2 owners' side (AlliedCrew/SovietCrew; aircraft: Pilot). Crewed defaults to no "
        "(ModEnc, \"Crewed\"), so only actors that set it get a crew.",
        listed=_globals("crew-escape")),
    ConversionException("superweapons",
        "Support power ChargeInterval and durations of superweapon twins",
        "RechargeTime minutes and [CombatDamage]/[General] frame durations converted by "
        "exact.conversions; paratrooper drops are <ParaDropInf> x <ParaDropNum> of the twin "
        "infantry.",
        listed=_globals("superweapons")),
    ConversionException("lightning-storm",
        "Lightning Storm weapon twin and timing",
        "RA2 has no weapon section for the storm: its damage is [General] LightningDamage with "
        "the LightningWarhead's Verses, CellSpread and PercentAtMax, written onto the mod's "
        "storm weapon. LightningDeferment (delay before the storm starts) and "
        "LightningSeparation have no field in WeatherControlSupportPower and are not applied. "
        "LightningCellSpread=10 is not written: the power's default OffsetsX/OffsetsY of "
        "-5c0..5c0 already spread the clouds over a 10-cell area.",
        listed=_globals("lightning-storm")),
    ConversionException("iron-curtain-kills-infantry",
        "ExternalCondition@INVULNERABILITY and KillsSelf@IRONCURTAIN on infantry twins",
        "The Iron Curtain kills infantry in RA2. The base mod's ^Infantry already does this, so "
        "only infantry twins whose base lacks KillsSelf@IRONCURTAIN get the condition and KillsSelf.",
        listed=_globals("iron-curtain-kills-infantry")),
    ConversionException("death-weapons",
        "FireWarheadsOnDeath of twins whose RA2 actor has Explodes=yes and a DeathWeapon (#514)",
        "They fire the twin of their death weapon, with RA2 values where the mod weapon takes "
        "them (weapon-structure-stubs otherwise). Twins whose RA2 actor does not explode keep "
        "the mod's death weapons: most are visual only (no damage warhead); a damaging one the "
        "mod fires only under a condition is listed (fv.exact: CRNuke while it carries a "
        "terrorist, RA2's DeathWeapon=CRNuke). The neutral Oil Derrick (RA2 Explodes=yes, "
        "DeathWeapon=OilExplosion) is not twinned and keeps the mod's DerrickExplode.",
        listed=_globals("death-weapons")),
    ConversionException("death-weapon-strength",
        "Twins whose RA2 actor has Explodes=yes without a DeathWeapon",
        "RA2 fires [CombatDamage] DeathWeapon (DefaultDeathWeapon), whose damage \"is based on\" "
        "the unit's strength by a formula rules.ini does not give. Nothing is invented: the "
        "mod's death weapon is kept.",
        listed=_globals("death-weapon-strength")),
    ConversionException("spy-satellite",
        "RevealsMap of the Spy Satellite twin (#803): the base trait is kept",
        "RA2 has no fog of war: once the SpySat reveals the map, it stays visible for good "
        "(only Gap Generators shroud it again). The mod plays with fog of war, and its "
        "RevealsMap (live vision of the whole map while powered, !lowpower) is the closest "
        "equivalent, so the twin keeps it. A faithful mechanic, permanent passive visibility "
        "that yields to Gap Generators, needs a new trait and is a follow-up.",
        listed=_globals("spy-satellite")),
    ConversionException("neutral-tech-buildings",
        "Exact/Reimagined trait pairs of caairp, cahosp, caoild, caoutp, cathosp (globals.yaml)",
        "Tech buildings are not twinned: they are neutral when the map starts and change owner "
        "when captured. GrantConditionOnPrerequisite@BALANCE grants balance-exact while the "
        "owner has balance.exact, and each balance trait whose RA2 value differs from the mod's "
        "becomes a X@EXACT/X@REIMAGINED pair (the Reimagined side is copied from the mod's rule "
        "files without globals.yaml). Health (not conditional) keeps the mod's value (RA2's "
        "Strength is the same). The Exact paratroopers have their own OrderName (`...Exact`): "
        "powers sharing an OrderName share one timer. The hospitals keep the mod's repair cost "
        "(RA2 gives none for Hospital=yes).",
        listed=_globals("neutral-tech-buildings")),
    ConversionException("frame-rate",
        "Every time, speed and turn rate conversion (exact.conversions)",
        "RA2's frame rate is the measured 30 frames per second (speed slider position 4, chosen as the reference for OpenRA's "
        "Normal speed; tools/exact/TIMING.md section 4), so one RA2 frame is 0.8333 OpenRA ticks, "
        "and a rules.ini minute is the engine's 900 frames = 30 s = 750 ticks. Speed and ROT "
        "keep the mod's x15 / x4 factors scaled by the same ratio (an unmeasured hypothesis). "
        "rules.ini comments IronCurtainDuration=750 with \"In frames 900 is a minute for 15fps\"; "
        "that is the design basis, not the measured rate.",
        listed=_frame_rate),
]


def apply(gen: Generated, exceptions: list[ConversionException] = EXCEPTIONS) -> dict[str, list[str]]:
    """Run every rule in order on `gen`; exception id -> lines describing the change."""
    return {e.id: e.rule(e, gen) for e in exceptions if e.rule is not None}


def render_report(header: str, facts: Mapping, applied: Mapping[str, list[str]],
        exceptions: list[ConversionException] = EXCEPTIONS) -> str:
    """EXCEPTIONS.md: every exception with its field, reason and what it affected.

    facts: the weapon report of exact.weapons.render_weapons plus the generator's lists
    (dropped, deploy_added, reimagined_only, missing, exact_host_users, point_spread, ...).
    """
    out = [header.rstrip("\n"), "",
        "Every place the Exact twins (actors.yaml, reimagined.yaml, weapons/exact/weapons.yaml) "
        "differ from a field-by-field conversion of the base RA2 rules.ini. Each section is an "
        "entry of `tools/exact/exceptions.py`; those with a rule adjust the generated values, "
        "the others are made by the generator itself. Units and constants: "
        "`tools/exact/conversions.py` and `tools/exact/TIMING.md`.", ""]
    for e in exceptions:
        items = applied.get(e.id, []) if e.rule is not None else e.listed(facts) if e.listed else []
        how = f"rule `{e.rule.__name__}`" if e.rule is not None else "the generator"
        out += [f"## {e.id}", "", f"- Field: {e.field}", f"- Applied by: {how}",
            f"- Reason: {e.reason}", "- Affected:"]
        out += [f"  - {item}" for item in items] if items else ["  - (nothing in this run)"]
        out.append("")
    return "\n".join(out)

