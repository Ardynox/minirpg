using System;
using System.Linq;
using Godot;
using MiniRPG.Module.Editor;

/// <summary>
/// 独立预览场景：仅用来看 fx_explosion 的爆炸特效视觉。
/// 不接入战斗 / 世界 / 数据流程，直接从 resource_catalog.json 加载帧动画，
/// 然后用和 Module/Render/CombatFxPlayer.PlaySprite 一致的 tween（scale 0.72x→1.06x、alpha→0）播放，
/// 以保证预览所见即游戏内所见。
/// 在 Godot 编辑器里选中 Scene/ExplosionFxPreview.tscn 按 F6 运行；
/// 左键或 R 键重播，Esc 退出。
/// </summary>
public partial class ExplosionFxPreview : Node2D
{
	private const string EffectId = "fx_explosion";
	private const string DefaultAnim = "default";

	// 与 Data/combat_fx.json -> effects.explosion.primary 保持一致
	private const float PrimaryDuration = 0.5f;
	private const float PrimaryScale = 0.6f;
	private static readonly Color PrimaryTint = new("ffaa44");

	// 与 Data/combat_fx.json -> effects.explosion.hit 保持一致
	private const float HitDuration = 0.35f;
	private const float HitScale = 0.4f;
	private static readonly Color HitTint = new("ff6622");

	// 二次闪光相对主体的延迟，让视觉有层次感（非游戏内实际数值，仅为预览观感）
	private const float HitDelaySeconds = 0.08f;

	private SpriteFrames? _frames;
	private Node2D _stage = null!;
	private Label _statusLabel = null!;
	private Camera2D _camera = null!;

