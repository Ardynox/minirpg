"""
Banana — Gemini 3.1 Flash Image (Nano Banana 2) 出图 CLI

调用底层模型：gemini-3.1-flash-image-preview（2026-02 发布的 Nano Banana 2）。
覆盖能力：512px-4K、14 种宽高比（含 1:8 / 8:1 极端比，可直接出 8 方向 sheet）、
多角色一致性、Pro 级文字渲染。

典型用法
========

# 1. dry-run 验证 prompt 拼接，不调用 API、不计费
python Tools/banana/banana_gen.py \
    --style style_base --subject "single small campfire with stones and logs" \
    --negatives standard --aspect 1:1 --size 1K \
    --out Artifacts/preview/campfire.png --dry-run

# 2. 真出图（一张 1K，约 $0.067）
python Tools/banana/banana_gen.py \
    --style style_base --subject "single closed wooden treasure chest with iron banding" \
    --negatives standard --aspect 1:1 --size 1K \
    --out Artifacts/preview/chest.png

# 3. 角色 8 方向 vertical sheet（1:8 比例，Nano Banana 2 才支持）
python Tools/banana/banana_gen.py \
    --prompt-file Tools/banana/prompts/example_char_8dir.txt \
    --aspect 1:8 --size 2K \
    --out Artifacts/preview/char_8dir.png

# 4. 一次出 4 张候选（按 Assets/Art/README.md SOP §4 「4 选 1-2」）
python Tools/banana/banana_gen.py \
    --style style_base --subject "..." --negatives standard \
    --aspect 1:1 --size 1K --count 4 \
    --out Artifacts/preview/grass_tile.png

认证
====

二选一：
- 系统环境变量：GEMINI_API_KEY=...
- 项目本地：Tools/banana/.env（参考 .env.example，本文件已加 .gitignore）

成本与配额
==========

- 1K image：$0.067
- 2K image：$0.134
- 免费档：5000 prompt/月（按 https://ai.google.dev/pricing）
- 本脚本会把每次调用记到 Tools/banana/.cache/usage.jsonl，便于盘账
- --count 上限硬编码 4，防误触发批量烧钱

安全限制
========

- 默认要求 --out 路径在 Artifacts/ 或 Assets/ 下，避免写到奇怪的位置
- API key 不会被打印
- 失败也会记账（ok:false），方便排查
"""

from __future__ import annotations

import argparse
import base64
import http.client
import json
import os
import re
import sys
import time
import urllib.error
import urllib.request
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TOOLS_DIR = Path(__file__).resolve().parent
PROMPTS_DIR = TOOLS_DIR / "prompts"
USAGE_LOG = TOOLS_DIR / ".cache" / "usage.jsonl"

# 官方支持的宽高比（gemini-3.1-flash-image-preview，2026-02）
VALID_ASPECTS = [
    "1:1", "2:3", "3:2", "3:4", "4:3", "4:5", "5:4",
    "9:16", "16:9", "21:9",
    "1:2", "2:1", "1:4", "4:1", "1:8", "8:1",
]
VALID_SIZES = ["1K", "2K", "4K"]

# 估算成本（USD / image），1K & 2K 来自官方价目；4K 暂按 2K 双倍兜底
COST_PER_IMAGE = {"1K": 0.067, "2K": 0.134, "4K": 0.268}

DEFAULT_MODEL_GEMINI = "gemini-3.1-flash-image-preview"
DEFAULT_MODEL_OPENAI = "Nano_Banana_2_2K_0"
SUPPORTED_PROTOCOLS = {"gemini", "openai"}
MAX_COUNT = 4
MAX_OUT_REL_DEPTH = 8  # 防御性：--out 深度不能超过这个


def load_env() -> None:
    """读 Tools/banana/.env（如有），写入 os.environ。失败不中断。"""
    env_path = TOOLS_DIR / ".env"
    if not env_path.exists():
        return
    # utf-8-sig 自动剥 BOM；PowerShell 5.1 的 Set-Content -Encoding utf8 会写 BOM
    for raw in env_path.read_text(encoding="utf-8-sig").splitlines():
        line = raw.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, _, value = line.partition("=")
        key = key.strip()
        value = value.strip().strip('"').strip("'")
        os.environ.setdefault(key, value)


