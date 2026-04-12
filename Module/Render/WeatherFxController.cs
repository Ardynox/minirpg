using System.Collections.Generic;
using Godot;
using MiniRPG.Core.Weather;

namespace MiniRPG.Module.Render;

/// <summary>
/// Owns all per-cell weather FX rendering, screen-level weather overlay, and
/// weather sprite pooling.  Extracted from TileMapRenderModule to reduce its
/// line count and isolate weather concerns.
/// </summary>
internal sealed class WeatherFxController
{
	// ── asset paths / constants ──────────────────────────────────────────
	private const string WeatherAssetRoot = "res://Assets/Art/Placeholders/weather";
	private const string WeatherScreenShaderPath = "res://Assets/Shaders/weather_screen_fx.gdshader";
	private const string WeatherRealtimeShaderPath = "res://Assets/Shaders/weather_realtime_fx.gdshader";
	private const string WeatherScreenFxOverlayName = "WeatherScreenFxOverlay";

	internal static readonly string[] WeatherAssetIds =
	[
		"snow_cover",
		"sand_cover",
		"ice_gloss",
		"wet_gloss",
		"rain",
		"fog",
		"snow",
		"dust",
		"lightning",
	];

	// ── injected state ──────────────────────────────────────────────────
	private readonly GameState _state;

	// ── sprite pools ────────────────────────────────────────────────────
	private readonly List<Sprite2D> _weatherOverlaySprites = [];
	private readonly List<Sprite2D> _peripheralWeatherOverlaySprites = [];
	private readonly List<Sprite2D> _memoryWeatherOverlaySprites = [];
	private readonly List<Sprite2D> _weatherFxSprites = [];
	private readonly List<Sprite2D> _peripheralWeatherFxSprites = [];

	private int _weatherOverlaySpriteCount;
	private int _peripheralWeatherOverlaySpriteCount;
	private int _memoryWeatherOverlaySpriteCount;
	private int _weatherFxSpriteCount;
	private int _peripheralWeatherFxSpriteCount;

	// ── caches / materials ──────────────────────────────────────────────
	private readonly Dictionary<string, Texture2D> _weatherTextureCache = new(System.StringComparer.OrdinalIgnoreCase);
	private Texture2D? _weatherFxQuadTexture;
	private Shader? _weatherFxShader;

	// ── screen FX ───────────────────────────────────────────────────────
	private ColorRect? _weatherScreenFxOverlay;
	private ShaderMaterial? _weatherScreenFxMaterial;
	private Shader? _weatherScreenFxShader;
	private WeatherScreenFxParams _weatherScreenFxTarget = WeatherScreenFxParams.Clear;
	private readonly WeatherScreenFxState _weatherScreenFxState = new();
	private WeatherScreenFxTuningSet _weatherScreenFxTuning = new();

	// ── node roots (owned by the controller after Init) ─────────────────
	private Node2D _weatherOverlayRoot = null!;
	private Node2D _peripheralWeatherOverlayRoot = null!;
	private Node2D _memoryWeatherOverlayRoot = null!;
	private Node2D _weatherFxRoot = null!;
	private Node2D _peripheralWeatherFxRoot = null!;

	// ── references into the parent renderer ─────────────────────────────
	private TileMapLayer _groundLayer = null!;
	private SubViewportContainer? _viewportContainer;
	private Vector2 _tilePixelSize;

	/// <summary>
	/// Total number of weather sprites currently active this frame (overlay + FX).
	/// Used by the parent renderer's perf counter.
	/// </summary>
	public int ActiveSpriteCount =>
		_weatherOverlaySpriteCount
		+ _peripheralWeatherOverlaySpriteCount
		+ _memoryWeatherOverlaySpriteCount
		+ _weatherFxSpriteCount
		+ _peripheralWeatherFxSpriteCount;

	public WeatherFxController(GameState state)
	{
		_state = state;
	}

