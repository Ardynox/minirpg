using System;
using System.Collections.Generic;
using Godot;

namespace MiniRPG.Module.Render;

public partial class FantasyCharacterAnimatable : Node2D, IAnimatable
{
	private const int FrameWidth = 128;
	private const int FrameHeight = 128;
	private const float DefaultFramesPerSecond = 12f;
	private static readonly StringComparer KeyComparer = StringComparer.OrdinalIgnoreCase;
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

		var texture = LoadTexture($"{_sheetDirectory}/{sheetKey}.png");
		if (texture == null && !KeyComparer.Equals(sheetKey, "Idle"))
			texture = EnsureSheetLoaded("Idle");
		if (texture == null)
			throw new InvalidOperationException($"Failed to load character sheet: {_sheetDirectory}/{sheetKey}.png");

		_sheets[sheetKey] = texture;
		return texture;
	}

	private static Texture2D? LoadTexture(string path)
	{
		return GD.Load<Texture2D>(path);
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
