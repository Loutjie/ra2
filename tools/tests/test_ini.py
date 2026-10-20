from pathlib import Path

from ra2data.ini import parse_ini, ra2ini_dir


def test_parse_ini_strips_comments_and_keeps_case(tmp_path):
    f = tmp_path / "a.ini"
    f.write_text("[HTNK]\nCost=900 ; comment\nPrimary=120mm\n")
    assert parse_ini(str(f)) == {"HTNK": {"Cost": "900", "Primary": "120mm"}}


def test_parse_ini_last_value_wins(tmp_path):
    f = tmp_path / "a.ini"
    f.write_text("; header\n[A]\nX=1\nX=2\nnoequals\n")
    assert parse_ini(str(f)) == {"A": {"X": "2"}}


def test_ra2ini_dir_env(monkeypatch, tmp_path):
    monkeypatch.setenv("RA2INI", str(tmp_path))
    assert ra2ini_dir() == tmp_path
    monkeypatch.setenv("RA2INI", str(tmp_path / "missing"))
    assert ra2ini_dir() is None


def test_skips_without_ra2ini(pytester, monkeypatch, tmp_path):
    tools = Path(__file__).resolve().parents[1]
    monkeypatch.setenv("RA2INI", str(tmp_path / "missing"))
    pytester.makeconftest((tools / "conftest.py").read_text())
    pytester.makepyfile("def test_x(ra2ini):\n    raise AssertionError('should skip')\n")
    pytester.syspathinsert(tools)
    pytester.runpytest("-p", "no:cacheprovider", "-p", "no:asyncio").assert_outcomes(skipped=1)