def load_template(name: str) -> str:
    path = PROMPTS_DIR / f"{name}.txt"
    if not path.exists():
        sys.exit(f"[banana] prompt 模板不存在: {path}")
    return path.read_text(encoding="utf-8").strip()


def assemble_prompt(args: argparse.Namespace) -> str:
    parts: list[str] = []
    if args.style:
        parts.append(load_template(args.style))

    if args.subject:
        parts.append(args.subject.strip())
    elif args.prompt_file:
        text = Path(args.prompt_file).read_text(encoding="utf-8").strip()
        if not text:
            sys.exit(f"[banana] prompt 文件为空: {args.prompt_file}")
        parts.append(text)
    elif args.prompt:
        parts.append(args.prompt.strip())
    else:
        sys.exit("[banana] 必须提供 --subject / --prompt / --prompt-file 之一")

    if args.negatives:
        parts.append(load_template(f"negatives_{args.negatives}"))

    return "\n\n".join(parts)


def validate_out_path(out: Path) -> None:
    """要求输出在 Artifacts/ 或 Assets/ 下，避免误写到 Tests/ / MiniRPG.Shared/ 等地方。"""
    try:
        rel = out.resolve().relative_to(ROOT)
    except ValueError:
        sys.exit(f"[banana] --out 必须在仓库内: {out}")
    top = rel.parts[0] if rel.parts else ""
    if top not in {"Artifacts", "Assets"}:
        sys.exit(
            f"[banana] --out 必须在 Artifacts/ 或 Assets/ 下，当前: {rel}\n"
            "        如确实要写到别处，临时改本脚本，但建议先放 Artifacts/ 评估"
        )
    if len(rel.parts) > MAX_OUT_REL_DEPTH:
        sys.exit(f"[banana] --out 路径太深: {rel}")


def log_usage(model: str, size: str, count: int, ok: bool, error: str | None = None) -> None:
    USAGE_LOG.parent.mkdir(parents=True, exist_ok=True)
    rec = {
        "ts": datetime.now(timezone.utc).isoformat(),
        "model": model,
        "size": size,
        "count": count,
        "ok": ok,
        "est_cost_usd": round(COST_PER_IMAGE.get(size, 0.0) * count, 4),
    }
    if error:
        rec["error"] = error[:200]
    with USAGE_LOG.open("a", encoding="utf-8") as fh:
        fh.write(json.dumps(rec, ensure_ascii=False) + "\n")


def _warmup(client) -> None:
    """跑一次 1-token 文本调用，把代理 / TLS / SDK 连接池预热。

    背景：在中国大陆 + Proxifier / Clash 这类二级代理的链路下，首次握手经常被
    上游切断（症状是 70 秒后 'Server disconnected' 或 'UNEXPECTED_EOF'），第二次起秒过。
    用最便宜的 gemini-2.5-flash 跑 1 token 暖一下，整体耗时 +1~2 秒，
    但能消除 70 秒级冷启动失败。
    """
    print("[banana] warmup ...", flush=True)
    t = time.time()
    try:
        client.models.generate_content(model="gemini-2.5-flash", contents="ok")
        print(f"[banana] warmup ok ({time.time() - t:.1f}s)")
    except Exception as exc:
        print(f"[banana] warmup 失败（不阻断主调用）: {type(exc).__name__}: {str(exc)[:120]}")


def _format_api_error(exc: Exception) -> str:
    """把 API 错误映射到人话 + 解决建议。"""
    msg = str(exc)
    head = f"[banana] API 调用失败: {type(exc).__name__}"
    if "RESOURCE_EXHAUSTED" in msg or "429" in msg:
        return (
            f"{head}: 配额耗尽 / 免费档不可用\n"
            f"        细节: {msg[:300]}\n"
            "        提示：2026 年起 Google 把所有 image generation 模型移出免费档，\n"
            "              必须绑定 billing account 升级到 Tier 1 才能用。\n"
            "              升级入口：https://aistudio.google.com/apikey 旁边的 'Set up Billing'\n"
            "              新用户首次绑卡有 $300 / 90 天试用额度（约可出 4400 张 1K 图）"
        )
    if "INVALID_ARGUMENT" in msg and "paid plan" in msg.lower():
        return (
            f"{head}: 该模型仅付费账号可用\n"
            f"        细节: {msg[:300]}\n"
            "        升级：https://aistudio.google.com/apikey"
        )
    if "UNEXPECTED_EOF" in msg or "Server disconnected" in msg:
        return (
            f"{head}: 代理 / TLS 链路被切断\n"
            f"        细节: {msg[:200]}\n"
            "        提示：通常是 Proxifier / Clash 链路冷启动 + 节点不稳，\n"
            "              重跑一次（脚本会自动 warmup 预热连接）；\n"
            "              如果连续 3 次都失败，换代理节点 / 检查 Proxifier 规则"
        )
    return f"{head}: {msg[:400]}"


