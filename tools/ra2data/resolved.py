"""Resolved actor/weapon rules of the built mod, via utility --resolved-rules."""
from __future__ import annotations

import hashlib
import os
import re
import shutil
import subprocess
import tempfile
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]


def config_value(name: str, default: str = "") -> str:
    """Value of `name` from mod.config, overridden by user.config (as utility.sh does)."""
    value = default
    for fname in ("mod.config", "user.config"):
        path = REPO_ROOT / fname
        if not path.is_file():
            continue
        m = re.search(rf'^{name}="?([^"\r\n]*)"?', path.read_text(encoding="utf-8"), re.M)
        if m:
            value = m.group(1)
    return value


def engine_version() -> str:
    return config_value("ENGINE_VERSION")


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


def parse_miniyaml(text: str) -> dict[str, str | dict]:
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


def utility_command(flag: str, name: str, dotnet: str = "dotnet", mods_dir: Path | None = None):
    """Build (argv, env_overrides, cwd) to run the Utility directly, as utility.sh
    and make.ps1 do: from the engine directory, with ENGINE_DIR=".." and
    MOD_SEARCH_PATHS covering the mod's own mods folder (or `mods_dir` in its place)."""
    mod_id = config_value("MOD_ID")
    if not mod_id:
        raise RuntimeError("MOD_ID is not set in mod.config/user.config")
    engine_dir = (REPO_ROOT / config_value("ENGINE_DIRECTORY", "./engine")).resolve()
    dll = engine_dir / "bin" / "OpenRA.Utility.dll"
    if not dll.is_file():
        raise RuntimeError(f"{dll} not found; run `make` (or make.ps1 all) to build the engine first")
    argv = [dotnet, str(dll), mod_id, flag, name]
    env = {"MOD_SEARCH_PATHS": f"{mods_dir or REPO_ROOT / 'mods'},./mods", "ENGINE_DIR": ".."}
    return argv, env, engine_dir


def cache_file(cache: Path, name: str) -> Path:
    """Collision- and traversal-safe cache path: readable sanitised prefix + sha1 of the exact name."""
    safe = re.sub(r"[^A-Za-z0-9_.-]", "_", name)[:40].lstrip(".") or "_"
    digest = hashlib.sha1(name.encode("utf-8")).hexdigest()
    return cache / f"{safe}-{digest}.yaml"


def mods_without(rules_files: tuple[str, ...]) -> Path:
    """A mods folder mirroring the repository's (by symlinks) whose mod.yaml lacks the given
    `Rules:` entries (paths relative to the mod, e.g. "rules/exact/globals.yaml"); for reading
    the rules as they resolve without some files. Built once per content hash, in the temp dir."""
    mod_id = config_value("MOD_ID")
    excluded = "|".join(sorted(rules_files))
    digest = hashlib.sha1(f"{mods_hash()}|{excluded}".encode()).hexdigest()
    target = Path(tempfile.gettempdir()) / f"ra2-mods-without-{digest}"
    if target.is_dir():
        return target
    staging = Path(tempfile.mkdtemp(prefix="ra2-mods-staging-"))
    for entry in (REPO_ROOT / "mods").iterdir():
        if entry.name != mod_id:
            (staging / entry.name).symlink_to(entry)
            continue
        mod = staging / entry.name
        mod.mkdir()
        for child in entry.iterdir():
            if child.name != "mod.yaml":
                (mod / child.name).symlink_to(child)
        drop = {f"{mod_id}|{f}" for f in rules_files}
        lines = (entry / "mod.yaml").read_text(encoding="utf-8").splitlines(keepends=True)
        kept = [line for line in lines if line.strip() not in drop]
        if len(lines) - len(kept) != len(drop):
            raise RuntimeError(f"mod.yaml does not list every one of {sorted(drop)}")
        (mod / "mod.yaml").write_text("".join(kept), encoding="utf-8")
    try:
        os.replace(staging, target)
    except OSError:
        shutil.rmtree(staging, ignore_errors=True)  # another process built it first
    return target


def _resolve(flag: str, kind: str, name: str, without: tuple[str, ...] = ()) -> dict:
    cache = cache_dir() / kind
    if without:
        cache = cache_dir() / f"{kind}-without-{hashlib.sha1('|'.join(sorted(without)).encode()).hexdigest()[:12]}"
    cached = cache_file(cache, name)
    if cached.is_file():
        tree = parse_miniyaml(cached.read_text(encoding="utf-8"))
        if tree:
            return tree
    dotnet = shutil.which("dotnet")
    if not dotnet:
        raise RuntimeError("dotnet was not found on PATH")
    if without:
        argv, env, cwd = utility_command(flag, name, dotnet, mods_without(without))
    else:
        argv, env, cwd = utility_command(flag, name, dotnet)
    result = subprocess.run(argv, cwd=cwd, env={**os.environ, **env},
                            capture_output=True, text=True, encoding="utf-8", check=False)
    if result.returncode != 0:
        detail = "\n".join(t.strip() for t in (result.stdout, result.stderr) if t.strip())
        raise RuntimeError(f"utility {flag} {name} failed ({result.returncode}): {detail}")
    tree = parse_miniyaml(result.stdout)
    if not tree:
        raise RuntimeError(f"utility {flag} {name} produced no rules (stderr: {result.stderr.strip()!r})")
    cache.mkdir(parents=True, exist_ok=True)
    fd, tmp = tempfile.mkstemp(dir=cache, suffix=".tmp")
    with os.fdopen(fd, "w", encoding="utf-8") as f:
        f.write(result.stdout)
    os.replace(tmp, cached)
    return tree


def resolve_actor(name: str, without: tuple[str, ...] = ()) -> dict[str, str | dict]:
    """Resolved rules of actor `name`.

    Returns a nested dict keyed by trait/field names (suffixes such as '@PRIMARY'
    and '-' removal prefixes are kept). Each value is a str (leaf), an empty dict
    (node with neither value nor children) or a dict. A node that has both a
    value and children stores its own value under the key "_value".
    Raises RuntimeError if the utility fails or yields nothing.
    without: mod.yaml `Rules:` files to leave out (see mods_without).
    """
    return _resolve("--resolved-rules", "actors", name, without)


def resolve_weapon(name: str) -> dict[str, str | dict]:
    """Resolved weapon definition of `name`; same shape as resolve_actor."""
    return _resolve("--resolved-weapons", "weapons", name)