	// ── Init / teardown ─────────────────────────────────────────────────

	public void Init(
		Node2D mapRoot,
		TileMapLayer groundLayer,
		Vector2 tilePixelSize,
		SubViewportContainer? viewportContainer,
		Node2D weatherOverlayRoot,
		Node2D peripheralWeatherOverlayRoot,
		Node2D memoryWeatherOverlayRoot,
		Node2D weatherFxRoot,
		Node2D peripheralWeatherFxRoot)
	{
		_groundLayer = groundLayer;
		_tilePixelSize = tilePixelSize;
		_viewportContainer = viewportContainer;

		_weatherOverlayRoot = weatherOverlayRoot;
		_peripheralWeatherOverlayRoot = peripheralWeatherOverlayRoot;
		_memoryWeatherOverlayRoot = memoryWeatherOverlayRoot;
		_weatherFxRoot = weatherFxRoot;
		_peripheralWeatherFxRoot = peripheralWeatherFxRoot;

		_weatherFxQuadTexture = null;
		_weatherFxShader = null;
		_weatherScreenFxShader = ResolveWeatherScreenFxShader();
		_weatherTextureCache.Clear();
		_weatherOverlaySprites.Clear();
		_peripheralWeatherOverlaySprites.Clear();
		_memoryWeatherOverlaySprites.Clear();
		_weatherFxSprites.Clear();
		_peripheralWeatherFxSprites.Clear();

		_weatherScreenFxOverlay = EnsureWeatherScreenFxOverlay(viewportContainer);
		_weatherScreenFxMaterial = EnsureWeatherScreenFxMaterial(_weatherScreenFxOverlay);
		RefreshWeatherScreenFxTarget(editorViewActive: false);
		UpdateWeatherScreenFxOverlay(0d, 0d);
	}

	/// <summary>Enumerate all known weather asset resource paths.</summary>
	public static System.Collections.Generic.IReadOnlyList<string> EnumerateWeatherAssetPaths() =>
		System.Array.ConvertAll(WeatherAssetIds, static id => $"{WeatherAssetRoot}/{id}.png");

	// ── per-frame lifecycle ─────────────────────────────────────────────

	public void BeginFrame()
	{
		_weatherOverlaySpriteCount = 0;
		_peripheralWeatherOverlaySpriteCount = 0;
		_memoryWeatherOverlaySpriteCount = 0;
		_weatherFxSpriteCount = 0;
		_peripheralWeatherFxSpriteCount = 0;
	}

	public void EndFrame()
	{
		HideUnusedSprites(_weatherOverlaySprites, _weatherOverlaySpriteCount);
		HideUnusedSprites(_peripheralWeatherOverlaySprites, _peripheralWeatherOverlaySpriteCount);
		HideUnusedSprites(_memoryWeatherOverlaySprites, _memoryWeatherOverlaySpriteCount);
		HideUnusedSprites(_weatherFxSprites, _weatherFxSpriteCount);
		HideUnusedSprites(_peripheralWeatherFxSprites, _peripheralWeatherFxSpriteCount);
	}

	// ── tuning API (delegated from TileMapRenderModule) ─────────────────

	public void SetTuning(WeatherScreenFxTuningSet? tuning, bool editorViewActive)
	{
		_weatherScreenFxTuning = tuning ?? new WeatherScreenFxTuningSet();
		RefreshWeatherScreenFxTarget(editorViewActive);
		UpdateWeatherScreenFxOverlay(0d, 0d);
	}

	// ── screen FX target refresh ────────────────────────────────────────

