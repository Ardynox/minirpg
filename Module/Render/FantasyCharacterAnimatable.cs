using System;
using System.Collections.Generic;
using Godot;

namespace MiniRPG.Module.Render;

public partial class FantasyCharacterAnimatable : Node2D, IAnimatable
{
	private const int FrameWidth = 128;
	private const int FrameHeight = 128;
	private const int DirectionRowCount = 8;
	private const float DefaultFramesPerSecond = 12f;
	private const string MissingSheetFallbackPath = "res://Assets/Art/Placeholders/fallbacks/missing_character_sheet_8dir.png";
	private static readonly StringComparer KeyComparer = StringComparer.OrdinalIgnoreCase;
	private static readonly HashSet<string> MissingSheetWarnings = new(StringComparer.OrdinalIgnoreCase);
	private static readonly Dictionary<string, string> AnimationSheetMap = new(KeyComparer)
	{
		["Idle"] = "Idle",
		["Walk"] = "Walk",
		["Run"] = "Run",
		["Attack_1"] = "Attack1",
		["Attack1"] = "Attack1",
		["Pain"] = "TakeDamage",
		["TakeDamage"] = "TakeDamage",
		["Die"] = "Die",
		["Special_1"] = "Special1",
		["Special1"] = "Special1",
	};
	private static Texture2D? _missingSheetFallbackTexture;
	private static string _missingSheetFallbackLabel = MissingSheetFallbackPath;
	private static bool _missingSheetFallbackInitialized;

	private readonly Sprite2D _sprite;
	private readonly Dictionary<string, Texture2D> _sheets = new(KeyComparer);
	private string _sheetDirectory = "";
	private string _currentAnimation = "Idle";
	private string _currentSheet = "Idle";
	private string? _queuedAnimation;
	private bool _loop = true;
	private bool _configured;
	private int _frameCount = 1;
	private int _frameIndex;
	private int _directionRow = DirectionalSpriteHelper.DefaultDirectionRow;
	private double _frameClockSeconds;

	public Node2D Node => this;
	bool IAnimatable.Ready => _configured;

	public FantasyCharacterAnimatable()
	{
		ZIndex = 3;
		_sprite = new Sprite2D
		{
			Centered = true,
			RegionEnabled = true,
			TextureFilter = TextureFilterEnum.Nearest,
		};
		AddChild(_sprite);
	}

	public void Configure(string sheetDirectory, string defaultAnimation = "Idle")
	{
		var normalizedDirectory = sheetDirectory.TrimEnd('/');
		if (!KeyComparer.Equals(_sheetDirectory, normalizedDirectory))
			_sheets.Clear();

		_sheetDirectory = normalizedDirectory;
		_directionRow = DirectionalSpriteHelper.DefaultDirectionRow;
		_frameClockSeconds = 0d;
		_frameIndex = 0;
		_queuedAnimation = null;
		_configured = true;
		Visible = false;
		_sprite.Visible = true;
		StartAnimation(ResolveAnimation(defaultAnimation), loop: true, queuedAnimation: null, forceRestart: true);
	}

	public override void _EnterTree()
	{
		SetProcess(true);
	}

	public override void _Process(double delta)
	{
		if (!_configured || _frameCount <= 1 || delta <= 0d)
			return;

		var frameDurationSeconds = 1d / DefaultFramesPerSecond;
		_frameClockSeconds += delta;
		var advanced = false;

		while (_frameClockSeconds >= frameDurationSeconds)
		{
			_frameClockSeconds -= frameDurationSeconds;
			_frameIndex++;
			advanced = true;

			if (_frameIndex < _frameCount)
				continue;

			if (_loop)
			{
				_frameIndex = 0;
				continue;
			}

			if (!string.IsNullOrWhiteSpace(_queuedAnimation))
			{
				var nextAnimation = _queuedAnimation!;
				_queuedAnimation = null;
				StartAnimation(nextAnimation, loop: true, queuedAnimation: null, forceRestart: true);
				return;
			}

			_frameIndex = _frameCount - 1;
			break;
		}

		if (advanced)
			ApplyFrame();
	}

	public void Play(string animName, bool loop = true)
	{
		if (!_configured)
			return;

		var resolvedAnimation = ResolveAnimation(animName);
		var shouldRestart = !loop || !KeyComparer.Equals(_currentAnimation, resolvedAnimation) || _loop != loop;
		if (!shouldRestart)
			return;

		StartAnimation(resolvedAnimation, loop, queuedAnimation: null, forceRestart: true);
	}

	public void PlayOneShot(string animName, string thenAnim = "Idle")
	{
		if (!_configured)
			return;

		StartAnimation(
			ResolveAnimation(animName),
			loop: false,
			queuedAnimation: ResolveAnimation(thenAnim),
			forceRestart: true);
	}

