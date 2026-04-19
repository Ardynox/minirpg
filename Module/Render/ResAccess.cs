using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using MiniRPG.Core.Config;

namespace MiniRPG.Module.Render;

/// <summary>
/// 资源访问中间层：数据驱动的资源注册表 + 异步加载 + LRU 缓存。
///
/// 业务代码通过 string key 获取资源或 IAnimatable，
/// 不关心底层是 Spine、Tile 还是占位图。
/// 替换素材时只改 entity_render.json，业务代码零修改。
/// </summary>
public static class ResAccess
{
	private const string RegistryPath = "entity_render.json";
	private const string Tag = "[ResAccess]";

	// ── 注册表 ──────────────────────────────────────────

	private static readonly Dictionary<string, RenderEntry> _registry = new();
	private static bool _loaded;

	// ── Godot Resource 缓存（path → Resource）──────────
	// Spine SkeletonData / Texture2D / 其他 Resource 共用

	private static readonly Dictionary<string, Resource> _cache = new();

	// ── 异步加载队列 ────────────────────────────────────

	private static readonly Dictionary<string, List<Action<Resource>>> _pending = new();

	// ── 活跃 IAnimatable 实例池（entityInstanceId → IAnimatable）──

	private static readonly Dictionary<string, IAnimatable> _activeInstances = new();

	// ═══════════════════════════════════════════════════
	//  初始化
	// ═══════════════════════════════════════════════════

	/// <summary>加载 entity_render.json 注册表。应在 Main._Ready 中调用一次。</summary>
	public static void Load()
	{
		if (_loaded) return;
		_loaded = true;

		if (!GameDataLocator.TryReadText(RegistryPath, out var json, out var sourceLabel))
		{
			GD.PrintErr($"{Tag} 找不到注册表文件：{sourceLabel}");
			return;
		}

		var opts = new JsonSerializerOptions
		{
			PropertyNameCaseInsensitive = true,
			ReadCommentHandling = JsonCommentHandling.Skip,
		};
		var map = JsonSerializer.Deserialize<Dictionary<string, RenderEntry>>(json, opts);
		if (map == null)
		{
			GD.PrintErr($"{Tag} 注册表 JSON 解析失败");
			return;
		}

		_registry.Clear();
		foreach (var (key, entry) in map)
			_registry[key] = entry;

		GD.Print($"{Tag} 已加载 {_registry.Count} 条渲染注册");
	}

	// ═══════════════════════════════════════════════════
	//  注册表查询
	// ═══════════════════════════════════════════════════

	/// <summary>查询某个 key 的渲染配置。不存在返回 null。</summary>
	public static RenderEntry? GetEntry(string key)
	{
		if (!_loaded) Load();
		return _registry.GetValueOrDefault(key);
	}

	/// <summary>查询某个 key 的渲染类型。不存在返回 "tile"（保底 fallback）。</summary>
	public static string GetRenderType(string key)
	{
		var entry = GetEntry(key);
		return entry?.Type ?? "tile";
	}

	/// <summary>判断某个 key 是否配置为 Spine 类型。</summary>
	public static bool IsSpine(string key) => GetRenderType(key) == "spine";

	// ═══════════════════════════════════════════════════
	//  同步资源获取（带缓存）
	// ═══════════════════════════════════════════════════

	/// <summary>
	/// 同步获取 Godot Resource（缓存命中直接返回，否则 GD.Load 并缓存）。
	/// 适用于小资源或确定已预加载完成的资源。
	/// </summary>
	public static T? Get<T>(string path) where T : Resource
	{
		if (_cache.TryGetValue(path, out var cached) && cached is T typed)
			return typed;

		var res = GD.Load<T>(path);
		if (res != null)
			_cache[path] = res;
		return res;
	}

	// ═══════════════════════════════════════════════════
	//  异步资源加载
	// ═══════════════════════════════════════════════════

	/// <summary>
	/// 异步请求加载资源。缓存命中则同步回调，否则发起线程加载。
	/// 需要在每帧调用 <see cref="PollAsyncLoads"/> 驱动完成回调。
	/// </summary>
	public static void RequestAsync(string path, Action<Resource> onLoaded)
	{
		if (_cache.TryGetValue(path, out var cached))
		{
			onLoaded(cached);
			return;
		}

		if (_pending.TryGetValue(path, out var list))
		{
			list.Add(onLoaded);
			return;
		}

		_pending[path] = [onLoaded];
		var err = ResourceLoader.LoadThreadedRequest(path);
		if (err != Error.Ok)
		{
			GD.PrintErr($"{Tag} 异步加载请求失败：{path} ({err})");
			_pending.Remove(path);
		}
	}