	public void RefreshWeatherScreenFxTarget(bool editorViewActive)
	{
		if (_state.World == null || editorViewActive)
		{
			_weatherScreenFxTarget = WeatherScreenFxParams.Clear;
			return;
		}

		var surface = WeatherSurface.GetSurfaceState(_state, _state.PlayerX, _state.PlayerY, _state.PlayerZ);
		var canShowScreenFx = _state.PlayerZ == 0 && surface.IsExposed;
		if (!canShowScreenFx)
		{
			_weatherScreenFxTarget = WeatherScreenFxParams.Clear;
			return;
		}

		var sample = WeatherRules.GetLocalWeather(_state, _state.PlayerX, _state.PlayerY, _state.PlayerZ);
		var hash = ComputeWeatherVisualHash(
			_state.WorldSeed,
			_state.PlayerX,
			_state.PlayerY,
			_state.PlayerZ,
			_state.Turn,
			(int)sample.Type,
			(int)sample.Intensity);
		_weatherScreenFxTarget = WeatherScreenFxResolver.ApplyTuning(
			WeatherScreenFxResolver.Resolve(sample, isActive: true, hash),
			_weatherScreenFxTuning);
	}

	/// <summary>Force target to clear (used when world is null or editor-only).</summary>
	public void ClearScreenFxTarget()
	{
		_weatherScreenFxTarget = WeatherScreenFxParams.Clear;
	}

	// ── per-frame screen overlay update ─────────────────────────────────

	public void UpdateWeatherScreenFxOverlay(double deltaSeconds, double animationClockSeconds)
	{
		if (_weatherScreenFxOverlay == null || _weatherScreenFxMaterial == null)
			return;

		_weatherScreenFxState.AdvanceTo(_weatherScreenFxTarget, deltaSeconds);
		var primary = _weatherScreenFxState.Primary;
		var secondary = _weatherScreenFxState.Secondary;
		_weatherScreenFxOverlay.Visible = _weatherScreenFxState.HasVisibleFx;
		if (!_weatherScreenFxOverlay.Visible)
			return;

		var viewportSize = _viewportContainer?.Size ?? Vector2.Zero;
		_weatherScreenFxMaterial.SetShaderParameter("time", (float)animationClockSeconds);
		_weatherScreenFxMaterial.SetShaderParameter("viewport_size", viewportSize);
		ApplyWeatherScreenFxParams("a", primary);
		ApplyWeatherScreenFxParams("b", secondary);
		ApplyWeatherScreenFxTuningProfile(_weatherScreenFxTuning.ResolveBlendedProfile(
			primary.Mode,
			secondary.Mode,
			_weatherScreenFxState.Blend));
		_weatherScreenFxMaterial.SetShaderParameter("blend_factor", _weatherScreenFxState.Blend);
	}

	// ── per-cell weather FX rendering ───────────────────────────────────

	public void RenderWeatherFx(
		Vector2I cell,
		int wx,
		int wy,
		int wz,
		WeatherSurfaceState surface,
		PlayerVisionBand band,
		double animationClockSeconds)
	{
		if (wz != 0 || !surface.IsExposed || band is PlayerVisionBand.Memory or PlayerVisionBand.Unknown)
			return;

		var sample = WeatherRules.GetLocalWeather(_state, wx, wy, wz);
		if (!sample.HasActiveWeather)
			return;

		var hash = ComputeWeatherVisualHash(wx, wy, wz, _state.Turn, (int)sample.Type, (int)sample.Intensity);
		if (WeatherFxVisualResolver.ResolveWeatherFxVisual(sample, band, hash) is { } weatherFx
			&& !TryRenderWeatherShaderFx(cell, band, weatherFx, animationClockSeconds))
		{
			var assetId = ResolveLegacyWeatherFxAssetId(sample.Type);
			if (!string.IsNullOrEmpty(assetId))
			{
				RenderWeatherTextureSprite(
					cell,
					assetId,
					band,
					overlay: false,
					weatherFx.FootprintTiles,
					new Color(weatherFx.Tint.R, weatherFx.Tint.G, weatherFx.Tint.B, weatherFx.Alpha));
			}
		}

		if (WeatherFxVisualResolver.ResolveLightningFxVisual(sample, band, hash) is { } lightningFx
			&& !TryRenderWeatherShaderFx(cell, band, lightningFx, animationClockSeconds))
		{
			RenderWeatherTextureSprite(
				cell,
				"lightning",
				band,
				overlay: false,
				lightningFx.FootprintTiles,
				new Color(lightningFx.Tint.R, lightningFx.Tint.G, lightningFx.Tint.B, lightningFx.Alpha));
		}
	}