	public void Stop()
	{
		_frameClockSeconds = 0d;
		_frameIndex = 0;
		_queuedAnimation = null;
		_loop = false;
		ApplyFrame();
	}

	public void SetFacing(int dx)
	{
		if (dx == 0)
			return;

		SetMovementDirection(dx, 0);
	}

	public void SetMovementDirection(int dx, int dy)
	{
		if (!_configured)
			return;

		dx = Math.Sign(dx);
		dy = Math.Sign(dy);
		if (dx == 0 && dy == 0)
			return;

		var row = DirectionalSpriteHelper.ResolveDirectionRow(dx, dy);
		if (row == _directionRow)
			return;

		_directionRow = row;
		ApplyFrame();
	}

	private void StartAnimation(string resolvedAnimation, bool loop, string? queuedAnimation, bool forceRestart)
	{
		var resolvedSheet = ResolveSheetKey(resolvedAnimation);
		if (!forceRestart
			&& KeyComparer.Equals(_currentAnimation, resolvedAnimation)
			&& KeyComparer.Equals(_currentSheet, resolvedSheet)
			&& _loop == loop
			&& KeyComparer.Equals(_queuedAnimation, queuedAnimation))
		{
			return;
		}

		_currentAnimation = resolvedAnimation;
		_currentSheet = resolvedSheet;
		_queuedAnimation = queuedAnimation;
		_loop = loop;
		_frameClockSeconds = 0d;
		_frameIndex = 0;

		var texture = EnsureSheetLoaded(resolvedSheet);
		_frameCount = Math.Max(1, texture.GetWidth() / FrameWidth);
		ApplyFrame();
	}

	private void ApplyFrame()
	{
		if (!_configured)
			return;

		var texture = EnsureSheetLoaded(_currentSheet);
		_frameCount = Math.Max(1, texture.GetWidth() / FrameWidth);
		var clampedFrameIndex = Math.Clamp(_frameIndex, 0, _frameCount - 1);
		var row = Math.Clamp(_directionRow, 0, Math.Max(0, (texture.GetHeight() / FrameHeight) - 1));

		_sprite.Texture = texture;
		_sprite.RegionRect = new Rect2I(
			clampedFrameIndex * FrameWidth,
			row * FrameHeight,
			FrameWidth,
			FrameHeight);
	}

	private Texture2D EnsureSheetLoaded(string sheetKey)
	{
		if (_sheets.TryGetValue(sheetKey, out var cached))
			return cached;

		var requestedPath = $"{_sheetDirectory}/{sheetKey}.png";
		var texture = LoadTexture(requestedPath);
		if (texture == null && !KeyComparer.Equals(sheetKey, "Idle"))
			texture = EnsureSheetLoaded("Idle");
		if (texture == null)
			texture = EnsureMissingSheetFallback(requestedPath);

		_sheets[sheetKey] = texture;
		return texture;
	}

	private static Texture2D? LoadTexture(string path)
	{
		return GD.Load<Texture2D>(path);
	}

	internal static Texture2D GetMissingSheetFallbackTexture(string missingPath)
	{
		return EnsureMissingSheetFallback(missingPath);
	}

	private static Texture2D EnsureMissingSheetFallback(string missingPath)
	{
		if (!_missingSheetFallbackInitialized)
			InitializeMissingSheetFallback();

		if (MissingSheetWarnings.Add(missingPath))
		{
			GD.PushWarning(
				$"[FantasyCharacterAnimatable] Missing character sheet '{missingPath}', using fallback '{_missingSheetFallbackLabel}'.");
		}

		return _missingSheetFallbackTexture!;
	}

	private static void InitializeMissingSheetFallback()
	{
		if (_missingSheetFallbackInitialized)
			return;

		_missingSheetFallbackTexture = LoadTexture(MissingSheetFallbackPath);
		_missingSheetFallbackLabel = MissingSheetFallbackPath;
		if (_missingSheetFallbackTexture == null)
		{
			_missingSheetFallbackTexture = CreateGeneratedMissingSheetFallbackTexture();
			_missingSheetFallbackLabel = "<generated red-x sheet>";
			GD.PushWarning(
				$"[FantasyCharacterAnimatable] Missing fallback texture asset '{MissingSheetFallbackPath}', generated an in-memory red-x sheet instead.");
		}

		_missingSheetFallbackInitialized = true;
	}