	/// <summary>
	/// 每帧轮询异步加载状态，完成的资源存入缓存并触发回调。
	/// 应在 Main._Process 中调用。
	/// </summary>
	public static void PollAsyncLoads()
	{
		if (_pending.Count == 0) return;

		var completed = new List<string>();

		foreach (var (path, callbacks) in _pending)
		{
			var status = ResourceLoader.LoadThreadedGetStatus(path);
			switch (status)
			{
				case ResourceLoader.ThreadLoadStatus.Loaded:
				{
					var res = ResourceLoader.LoadThreadedGet(path);
					if (res != null)
						_cache[path] = res;
					foreach (var cb in callbacks)
					{
						try { cb(res); }
						catch (Exception ex)
						{
							GD.PrintErr($"{Tag} 异步回调异常 [{path}]：{ex.Message}");
						}
					}
					completed.Add(path);
					break;
				}
				case ResourceLoader.ThreadLoadStatus.Failed:
				case ResourceLoader.ThreadLoadStatus.InvalidResource:
					GD.PrintErr($"{Tag} 异步加载失败：{path} (status={status})");
					completed.Add(path);
					break;
			}
		}

		foreach (var path in completed)
			_pending.Remove(path);
	}

	// ═══════════════════════════════════════════════════
	//  IAnimatable 实例管理
	// ═══════════════════════════════════════════════════

	/// <summary>
	/// 获取已创建的 IAnimatable 实例。不存在返回 null。
	/// </summary>
	public static IAnimatable? GetAnimatable(string instanceId)
		=> _activeInstances.GetValueOrDefault(instanceId);

	/// <summary>注册一个 IAnimatable 实例。</summary>
	public static void RegisterAnimatable(string instanceId, IAnimatable animatable)
		=> _activeInstances[instanceId] = animatable;

	/// <summary>注销一个 IAnimatable 实例。</summary>
	public static void UnregisterAnimatable(string instanceId)
		=> _activeInstances.Remove(instanceId);

	/// <summary>清除所有活跃实例（切换地图/楼层时调用）。</summary>
	public static void ClearAnimatables()
		=> _activeInstances.Clear();

	// ═══════════════════════════════════════════════════
	//  缓存管理
	// ═══════════════════════════════════════════════════

	/// <summary>从缓存中释放指定资源。</summary>
	public static void Release(string path) => _cache.Remove(path);

	/// <summary>清空全部缓存和异步队列。谨慎使用。</summary>
	public static void Reset()
	{
		_cache.Clear();
		_pending.Clear();
		_activeInstances.Clear();
		_registry.Clear();
		_loaded = false;
	}

	// ═══════════════════════════════════════════════════
	//  JSON 数据模型
	// ═══════════════════════════════════════════════════

	/// <summary>entity_render.json 中一条渲染配置。</summary>
	public class RenderEntry
	{
		/// <summary>"spine" / "sprite_sheet" / "texture" / "tile" / "placeholder"</summary>
		[JsonPropertyName("type")]
		public string Type { get; set; } = "tile";

		[JsonPropertyName("atlas")]
		public string? Atlas { get; set; }

		[JsonPropertyName("skel")]
		public string? Skel { get; set; }

		[JsonPropertyName("defaultAnim")]
		public string? DefaultAnim { get; set; }

		/// <summary>[scaleX, scaleY]</summary>
		[JsonPropertyName("scale")]
		public float[]? Scale { get; set; }

		/// <summary>[offsetX, offsetY] in rendered map pixels.</summary>
		[JsonPropertyName("offset")]
		public float[]? Offset { get; set; }

		[JsonPropertyName("sheetDir")]
		public string? SheetDir { get; set; }

		[JsonPropertyName("texturePath")]
		public string? TexturePath { get; set; }

		[JsonPropertyName("frameWidth")]
		public int FrameWidth { get; set; }

		[JsonPropertyName("frameHeight")]
		public int FrameHeight { get; set; }

		[JsonPropertyName("useFacing")]
		public bool UseFacing { get; set; }

		[JsonPropertyName("tileName")]
		public string? TileName { get; set; }
	}
}
