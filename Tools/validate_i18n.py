from __future__ import annotations

import json
import re
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
I18N_DIR = ROOT / "Data" / "I18n"
MARKERS_FILE = ROOT / "Tools" / "mojibake_markers.txt"
IGNORED_DIRS = {".git", ".godot", ".claude", ".codex", "bin", "obj", "Artifacts", "data_MiniRPG_windows_x86_64"}
SCANNED_TEXT_EXTENSIONS = {".cs", ".json", ".py", ".tscn"}
KEY_PATTERN = re.compile(r"^[A-Za-z0-9_.-]+$")
CODE_KEY_PATTERN = re.compile(r'LocalizationService\.(?:T|TOrFallback|GetList)\(\s*"([^"]+)"')
SCENE_KEY_PATTERN = re.compile(r'^\s*(?:text|placeholder_text)\s*=\s*"([^"]+)"\s*$')
UNSUPPORTED_GLYPHS = set("⚔⚙✦✗⛏")


def load_catalog(locale: str) -> dict[str, str]:
    path = I18N_DIR / f"{locale}.json"
    return json.loads(path.read_text(encoding="utf-8"))


def load_mojibake_markers() -> list[str]:
    markers: list[str] = []
    for line in MARKERS_FILE.read_text(encoding="utf-8").splitlines():
        marker = line.strip()
        if not marker or marker.startswith("#"):
            continue
        markers.append(marker)
    return markers


def iter_files(suffix: str):
    for path in ROOT.rglob(f"*{suffix}"):
        if any(part in IGNORED_DIRS for part in path.parts):
            continue
        yield path


def iter_controlled_text_files():
    for path in ROOT.rglob("*"):
        if not path.is_file():
            continue
        if any(part in IGNORED_DIRS for part in path.parts):
            continue
        if path.suffix.lower() not in SCANNED_TEXT_EXTENSIONS:
            continue
        yield path


def collect_code_keys() -> set[str]:
    keys: set[str] = set()
    for path in iter_files(".cs"):
        text = path.read_text(encoding="utf-8")
        keys.update(match.group(1) for match in CODE_KEY_PATTERN.finditer(text))
    for path in iter_files(".tscn"):
        for line in path.read_text(encoding="utf-8").splitlines():
            match = SCENE_KEY_PATTERN.match(line)
            if not match:
                continue
            value = match.group(1)
            if KEY_PATTERN.match(value) and "." in value:
                keys.add(value)
    return keys


def validate_catalog_shape(name: str, catalog: dict[str, str], errors: list[str]) -> None:
    if not isinstance(catalog, dict):
        errors.append(f"{name} is not a JSON object.")
        return
    for key, value in catalog.items():
        if not isinstance(value, str):
            errors.append(f"{name} key '{key}' is not a string.")


def validate_zh_integrity(catalog: dict[str, str], errors: list[str]) -> None:
    bad_keys = []
    for key, value in catalog.items():
        if "?" in value or "\ufffd" in value:
            bad_keys.append(key)
    if bad_keys:
        errors.append(f"zh_CN contains suspicious placeholder text in {len(bad_keys)} keys.")
        errors.extend(f"  {key}" for key in bad_keys[:20])


def validate_unsupported_glyphs(name: str, catalog: dict[str, str], errors: list[str]) -> None:
    bad_keys = []
    for key, value in catalog.items():
        if any(ord(ch) > 0xFFFF or ch in UNSUPPORTED_GLYPHS for ch in value):
            bad_keys.append(key)
    if bad_keys:
        errors.append(f"{name} contains unsupported bundled-font glyphs in {len(bad_keys)} keys.")
        errors.extend(f"  {key}" for key in bad_keys[:20])


def validate_key_parity(zh: dict[str, str], en: dict[str, str], errors: list[str]) -> None:
    missing_in_zh = sorted(set(en) - set(zh))
    missing_in_en = sorted(set(zh) - set(en))
    if missing_in_zh:
        errors.append(f"zh_CN is missing {len(missing_in_zh)} keys present in en.")
        errors.extend(f"  {key}" for key in missing_in_zh[:20])
    if missing_in_en:
        errors.append(f"en is missing {len(missing_in_en)} keys present in zh_CN.")
        errors.extend(f"  {key}" for key in missing_in_en[:20])


def validate_code_references(code_keys: set[str], zh: dict[str, str], en: dict[str, str], errors: list[str]) -> None:
    missing = []
    for key in sorted(code_keys):
        missing_locales = []
        if key not in zh:
            missing_locales.append("zh_CN")
        if key not in en:
            missing_locales.append("en")
        if missing_locales:
            missing.append((key, ", ".join(missing_locales)))
    if missing:
        errors.append(f"Found {len(missing)} referenced localization keys missing from catalogs.")
        errors.extend(f"  {key} [{locales}]" for key, locales in missing[:30])


def validate_repo_text_files(markers: list[str], errors: list[str]) -> None:
    hits: list[tuple[str, str]] = []
    decode_failures: list[str] = []
    for path in iter_controlled_text_files():
        try:
            text = path.read_text(encoding="utf-8")
        except UnicodeDecodeError:
            decode_failures.append(str(path.relative_to(ROOT)))
            continue

        for marker in markers:
            if marker in text:
                hits.append((str(path.relative_to(ROOT)), marker))
                break

    if decode_failures:
        errors.append(f"Found {len(decode_failures)} controlled text files that are not valid UTF-8.")
        errors.extend(f"  {path}" for path in decode_failures[:20])

    if hits:
        errors.append(f"Found known mojibake markers in {len(hits)} controlled text files.")
        errors.extend(f"  {path} [{marker}]" for path, marker in hits[:30])


def main() -> int:
    errors: list[str] = []
    zh = load_catalog("zh_CN")
    en = load_catalog("en")
    code_keys = collect_code_keys()
    markers = load_mojibake_markers()

    validate_catalog_shape("zh_CN", zh, errors)
    validate_catalog_shape("en", en, errors)
    validate_zh_integrity(zh, errors)
    validate_unsupported_glyphs("zh_CN", zh, errors)
    validate_unsupported_glyphs("en", en, errors)
    validate_key_parity(zh, en, errors)
    validate_code_references(code_keys, zh, en, errors)
    validate_repo_text_files(markers, errors)

    if errors:
        print("i18n validation failed:")
        for error in errors:
            print(error)
        return 1

    print(f"i18n validation passed. keys={len(zh)} referenced={len(code_keys)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
