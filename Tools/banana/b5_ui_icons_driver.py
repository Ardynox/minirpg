"""
B5 UI icons 批处理 driver（need + condition + thought 三联）

流水线：banana_gen --count 1 -> rembg 抠 -> crop 128 -> 落 Assets/Art/Placeholders/ui_icons/

运行：
    python Tools/banana/b5_ui_icons_driver.py          # 全量跑
    python Tools/banana/b5_ui_icons_driver.py --only need,condition   # 只跑某些类别
    python Tools/banana/b5_ui_icons_driver.py --skip-existing  # 已有成品跳过
    python Tools/banana/b5_ui_icons_driver.py --dry-run        # 只打印，不调用 API

错误：单张失败不中断，记录到 Tools/banana/.cache/b5_driver.log，最后统一汇报。
"""
from __future__ import annotations

import argparse
import json
import subprocess
import sys
import time
from pathlib import Path
from datetime import datetime, timezone

ROOT = Path(__file__).resolve().parents[2]
TOOLS_DIR = Path(__file__).resolve().parent
PREVIEW_DIR = ROOT / "Artifacts" / "preview" / "b5_ui_icons"
OUT_DIR = ROOT / "Assets" / "Art" / "Placeholders" / "ui_icons"
LOG = TOOLS_DIR / ".cache" / "b5_driver.log"

STYLE_TAIL = (
    "centered composition, fills ~80% of canvas, 2-color hint, "
    "thin dark-brown outline, muted earthy low-saturation palette, "
    "hand-painted semi-realistic game icon, "
    "no text, no watermark, no frame, no background scene, "
    "output on a solid neutral flat gray background for easy keying"
)

def _p(subject: str) -> str:
    """把主体描述拼成 banana --subject 整段。"""
    return f"single flat game UI icon: {subject}, {STYLE_TAIL}"


# need 主图标（5 张）
NEEDS: dict[str, str] = {
    "need_hunger":    _p("stylized bowl with a single spoon, empty bowl hinting hunger, warm beige ceramic"),
    "need_thirst":    _p("simple water droplet in a wooden cup, single clear drop falling, dusty cup"),
    "need_rest":      _p("crescent moon over a small pillow, calm sleep symbol, muted navy and cream"),
    "need_baby_food": _p("small baby bottle with a single cream-colored drop, worn pastel tones"),
    "need_mood":      _p("a simple smiling-and-frowning face mask split vertically, emotion duality, muted beige"),
}

# health_conditions（12 张）
CONDITIONS: dict[str, str] = {
    "condition_cut_wound":     _p("red blood droplet with a crossed diagonal crack behind it, bleeding wound icon"),
    "condition_blunt_trauma":  _p("purple-black bruise shape with small impact lines, blunt bruise icon"),
    "condition_toxic_wound":   _p("green poison drop with small skull silhouette inside, toxic wound icon"),
    "condition_burn_wound":    _p("orange flame silhouette on darkened scorched patch, burn wound icon"),
    "condition_infection":     _p("yellow-green bubble cluster with faint wavy lines, festering infection icon"),
    "condition_on_fire":       _p("full orange-red flame with ember sparks, burning body icon"),
    "condition_blood_loss":    _p("pale translucent droplet with a small red heart fading, blood loss icon"),
    "condition_hypothermia":   _p("simple snowflake over a shivering figure silhouette, cold exposure icon"),
    "condition_heatstroke":    _p("bright sun with heat waves over a drooping figure silhouette, heatstroke icon"),
    "condition_scar":          _p("diagonal stitched scar line on skin-tone patch, healed scar icon"),
    "condition_missing_limb":  _p("silhouette of a body with one arm missing, empty sleeve, amputation icon"),
    "condition_food_poisoning":_p("green queasy stomach silhouette with upward wavy fumes, food poisoning icon"),
}

