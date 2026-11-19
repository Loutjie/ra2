"""Deploy-order rules over the mod's raw YAML (issue #757).

RA2 resolves a deploy order selection-wide: if any selected unit is undeployed,
every undeployed unit deploys and deployed ones stay put; only a fully deployed
selection undeploys. The engine's GrantConditionOnDeploy implements this when
SmartDeploy is true (a deployed actor declines the deploy-hotkey order while
another selected, in-world deployer is undeployed; transported units are not in
the world and are ignored), so every deployer in this mod must enable it.
"""
import re
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
RULES_DIR = REPO_ROOT / "mods" / "ra2" / "rules"

_KEY = re.compile(r"^(?P<indent>\t*)(?P<key>[^\s:#][^:]*?):(?P<value>.*)$")
_TRAIT = "GrantConditionOnDeploy"


def deployers():
    """Return {(file, actor, trait): SmartDeploy value or None} for each GrantConditionOnDeploy."""
    found = {}
    for path in sorted(RULES_DIR.glob("*.yaml")):
        actor = trait = None
        for raw in path.read_text(encoding="utf-8-sig").splitlines():
            if not raw.strip() or raw.lstrip().startswith("#"):
                continue
            m = _KEY.match(raw)
            if not m:
                continue
            depth, key = len(m["indent"]), m["key"].strip()
            value = m["value"].split("#")[0].strip()
            if depth == 0:
                actor, trait = key, None
            elif depth == 1:
                trait = key if key == _TRAIT or key.startswith(_TRAIT + "@") else None
                if trait:
                    found[(path.name, actor, trait)] = None
            elif depth == 2 and trait and key == "SmartDeploy":
                found[(path.name, actor, trait)] = value
    return found


def test_all_deployers_use_smart_deploy():
    found = deployers()
    assert found, "no GrantConditionOnDeploy found; scanner or paths are wrong"
    bad = sorted(k for k, v in found.items() if (v or "").lower() != "true")
    assert not bad, f"GrantConditionOnDeploy without SmartDeploy: true: {bad}"
