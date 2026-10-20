"""Shared pytest fixtures for the Python data tools."""
import pytest

from ra2data import ini


@pytest.fixture
def ra2ini():
    """Parsed original rules.ini, or skip when the game data is absent."""
    folder = ini.ra2ini_dir()
    if folder is None:
        pytest.skip("original game INI files not found (set $RA2INI)")
    path = folder / ini.RULES_FILE
    if not path.is_file():
        pytest.skip(f"{ini.RULES_FILE} not found in {folder}")
    return ini.parse_ini(str(path))