# thoughts（34 张）
THOUGHTS: dict[str, str] = {
    "thought_peckish":         _p("small empty bowl with a single crumb, slight hunger hint"),
    "thought_hungry":          _p("empty bowl tipped sideways showing hunger, muted warm ceramic"),
    "thought_starving":        _p("ribcage silhouette over an empty cracked plate, severe hunger icon"),
    "thought_parched":         _p("dry cracked lips silhouette with tiny dot, slight thirst hint"),
    "thought_thirsty":         _p("tilted empty cup with a single drop falling, thirst icon"),
    "thought_dehydrated":      _p("cracked dry desert mud pattern with tiny drop silhouette, severe thirst icon"),
    "thought_drank_water":     _p("fresh water droplet with gentle ripple under it, satisfied drink icon"),
    "thought_tired":           _p("small silhouette figure with head lowered and 'Z' above, tired icon"),
    "thought_exhausted":       _p("silhouette slumped on knees with double 'Z' trailing up, exhausted icon"),
    "thought_ate_simple":      _p("plain bread loaf with a small bowl, simple meal icon"),
    "thought_ate_fine":        _p("small roast on a plate with garnish leaf, fine meal icon"),
    "thought_ate_lavish":      _p("decorated feast plate with roasted meat, fruit and chalice, lavish meal icon"),
    "thought_ate_raw":         _p("raw red meat cut on a wooden board, blood-stained, raw food icon"),
    "thought_ate_stale":       _p("stale dry bread loaf with crumbs breaking off, stale food icon"),
    "thought_ate_spoiled":     _p("slightly green-tinted food chunk on a plate, mild wavy fumes, spoiled icon"),
    "thought_ate_rotten":      _p("rotten green-brown food chunk with small flies, severe rotten icon"),
    "thought_slept_bedroll":   _p("rolled fabric bedroll with a single small 'Z' above, sleep icon"),
    "thought_slept_home_floor":_p("rough wooden floor plank silhouette with a small pillow, floor sleep icon"),
    "thought_sleep_interrupted":_p("crossed-out 'Z' with jagged lightning bolt behind, interrupted sleep icon"),
    "thought_hurt_recently":   _p("bandage wrap with a small red stain in the center, recent injury icon"),
    "thought_limb_destroyed":  _p("broken chain silhouette with severed ends, lost limb symbol"),
    "thought_cold_snap_chill": _p("large snowflake over a shivering body silhouette, cold snap icon"),
    "thought_heat_wave_fatigue":_p("large sun with drooping body silhouette beneath, heat wave fatigue icon"),
    "thought_rescued_traveler":_p("two silhouetted figures holding hands, one assisting the other, rescue icon"),
    "thought_supplies_arrived":_p("wooden crate with small sack on top, supply delivery icon"),
    "thought_revived_memory":  _p("faint ghostly face silhouette emerging from mist, haunting memory icon"),
    "thought_revived_ally":    _p("silhouette rising with glowing aura, resurrection icon, soft teal hint"),
    "thought_lost_child":      _p("small child silhouette walking away into fog, fading tone, lost child icon"),
    "thought_lost_parent":     _p("adult figure silhouette with a single tear drop, lost parent icon"),
    "thought_lost_spouse":     _p("two rings intertwined, one cracked, lost spouse icon"),
    "thought_lost_family":     _p("silhouette of a house with a cracked roof and broken heart symbol, lost family icon"),
    "thought_witnessed_death": _p("closed eye silhouette with a single tear, witnessed death icon"),
    "thought_ally_died":       _p("silhouette of a fallen figure with companion silhouette bowing over, fallen ally icon"),
    "thought_gene_modified":   _p("DNA double helix with one strand highlighted, altered body icon, muted teal hint"),
}


def all_items(selected_categories: set[str]) -> list[tuple[str, str, str]]:
    """返回 [(category, id, prompt), ...]"""
    items: list[tuple[str, str, str]] = []
    if "need" in selected_categories:
        for k, v in NEEDS.items():
            items.append(("need", k, v))
    if "condition" in selected_categories:
        for k, v in CONDITIONS.items():
            items.append(("condition", k, v))
    if "thought" in selected_categories:
        for k, v in THOUGHTS.items():
            items.append(("thought", k, v))
    return items


def _log(line: str) -> None:
    LOG.parent.mkdir(parents=True, exist_ok=True)
    with LOG.open("a", encoding="utf-8") as fh:
        ts = datetime.now(timezone.utc).isoformat()
        fh.write(f"{ts}  {line}\n")
    print(line, flush=True)


def run_cmd(cmd: list[str], timeout: int = 600) -> tuple[int, str]:
    try:
        proc = subprocess.run(
            cmd, capture_output=True, text=True,
            timeout=timeout, cwd=str(ROOT),
            encoding="utf-8", errors="replace",
        )
        out = (proc.stdout or "") + (proc.stderr or "")
        return proc.returncode, out
    except subprocess.TimeoutExpired:
        return 124, "[timeout]"
    except Exception as e:
        return 1, f"[exception] {type(e).__name__}: {e}"


