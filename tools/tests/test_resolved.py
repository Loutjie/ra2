import os

import pytest

from ra2data import resolved
from ra2data.resolved import cells, parse_miniyaml


def test_cells():
    assert cells("5c768") == 5.75
    assert cells("8c0") == 8.0
    assert cells("-1c512") == -1.5


def test_parse_miniyaml_nesting_and_suffixes():
    text = (
        "Valued:\n\tCost: 900\n"
        "Armament@PRIMARY:\n\tWeapon: 120mm\n"
        "Warhead@1Dam: SpreadDamage\n\tDamage: 50\n\tVersus:\n\t\tNone: 100\n\t\tHeavy: 80\n"
        "-Removed:\n"
    )
    t = parse_miniyaml(text)
    assert t["Valued"]["Cost"] == "900"
    assert t["Armament@PRIMARY"]["Weapon"] == "120mm"
    assert t["Warhead@1Dam"]["_value"] == "SpreadDamage"
    assert t["Warhead@1Dam"]["Versus"] == {"None": "100", "Heavy": "80"}
    assert "-Removed" in t


def test_cache_invalidates_on_yaml_change(monkeypatch):
    monkeypatch.setattr(resolved, "mods_hash", lambda: "aaa")
    first = resolved.cache_dir()
    monkeypatch.setattr(resolved, "mods_hash", lambda: "bbb")
    assert resolved.cache_dir() != first


@pytest.mark.integration
def test_resolve_actor_htnk():
    t = resolved.resolve_actor("htnk")
    assert t["Valued"]["Cost"] == "900"
    assert t["Health"]["HP"] == "400"


@pytest.mark.integration
def test_missing_actor_error_includes_utility_output():
    with pytest.raises(RuntimeError, match="Could not find actor 'nosuch_actor'"):
        resolved.resolve_actor("nosuch_actor")


def test_hash_changes_with_yaml_and_dll(monkeypatch, tmp_path):
    (tmp_path / "mods" / "ra2").mkdir(parents=True)
    (tmp_path / "mod.config").write_text('ENGINE_VERSION="x"\n')
    yaml = tmp_path / "mods" / "ra2" / "a.yaml"
    yaml.write_text("A:\n")
    dll = tmp_path / "mod.dll"
    monkeypatch.setattr(resolved, "REPO_ROOT", tmp_path)
    monkeypatch.setattr(resolved, "MOD_DLL", dll)
    missing = resolved.mods_hash()
    dll.write_text("1")
    with_dll = resolved.mods_hash()
    assert with_dll != missing
    dll.write_text("22")
    assert resolved.mods_hash() != with_dll
    base = resolved.mods_hash()
    os.utime(dll, ns=(1, 1))
    assert resolved.mods_hash() != base
    base = resolved.mods_hash()
    yaml.write_text("A:\n\tB: 1\n")
    assert resolved.mods_hash() != base
    base = resolved.mods_hash()
    os.utime(yaml, ns=(1, 1))
    assert resolved.mods_hash() != base


def test_cache_file_is_collision_and_traversal_safe(tmp_path):
    a, b = resolved.cache_file(tmp_path, "HTNK"), resolved.cache_file(tmp_path, "htnk")
    assert a != b and a.name.lower() != b.name.lower()
    evil = resolved.cache_file(tmp_path, "../x")
    assert evil.parent == tmp_path
    assert resolved.cache_file(tmp_path, "a/b") != resolved.cache_file(tmp_path, "a_b")


def test_utility_command(monkeypatch, tmp_path):
    (tmp_path / "engine" / "bin").mkdir(parents=True)
    (tmp_path / "engine" / "bin" / "OpenRA.Utility.dll").write_text("")
    (tmp_path / "mod.config").write_text('MOD_ID="mymod"\nENGINE_DIRECTORY="./engine"\n')
    monkeypatch.setattr(resolved, "REPO_ROOT", tmp_path)
    argv, env, cwd = resolved.utility_command("--resolved-rules", "htnk", "/x/dotnet")
    engine = (tmp_path / "engine").resolve()
    assert argv == ["/x/dotnet", str(engine / "bin" / "OpenRA.Utility.dll"),
                    "mymod", "--resolved-rules", "htnk"]
    assert cwd == engine
    assert env["ENGINE_DIR"] == ".."
    assert env["MOD_SEARCH_PATHS"] == f"{tmp_path / 'mods'},./mods"
    (tmp_path / "user.config").write_text("MOD_ID=other\n")
    assert resolved.utility_command("--resolved-weapons", "w")[0][2] == "other"


def test_utility_command_requires_built_engine(monkeypatch, tmp_path):
    (tmp_path / "mod.config").write_text("MOD_ID=m\n")
    monkeypatch.setattr(resolved, "REPO_ROOT", tmp_path)
    with pytest.raises(RuntimeError, match="not found"):
        resolved.utility_command("--resolved-rules", "x")


def test_empty_output_is_not_cached(monkeypatch, tmp_path):
    monkeypatch.setattr(resolved, "cache_dir", lambda: tmp_path)
    monkeypatch.setattr(resolved.shutil, "which", lambda _: "/x/dotnet")
    monkeypatch.setattr(resolved, "utility_command", lambda f, n, d: ([d], {}, tmp_path))

    class Done:
        returncode, stdout, stderr = 0, "", ""
    monkeypatch.setattr(resolved.subprocess, "run", lambda *a, **k: Done())
    with pytest.raises(RuntimeError, match="no rules"):
        resolved.resolve_actor("x")
    assert not list(tmp_path.rglob("*.yaml"))


def test_mods_without_drops_only_the_named_rules(tmp_path, monkeypatch):
    from ra2data import resolved
    repo = tmp_path / "repo"
    (repo / "mods" / "fake" / "rules").mkdir(parents=True)
    (repo / "mods" / "other").mkdir()
    (repo / "mod.config").write_text('MOD_ID="fake"\n', encoding="utf-8")
    (repo / "mods" / "fake" / "mod.yaml").write_text(
        "Rules:\n\tfake|rules/a.yaml\n\tfake|rules/b.yaml\n", encoding="utf-8")
    (repo / "mods" / "fake" / "rules" / "a.yaml").write_text("x:\n", encoding="utf-8")
    monkeypatch.setattr(resolved, "REPO_ROOT", repo)
    monkeypatch.setattr(resolved, "MOD_DLL", repo / "missing.dll")
    monkeypatch.setattr(resolved.tempfile, "tempdir", str(tmp_path / "tmp"))
    (tmp_path / "tmp").mkdir()
    out = resolved.mods_without(("rules/b.yaml",))
    assert (out / "fake" / "mod.yaml").read_text(encoding="utf-8") == "Rules:\n\tfake|rules/a.yaml\n"
    assert (out / "fake" / "rules" / "a.yaml").read_text(encoding="utf-8") == "x:\n"
    assert (out / "other").resolve() == (repo / "mods" / "other").resolve()
    assert resolved.mods_without(("rules/b.yaml",)) == out
    with pytest.raises(RuntimeError, match="does not list"):
        resolved.mods_without(("rules/nope.yaml",))