	// ── internals: per-cell rendering helpers ───────────────────────────

	private void RenderWeatherTextureSprite(
		Vector2I cell,
		string assetId,
		PlayerVisionBand band,
		bool overlay,
		float footprintTiles,
		Color modulate)
	{
		var texture = ResolveWeatherTexture(assetId);
		if (texture == null)
			return;

		var sprite = AcquireWeatherSprite(band, overlay);
		ConfigureWeatherTextureSprite(sprite, texture, cell, footprintTiles, modulate);
	}

	private bool TryRenderWeatherShaderFx(
		Vector2I cell,
		PlayerVisionBand band,
		WeatherFxVisualParams visual,
		double animationClockSeconds)
	{
		if (_weatherFxShader == null || _weatherFxQuadTexture == null)
			return false;

		var sprite = AcquireWeatherSprite(band, overlay: false);
		ConfigureWeatherFxSprite(sprite, cell, visual, animationClockSeconds);
		return true;
	}

	private static string ResolveLegacyWeatherFxAssetId(WeatherType type) => type switch
	{
		WeatherType.Rain => "rain",
		WeatherType.Fog => "fog",
		WeatherType.Snow => "snow",
		WeatherType.Storm => "rain",
		WeatherType.Thunderstorm => "rain",
		WeatherType.Sandstorm => "dust",
		_ => string.Empty,
	};

	private void ConfigureWeatherTextureSprite(
		Sprite2D sprite,
		Texture2D texture,
		Vector2I cell,
		float footprintTiles,
		Color modulate)
	{
		sprite.Material = null;
		sprite.Texture = texture;
		sprite.RegionEnabled = false;
		sprite.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
		sprite.Scale = ResolveWeatherTextureScale(texture, footprintTiles);
		sprite.Position = _groundLayer.MapToLocal(cell);
		sprite.Modulate = modulate;
		sprite.Visible = true;
	}

	private void ConfigureWeatherFxSprite(
		Sprite2D sprite,
		Vector2I cell,
		WeatherFxVisualParams visual,
		double animationClockSeconds)
	{
		if (_weatherFxQuadTexture == null || EnsureWeatherFxMaterial(sprite) is not { } material)
			return;

		sprite.Texture = _weatherFxQuadTexture;
		sprite.RegionEnabled = false;
		sprite.TextureFilter = CanvasItem.TextureFilterEnum.Linear;
		sprite.Scale = _tilePixelSize * visual.FootprintTiles;
		sprite.Position = _groundLayer.MapToLocal(cell);
		sprite.Modulate = Colors.White;
		sprite.Visible = true;

		material.SetShaderParameter("mode", (int)visual.Mode);
		material.SetShaderParameter("intensity", visual.Intensity);
		material.SetShaderParameter("band_strength", visual.BandStrength);
		material.SetShaderParameter("time", (float)animationClockSeconds);
		material.SetShaderParameter("seed", visual.Seed);
		material.SetShaderParameter("wind_dir", visual.WindDirection);
		material.SetShaderParameter("coverage_alpha", visual.CoverageAlpha * visual.Alpha);
		material.SetShaderParameter("tint", visual.Tint);
		material.SetShaderParameter("density", visual.Density);
		material.SetShaderParameter("speed", visual.Speed);
	}

	// ── texture / shader resolution ─────────────────────────────────────

	private Texture2D? ResolveWeatherTexture(string assetId)
	{
		if (_weatherTextureCache.TryGetValue(assetId, out var cached))
			return cached;

		var texture = ResAccess.Get<Texture2D>($"{WeatherAssetRoot}/{assetId}.png");
		if (texture != null)
			_weatherTextureCache[assetId] = texture;
		return texture;
	}