	public override void _Ready()
	{
		BuildBackdrop();
		BuildCamera();
		BuildStage();
		BuildHud();

		_frames = LoadExplosionFrames();
		if (_frames == null)
		{
			_statusLabel.Text =
				"[错误] 无法加载 fx_explosion 动画\n" +
				"检查 Data/resource_catalog.json 与 Assets/Art/Placeholders/effects/fx_explosion/";
			return;
		}

		UpdateStatusLabel();
		Spawn();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (_frames == null)
			return;

		switch (@event)
		{
			case InputEventKey { Pressed: true, Echo: false, Keycode: Key.R }:
			case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }:
				Spawn();
				break;
			case InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }:
				GetTree().Quit();
				break;
		}
	}

	private void BuildBackdrop()
	{
		var layer = new CanvasLayer { Layer = -10, Name = "BackdropLayer" };
		AddChild(layer);

		var bg = new ColorRect
		{
			Name = "Backdrop",
			Color = new Color(0.05f, 0.05f, 0.08f),
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		layer.AddChild(bg);
	}

	private void BuildCamera()
	{
		_camera = new Camera2D { Name = "PreviewCamera" };
		AddChild(_camera);
		_camera.MakeCurrent();
	}

	private void BuildStage()
	{
		_stage = new Node2D { Name = "Stage" };
		AddChild(_stage);
	}

	private void BuildHud()
	{
		var layer = new CanvasLayer { Layer = 10, Name = "HudLayer" };
		AddChild(layer);

		_statusLabel = new Label
		{
			Name = "Status",
			Text = "加载中...",
			Position = new Vector2(16, 16),
		};
		layer.AddChild(_statusLabel);
	}

	private void UpdateStatusLabel()
	{
		_statusLabel.Text =
			"fx_explosion 爆炸预览\n" +
			"左键 / R 键 = 重播    Esc = 退出\n" +
			$"primary: tint={FormatColorHex(PrimaryTint)} scale={PrimaryScale} duration={PrimaryDuration}s\n" +
			$"hit:     tint={FormatColorHex(HitTint)} scale={HitScale} duration={HitDuration}s (延迟 {HitDelaySeconds:0.00}s)";
	}

	private void Spawn()
	{
		if (_frames == null)
			return;

		SpawnBurst(PrimaryTint, PrimaryScale, PrimaryDuration, zIndex: 14);

		var timer = GetTree().CreateTimer(HitDelaySeconds);
		timer.Timeout += () =>
		{
			if (!GodotObject.IsInstanceValid(_stage) || _stage.GetParent() == null)
				return;
			SpawnBurst(HitTint, HitScale, HitDuration, zIndex: 12);
		};

		ShakeCamera();
	}

	// 与 CombatFxPlayer.PlaySprite 保持一致：起始 0.72x、结束 1.06x、同步 scale + modulate:a Quad-Out。
	// 这样预览里看到的爆炸手感，就是真实战斗回合里触发 "explosion" 特效时能看到的手感。
	private void SpawnBurst(Color tint, float baseScale, float duration, int zIndex)
	{
		var sprite = new AnimatedSprite2D
		{
			Name = $"ExplosionBurst_{Time.GetTicksMsec()}",
			SpriteFrames = _frames,
			Animation = DefaultAnim,
			Centered = true,
			TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
			Modulate = tint,
			ZIndex = zIndex,
			Scale = Vector2.One * Mathf.Max(baseScale * 0.72f, 0.01f),
		};
		_stage.AddChild(sprite);
		sprite.Play(DefaultAnim);

		var endScale = Vector2.One * Mathf.Max(baseScale * 1.06f, 0.01f);
		var tween = sprite.CreateTween();
		tween.SetTrans(Tween.TransitionType.Quad);
		tween.SetEase(Tween.EaseType.Out);
		tween.Parallel().TweenProperty(sprite, "scale", endScale, duration);
		tween.Parallel().TweenProperty(sprite, "modulate:a", 0.0f, duration);
		tween.Finished += () =>
		{
			if (GodotObject.IsInstanceValid(sprite))
				sprite.QueueFree();
		};
	}

	private void ShakeCamera()
	{
		var tween = _camera.CreateTween();
		tween.TweenProperty(_camera, "offset", new Vector2(10f, -6f), 0.04f);
		tween.TweenProperty(_camera, "offset", new Vector2(-7f, 8f), 0.05f);
		tween.TweenProperty(_camera, "offset", new Vector2(4f, -3f), 0.05f);
		tween.TweenProperty(_camera, "offset", Vector2.Zero, 0.08f);
	}

	// 从 resource_catalog.json 加载 fx_explosion 帧列表并组装 SpriteFrames。
	// 逻辑对应 Module/Render/CombatFxPlayer.ResolveAnimation；这里不走 CombatFxPlayer 是因为它要求
	// 传入 IsometricVoxelRenderer，不适合独立预览场景。
	private static SpriteFrames? LoadExplosionFrames()
	{
		ResourceCatalogDocument doc;
		try
		{
			doc = ResourceCatalogStore.Load();
		}
		catch (Exception ex)
		{
			GD.PushError($"[ExplosionFxPreview] resource catalog 加载失败: {ex.Message}");
			return null;
		}

		var entry = doc.Entries.FirstOrDefault(e =>
			string.Equals(e.Id, EffectId, StringComparison.OrdinalIgnoreCase));
		if (entry == null || entry.Frames.Count == 0)
		{
			GD.PushError($"[ExplosionFxPreview] 找不到 resource catalog 条目: {EffectId}");
			return null;
		}

		var frames = new SpriteFrames();
		if (!frames.HasAnimation(DefaultAnim))
			frames.AddAnimation(DefaultAnim);
		frames.SetAnimationLoop(DefaultAnim, false);
		frames.SetAnimationSpeed(DefaultAnim, Math.Max(entry.Fps ?? 12f, 0.1f));

		foreach (var frame in entry.Frames.OrderBy(f => f.Order))
		{
			if (string.IsNullOrWhiteSpace(frame.ImagePath))
				continue;
			var texture = GD.Load<Texture2D>(frame.ImagePath);
			if (texture != null)
				frames.AddFrame(DefaultAnim, texture);
		}

		return frames.GetFrameCount(DefaultAnim) == 0 ? null : frames;
	}

	private static string FormatColorHex(Color color)
	{
		var r = Mathf.RoundToInt(color.R * 255f);
		var g = Mathf.RoundToInt(color.G * 255f);
		var b = Mathf.RoundToInt(color.B * 255f);
		return $"#{r:X2}{g:X2}{b:X2}";
	}
}
