"""
B10 — `Assets/Art/Generated/` 审美换血批跑（身份卡 §B10 · 不含 B11 蓝图）

尺寸契约（与 `CharacterSpriteUtilities.BuildProjectedDirectionalSheet` / `entity_render.json` 一致）：
- **8 向**（玩家 / 怪 / 异常）：`256×2048` 竖表（8×256×256 行，`DirectionalSpriteHelper` 行序）
- **设施 4 向**：`256×1024`（4×256×256，`ResolveFacilityRotationRow`）
- **固件**：`256×256` 单格
- **体素**（可选）：默认跟随磁盘上已有 PNG 的宽高；不存在则 `128×128`

流水线：`banana_gen` → `rembg` → PIL `resize` 到目标像素。

运行：
    python Tools/banana/b10_generated_driver.py --dry-run
    python Tools/banana/b10_generated_driver.py --only player,monster --skip-existing
    python Tools/banana/b10_generated_driver.py --only facility --limit 2
    python Tools/banana/b10_generated_driver.py --include-voxel-tiles --voxel-subset top

`--only`：`player` / `monster` / `anomaly` / `facility` / `fixture` / `voxel`（逗号分隔）

子集重试：`--facility-files`（`facilities/*.png`）、`--fixture-files`（`fixtures/*.png`）、`--monster-files`、`--anomaly-files`。

体素子集：`--voxel-subset top` = 仅 `voxel_tiles/tile_*.png`（不含 atlas / ores / overlays）；
`all` = README 下列出的常见文件（仍远少于全仓库；慎用 API 费用）。

日志：`Tools/banana/.cache/b10_generated_driver.log`
"""
from __future__ import annotations

import argparse
import json
import subprocess
import sys
import time
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TOOLS_DIR = Path(__file__).resolve().parent
GENERATED = ROOT / "Assets" / "Art" / "Generated"
PREVIEW_DIR = ROOT / "Artifacts" / "preview" / "b10_generated"
LOG = TOOLS_DIR / ".cache" / "b10_generated_driver.log"
ENTITY_RENDER = ROOT / "Data" / "entity_render.json"

STYLE_ISO = (
    "isometric 3/4 top-down game craft, hand-painted semi-realistic, muted earthy low-saturation, "
    "soft overcast lighting, no hard cast shadow, thin brown outline, no text, no watermark, "
    "solid neutral flat grey background for easy keying"
)

# 竖表 8 帧 × 256²
SHEET_8_W, SHEET_8_H = 256, 2048
# 设施 4 帧 × 256²
SHEET_4_W, SHEET_4_H = 256, 1024


def _log(line: str) -> None:
    LOG.parent.mkdir(parents=True, exist_ok=True)
    with LOG.open("a", encoding="utf-8") as fh:
        fh.write(f"{datetime.now(timezone.utc).isoformat()}  {line}\n")
    # Windows 控制台默认非 UTF-8 时，API 回包里的符号（如 ⚠）会炸 print
    try:
        print(line, flush=True)
    except UnicodeEncodeError:
        enc = getattr(sys.stdout, "encoding", None) or "utf-8"
        print(line.encode(enc, errors="replace").decode(enc, errors="replace"), flush=True)


def run_cmd(cmd: list[str], timeout: int = 900) -> tuple[int, str]:
    try:
        proc = subprocess.run(
            cmd,
            capture_output=True,
            text=True,
            timeout=timeout,
            cwd=str(ROOT),
            encoding="utf-8",
            errors="replace",
        )
        out = (proc.stdout or "") + (proc.stderr or "")
        return proc.returncode, out
    except subprocess.TimeoutExpired:
        return 124, "[timeout]"
    except Exception as e:
        return 1, f"[exception] {type(e).__name__}: {e}"


def _resize_rgba(src: Path, dst: Path, w: int, h: int) -> None:
    from PIL import Image

    im = Image.open(src).convert("RGBA")
    im = im.resize((w, h), Image.Resampling.LANCZOS)
    dst.parent.mkdir(parents=True, exist_ok=True)
    im.save(dst, format="PNG")


