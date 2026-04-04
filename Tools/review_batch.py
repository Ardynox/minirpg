from __future__ import annotations

import argparse
import hashlib
import json
import subprocess
import sys
from collections import Counter
from datetime import datetime
from pathlib import Path
from typing import Any


GROUP_ORDER = ["root", "Core", "Module", "Scene", "Data", "Tools", "Docs", "Balin", "other"]
TEXT_BATCH_LIMIT = 25
FAST_BATCH_LIMIT = 60
PLACEHOLDER_TOKEN = "[PENDING]"
SUMMARY_STATUS_BEGIN = "<!-- REVIEW:STATUS:BEGIN -->"
SUMMARY_STATUS_END = "<!-- REVIEW:STATUS:END -->"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Manage the hourly batch-review workflow for a git repository."
    )
    parser.add_argument(
        "command",
        choices=["prepare", "complete", "status"],
        help="Workflow command to run.",
    )
    parser.add_argument(
        "--root",
        type=Path,
        default=Path.cwd(),
        help="Repository root. Defaults to the current working directory.",
    )
    parser.add_argument(
        "--batch-id",
        help="Batch identifier to complete. Defaults to the active batch.",
    )
    parser.add_argument(
        "--now",
        help="Override the current local timestamp with an ISO-8601 value.",
    )
    parser.add_argument(
        "--json",
        action="store_true",
        help="Emit machine-readable JSON.",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    root = args.root.resolve()
    now = parse_now(args.now)
    manifest_path = review_dir(root) / "manifest.json"

    git_files = list_git_files(root)
    manifest = load_manifest(manifest_path)
    manifest = sync_manifest(manifest, git_files, root)
    ensure_review_workspace(root, manifest, now)

    if args.command == "prepare":
        result = prepare_batch(root, manifest, now)
    elif args.command == "complete":
        result = complete_batch(root, manifest, now, args.batch_id)
    else:
        result = status_snapshot(root, manifest, now)

    update_summary_status(root, manifest, now)
    save_manifest(manifest_path, manifest, now)
    if args.json:
        print(json.dumps(result, ensure_ascii=False, indent=2))
    else:
        print(format_result(result))
    return 0


def parse_now(raw: str | None) -> datetime:
    if not raw:
        return datetime.now().astimezone()

    try:
        parsed = datetime.fromisoformat(raw)
    except ValueError as exc:
        raise SystemExit(f"Invalid --now value: {raw}") from exc

    if parsed.tzinfo is None:
        return parsed.astimezone()
    return parsed.astimezone()


def review_dir(root: Path) -> Path:
    return root / ".codex" / "review"


def list_git_files(root: Path) -> list[str]:
    completed = subprocess.run(
        ["git", "ls-files", "-z"],
        cwd=root,
        capture_output=True,
        check=True,
        text=False,
    )
    items = [item.decode("utf-8") for item in completed.stdout.split(b"\x00") if item]
    return sorted(items)


def load_manifest(path: Path) -> dict[str, Any]:
    if not path.exists():
        return {"schema_version": 1, "generated_at": None, "active_batch_id": None, "files": []}

    with path.open("r", encoding="utf-8") as handle:
        data = json.load(handle)

    if "files" not in data or not isinstance(data["files"], list):
        raise SystemExit(f"Invalid manifest format: {path}")
    return data


def sync_manifest(manifest: dict[str, Any], git_files: list[str], root: Path) -> dict[str, Any]:
    previous_entries = {
        entry["path"]: entry
        for entry in manifest.get("files", [])
        if isinstance(entry, dict) and "path" in entry
    }
    next_entries: list[dict[str, Any]] = []

    for relative_path in git_files:
        current_hash = sha256_file(root / relative_path)
        current_kind = classify_kind(relative_path)
        existing = previous_entries.get(relative_path)

        if existing is None:
            next_entries.append(new_entry(relative_path, current_hash, current_kind))
            continue

        entry = {
            "path": relative_path,
            "sha256": current_hash,
            "kind": current_kind,
            "status": existing.get("status", "pending"),
            "batch_id": existing.get("batch_id"),
            "last_checked_at": existing.get("last_checked_at"),
            "evidence_refs": ensure_string_list(existing.get("evidence_refs")),
        }

        if existing.get("sha256") != current_hash or existing.get("kind") != current_kind:
            entry["status"] = "pending"
            entry["batch_id"] = None
            entry["last_checked_at"] = None
            entry["evidence_refs"] = []

        next_entries.append(entry)

    manifest["files"] = sorted(next_entries, key=lambda item: item["path"])
    manifest["active_batch_id"] = infer_active_batch_id(manifest["files"])
    return manifest


def ensure_string_list(value: Any) -> list[str]:
    if not isinstance(value, list):
        return []
    return [item for item in value if isinstance(item, str)]


def new_entry(relative_path: str, current_hash: str, current_kind: str) -> dict[str, Any]:
    return {
        "path": relative_path,
        "sha256": current_hash,
        "kind": current_kind,
        "status": "pending",
        "batch_id": None,
        "last_checked_at": None,
        "evidence_refs": [],
    }


def infer_active_batch_id(entries: list[dict[str, Any]]) -> str | None:
    active_ids = {entry["batch_id"] for entry in entries if entry.get("status") == "in_progress" and entry.get("batch_id")}
    if not active_ids:
        return None
    if len(active_ids) > 1:
        raise SystemExit(f"Manifest contains multiple active batch ids: {sorted(active_ids)}")
    return next(iter(active_ids))


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def classify_kind(relative_path: str) -> str:
    lower_path = relative_path.lower()
    name = Path(relative_path).name.lower()
    suffix = Path(relative_path).suffix.lower()

    if suffix == ".cs":
        return "source"
    if suffix == ".json":
        return "data"
    if suffix == ".md":
        return "doc"
    if suffix == ".tscn":
        return "scene"
    if suffix == ".tres":
        return "resource_text"
    if suffix in {".godot", ".csproj", ".sln", ".old"} or name in {
        ".editorconfig",
        ".gitattributes",
        ".gitignore",
    }:
        return "config"
    if suffix in {".atlas", ".spine", ".skel", ".svg"}:
        return "resource_text"
    if suffix in {".uid", ".import"}:
        return "metadata"
    if suffix in {".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif"}:
        return "binary_asset"
    if suffix in {".mp4", ".mov", ".avi", ".webm"}:
        return "video"
    if lower_path.startswith(".claude/"):
        return "config"
    return "text"


def ensure_review_workspace(root: Path, manifest: dict[str, Any], now: datetime) -> None:
    workspace = review_dir(root)
    batches_dir = workspace / "batches"
    batches_dir.mkdir(parents=True, exist_ok=True)

    ensure_file(
        workspace / "ARCHITECTURE_VERIFIED.md",
        """# ARCHITECTURE_VERIFIED

This document stores only verified architecture facts.

Rules:
- Record only facts verified from repository files or completed batch notes.
- End each fact with an `Evidence:` reference to one or more batch files.
- Do not write guesses, inferred behavior, or TODO items here.

## Verified Architecture Facts

No verified facts have been recorded yet.
""",
    )
    ensure_file(
        workspace / "TODO_VERIFIED.md",
        """# TODO_VERIFIED

This document stores only verified issues and optimization items.

Rules:
- Use `P0`, `P1`, or `P2` for priority.
- Include the affected files.
- End each item with an `Evidence:` reference to one or more batch files.
- Do not record speculative issues here.

## Verified TODO Items

No verified TODO items have been recorded yet.
""",
    )
    ensure_file(
        workspace / "OPEN_QUESTIONS.md",
        """# OPEN_QUESTIONS

This document stores unresolved questions that could not be verified yet.

Rules:
- Phrase each item as an open question, not a conclusion.
- Include the files inspected so far.
- Move an item out of this file once it becomes verified.

## Open Questions

No open questions have been recorded yet.
""",
    )
    ensure_file(workspace / "SUMMARY_VERIFIED.md", summary_template())
    update_summary_status(root, manifest, now)


def ensure_file(path: Path, content: str) -> None:
    if path.exists():
        return
    path.parent.mkdir(parents=True, exist_ok=True)
    write_text(path, content.rstrip() + "\n")


def summary_template() -> str:
    return "\n".join(
        [
            "# SUMMARY_VERIFIED",
            "",
            SUMMARY_STATUS_BEGIN,
            "Status has not been generated yet.",
            SUMMARY_STATUS_END,
            "",
            "## Final Verified Summary",
            "",
            "Write this section only when every tracked file is marked `done` in `manifest.json`.",
            "Use only verified facts that can be traced back to completed batch files.",
            "",
        ]
    )


def update_summary_status(root: Path, manifest: dict[str, Any], now: datetime) -> None:
    summary_path = review_dir(root) / "SUMMARY_VERIFIED.md"
    existing = summary_path.read_text(encoding="utf-8") if summary_path.exists() else summary_template()
    counts = manifest_counts(manifest["files"])
    active_batch_id = manifest.get("active_batch_id")

    lines = [
        SUMMARY_STATUS_BEGIN,
        f"Last sync: {iso_timestamp(now)}",
        f"Tracked files: {counts['total']}",
        f"Done: {counts['done']}",
        f"In progress: {counts['in_progress']}",
        f"Pending: {counts['pending']}",
        f"Active batch: {active_batch_id or 'none'}",
    ]
    if counts["pending"] == 0 and counts["in_progress"] == 0:
        lines.append("Final summary may be refreshed now.")
    else:
        lines.append("Final summary is locked until every tracked file reaches done.")
    lines.append(SUMMARY_STATUS_END)
    status_block = "\n".join(lines)

    if SUMMARY_STATUS_BEGIN in existing and SUMMARY_STATUS_END in existing:
        start = existing.index(SUMMARY_STATUS_BEGIN)
        end = existing.index(SUMMARY_STATUS_END) + len(SUMMARY_STATUS_END)
        updated = existing[:start] + status_block + existing[end:]
    else:
        updated = status_block + "\n\n" + existing

    write_text(summary_path, updated.rstrip() + "\n")


def prepare_batch(root: Path, manifest: dict[str, Any], now: datetime) -> dict[str, Any]:
    entries = manifest["files"]
    active_batch_id = manifest.get("active_batch_id")
    if active_batch_id:
        active_entries = [entry for entry in entries if entry["status"] == "in_progress" and entry["batch_id"] == active_batch_id]
        if not active_entries:
            raise SystemExit(f"Active batch id {active_batch_id} has no in-progress files.")
        batch_path = ensure_batch_file(root, active_batch_id, active_entries, now)
        result = build_batch_result("active", active_batch_id, active_entries, batch_path, manifest)
        return result

    selected = select_next_batch(entries)
    if not selected:
        return {
            "command": "prepare",
            "state": "complete",
            "message": "No pending files remain.",
            "counts": manifest_counts(entries),
            "batch_id": None,
            "batch_path": None,
            "files": [],
        }

    group_name = group_for_path(selected[0]["path"])
    batch_id = next_batch_id(root, entries, now, group_name)
    for entry in selected:
        entry["status"] = "in_progress"
        entry["batch_id"] = batch_id
        entry["evidence_refs"] = []

    manifest["active_batch_id"] = batch_id
    batch_path = ensure_batch_file(root, batch_id, selected, now)
    return build_batch_result("prepared", batch_id, selected, batch_path, manifest)


def select_next_batch(entries: list[dict[str, Any]]) -> list[dict[str, Any]]:
    for group_name in GROUP_ORDER:
        pending = [entry for entry in entries if entry["status"] == "pending" and group_for_path(entry["path"]) == group_name]
        if not pending:
            continue
        pending.sort(key=lambda item: item["path"])
        category = review_category(pending[0])
        limit = FAST_BATCH_LIMIT if category == "fast" else TEXT_BATCH_LIMIT
        return [entry for entry in pending if review_category(entry) == category][:limit]
    return []


def review_category(entry: dict[str, Any]) -> str:
    kind = entry["kind"]
    if kind in {"metadata", "binary_asset", "video"}:
        return "fast"
    return "text"


def next_batch_id(
    root: Path,
    entries: list[dict[str, Any]],
    now: datetime,
    group_name: str,
) -> str:
    base = f"{now.strftime('%Y%m%d-%H%M')}-{group_name.lower()}"
    existing_ids = {entry["batch_id"] for entry in entries if entry.get("batch_id")}
    if base not in existing_ids and not (review_dir(root) / "batches" / f"{base}.md").exists():
        return base

    counter = 2
    while True:
        candidate = f"{base}-{counter:02d}"
        batch_path = review_dir(root) / "batches" / f"{candidate}.md"
        if candidate not in existing_ids and not batch_path.exists():
            return candidate
        counter += 1


def group_for_path(relative_path: str) -> str:
    parts = Path(relative_path).parts
    if len(parts) == 1:
        return "root"
    first = parts[0]
    if first in {"Core", "Module", "Scene", "Data", "Tools", "Docs", "Balin"}:
        return first
    return "other"


def ensure_batch_file(root: Path, batch_id: str, entries: list[dict[str, Any]], now: datetime) -> Path:
    path = review_dir(root) / "batches" / f"{batch_id}.md"
    if path.exists():
        return path

    content_lines = [
        f"# Batch {batch_id}",
        "",
        f"- Generated: {iso_timestamp(now)}",
        f"- Group: {group_for_path(entries[0]['path']) if entries else 'unknown'}",
        f"- File count: {len(entries)}",
        "",
        "## Files In Scope",
        "",
    ]

    for entry in entries:
        content_lines.append(
            f"- `{entry['path']}` | kind=`{entry['kind']}` | sha256=`{entry['sha256']}`"
        )

    content_lines.extend(
        [
            "",
            "## Verified Facts",
            "",
            f"- {PLACEHOLDER_TOKEN} Replace with verified facts only. Reference the inspected files directly.",
            "",
            "## Findings",
            "",
            f"- {PLACEHOLDER_TOKEN} Replace with verified issues, risks, or optimization items.",
            "",
            "## Open Questions",
            "",
            f"- {PLACEHOLDER_TOKEN} Replace with unresolved questions, or write `- None.`",
            "",
        ]
    )
    write_text(path, "\n".join(content_lines).rstrip() + "\n")
    return path


def build_batch_result(
    state: str,
    batch_id: str,
    entries: list[dict[str, Any]],
    batch_path: Path,
    manifest: dict[str, Any],
) -> dict[str, Any]:
    return {
        "command": "prepare",
        "state": state,
        "batch_id": batch_id,
        "batch_path": str(batch_path),
        "group": group_for_path(entries[0]["path"]) if entries else None,
        "review_category": review_category(entries[0]) if entries else None,
        "file_count": len(entries),
        "files": [entry["path"] for entry in entries],
        "counts": manifest_counts(manifest["files"]),
    }


def complete_batch(
    root: Path,
    manifest: dict[str, Any],
    now: datetime,
    batch_id: str | None,
) -> dict[str, Any]:
    active_batch_id = batch_id or manifest.get("active_batch_id")
    if not active_batch_id:
        raise SystemExit("No active batch to complete.")

    entries = [
        entry
        for entry in manifest["files"]
        if entry["status"] == "in_progress" and entry["batch_id"] == active_batch_id
    ]
    if not entries:
        raise SystemExit(f"No in-progress files found for batch {active_batch_id}.")

    batch_path = review_dir(root) / "batches" / f"{active_batch_id}.md"
    validate_batch_file(batch_path)
    evidence_ref = batch_path.relative_to(review_dir(root)).as_posix()
    checked_at = iso_timestamp(now)
    for entry in entries:
        entry["status"] = "done"
        entry["last_checked_at"] = checked_at
        entry["evidence_refs"] = [evidence_ref]

    manifest["active_batch_id"] = None
    update_summary_status(root, manifest, now)
    counts = manifest_counts(manifest["files"])
    return {
        "command": "complete",
        "state": "completed",
        "batch_id": active_batch_id,
        "batch_path": str(batch_path),
        "file_count": len(entries),
        "files": [entry["path"] for entry in entries],
        "counts": counts,
        "final_summary_unlocked": counts["pending"] == 0 and counts["in_progress"] == 0,
    }


def validate_batch_file(path: Path) -> None:
    if not path.exists():
        raise SystemExit(f"Batch file does not exist: {path}")

    text = path.read_text(encoding="utf-8")
    required_headings = ["## Files In Scope", "## Verified Facts", "## Findings", "## Open Questions"]
    missing = [heading for heading in required_headings if heading not in text]
    if missing:
        raise SystemExit(f"Batch file is missing required headings: {', '.join(missing)}")
    if PLACEHOLDER_TOKEN in text:
        raise SystemExit(f"Batch file still contains placeholder tokens: {path}")


def status_snapshot(root: Path, manifest: dict[str, Any], now: datetime) -> dict[str, Any]:
    update_summary_status(root, manifest, now)
    counts = manifest_counts(manifest["files"])
    batch_id = manifest.get("active_batch_id")
    files = []
    if batch_id:
        files = [entry["path"] for entry in manifest["files"] if entry["status"] == "in_progress" and entry["batch_id"] == batch_id]
    return {
        "command": "status",
        "counts": counts,
        "active_batch_id": batch_id,
        "active_files": files,
        "review_dir": str(review_dir(root)),
    }


def manifest_counts(entries: list[dict[str, Any]]) -> dict[str, int]:
    counter = Counter(entry["status"] for entry in entries)
    return {
        "total": len(entries),
        "done": counter.get("done", 0),
        "in_progress": counter.get("in_progress", 0),
        "pending": counter.get("pending", 0),
    }


def save_manifest(path: Path, manifest: dict[str, Any], now: datetime) -> None:
    manifest["generated_at"] = iso_timestamp(now)
    manifest["active_batch_id"] = infer_active_batch_id(manifest["files"])
    payload = {
        "schema_version": manifest.get("schema_version", 1),
        "generated_at": manifest["generated_at"],
        "active_batch_id": manifest["active_batch_id"],
        "files": manifest["files"],
    }
    write_text(path, json.dumps(payload, ensure_ascii=False, indent=2) + "\n")


def write_text(path: Path, content: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8", newline="\n") as handle:
        handle.write(content)


def iso_timestamp(now: datetime) -> str:
    return now.isoformat(timespec="seconds")


def format_result(result: dict[str, Any]) -> str:
    lines = [f"command: {result.get('command', 'unknown')}"]
    for key in ["state", "batch_id", "batch_path", "group", "review_category", "file_count"]:
        if key in result and result[key] is not None:
            lines.append(f"{key}: {result[key]}")

    counts = result.get("counts")
    if counts:
        lines.append(
            "counts: total={total} done={done} in_progress={in_progress} pending={pending}".format(
                **counts
            )
        )

    files = result.get("files") or result.get("active_files") or []
    if files:
        lines.append("files:")
        lines.extend(f"  - {item}" for item in files)

    message = result.get("message")
    if message:
        lines.append(f"message: {message}")

    if "final_summary_unlocked" in result:
        lines.append(f"final_summary_unlocked: {str(result['final_summary_unlocked']).lower()}")

    review_workspace = result.get("review_dir")
    if review_workspace:
        lines.append(f"review_dir: {review_workspace}")

    return "\n".join(lines)


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except subprocess.CalledProcessError as exc:
        if exc.stderr:
            sys.stderr.buffer.write(exc.stderr)
        raise SystemExit(exc.returncode) from exc
