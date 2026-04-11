from __future__ import annotations

import json
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
MANIFEST_PATH = ROOT / "Assets" / "Art" / "Placeholders" / "manifest.json"
CATALOG_PATH = ROOT / "Data" / "resource_catalog.json"
PLACEHOLDER_ROOT = ROOT / "Assets" / "Art" / "Placeholders"
PLACEHOLDER_RES_ROOT = "res://Assets/Art/Placeholders"

EFFECT_FPS = {
    "fx_slash_arc": 18.0,
    "fx_hit_blunt": 16.0,
    "fx_arrow_projectile": 18.0,
    "fx_poison_spit": 16.0,
    "fx_buff_flash": 14.0,
    "fx_pickup_glint": 12.0,
}


def to_res_path(relative_path: str) -> str:
    normalized = relative_path.replace("\\", "/").lstrip("/")
    return f"{PLACEHOLDER_RES_ROOT}/{normalized}"


def resolve_placeholder_path(relative_path: str) -> Path:
    return PLACEHOLDER_ROOT / Path(relative_path)


def collect_animation_frames(effect_id: str) -> list[dict[str, object]]:
    folder = PLACEHOLDER_ROOT / "effects" / effect_id
    if not folder.is_dir():
        return []

    frame_paths = sorted(folder.glob("*.png"))
    if len(frame_paths) <= 1:
        return []

    frames: list[dict[str, object]] = []
    for index, path in enumerate(frame_paths):
        frames.append(
            {
                "imagePath": to_res_path(str(path.relative_to(PLACEHOLDER_ROOT))),
                "order": index,
            }
        )
    return frames


def build_catalog_entry(source: dict[str, object]) -> dict[str, object]:
    entry_id = str(source["id"])
    category = str(source.get("category", ""))
    tags = list(source.get("tags", []))
    result: dict[str, object] = {
        "id": entry_id,
        "displayName": str(source.get("name", entry_id)),
        "description": str(source.get("description", "")),
        "tags": tags,
        "category": category,
    }

    animation_frames = collect_animation_frames(entry_id) if category == "effects" else []
    if animation_frames:
        result.update(
            {
                "kind": "animation",
                "sourceMode": "frames",
                "sourceFolderPath": to_res_path(f"effects/{entry_id}"),
                "frames": animation_frames,
                "fps": EFFECT_FPS.get(entry_id, 12.0),
            }
        )
        return result

    frames = source.get("frames", [])
    if not isinstance(frames, list) or not frames:
        raise ValueError(f"Manifest entry {entry_id} is missing frames.")

    image_path = str(frames[0]["imagePath"])
    absolute_path = resolve_placeholder_path(image_path)
    if not absolute_path.is_file():
        raise FileNotFoundError(f"Missing placeholder image for {entry_id}: {absolute_path}")

    result.update(
        {
            "kind": "tile",
            "sourceMode": "single",
            "sourceImagePath": to_res_path(image_path),
        }
    )
    return result


def main() -> None:
    manifest = json.loads(MANIFEST_PATH.read_text(encoding="utf-8"))
    raw_entries = manifest.get("entries", [])
    catalog_entries = [build_catalog_entry(entry) for entry in raw_entries]
    catalog_entries.sort(key=lambda entry: str(entry["id"]).lower())

    document = {
        "version": 1,
        "entries": catalog_entries,
    }
    CATALOG_PATH.write_text(
        json.dumps(document, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
        newline="\n",
    )
    print(f"Wrote {len(catalog_entries)} catalog entries to {CATALOG_PATH}")


if __name__ == "__main__":
    main()