# 匹配中转站常见的 markdown 图片 URL 格式：![...](url) 或裸 URL
_MD_IMAGE_URL_RE = re.compile(r"!\[[^\]]*\]\((https?://[^\s)]+)\)")
_BARE_IMAGE_URL_RE = re.compile(r"(https?://[^\s)]+?\.(?:png|jpg|jpeg|webp))", re.IGNORECASE)


def _save_image(resp, out_path: Path, total: int, idx: int) -> Path:
    """抽出第一张图写到 out_path。支持两种响应格式：

    1. Google 原生：``candidates[].content.parts[].inline_data.data`` (base64)
    2. 中转网关（如 suxi）：``text`` 字段里塞 ``![image](https://...)``

    多张时自动加 ``_NN`` 后缀。如果服务端返回的 MIME 和 out_path 扩展名不符
    （例如 out 写 .png 但中转返回了 .jpg），**使用服务端的真实扩展名覆盖**。
    """
    target = out_path if total == 1 else out_path.with_stem(f"{out_path.stem}_{idx + 1:02d}")
    target.parent.mkdir(parents=True, exist_ok=True)

    if not resp.candidates:
        _dump_debug_response(resp, target)
        raise RuntimeError("响应里没有 candidates；详见 *.debug.json")

    for part in resp.candidates[0].content.parts:
        # 路径 1：原生 inline_data
        inline = getattr(part, "inline_data", None)
        if inline is not None and getattr(inline, "data", None):
            data = inline.data
            if isinstance(data, str):
                data = base64.b64decode(data)
            target.write_bytes(data)
            return target

        # 路径 2：text 里的 URL（中转常见）
        text = getattr(part, "text", None)
        if not text:
            continue
        url = _extract_image_url(text)
        if url is None:
            continue
        return _download_image_url(url, target)

    _dump_debug_response(resp, target)
    raise RuntimeError(
        "响应里没找到图片；详见 *.debug.json\n"
        "        常见原因：prompt 被安全过滤 / 中转协议不兼容 / 模型未开通"
    )


def _extract_image_url(text: str) -> str | None:
    match = _MD_IMAGE_URL_RE.search(text)
    if match:
        return match.group(1)
    match = _BARE_IMAGE_URL_RE.search(text)
    if match:
        return match.group(1)
    return None


def _download_image_url(url: str, target: Path) -> Path:
    """下载 URL 图片；如果扩展名与 target 不符，改 target 后缀。"""
    actual_ext = Path(url.split("?", 1)[0]).suffix.lower() or ".png"
    if actual_ext != target.suffix.lower():
        new_target = target.with_suffix(actual_ext)
        print(
            f"[banana] 中转返回 {actual_ext} 格式（原 --out 是 {target.suffix}）；"
            f"实际写入 {new_target}"
        )
        if actual_ext in {".jpg", ".jpeg"}:
            print("[banana] ⚠ JPG 不支持透明背景，游戏素材可能需要人工抠图")
        target = new_target

    req = urllib.request.Request(url, headers={"User-Agent": "banana-cli/1.0"})
    with urllib.request.urlopen(req, timeout=30) as resp:
        target.write_bytes(resp.read())
    return target


def _dump_debug_response(resp, target: Path) -> None:
    """把响应对象尽可能完整地 dump 成 JSON，便于肉眼诊断。"""
    debug_path = target.with_suffix(".debug.json")
    debug_path.parent.mkdir(parents=True, exist_ok=True)
    try:
        dump = resp.model_dump(exclude_none=False)
    except Exception:
        try:
            dump = {"repr": repr(resp)[:4000]}
        except Exception as exc:
            dump = {"dump_failed": str(exc)}
    try:
        debug_path.write_text(
            json.dumps(dump, ensure_ascii=False, indent=2, default=str),
            encoding="utf-8",
        )
        print(f"[banana] debug response 写入: {debug_path}")
    except Exception as exc:
        print(f"[banana] 无法写 debug json: {exc}")