def _read_existing_size(path: Path, default: tuple[int, int]) -> tuple[int, int]:
    if not path.is_file():
        return default
    from PIL import Image

    with Image.open(path) as im:
        return im.size


# —— Prompts：英文主体 + STYLE_ISO —— #

def _prompt_8dir(kind: str, name: str) -> str:
    return (
        f"single vertical game sprite sheet, exactly eight equal horizontal bands stacked top to bottom, "
        f"each band is one compass facing row for isometric character, "
        f"{kind}: {name}, "
        f"256px frame grid implied, "
        f"{STYLE_ISO}, "
        f"8 rows N→NE→E→SE→S→SW→W→NW order top to bottom"
    )


def _prompt_4dir_facility(name: str) -> str:
    return (
        f"single vertical game sprite sheet, four equal horizontal bands stacked top to bottom, "
        f"isometric {name}, facility prop, four rotation variants, "
        f"{STYLE_ISO}"
    )


def _prompt_fixture(name: str) -> str:
    return f"single isometric small fixture prop: {name}, {STYLE_ISO}"


def _prompt_voxel(desc: str) -> str:
    return f"single top-down seamless voxel texture tile: {desc}, {STYLE_ISO}"


# 怪物 id → 英文描述（与 MonsterMapAssetGenerator 命名对齐）
_MONSTER: list[tuple[str, str]] = [
    ("monster_bear_8dir.png", "bear beast heavy silhouette"),
    ("monster_goblin_8dir.png", "small green goblin humanoid"),
    ("monster_goblin_miner_8dir.png", "goblin miner with pick"),
    ("monster_elf_ranger_8dir.png", "slim elf ranger"),
    ("monster_orc_warrior_8dir.png", "muscular orc warrior"),
    ("monster_orc_shaman_8dir.png", "orc shaman with staff"),
    ("monster_rat_8dir.png", "giant rat creature"),
    ("monster_scorpion_8dir.png", "giant scorpion"),
    ("monster_skeleton_8dir.png", "undead skeleton warrior"),
    ("monster_slime_8dir.png", "bouncing slime blob"),
    ("monster_spider_8dir.png", "giant spider"),
    ("monster_treant_8dir.png", "walking treant tree creature"),
    ("monster_wolf_8dir.png", "wolf predator"),
]

_ANOMALY: list[tuple[str, str]] = [
    ("monster_anomaly_eye_cluster_8dir.png", "floating cluster of alien eyes anomaly"),
    ("monster_anomaly_flesh_bloom_8dir.png", "blooming corrupted flesh anomaly"),
    ("monster_anomaly_tentacle_maw_8dir.png", "maw of tentacles horror"),
    ("monster_anomaly_void_hound_8dir.png", "void hound eldritch beast"),
]

_FACILITY: list[tuple[str, str]] = [
    ("facility_bed_4dir.png", _prompt_4dir_facility("wooden bed")),
    ("facility_dormitory_bed_4dir.png", _prompt_4dir_facility("compact dormitory bed")),
    ("facility_stove_4dir.png", _prompt_4dir_facility("metal stove")),
    ("facility_butcher_table_4dir.png", _prompt_4dir_facility("butcher table")),
    ("facility_smithy_4dir.png", _prompt_4dir_facility("smithy anvil workstation")),
    ("facility_loom_4dir.png", _prompt_4dir_facility("weaving loom")),
    ("facility_herbal_bench_4dir.png", _prompt_4dir_facility("herbalist bench")),
    ("facility_market_stall_4dir.png", _prompt_4dir_facility("wooden market stall")),
    ("facility_shelf_4dir.png", _prompt_4dir_facility("storage shelf")),
    ("facility_fire_brazier_4dir.png", _prompt_4dir_facility("metal fire brazier")),
]

_FIXTURE: list[tuple[str, str]] = [
    ("campfire.png", _prompt_fixture("campfire ring of stones with flame")),
    ("door.png", _prompt_fixture("wooden closed door")),
    ("fire.png", _prompt_fixture("small magical flame")),
    ("nest.png", _prompt_fixture("creature nest")),
    ("stair_down.png", _prompt_fixture("stairs leading down")),
    ("stair_up.png", _prompt_fixture("stairs leading up")),
]