def process_one(cat: str, icon_id: str, prompt: str, *,
                skip_existing: bool, dry_run: bool, retry_once: bool) -> str:
    out_final = OUT_DIR / f"{icon_id}.png"
    if skip_existing and out_final.exists():
        return f"[skip] {icon_id} (existing)"

    raw = PREVIEW_DIR / f"{icon_id}.png"
    nobg = PREVIEW_DIR / f"{icon_id}_nobg.png"

    if dry_run:
        return f"[dry-run] would gen {icon_id}"

    # Step 1: banana_gen (single count)
    cmd = [
        sys.executable, str(TOOLS_DIR / "banana_gen.py"),
        "--protocol", "openai",
        "--style", "style_base",
        "--negatives", "standard",
        "--subject", prompt,
        "--aspect", "1:1",
        "--size", "1K",
        "--count", "1",
        "--out", str(raw.relative_to(ROOT)),
    ]
    rc, out = run_cmd(cmd, timeout=420)
    if rc != 0:
        if retry_once:
            _log(f"[retry] {icon_id} banana_gen rc={rc}, retrying once")
            rc, out = run_cmd(cmd, timeout=420)
        if rc != 0:
            return f"[FAIL-gen] {icon_id} rc={rc}  {out[-300:]}"

    # banana 可能改写了扩展名 (e.g. .jpeg)。查找实际文件。
    candidates = list(PREVIEW_DIR.glob(f"{icon_id}.*"))
    raw_real = None
    for c in candidates:
        if c.suffix.lower() in {".png", ".jpg", ".jpeg", ".webp"} and c.name != nobg.name and "_128" not in c.name:
            raw_real = c
            break
    if raw_real is None:
        return f"[FAIL-gen-missing] {icon_id} (no output file found)"

    # Step 2: rembg cutout
    cmd = [
        sys.executable, str(TOOLS_DIR / "rembg_cutout.py"),
        "--in", str(raw_real.relative_to(ROOT)),
        "--out", str(nobg.relative_to(ROOT)),
    ]
    rc, out = run_cmd(cmd, timeout=120)
    if rc != 0:
        return f"[FAIL-rembg] {icon_id} rc={rc}  {out[-300:]}"

    # Step 3: crop + resize 128
    cmd = [
        sys.executable, str(TOOLS_DIR / "crop_square_resize.py"),
        "--in", str(nobg.relative_to(ROOT)),
        "--out", str(out_final.relative_to(ROOT)),
        "--size", "128",
        "--margin", "0.08",
    ]
    rc, out = run_cmd(cmd, timeout=60)
    if rc != 0:
        return f"[FAIL-crop] {icon_id} rc={rc}  {out[-300:]}"

    return f"[ok]   {icon_id}"


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", default="need,condition,thought",
                    help="comma-separated: need,condition,thought")
    ap.add_argument("--skip-existing", action="store_true")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--retry-once", action="store_true", default=True)
    args = ap.parse_args()

    selected = {s.strip() for s in args.only.split(",") if s.strip()}
    items = all_items(selected)
    _log(f"===== B5 driver start: {len(items)} items, categories={sorted(selected)} =====")

    OUT_DIR.mkdir(parents=True, exist_ok=True)
    PREVIEW_DIR.mkdir(parents=True, exist_ok=True)

    t0 = time.time()
    results: list[str] = []
    for i, (cat, icon_id, prompt) in enumerate(items, 1):
        t_item = time.time()
        msg = process_one(cat, icon_id, prompt,
                          skip_existing=args.skip_existing,
                          dry_run=args.dry_run,
                          retry_once=args.retry_once)
        elapsed = time.time() - t_item
        _log(f"[{i:>3}/{len(items)}] ({cat:<9}) {msg}  [+{elapsed:.0f}s]")
        results.append(msg)

    total = time.time() - t0
    ok = sum(1 for r in results if r.startswith("[ok]"))
    fail = sum(1 for r in results if r.startswith("[FAIL"))
    skip = sum(1 for r in results if r.startswith("[skip]"))
    _log(f"===== done in {total:.0f}s: ok={ok} fail={fail} skip={skip} =====")
    if fail:
        _log("Failed items:")
        for r in results:
            if r.startswith("[FAIL"):
                _log(f"  {r}")
    return 0 if fail == 0 else 2


if __name__ == "__main__":
    sys.exit(main())