	private static Texture2D CreateGeneratedMissingSheetFallbackTexture()
	{
		var image = Image.CreateEmpty(FrameWidth, FrameHeight * DirectionRowCount, false, Image.Format.Rgba8);
		image.Fill(Colors.Transparent);

		for (var row = 0; row < DirectionRowCount; row++)
			DrawGeneratedFallbackRow(image, row * FrameHeight);

		return ImageTexture.CreateFromImage(image);
	}

	private static void DrawGeneratedFallbackRow(Image image, int rowOffset)
	{
		var centerX = FrameWidth * 0.5f;
		var centerY = rowOffset + FrameHeight * 0.5f;
		DrawFilledCircle(image, centerX, centerY, 50f, new Color(0.06f, 0.06f, 0.06f, 0.68f));
		DrawFilledCircle(image, centerX, centerY, 42f, new Color(0.12f, 0.12f, 0.12f, 0.22f));
		DrawThickLine(image, 32f, rowOffset + 32f, 96f, rowOffset + 96f, 18f, new Color(0.25f, 0.02f, 0.02f, 0.60f));
		DrawThickLine(image, 96f, rowOffset + 32f, 32f, rowOffset + 96f, 18f, new Color(0.25f, 0.02f, 0.02f, 0.60f));
		DrawThickLine(image, 30f, rowOffset + 30f, 98f, rowOffset + 98f, 12f, new Color(0.86f, 0.16f, 0.16f, 1f));
		DrawThickLine(image, 98f, rowOffset + 30f, 30f, rowOffset + 98f, 12f, new Color(0.86f, 0.16f, 0.16f, 1f));
		DrawThickLine(image, 38f, rowOffset + 38f, 90f, rowOffset + 90f, 4f, new Color(1f, 0.62f, 0.62f, 0.92f));
		DrawThickLine(image, 90f, rowOffset + 38f, 38f, rowOffset + 90f, 4f, new Color(1f, 0.62f, 0.62f, 0.92f));
	}

	private static void DrawFilledCircle(Image image, float centerX, float centerY, float radius, Color color)
	{
		var radiusSquared = radius * radius;
		var minX = Math.Max(0, (int)Math.Floor(centerX - radius));
		var maxX = Math.Min(image.GetWidth() - 1, (int)Math.Ceiling(centerX + radius));
		var minY = Math.Max(0, (int)Math.Floor(centerY - radius));
		var maxY = Math.Min(image.GetHeight() - 1, (int)Math.Ceiling(centerY + radius));

		for (var py = minY; py <= maxY; py++)
		for (var px = minX; px <= maxX; px++)
		{
			var dx = (px + 0.5f) - centerX;
			var dy = (py + 0.5f) - centerY;
			if (dx * dx + dy * dy <= radiusSquared)
				image.SetPixel(px, py, color);
		}
	}

	private static void DrawThickLine(Image image, float x0, float y0, float x1, float y1, float thickness, Color color)
	{
		var halfThickness = thickness * 0.5f;
		var minX = Math.Max(0, (int)Math.Floor(Math.Min(x0, x1) - halfThickness - 1f));
		var maxX = Math.Min(image.GetWidth() - 1, (int)Math.Ceiling(Math.Max(x0, x1) + halfThickness + 1f));
		var minY = Math.Max(0, (int)Math.Floor(Math.Min(y0, y1) - halfThickness - 1f));
		var maxY = Math.Min(image.GetHeight() - 1, (int)Math.Ceiling(Math.Max(y0, y1) + halfThickness + 1f));
		var dx = x1 - x0;
		var dy = y1 - y0;
		var lengthSquared = dx * dx + dy * dy;
		if (lengthSquared <= float.Epsilon)
		{
			DrawFilledCircle(image, x0, y0, halfThickness, color);
			return;
		}

		var thresholdSquared = halfThickness * halfThickness;
		for (var py = minY; py <= maxY; py++)
		for (var px = minX; px <= maxX; px++)
		{
			var pointX = px + 0.5f;
			var pointY = py + 0.5f;
			var projection = ((pointX - x0) * dx + (pointY - y0) * dy) / lengthSquared;
			projection = Math.Clamp(projection, 0f, 1f);
			var closestX = x0 + dx * projection;
			var closestY = y0 + dy * projection;
			var distanceX = pointX - closestX;
			var distanceY = pointY - closestY;
			if (distanceX * distanceX + distanceY * distanceY <= thresholdSquared)
				image.SetPixel(px, py, color);
		}
	}

	private static string ResolveAnimation(string animName)
	{
		if (string.IsNullOrWhiteSpace(animName))
			return "Idle";

		return AnimationSheetMap.ContainsKey(animName) ? animName : "Idle";
	}

	private static string ResolveSheetKey(string animationKey)
	{
		return AnimationSheetMap.TryGetValue(animationKey, out var sheetKey) ? sheetKey : "Idle";
	}

}