	private Vector2 ResolveWeatherTextureScale(Texture2D texture, float footprintTiles)
	{
		var size = texture.GetSize();
		if (size.X <= 0f || size.Y <= 0f)
			return Vector2.One;

		return new Vector2(
			(_tilePixelSize.X * footprintTiles) / size.X,
			(_tilePixelSize.Y * footprintTiles) / size.Y);
	}

	private ShaderMaterial? EnsureWeatherFxMaterial(Sprite2D sprite)
	{
		if (_weatherFxShader == null)
			return null;

		if (sprite.Material is ShaderMaterial material)
		{
			if (material.Shader != _weatherFxShader)
				material.Shader = _weatherFxShader;
			return material;
		}

		var created = new ShaderMaterial
		{
			Shader = _weatherFxShader,
		};
		sprite.Material = created;
		return created;
	}

	private static Shader? ResolveWeatherFxShader() =>
		ResAccess.Get<Shader>(WeatherRealtimeShaderPath);

	private static Shader? ResolveWeatherScreenFxShader() =>
		ResAccess.Get<Shader>(WeatherScreenShaderPath);

	private static Texture2D ResolveWeatherQuadTexture()
	{
		var image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
		image.SetPixel(0, 0, Colors.White);
		return ImageTexture.CreateFromImage(image);
	}

	// ── screen FX overlay setup ─────────────────────────────────────────

	private ColorRect? EnsureWeatherScreenFxOverlay(SubViewportContainer? viewportContainer)
	{
		if (viewportContainer == null)
			return null;

		var existingNode = viewportContainer.GetNodeOrNull<Node>(WeatherScreenFxOverlayName);
		if (existingNode is ColorRect existingOverlay)
		{
			ConfigureWeatherScreenFxOverlay(existingOverlay);
			return existingOverlay;
		}

		existingNode?.QueueFree();
		var overlay = new ColorRect
		{
			Name = WeatherScreenFxOverlayName,
			Color = Colors.White,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Visible = false,
			ZIndex = 32,
		};
		ConfigureWeatherScreenFxOverlay(overlay);
		viewportContainer.AddChild(overlay);
		return overlay;
	}

	private static void ConfigureWeatherScreenFxOverlay(ColorRect overlay)
	{
		overlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		overlay.OffsetLeft = 0f;
		overlay.OffsetTop = 0f;
		overlay.OffsetRight = 0f;
		overlay.OffsetBottom = 0f;
		overlay.MouseFilter = Control.MouseFilterEnum.Ignore;
	}

	private ShaderMaterial? EnsureWeatherScreenFxMaterial(ColorRect? overlay)
	{
		if (overlay == null || _weatherScreenFxShader == null)
			return null;

		if (overlay.Material is ShaderMaterial material)
		{
			if (material.Shader != _weatherScreenFxShader)
				material.Shader = _weatherScreenFxShader;
			return material;
		}

		var created = new ShaderMaterial
		{
			Shader = _weatherScreenFxShader,
		};
		overlay.Material = created;
		return created;
	}

	// ── screen FX shader parameter helpers ──────────────────────────────