def _force_utf8_stdio() -> None:
    """Windows PowerShell 默认 cp936，会让中文输出乱码；强制 UTF-8。"""
    for stream_name in ("stdout", "stderr"):
        stream = getattr(sys, stream_name, None)
        reconfigure = getattr(stream, "reconfigure", None)
        if reconfigure is None:
            continue
        try:
            reconfigure(encoding="utf-8")
        except Exception:
            pass


def main() -> int:
    _force_utf8_stdio()
    load_env()

    parser = argparse.ArgumentParser(
        description="Banana — Gemini 3.1 Flash Image (Nano Banana 2) 出图 CLI",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=__doc__,
    )
    src = parser.add_mutually_exclusive_group(required=False)
    src.add_argument("--subject", help="主体描述（短句，会和 --style/--negatives 拼接）")
    src.add_argument("--prompt", help="完整 prompt（不会再拼 style / negatives）")
    src.add_argument("--prompt-file", help="从文件读 prompt（同 --prompt，不再拼接）")

    parser.add_argument("--style", help="prepend 的风格模板名，对应 prompts/<name>.txt")
    parser.add_argument("--negatives", help="append 的反向词模板名，对应 prompts/negatives_<name>.txt")
    parser.add_argument("--aspect", default="1:1", choices=VALID_ASPECTS)
    parser.add_argument("--size", default="1K", choices=VALID_SIZES)
    parser.add_argument("--count", type=int, default=1, help=f"出图数量，1-{MAX_COUNT}")
    parser.add_argument("--out", required=True, help="输出 PNG 路径（多张时自动加 _01/_02 后缀）")
    parser.add_argument("--model", help="模型名；不传则按协议选默认（gemini 协议:%s / openai 协议:%s）" % (DEFAULT_MODEL_GEMINI, DEFAULT_MODEL_OPENAI))
    parser.add_argument(
        "--protocol",
        choices=sorted(SUPPORTED_PROTOCOLS),
        help="API 协议：gemini=Google 原生 (官方/suxi/oneapi-gemini-mode)，openai=OpenAI 兼容 (xais 等)；不传则读 GEMINI_PROTOCOL 环境变量，默认 gemini",
    )
    parser.add_argument("--dry-run", action="store_true", help="只打印 prompt 与预估成本，不调用 API")
    parser.add_argument("--no-warmup", action="store_true", help="跳过 warmup 调用（默认会先发一个 1-token 文本调用预热代理）")

    args = parser.parse_args()

    if args.count < 1 or args.count > MAX_COUNT:
        sys.exit(f"[banana] --count 必须在 1-{MAX_COUNT} 之间")

    out_path = Path(args.out)
    validate_out_path(out_path)

    protocol = (args.protocol or os.environ.get("GEMINI_PROTOCOL", "gemini")).strip().lower()
    if protocol not in SUPPORTED_PROTOCOLS:
        sys.exit(f"[banana] 未知协议 '{protocol}'，支持: {sorted(SUPPORTED_PROTOCOLS)}")

    if not args.model:
        args.model = DEFAULT_MODEL_OPENAI if protocol == "openai" else DEFAULT_MODEL_GEMINI

    prompt = assemble_prompt(args)
    base_url = os.environ.get("GEMINI_BASE_URL", "").strip() or None
    est_cost = COST_PER_IMAGE.get(args.size, 0.0) * args.count

    print(f"[banana] protocol = {protocol}")
    print(f"[banana] model    = {args.model}")
    print(f"[banana] aspect   = {args.aspect}")
    print(f"[banana] size     = {args.size}")
    print(f"[banana] count    = {args.count}")
    print(f"[banana] out      = {out_path}")
    if base_url:
        print(f"[banana] base_url = {base_url}")
    print(f"[banana] est_cost = ${est_cost:.4f} (官方 Gemini 价格估算；中转价格以渠道为准)")
    print("[banana] --- prompt ---")
    print(prompt)
    print("[banana] --- end prompt ---")

    if args.dry_run:
        print("[banana] dry-run: 未调用 API")
        return 0

    api_key = os.environ.get("GEMINI_API_KEY")
    if not api_key:
        sys.exit(
            "[banana] GEMINI_API_KEY 未设置。\n"
            "        - 或 cp Tools/banana/.env.example Tools/banana/.env 然后填进去\n"
            "        申请：https://aistudio.google.com/apikey  或拿第三方中转的 key"
        )

    runner = _run_openai_compat if protocol == "openai" else _run_gemini_native

    saved: list[Path] = []
    t0 = time.time()
    try:
        runner(args, prompt, api_key, base_url, out_path, saved)
    except Exception as exc:
        log_usage(args.model, args.size, args.count, ok=False, error=str(exc))
        sys.exit(_format_api_error(exc))

    log_usage(args.model, args.size, args.count, ok=True)
    elapsed = time.time() - t0
    print(f"[banana] done in {elapsed:.1f}s, saved:")
    for path in saved:
        print(f"  - {path}")
    return 0


