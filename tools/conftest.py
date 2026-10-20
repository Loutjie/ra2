"""Shared pytest fixtures for the Python data tools."""
from __future__ import annotations

import pytest

from ra2data import ini

pytest_plugins = ["pytester"]


@pytest.fixture(scope="session")
def ra2ini():
    """Parsed original rules.ini (session-scoped, treat as read-only), or skip when absent."""
    folder = ini.ra2ini_dir()
    if folder is None:
        pytest.skip("original game INI files not found (set $RA2INI)")
    path = folder / ini.RULES_FILE
    if not path.is_file():
        pytest.skip(f"{ini.RULES_FILE} not found in {folder}")
    return ini.parse_ini(str(path))
