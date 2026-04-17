#!/usr/bin/env python3
"""Validate the lightweight audio manifest used for sourced and procedural assets."""

from __future__ import annotations

import json
import pathlib
import sys

REPO_ROOT = pathlib.Path(__file__).resolve().parent.parent
MANIFEST_PATH = REPO_ROOT / "Assets" / "Audio" / "audio_manifest.json"
ATTRIBUTION_PATH = REPO_ROOT / "Assets" / "Audio" / "ATTRIBUTION.md"

ALLOWED_PATH_TYPES = {"file", "directory"}
ALLOWED_ASSET_TYPES = {"external_pack", "external_track", "generated_sample_set"}
ALLOWED_CATEGORIES = {
    "ui",
    "ambience",
    "foley",
    "combat",
    "music",
    "procedural_source",
}
ALLOWED_SOURCE_KINDS = {"external", "repo_generated"}


def fail(message: str) -> None:
    print(f"[audio-manifest] ERROR: {message}")
    raise SystemExit(1)


def require_string(value: object, field_name: str) -> str:
    if not isinstance(value, str) or not value.strip():
        fail(f"{field_name} must be a non-empty string")
    return value


def require_bool(value: object, field_name: str) -> bool:
    if not isinstance(value, bool):
        fail(f"{field_name} must be a boolean")
    return value


def require_string_list(value: object, field_name: str) -> list[str]:
    if not isinstance(value, list) or not value:
        fail(f"{field_name} must be a non-empty list")
    items: list[str] = []
    for item in value:
        items.append(require_string(item, f"{field_name}[]").lower())
    return items


def count_audio_files(path: pathlib.Path, formats: list[str]) -> int:
    allowed = {f".{ext}" for ext in formats}
    return sum(
        1
        for child in path.rglob("*")
        if child.is_file() and child.suffix.lower() in allowed
    )


def validate_root_paths(manifest: dict[str, object]) -> None:
    roots = manifest.get("roots")
    if not isinstance(roots, dict):
        fail("roots must be an object")

    for key in ("externalLibrary", "runtimeSamples", "proceduralSources"):
        root_value = require_string(roots.get(key), f"roots.{key}")
        root_path = REPO_ROOT / root_value
        if not root_path.exists() or not root_path.is_dir():
            fail(f"roots.{key} points to missing directory: {root_value}")

    require_string(roots.get("newExternalDropPattern"), "roots.newExternalDropPattern")
    require_string(
        roots.get("newProceduralDropPattern"),
        "roots.newProceduralDropPattern",
    )


