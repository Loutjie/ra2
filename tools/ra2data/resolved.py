"""Resolved actor/weapon rules of the built mod, via utility --resolved-rules."""
import hashlib
import os
import re
import subprocess
import sys
import tempfile
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]


def engine_version() -> str:
    m = re.search(r'^ENGINE_VERSION="?([^"\n]+)"?', (REPO_ROOT / "mod.config").read_text(), re.M)
    return m.group(1) if m else ""


MOD_DLL = REPO_ROOT / "engine" / "bin" / "OpenRA.Mods.RA2.dll"


def mods_hash() -> str:
    """sha1 of the engine version, the mod dll's mtime/size and the relative
    paths, mtimes and sizes of mods/**/*.yaml."""
    h = hashlib.sha1(engine_version().encode())
    try:
        st = MOD_DLL.stat()
        h.update(f"dll|{st.st_mtime_ns}|{st.st_size}\n".encode())
    except OSError:
        h.update(b"dll|missing\n")
    for p in sorted((REPO_ROOT / "mods").rglob("*.yaml")):
        st = p.stat()
        h.update(f"{p.relative_to(REPO_ROOT).as_posix()}|{st.st_mtime_ns}|{st.st_size}\n".encode())
    return h.hexdigest()


def cache_dir() -> Path:
    return Path(tempfile.gettempdir()) / f"ra2-resolved-{mods_hash()}"


def cells(value: str) -> float:
    """Parse a WDist such as '5c768' into cells (5.75)."""
    m = re.fullmatch(r"\s*(-?)(\d+)c(\d+)\s*", value)
    if not m:
        raise ValueError(f"not a cell distance: {value!r}")
    result = int(m.group(2)) + int(m.group(3)) / 1024
    return -result if m.group(1) else result


def parse_miniyaml(text: str) -> dict[str, dict]:
    """Parse tab-indented MiniYaml (tabs only, no space indentation) into nested dicts.

    Keys keep their '@' suffixes and '-' removal prefixes. A node's own value
    is stored under '_value' when it also has children; leaves map to strings.
    """
    root: dict = {}
    stack = [(-1, root)]
    for raw in text.splitlines():
        if not raw.strip() or raw.lstrip().startswith("#"):
            continue
        depth = len(raw) - len(raw.lstrip("\t"))
        key, _, value = raw.strip().partition(":")
        key, value = key.strip(), value.strip()
        while stack[-1][0] >= depth:
            stack.pop()
        parent = stack[-1][1]
        node = {"_value": value} if value else {}
        parent[key] = node
        stack.append((depth, node))
    _collapse(root)
    return root


def _collapse(node: dict) -> None:
    """Turn childless {'_value': v} nodes into plain strings, in place."""
    for key, child in list(node.items()):
        if key == "_value":
            continue
        if set(child) == {"_value"}:
            node[key] = child["_value"]
        else:
            _collapse(child)


def _utility() -> list[str]:
    return [str(REPO_ROOT / ("utility.cmd" if sys.platform == "win32" else "utility.sh"))]


def _resolve(flag: str, kind: str, name: str) -> dict[str, dict]:
    cache = cache_dir() / kind
    cached = cache / f"{name}.yaml"
    if cached.is_file():
        return parse_miniyaml(cached.read_text(encoding="utf-8"))
    result = subprocess.run(_utility() + [flag, name], cwd=REPO_ROOT,
                            capture_output=True, text=True, encoding="utf-8", check=False)
    if result.returncode != 0:
        detail = "\n".join(t.strip() for t in (result.stdout, result.stderr) if t.strip())
        raise RuntimeError(f"utility {flag} {name} failed ({result.returncode}): {detail}")
    cache.mkdir(parents=True, exist_ok=True)
    fd, tmp = tempfile.mkstemp(dir=cache, suffix=".tmp")
    with os.fdopen(fd, "w", encoding="utf-8") as f:
        f.write(result.stdout)
    os.replace(tmp, cached)
    return parse_miniyaml(result.stdout)


def resolve_actor(name: str) -> dict[str, dict]:
    return _resolve("--resolved-rules", "actors", name)


def resolve_weapon(name: str) -> dict[str, dict]:
    return _resolve("--resolved-weapons", "weapons", name)