_VOXEL_TOP: list[tuple[str, str]] = [
    ("voxel_tiles/tile_dirt.png", _prompt_voxel("packed brown dirt")),
    ("voxel_tiles/tile_stone.png", _prompt_voxel("grey rough stone")),
    ("voxel_tiles/tile_sand.png", _prompt_voxel("beach sand")),
    ("voxel_tiles/tile_grass_top.png", _prompt_voxel("grass turf top")),
    ("voxel_tiles/tile_snow.png", _prompt_voxel("packed snow")),
    ("voxel_tiles/tile_brick.png", _prompt_voxel("brick masonry")),
    ("voxel_tiles/tile_plank.png", _prompt_voxel("wooden planks")),
    ("voxel_tiles/tile_gravel.png", _prompt_voxel("gravel mix")),
    ("voxel_tiles/tile_mud.png", _prompt_voxel("wet mud")),
    ("voxel_tiles/tile_clay.png", _prompt_voxel("tan clay")),
    ("voxel_tiles/tile_ash.png", _prompt_voxel("grey ash")),
]


@dataclass(frozen=True)
class B10Job:
    tag: str
    rel_under_generated: str  # e.g. character_map_iso8/base_human_default_8dir.png
    aspect: str
    tw: int
    th: int
    full_prompt: str


def _build_jobs(
    *,
    include_player: bool,
    include_monster: bool,
    include_anomaly: bool,
    include_facility: bool,
    include_fixture: bool,
    include_voxel: bool,
    voxel_subset: str,
    facility_files_filter: set[str] | None,
    fixture_files_filter: set[str] | None,
    monster_files_filter: set[str] | None,
    anomaly_files_filter: set[str] | None,
) -> list[B10Job]:
    jobs: list[B10Job] = []

    if include_player:
        jobs.append(
            B10Job(
                "player",
                "character_map_iso8/base_human_default_8dir.png",
                "1:8",
                SHEET_8_W,
                SHEET_8_H,
                _prompt_8dir("neutral humanoid adventurer", "simple tunic, short hair, unarmed idle"),
            )
        )

    if include_monster:
        for fname, desc in _MONSTER:
            if monster_files_filter is not None and fname not in monster_files_filter:
                continue
            jobs.append(
                B10Job(
                    f"monster:{fname}",
                    f"monster_map_iso8/{fname}",
                    "1:8",
                    SHEET_8_W,
                    SHEET_8_H,
                    _prompt_8dir("creature", desc),
                )
            )

    if include_anomaly:
        for fname, desc in _ANOMALY:
            if anomaly_files_filter is not None and fname not in anomaly_files_filter:
                continue
            jobs.append(
                B10Job(
                    f"anomaly:{fname}",
                    f"monster_map_iso8_anomaly/{fname}",
                    "1:8",
                    SHEET_8_W,
                    SHEET_8_H,
                    _prompt_8dir("horror anomaly creature", desc),
                )
            )

    if include_facility:
        for fname, ptext in _FACILITY:
            if facility_files_filter is not None and fname not in facility_files_filter:
                continue
            jobs.append(
                B10Job(
                    f"facility:{fname}",
                    f"facilities/{fname}",
                    "1:4",
                    SHEET_4_W,
                    SHEET_4_H,
                    ptext,
                )
            )

    if include_fixture:
        for fname, ptext in _FIXTURE:
            if fixture_files_filter is not None and fname not in fixture_files_filter:
                continue
            jobs.append(
                B10Job(
                    f"fixture:{fname}",
                    f"fixtures/{fname}",
                    "1:1",
                    256,
                    256,
                    ptext,
                )
            )

    if include_voxel:
        rels: list[tuple[str, str]] = []
        if voxel_subset == "top":
            rels = list(_VOXEL_TOP)
        elif voxel_subset == "all":
            vdir = GENERATED / "voxel_tiles"
            for p in sorted(vdir.rglob("tile_*.png")):
                if "atlas" in p.name.lower():
                    continue
                rel = p.relative_to(GENERATED).as_posix()
                rels.append((rel, _prompt_voxel(p.stem.replace("tile_", "").replace("_", " "))))
        for rel, ptext in rels:
            fake = GENERATED / rel
            w, h = _read_existing_size(fake, (128, 128))
            jobs.append(
                B10Job(
                    f"voxel:{rel}",
                    rel,
                    "1:1",
                    w,
                    h,
                    ptext,
                )
            )

    return jobs


