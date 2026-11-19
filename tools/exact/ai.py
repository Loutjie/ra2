"""The AI's actor lists with the Exact twins added (mods/ra2/rules/exact/ai.yaml).

The bot modules of mods/ra2/rules/ai.yaml name actors: comma lists (ConstructionYardTypes,
McvTypes, NavalUnitsTypes, ...) and dictionaries keyed by actor (BuildingFractions,
BuildingLimits, UnitsToBuild, ...). The generated file lists, for every one of those
fields that names a twinned actor:

  * a comma list again in full, with each twin beside its base (MiniYaml replaces a value);
  * a dictionary only with the added `<name>.exact` keys and the base's value (MiniYaml
    merges child nodes).

Which fields name actors is not hard-coded: every field of a `*BotModule`/`*BotManager`
trait is checked, and only names that have a twin are touched (queue names, terrain or
target types never do). The AI only builds what its queues offer, so in Reimagined the
twins are skipped (they need `~balance.exact`) and in Exact the bases are.

SupportPowerBotModule decisions are keyed by the power's OrderName. A twin power that keeps
the base OrderName needs nothing; one whose Exact side uses another OrderName (a twin
override, or an `X@EXACT` / `X@REIMAGINED` pair of a neutral tech building) gets a copy of
the decision, `<decision>.exact`, with the Exact OrderName.
"""
from __future__ import annotations

from collections.abc import Collection, Mapping

from exact.actors import SUFFIX, _rewrite, split_list, trait_type
from exact.globals import _lines
from exact.snapshot import EXACT_SUFFIX, REIMAGINED_SUFFIX

AI_SOURCE = "mods/ra2/rules/ai.yaml"
SUPPORT_POWER_MODULE = "SupportPowerBotModule"


def is_bot_module(key: str) -> bool:
    kind = trait_type(key)
    return kind.endswith(("BotModule", "BotManager")) and kind != SUPPORT_POWER_MODULE


def _field(value, twins: Collection[str]):
    """The override of one bot module field, or None when it names no twinned actor."""
    if isinstance(value, str):
        return _rewrite(value, twins, beside=True) if any(n in twins for n in split_list(value)) else None
    if isinstance(value, Mapping) and "_value" not in value:
        added = {k + SUFFIX: v for k, v in value.items() if k in twins and isinstance(v, str)}
        return added or None
    return None


def renamed_orders(generated: Mapping[str, Mapping], bases: Mapping[str, Mapping]) -> dict[str, str]:
    """Reimagined OrderName -> Exact OrderName of every support power whose Exact side renames it.

    generated: the parsed generated rules (twins and neutral pairs); bases: base actor -> tree.
    """
    out: dict[str, str] = {}
    for actor, traits in generated.items():
        for key, trait in traits.items():
            if not isinstance(trait, Mapping) or "OrderName" not in trait:
                continue
            if key.endswith(EXACT_SUFFIX):
                other = traits.get(key[: -len(EXACT_SUFFIX)] + REIMAGINED_SUFFIX)
            elif actor.endswith(SUFFIX):
                other = bases.get(actor[: -len(SUFFIX)], {}).get(key)
            else:
                continue
            old = other.get("OrderName") if isinstance(other, Mapping) else None
            if old and old != trait["OrderName"]:
                out[old] = trait["OrderName"]
    return out


def ai_overrides(source: Mapping[str, Mapping], twins: Collection[str],
        renamed: Mapping[str, str]) -> dict[str, dict]:
    """Actor -> trait -> overridden fields, in source order."""
    twins = set(twins)
    out: dict[str, dict] = {}
    for actor, traits in source.items():
        if not isinstance(traits, Mapping):
            continue
        for key, trait in traits.items():
            if not isinstance(trait, Mapping):
                continue
            fields: dict = {}
            if trait_type(key) == SUPPORT_POWER_MODULE:
                decisions = trait.get("Decisions")
                added = {name + SUFFIX: {**d, "OrderName": renamed[d["OrderName"]]}
                    for name, d in (decisions.items() if isinstance(decisions, Mapping) else ())
                    if isinstance(d, Mapping) and d.get("OrderName") in renamed}
                if added:
                    fields["Decisions"] = added
            elif is_bot_module(key):
                for name, value in trait.items():
                    changed = _field(value, twins)
                    if changed is not None:
                        fields[name] = changed
            if fields:
                out.setdefault(actor, {})[key] = fields
    return out


def render_ai(source: Mapping[str, Mapping], twins: Collection[str], renamed: Mapping[str, str]) -> str:
    """MiniYaml of the AI overrides ("" when no list names a twinned actor)."""
    blocks = [f"{actor}:\n" + "\n".join(_lines(traits, 1)) + "\n"
        for actor, traits in ai_overrides(source, twins, renamed).items()]
    return "\n".join(blocks)