	private void ApplyWeatherScreenFxParams(string suffix, WeatherScreenFxParams value)
	{
		if (_weatherScreenFxMaterial == null)
			return;

		_weatherScreenFxMaterial.SetShaderParameter($"mode_{suffix}", (int)value.Mode);
		_weatherScreenFxMaterial.SetShaderParameter($"overlay_alpha_{suffix}", value.OverlayAlpha);
		_weatherScreenFxMaterial.SetShaderParameter($"fog_alpha_{suffix}", value.FogAlpha);
		_weatherScreenFxMaterial.SetShaderParameter($"edge_tint_alpha_{suffix}", value.EdgeTintAlpha);
		_weatherScreenFxMaterial.SetShaderParameter($"edge_shadow_{suffix}", value.EdgeShadow);
		_weatherScreenFxMaterial.SetShaderParameter($"density_{suffix}", value.Density);
		_weatherScreenFxMaterial.SetShaderParameter($"speed_{suffix}", value.Speed);
		_weatherScreenFxMaterial.SetShaderParameter($"lightning_flash_{suffix}", value.LightningFlash);
		_weatherScreenFxMaterial.SetShaderParameter($"temperature_bias_{suffix}", value.TemperatureBias);
		_weatherScreenFxMaterial.SetShaderParameter($"seed_{suffix}", value.Seed);
		_weatherScreenFxMaterial.SetShaderParameter($"wind_dir_{suffix}", value.WindDirection);
		_weatherScreenFxMaterial.SetShaderParameter($"tint_{suffix}", value.Tint);
	}

	private void ApplyWeatherScreenFxTuningProfile(WeatherScreenFxTuningProfile value)
	{
		if (_weatherScreenFxMaterial == null)
			return;

		_weatherScreenFxMaterial.SetShaderParameter("particle_size_scale", value.ParticleSizeScale);
		_weatherScreenFxMaterial.SetShaderParameter("particle_frequency_scale", value.ParticleFrequencyScale);
		_weatherScreenFxMaterial.SetShaderParameter("particle_blend_scale", value.ParticleBlendScale);
		_weatherScreenFxMaterial.SetShaderParameter("tint_strength_scale", value.TintStrengthScale);
	}

	// ── weather sprite pooling ──────────────────────────────────────────

	private Sprite2D AcquireWeatherSprite(PlayerVisionBand band, bool overlay)
	{
		return (band, overlay) switch
		{
			(PlayerVisionBand.Focused, true) => AcquireSprite(_weatherOverlaySprites, _weatherOverlayRoot, "WeatherOverlaySprite", ref _weatherOverlaySpriteCount),
			(PlayerVisionBand.Peripheral, true) => AcquireSprite(_peripheralWeatherOverlaySprites, _peripheralWeatherOverlayRoot, "PeripheralWeatherOverlaySprite", ref _peripheralWeatherOverlaySpriteCount),
			(PlayerVisionBand.Memory, true) => AcquireSprite(_memoryWeatherOverlaySprites, _memoryWeatherOverlayRoot, "MemoryWeatherOverlaySprite", ref _memoryWeatherOverlaySpriteCount),
			(PlayerVisionBand.Peripheral, false) => AcquireSprite(_peripheralWeatherFxSprites, _peripheralWeatherFxRoot, "PeripheralWeatherFxSprite", ref _peripheralWeatherFxSpriteCount),
			_ => AcquireSprite(_weatherFxSprites, _weatherFxRoot, "WeatherFxSprite", ref _weatherFxSpriteCount),
		};
	}

	private static Sprite2D AcquireSprite(List<Sprite2D> pool, Node2D root, string namePrefix, ref int count)
	{
		var index = count++;
		if (index >= pool.Count)
		{
			var sprite = new Sprite2D
			{
				Name = $"{namePrefix}{pool.Count}",
				Centered = true,
				TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
			};
			root.AddChild(sprite);
			pool.Add(sprite);
		}

		return pool[index];
	}

	// ── hashing helper ──────────────────────────────────────────────────

	internal static int ComputeWeatherVisualHash(params int[] values)
	{
		unchecked
		{
			var hash = 19;
			foreach (var value in values)
				hash = (hash * 397) ^ value;
			return hash & int.MaxValue;
		}
	}

	// ── utility ─────────────────────────────────────────────────────────

	private static void HideUnusedSprites(IReadOnlyList<Sprite2D> sprites, int activeCount)
	{
		for (var i = activeCount; i < sprites.Count; i++)
			sprites[i].Visible = false;
	}
}