def process_job(
    job: B10Job,
    *,
    skip_existing: bool,
    dry_run: bool,
    max_gen_retries: int,
    size: str,
) -> str:
    out_final = GENERATED / job.rel_under_generated
    if skip_existing and out_final.is_file():
        return f"[skip] {job.tag}"

    pv = job.tag.replace(":", "__").replace("/", "_")[:120]
    raw = PREVIEW_DIR / f"{pv}_raw.png"
    nobg = PREVIEW_DIR / f"{pv}_nobg.png"
    tw, th = job.tw, job.th

    if dry_run:
        return f"[dry-run] {job.tag} {job.aspect} -> {tw}x{th}"

    for attempt in range(1, max_gen_retries + 1):
        cmd = [
            sys.executable,
            str(TOOLS_DIR / "banana_gen.py"),
            "--protocol",
            "openai",
            "--prompt",
            job.full_prompt,
            "--aspect",
            job.aspect,
            "--size",
            size,
            "--count",
            "1",
            "--out",
            str(raw.relative_to(ROOT)),
        ]
        rc, out = run_cmd(cmd, timeout=1500)
        if rc == 0:
            break
        _log(f"[retry {attempt}/{max_gen_retries}] {job.tag} banana_gen rc={rc}")
        if attempt < max_gen_retries:
            time.sleep(18.0)
    else:
        return f"[FAIL-gen] {job.tag} rc={rc}  {out[-380:]}"

    candidates = list(PREVIEW_DIR.glob(f"{pv}_raw.*"))
    raw_real = next((c for c in candidates if c.suffix.lower() in {".png", ".jpg", ".jpeg", ".webp"}), None)
    if raw_real is None:
        return f"[FAIL-gen-missing] {job.tag}"

    cmd = [
        sys.executable,
        str(TOOLS_DIR / "rembg_cutout.py"),
        "--in",
        str(raw_real.relative_to(ROOT)),
        "--out",
        str(nobg.relative_to(ROOT)),
    ]
    rc, out = run_cmd(cmd, timeout=200)
    if rc != 0:
        return f"[FAIL-rembg] {job.tag} rc={rc}  {out[-200:]}"

    try:
        _resize_rgba(nobg, out_final, tw, th)
    except Exception as e:
        return f"[FAIL-resize] {job.tag} {e!s}"

    return f"[ok]   {job.tag}"


