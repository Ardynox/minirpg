#!/usr/bin/env python3
"""
Validate that res://Assets/Art/... paths referenced in key Data/*.json files exist on disk.

Usage (repo root):
  python Tools/validate_art_res_paths.py
  python Tools/validate_art_res_paths.py --catalog        # include resource_catalog.json (many paths)
  python Tools/validate_art_res_paths.py --combat-audit    # combat_fx.json resourceId vs resource_catalog ids
  python Tools/validate_art_res_paths.py --no-faces       # skip Data/FaceParts/*.json
  python Tools/validate_art_res_paths.py --facilities-blueprint  # B11 blueprint PNGs per facility_* key

Exit 0 if no errors. Exit 1 on missing files or catalog drift.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path
from typing import Any, Iterator

ROOT = Path(__file__).resolve().parents[1]

# Primary JSON we expect to drive runtime loads of Art paths
DEFAULT_SCAN_FILES: tuple[str, ...] = (
    "Data/entity_render.json",
    "Data/item_world_render.json",
)

# Face customization (res://Assets/Faces/...)
DEFAULT_FACEPART_GLOB = "Data/FaceParts/*.json"

# Broader than Art-only: includes Assets/Faces for character customization
RES_ASSETS = re.compile(r"res://Assets/[^\s\"]+\.(?:png|jpg|jpeg|webp|tres|tscn)\b")


def iter_strings(obj: Any) -> Iterator[str]:
    if isinstance(obj, dict):
        for v in obj.values():
            yield from iter_strings(v)
    elif isinstance(obj, list):
        for x in obj:
            yield from iter_strings(x)
    elif isinstance(obj, str):
        yield obj


def iter_res_paths_in_file(path: Path) -> set[str]:
    data = json.loads(path.read_text(encoding="utf-8"))
    found: set[str] = set()
    for s in iter_strings(data):
        for m in RES_ASSETS.finditer(s):
            found.add(m.group(0))
    return found


def res_to_fs(res: str) -> Path:
    assert res.startswith("res://"), res
    rel = res[len("res://") :].lstrip("/")
    return ROOT / rel


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument(
        "--catalog",
        action="store_true",
        help="Include Data/resource_catalog.json (many paths; slower)",
    )
    ap.add_argument(
        "--combat-audit",
        action="store_true",
        help="Ensure every resourceId in combat_fx.json exists in resource_catalog entries",
    )
    ap.add_argument(
        "--no-faces",
        action="store_true",
        help="Skip Data/FaceParts/*.json (face part image paths)",
    )
    ap.add_argument(
        "--facilities-blueprint",
        action="store_true",
        help="Require facility_<id>_blueprint.png for each facility_* key in entity_render.json",
    )
    args = ap.parse_args()

    errors: list[str] = []

    files = list(DEFAULT_SCAN_FILES)
    if not args.no_faces:
        for fp in sorted(ROOT.glob(DEFAULT_FACEPART_GLOB)):
            files.append(str(fp.relative_to(ROOT)).replace("\\", "/"))
    if args.catalog:
        files.append("Data/resource_catalog.json")

    for rel in files:
        p = ROOT / rel
        if not p.is_file():
            errors.append(f"Missing JSON file: {rel}")
            continue
        res_paths = iter_res_paths_in_file(p)
        for res in sorted(res_paths):
            fs = res_to_fs(res)
            if fs.is_file():
                continue
            # Godot often pairs .png.import; missing png is still a hard error for exporters/CI without Godot reimport.
            errors.append(f"{rel} -> missing file: {res} (expected {fs})")

    if args.combat_audit:
        fx_path = ROOT / "Data" / "combat_fx.json"
        cat_path = ROOT / "Data" / "resource_catalog.json"
        if not fx_path.is_file() or not cat_path.is_file():
            errors.append("combat audit: combat_fx.json or resource_catalog.json missing")
        else:
            fx_text = fx_path.read_text(encoding="utf-8")
            ids_in_fx = set(re.findall(r'"resourceId"\s*:\s*"([^"]+)"', fx_text))
            catalog = json.loads(cat_path.read_text(encoding="utf-8"))
            entries = catalog.get("entries") or []
            cat_ids = {e.get("id") for e in entries if isinstance(e, dict)}
            for rid in sorted(ids_in_fx):
                if rid not in cat_ids:
                    errors.append(f"combat_fx resourceId '{rid}' not found in resource_catalog.entries[].id")

    if args.facilities_blueprint:
        er = ROOT / "Data" / "entity_render.json"
        if not er.is_file():
            errors.append("facilities-blueprint: entity_render.json missing")
        else:
            data = json.loads(er.read_text(encoding="utf-8"))
            bp_root = ROOT / "Assets" / "Art" / "Generated" / "facilities_blueprint"
            for key in sorted(data.keys()):
                if not key.startswith("facility_"):
                    continue
                fid = key[len("facility_") :]
                fs = bp_root / f"facility_{fid}_blueprint.png"
                if not fs.is_file():
                    errors.append(f"facilities-blueprint: missing {fs.relative_to(ROOT)}")

    for line in errors:
        print(f"ERROR {line}", file=sys.stderr)

    if errors:
        print(f"\nvalidate_art_res_paths: {len(errors)} error(s)", file=sys.stderr)
        return 1
    print("validate_art_res_paths: OK", file=sys.stderr)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