def validate_entry(entry: dict[str, object], seen_ids: set[str]) -> None:
    entry_id = require_string(entry.get("id"), "entries[].id")
    if entry_id in seen_ids:
        fail(f"duplicate entry id: {entry_id}")
    seen_ids.add(entry_id)

    path_type = require_string(entry.get("pathType"), f"{entry_id}.pathType")
    if path_type not in ALLOWED_PATH_TYPES:
        fail(f"{entry_id}.pathType must be one of {sorted(ALLOWED_PATH_TYPES)}")

    asset_type = require_string(entry.get("assetType"), f"{entry_id}.assetType")
    if asset_type not in ALLOWED_ASSET_TYPES:
        fail(f"{entry_id}.assetType must be one of {sorted(ALLOWED_ASSET_TYPES)}")

    category = require_string(entry.get("category"), f"{entry_id}.category")
    if category not in ALLOWED_CATEGORIES:
        fail(f"{entry_id}.category must be one of {sorted(ALLOWED_CATEGORIES)}")

    path_value = require_string(entry.get("path"), f"{entry_id}.path")
    target_path = REPO_ROOT / path_value
    if not target_path.exists():
        fail(f"{entry_id}.path does not exist: {path_value}")

    if path_type == "file" and not target_path.is_file():
        fail(f"{entry_id}.pathType=file but target is not a file: {path_value}")
    if path_type == "directory" and not target_path.is_dir():
        fail(
            f"{entry_id}.pathType=directory but target is not a directory: {path_value}"
        )

    require_string(entry.get("usage"), f"{entry_id}.usage")
    formats = require_string_list(entry.get("format"), f"{entry_id}.format")

    duration_sec = entry.get("durationSec")
    duration_note = entry.get("durationNote")
    if duration_sec is None and not isinstance(duration_note, str):
        fail(f"{entry_id} must provide durationSec or durationNote")
    if duration_sec is not None and not isinstance(duration_sec, (int, float)):
        fail(f"{entry_id}.durationSec must be a number or null")
    if duration_note is not None:
        require_string(duration_note, f"{entry_id}.durationNote")

    source = entry.get("source")
    if not isinstance(source, dict):
        fail(f"{entry_id}.source must be an object")
    source_kind = require_string(source.get("kind"), f"{entry_id}.source.kind")
    if source_kind not in ALLOWED_SOURCE_KINDS:
        fail(
            f"{entry_id}.source.kind must be one of {sorted(ALLOWED_SOURCE_KINDS)}"
        )
    require_string(source.get("name"), f"{entry_id}.source.name")
    require_string(source.get("author"), f"{entry_id}.source.author")
    require_string(source.get("reference"), f"{entry_id}.source.reference")

    require_string(entry.get("license"), f"{entry_id}.license")
    license_url = entry.get("licenseUrl")
    if license_url is not None:
        require_string(license_url, f"{entry_id}.licenseUrl")
    require_bool(entry.get("attributionRequired"), f"{entry_id}.attributionRequired")
    require_string(entry.get("status"), f"{entry_id}.status")

    notes = entry.get("notes")
    if notes is not None:
        require_string(notes, f"{entry_id}.notes")

    if path_type == "file":
        actual_ext = target_path.suffix.lower().lstrip(".")
        if actual_ext not in formats:
            fail(
                f"{entry_id}.format does not include file extension '.{actual_ext}' for {path_value}"
            )
    else:
        file_count = entry.get("fileCount")
        if not isinstance(file_count, int) or file_count <= 0:
            fail(f"{entry_id}.fileCount must be a positive integer for directories")
        actual_count = count_audio_files(target_path, formats)
        if actual_count != file_count:
            fail(
                f"{entry_id}.fileCount={file_count} but found {actual_count} matching files in {path_value}"
            )


def validate_attribution(entries: list[dict[str, object]]) -> None:
    if not ATTRIBUTION_PATH.exists():
        fail(f"attribution file not found: {ATTRIBUTION_PATH}")

    attribution_text = ATTRIBUTION_PATH.read_text(encoding="utf-8")
    missing_ids = []
    for entry in entries:
        source = entry.get("source")
        if isinstance(source, dict) and source.get("kind") == "external":
            entry_id = entry.get("id")
            if isinstance(entry_id, str) and entry_id not in attribution_text:
                missing_ids.append(entry_id)

    if missing_ids:
        fail(
            "ATTRIBUTION.md is missing external entry ids: "
            + ", ".join(sorted(missing_ids))
        )


def main() -> None:
    if not MANIFEST_PATH.exists():
        fail(f"manifest not found: {MANIFEST_PATH}")

    try:
        manifest = json.loads(MANIFEST_PATH.read_text(encoding="utf-8"))
    except json.JSONDecodeError as exc:
        fail(f"invalid JSON: {exc}")

    if not isinstance(manifest, dict):
        fail("manifest root must be an object")

    schema_version = manifest.get("schemaVersion")
    if not isinstance(schema_version, int) or schema_version < 1:
        fail("schemaVersion must be a positive integer")

    require_string(manifest.get("updatedOn"), "updatedOn")
    validate_root_paths(manifest)

    rules = manifest.get("rules")
    if not isinstance(rules, list) or not rules:
        fail("rules must be a non-empty list")
    for index, rule in enumerate(rules):
        require_string(rule, f"rules[{index}]")

    entries = manifest.get("entries")
    if not isinstance(entries, list) or not entries:
        fail("entries must be a non-empty list")

    seen_ids: set[str] = set()
    for raw_entry in entries:
        if not isinstance(raw_entry, dict):
            fail("each entry must be an object")
        validate_entry(raw_entry, seen_ids)

    validate_attribution(entries)

    print(
        f"[audio-manifest] OK: validated {len(entries)} entries from {MANIFEST_PATH.relative_to(REPO_ROOT)}"
    )


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        sys.exit(130)