def _validate_entity_paths() -> None:
    """冒烟：entity_render 引用的 Generated 路径与表一致（缺文件仅提示）。"""
    data = json.loads(ENTITY_RENDER.read_text(encoding="utf-8"))
    missing = []
    for _k, v in data.items():
        if not isinstance(v, dict):
            continue
        tp = v.get("texturePath")
        if isinstance(tp, str) and "Assets/Art/Generated/" in tp:
            rel = tp.split("Assets/Art/Generated/", 1)[-1]
            p = GENERATED / rel.replace("\\", "/")
            if not p.is_file():
                missing.append(rel)
    if missing:
        _log(f"[note] entity_render 指向但磁盘缺 PNG（可忽略若尚未导出）：{len(missing)} 条")


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", default="", help="comma: player,monster,anomaly,facility,fixture,voxel")
    ap.add_argument("--skip-existing", action="store_true")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--limit", type=int, default=0)
    ap.add_argument("--max-gen-retries", type=int, default=5)
    ap.add_argument("--size", choices=("1K", "2K"), default="2K", help="8dir/4dir 建议 2K 再压；fixture/voxel 可 1K")
    ap.add_argument(
        "--include-voxel-tiles",
        action="store_true",
        help="纳入体素贴图任务（另需 --only voxel 或 only 留空时才生效）",
    )
    ap.add_argument(
        "--voxel-subset",
        choices=("top", "all"),
        default="top",
        help="top=少量 tile_*.png；all=Generated/voxel_tiles 下全部 tile_*.png（耗 API）",
    )
    ap.add_argument("--validate-entity-render", action="store_true", help="打印 entity_render 缺图统计后退出")
    ap.add_argument(
        "--facility-files",
        default="",
        help="仅当包含 facility 时：facilities/ 下文件名，如 facility_loom_4dir.png（逗号分隔）",
    )
    ap.add_argument(
        "--fixture-files",
        default="",
        help="仅当包含 fixture 时：fixtures/ 下文件名，如 campfire.png（逗号分隔）",
    )
    ap.add_argument(
        "--monster-files",
        default="",
        help="仅当包含 monster 时有效：逗号分隔，如 monster_bear_8dir.png,monster_goblin_8dir.png",
    )
    ap.add_argument(
        "--anomaly-files",
        default="",
        help="仅当包含 anomaly 时有效：逗号分隔，如 monster_anomaly_void_hound_8dir.png",
    )
    args = ap.parse_args()

    if args.validate_entity_render:
        _validate_entity_paths()
        return 0

    only_raw = {s.strip().lower() for s in args.only.split(",") if s.strip()}
    if not only_raw:
        only_set = {"player", "monster", "anomaly", "facility", "fixture"}
        if args.include_voxel_tiles:
            only_set.add("voxel")
    else:
        only_set = only_raw

    fac_filter: set[str] | None = None
    if args.facility_files.strip():
        fac_filter = {s.strip() for s in args.facility_files.split(",") if s.strip()}

    fix_filter: set[str] | None = None
    if args.fixture_files.strip():
        fix_filter = {s.strip() for s in args.fixture_files.split(",") if s.strip()}

    mon_filter: set[str] | None = None
    if args.monster_files.strip():
        mon_filter = {s.strip() for s in args.monster_files.split(",") if s.strip()}

    ano_filter: set[str] | None = None
    if args.anomaly_files.strip():
        ano_filter = {s.strip() for s in args.anomaly_files.split(",") if s.strip()}

    jobs = _build_jobs(
        include_player="player" in only_set,
        include_monster="monster" in only_set,
        include_anomaly="anomaly" in only_set,
        include_facility="facility" in only_set,
        include_fixture="fixture" in only_set,
        include_voxel="voxel" in only_set,
        voxel_subset=args.voxel_subset,
        facility_files_filter=fac_filter,
        fixture_files_filter=fix_filter,
        monster_files_filter=mon_filter,
        anomaly_files_filter=ano_filter,
    )
    if args.limit > 0:
        jobs = jobs[: args.limit]

    _log(
        f"===== B10 Generated driver: {len(jobs)} jobs "
        f"(only={sorted(only_set)} voxel_subset={args.voxel_subset}) =====",
    )
    PREVIEW_DIR.mkdir(parents=True, exist_ok=True)

    t0 = time.time()
    results: list[str] = []
    for i, job in enumerate(jobs, 1):
        t1 = time.time()
        msg = process_job(
            job,
            skip_existing=args.skip_existing,
            dry_run=args.dry_run,
            max_gen_retries=args.max_gen_retries,
            size=args.size,
        )
        _log(f"[{i:>3}/{len(jobs)}] {msg}  [+{time.time() - t1:.0f}s]")
        results.append(msg)

    total = time.time() - t0
    ok = sum(1 for r in results if r.startswith("[ok]"))
    fail = sum(1 for r in results if r.startswith("[FAIL"))
    skip = sum(1 for r in results if r.startswith("[skip]"))
    dr = sum(1 for r in results if r.startswith("[dry-run"))
    _log(f"===== done {total:.0f}s ok={ok} fail={fail} skip={skip} dry={dr} =====")
    if fail:
        for r in results:
            if r.startswith("[FAIL"):
                _log(f"  {r}")
    return 0 if fail == 0 else 2


if __name__ == "__main__":
    sys.exit(main())