def _run_gemini_native(args, prompt: str, api_key: str, base_url: str | None,
                       out_path: Path, saved: list[Path]) -> None:
    """走 Google 原生 Gemini 协议（google-genai SDK）。

    适配：Google 官方 API、suxi 这类 Gemini 兼容中转。
    """
    try:
        from google import genai
        from google.genai import types
    except ImportError:
        sys.exit(
            "[banana] 缺依赖 google-genai，请先安装：\n"
            "        pip install -r Tools/banana/requirements.txt"
        )

    client_kwargs: dict = {"api_key": api_key}
    if base_url:
        client_kwargs["http_options"] = types.HttpOptions(base_url=base_url)
    client = genai.Client(**client_kwargs)

    if not args.no_warmup:
        _warmup(client)

    for i in range(args.count):
        resp = client.models.generate_content(
            model=args.model,
            contents=prompt,
            config=types.GenerateContentConfig(
                response_modalities=["TEXT", "IMAGE"],
                image_config=types.ImageConfig(
                    aspect_ratio=args.aspect,
                    image_size=args.size,
                ),
            ),
        )
        saved.append(_save_image(resp, out_path, args.count, i))


def _run_openai_compat(args, prompt: str, api_key: str, base_url: str | None,
                       out_path: Path, saved: list[Path]) -> None:
    """走 OpenAI 兼容 /v1/chat/completions 协议。

    适配：xais 这类 OpenAI-API-compatible 中转（含 Nano Banana 系列）。
    特点：
      - aspect ratio 必须**写在 prompt 里**（OpenAI 协议本身没有这个字段）
      - 模型名是中转自定义别名（如 Nano_Banana_2_2K_0），不是 gemini-3.1-...
      - size 由模型名决定（_2K_0 / _4K_0），--size 仅用于成本估算
      - 响应通常是 markdown image URL（jpg/png 看中转）
    """
    if not base_url:
        sys.exit("[banana] OpenAI 协议必须设 GEMINI_BASE_URL（指向中转的根 URL）")

    # 把 aspect 提示嵌入 prompt 末尾。中文/英文都试，提高命中率。
    prompt_with_aspect = (
        f"{prompt}\n\n"
        f"image aspect ratio {args.aspect}（图片长宽比 {args.aspect}）"
    )

    endpoint = base_url.rstrip("/") + "/v1/chat/completions"

    for i in range(args.count):
        resp_json = _openai_chat_completion(
            endpoint=endpoint,
            api_key=api_key,
            model=args.model,
            user_text=prompt_with_aspect,
            timeout_seconds=600,  # 中转出图常 60-300s，给足
        )
        saved.append(_save_openai_image(resp_json, out_path, args.count, i))


