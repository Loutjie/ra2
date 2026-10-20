"""Lenient parser for the original Westwood INI files."""
import os
from pathlib import Path

DEFAULT_DIR = Path.home() / "Projects" / "ra2-reference" / "ini"
RULES_FILE = "ra2.mix__local.mix__rules.ini"


def parse_ini(path: str) -> dict[str, dict[str, str]]:
    """Parse an INI file into {section: {key: value}}.

    Strips ';' comments, keeps the last value of repeated keys and preserves
    key case. Lines outside a section and lines without '=' are ignored.
    """
    sections: dict[str, dict[str, str]] = {}
    current = None
    with open(path, encoding="latin-1") as f:
        for line in f:
            line = line.split(";", 1)[0].strip()
            if line.startswith("[") and "]" in line:
                current = sections.setdefault(line[1:line.index("]")], {})
            elif current is not None and "=" in line:
                key, value = line.split("=", 1)
                current[key.strip()] = value.strip()
    return sections


def ra2ini_dir() -> Path | None:
    """$RA2INI or the default folder if it exists, else None."""
    folder = Path(os.environ.get("RA2INI") or DEFAULT_DIR)
    return folder if folder.is_dir() else None