def _openai_chat_completion(*, endpoint: str, api_key: str, model: str,
                            user_text: str, timeout_seconds: int,
                            max_retries: int = 3,
                            retry_backoff_base: float = 3.0) -> dict:
    """走中转的 /v1/chat/completions；遇到瞬时断流自动重试。

    触发重试的异常：
      - ``http.client.IncompleteRead``：中转返回 4MB 图时常被上游代理切流
      - ``urllib.error.URLError``（不含 HTTPError）：连接/超时/DNS 等
      - ``TimeoutError``：socket 级超时
    重试策略：指数退避 ``retry_backoff_base^attempt``，最多 ``max_retries`` 次
    （总共尝试 max_retries+1 次）。``HTTPError`` 是服务端明确拒绝（403/400/...），
    不重试，直接向上抛。
    """
    body = {
        "model": model,
        "messages": [{"role": "user", "content": user_text}],
    }
    data = json.dumps(body).encode("utf-8")
    last_err: Exception | None = None
    for attempt in range(max_retries + 1):
        req = urllib.request.Request(
            endpoint,
            data=data,
            method="POST",
            headers={
                "Content-Type": "application/json",
                "Authorization": f"Bearer {api_key}",
                "User-Agent": "banana-cli/1.0",
            },
        )
        try:
            with urllib.request.urlopen(req, timeout=timeout_seconds) as fh:
                raw = fh.read()
            return json.loads(raw.decode("utf-8"))
        except urllib.error.HTTPError as exc:
            body_text = exc.read().decode("utf-8", errors="replace")
            # 5xx（含 Cloudflare 520/521/522/524）= 上游瞬时故障，值得重试；
            # 4xx 是客户端/模型权限问题，重试也没用，立刻抛。
            if 500 <= exc.code < 600 and attempt < max_retries:
                last_err = RuntimeError(f"HTTP {exc.code}: {body_text[:200]}")
                wait = retry_backoff_base ** attempt
                print(
                    f"[banana] 上游 {exc.code} ({exc.reason})；"
                    f"{wait:.1f}s 后重试 ({attempt + 1}/{max_retries})",
                    flush=True,
                )
                time.sleep(wait)
                continue
            raise RuntimeError(f"HTTP {exc.code} {exc.reason}: {body_text[:400]}") from exc
        except (http.client.IncompleteRead, urllib.error.URLError,
                TimeoutError, ConnectionError) as exc:
            last_err = exc
            if attempt >= max_retries:
                break
            wait = retry_backoff_base ** attempt
            name = type(exc).__name__
            detail = str(exc)[:180]
            print(
                f"[banana] 瞬时失败 ({name}: {detail})；"
                f"{wait:.1f}s 后重试 ({attempt + 1}/{max_retries})",
                flush=True,
            )
            time.sleep(wait)
    raise RuntimeError(
        f"重试 {max_retries} 次仍失败：{type(last_err).__name__}: {last_err}"
    ) from last_err


def _save_openai_image(resp_json: dict, out_path: Path, total: int, idx: int) -> Path:
    target = out_path if total == 1 else out_path.with_stem(f"{out_path.stem}_{idx + 1:02d}")
    target.parent.mkdir(parents=True, exist_ok=True)

    try:
        content = resp_json["choices"][0]["message"]["content"]
    except (KeyError, IndexError, TypeError) as exc:
        _dump_openai_debug(resp_json, target)
        raise RuntimeError(
            f"OpenAI 响应结构异常 ({type(exc).__name__})；详见 *.debug.json"
        ) from exc

    if isinstance(content, list):
        # 某些中转返回 multipart content
        text_parts = [p.get("text", "") for p in content if isinstance(p, dict)]
        content = "\n".join(text_parts)

    url = _extract_image_url(content) if isinstance(content, str) else None
    if url is None:
        _dump_openai_debug(resp_json, target)
        raise RuntimeError(
            "OpenAI 响应里没找到图片 URL；详见 *.debug.json\n"
            "        常见原因：中转拒绝出图（额度 / 安全 / 模型未开通）/ aspect 写法不被识别"
        )
    # 中转有时把 URL 里 & 编码为 \u0026（看 xais 文档说明）
    url = url.replace("\\u0026", "&")
    return _download_image_url(url, target)


def _dump_openai_debug(resp_json: dict, target: Path) -> None:
    debug_path = target.with_suffix(".debug.json")
    debug_path.parent.mkdir(parents=True, exist_ok=True)
    try:
        debug_path.write_text(
            json.dumps(resp_json, ensure_ascii=False, indent=2, default=str),
            encoding="utf-8",
        )
        print(f"[banana] debug response 写入: {debug_path}")
    except Exception as exc:
        print(f"[banana] 无法写 debug json: {exc}")


if __name__ == "__main__":
    sys.exit(main())
