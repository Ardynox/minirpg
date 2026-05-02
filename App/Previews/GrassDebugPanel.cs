using System;
using Godot;
using MiniRPG.Core.Debug;
using MiniRPG.Module.Render.Surface;

namespace MiniRPG.App.Previews;

/// <summary>
/// 草地预览的运行时调试面板：右上角 PanelContainer，按组列出所有 shader uniform / DebugModule 调参。
///
/// 设计约束：
///  - 纯 UI，不持有业务状态；所有写操作直接作用于 <see cref="DebugModule"/> 或 <see cref="GrassBladeField"/>。
///  - 需要触发 MultiMesh 重建的改动（bladesPerTile / threshold / variant / AO / seed）通过 <see cref="_onRebuildBlades"/> 回调。
///  - 纯 shader uniform（wind/taper/bleach/tone）直接 push 到 material，无需 rebuild。
///  - 滑条默认值要和 <see cref="GrassBladeField"/> / shader 默认保持一致——改 shader 默认后记得同步这里。
///
/// 布局：
///   PanelContainer
///    └ MarginContainer
///       └ VBoxContainer
///          ├ 标题
///          ├ [General] 渲染模式、强制 overlay、AO、阈值、变体、blade/tile、种子
///          ├ [Wind]    wind_speed / wind_amp / cross_wind_gain / cross_wind_ratio
///          ├ [Shape]   tip_width_ratio / taper_curve
///          ├ [Tone]    root_darken / tip_boost / ambient
///          └ [Bleach]  tip_bleach_threshold / tip_bleach_strength
/// </summary>
public sealed partial class GrassDebugPanel : PanelContainer
{
	private readonly GrassBladeField _bladeField;
	private readonly Action _onRebuildBlades;
	private readonly Action _onSeedReroll;
	private readonly Action _onStatusRefresh;

	// 面板本地维持的 shader uniform 状态（DebugModule 里没有对应字段，刷新也只回到面板自己这里）
	private float _ambient         = 0.55f;
	private float _rootDarken      = 0.55f;
	private float _tipBoost        = 1.25f;
	private float _crossGain       = 0.35f;
	private float _crossRatio      = 2.1f;
	private float _bleachThreshold = 0.32f;
	private float _bleachStrength  = 0.75f;
	private float _tipWidthRatio   = 0.15f;
	private float _taperCurve      = 0.55f;

	public GrassDebugPanel(
		GrassBladeField bladeField,
		Action onRebuildBlades,
		Action onSeedReroll,
		Action onStatusRefresh)
	{
		_bladeField       = bladeField;
		_onRebuildBlades  = onRebuildBlades;
		_onSeedReroll     = onSeedReroll;
		_onStatusRefresh  = onStatusRefresh;

		// 右上角锚定：距右边 16、距顶 16，宽 ~380
		AnchorLeft   = 1f;
		AnchorRight  = 1f;
		AnchorTop    = 0f;
		AnchorBottom = 0f;
		OffsetLeft   = -396f;
		OffsetRight  = -16f;
		OffsetTop    = 16f;
		OffsetBottom = 16f; // Grow by content
		GrowVertical = GrowDirection.End;

		// 深色半透明 stylebox，让面板在草地上可读
		var style = new StyleBoxFlat
		{
			BgColor             = new Color(0.08f, 0.10f, 0.12f, 0.88f),
			BorderColor         = new Color(0.30f, 0.35f, 0.40f, 0.85f),
			BorderWidthLeft     = 1,
			BorderWidthRight    = 1,
			BorderWidthTop      = 1,
			BorderWidthBottom   = 1,
			CornerRadiusTopLeft = 4,
			CornerRadiusTopRight    = 4,
			CornerRadiusBottomLeft  = 4,
			CornerRadiusBottomRight = 4,
			ContentMarginLeft   = 10,
			ContentMarginRight  = 10,
			ContentMarginTop    = 10,
			ContentMarginBottom = 10,
		};
		AddThemeStyleboxOverride("panel", style);

		BuildUi();
	}

	// ─────────────────────────────────────────────────────────────
	//  UI 构建
	// ─────────────────────────────────────────────────────────────

	private void BuildUi()
	{
		var vbox = new VBoxContainer();
		vbox.AddThemeConstantOverride("separation", 4);
		AddChild(vbox);

		AddTitle(vbox, "草地调试面板  (F1 切换显示)");
		AddSpacer(vbox, 4);

		// ── [General] ──
		AddSection(vbox, "通用 General");
		AddOptionRow(vbox, "渲染模式", new[] { "Shader3D", "Legacy", "Off" },
			defaultIndex: IndexOfRenderMode(DebugModule.GrassRenderMode),
			onSelect: idx =>
			{
				DebugModule.GrassRenderMode = idx switch
				{
					0 => GrassRenderMode.Shader3D,
					1 => GrassRenderMode.Legacy,
					_ => GrassRenderMode.Off,
				};
				_onRebuildBlades();
				_onStatusRefresh();
			});

		AddOptionRow(vbox, "强制 Overlay", new[] { "Auto", "On", "Off" },
			defaultIndex: IndexOfForce(DebugModule.ForceGrassOverlay),
			onSelect: idx =>
			{
				DebugModule.ForceGrassOverlay = idx switch
				{
					0 => GrassOverlayForceMode.Auto,
					1 => GrassOverlayForceMode.On,
					_ => GrassOverlayForceMode.Off,
				};
				_onRebuildBlades();
				_onStatusRefresh();
			});

		AddCheckRow(vbox, "根部 AO",
			initial: DebugModule.GrassBladeAo,
			onToggle: on =>
			{
				DebugModule.GrassBladeAo = on;
				_bladeField.AoEnabled = on;
				_onRebuildBlades();
				_onStatusRefresh();
			});

		AddSlider(vbox, "密度阈值", min: 0f, max: 255f, value: DebugModule.GrassDensityThreshold, step: 1f,
			onChange: v =>
			{
				DebugModule.GrassDensityThreshold = (byte)Math.Clamp(v, 0f, 255f);
				_onRebuildBlades();
				_onStatusRefresh();
			});

		AddSlider(vbox, "变体 (−1=哈希)", min: -1f, max: 5f, value: DebugModule.GrassVariantOverride, step: 1f,
			onChange: v =>
			{
				DebugModule.GrassVariantOverride = (int)Math.Round(v);
				_onRebuildBlades();
				_onStatusRefresh();
			});

		// 上限 1024：批量 Buffer 上传后 CPU 已不再是瓶颈；继续往上拉则 GPU fill-rate 会先吃满。
		// step 16：滑条拖 64 下即可覆盖全程。
		AddSlider(vbox, "blade / tile", min: 0f, max: (float)GrassSurfacePreview.MaxBladesPerTile, value: DebugModule.GrassBladesPerTile, step: 16f,
			onChange: v =>
			{
				DebugModule.GrassBladesPerTile = (int)Math.Round(v);
				_onRebuildBlades();
				_onStatusRefresh();
			});

		AddButtonRow(vbox, "重新 Roll 种子", _onSeedReroll);

		// ── [Wind] ──
		AddSection(vbox, "风 Wind");
		AddSlider(vbox, "主风速 (Hz)",    min: 0f,   max: 10f, value: DebugModule.GrassWindSpeed,     step: 0.1f,
			onChange: v =>
			{
				DebugModule.GrassWindSpeed = v;
				// _Process 会在下一帧把它 push 到 material
				_onStatusRefresh();
			});
		AddSlider(vbox, "主风振幅 (px)",  min: 0f,   max: 8f,  value: DebugModule.GrassWindAmplitude, step: 0.1f,
			onChange: v => DebugModule.GrassWindAmplitude = v);
		AddSlider(vbox, "副风强度",       min: 0f,   max: 1.5f, value: _crossGain,  step: 0.05f,
			onChange: v => { _crossGain  = v; _bladeField.UpdateCrossWindUniforms(_crossGain, _crossRatio); });
		AddSlider(vbox, "副风频率倍率",   min: 0.5f, max: 5f,  value: _crossRatio, step: 0.1f,
			onChange: v => { _crossRatio = v; _bladeField.UpdateCrossWindUniforms(_crossGain, _crossRatio); });

		// ── [Shape] ──
		AddSection(vbox, "形状 Shape");
		AddSlider(vbox, "尖端宽比",   min: 0f,   max: 1f,   value: _tipWidthRatio, step: 0.01f,
			onChange: v => { _tipWidthRatio = v; _bladeField.UpdateTaperUniforms(_tipWidthRatio, _taperCurve); });
		AddSlider(vbox, "收口曲率",   min: 0.2f, max: 2.5f, value: _taperCurve,    step: 0.05f,
			onChange: v => { _taperCurve    = v; _bladeField.UpdateTaperUniforms(_tipWidthRatio, _taperCurve); });

		// ── [Tone] ──
		AddSection(vbox, "明暗 Tone");
		AddSlider(vbox, "根部压暗",   min: 0f, max: 1.2f, value: _rootDarken, step: 0.02f,
			onChange: v => { _rootDarken = v; _bladeField.UpdateToneUniforms(_rootDarken, _tipBoost); });
		AddSlider(vbox, "尖端提亮",   min: 0.5f, max: 2.5f, value: _tipBoost, step: 0.05f,
			onChange: v => { _tipBoost   = v; _bladeField.UpdateToneUniforms(_rootDarken, _tipBoost); });
		AddSlider(vbox, "环境光",     min: 0f, max: 1f, value: _ambient, step: 0.02f,
			onChange: v =>
			{
				_ambient = v;
				_bladeField.UpdateSunUniforms(
					sunDir: new Vector2(0.35f, -0.94f),
					sunColor: new Color(1f, 0.96f, 0.86f),
					ambient: _ambient);
			});

		// ── [Bleach] ──
		AddSection(vbox, "草尖漂白 Pale Tip");
		AddSlider(vbox, "触发阈值",   min: 0f, max: 0.5f, value: _bleachThreshold, step: 0.01f,
			onChange: v =>
			{
				_bleachThreshold = v;
				_bladeField.UpdateTipBleachUniforms(_bleachThreshold, _bleachStrength,
					new Color(0.92f, 0.90f, 0.62f));
			});
		AddSlider(vbox, "漂白强度",   min: 0f, max: 1f, value: _bleachStrength, step: 0.02f,
			onChange: v =>
			{
				_bleachStrength = v;
				_bladeField.UpdateTipBleachUniforms(_bleachThreshold, _bleachStrength,
					new Color(0.92f, 0.90f, 0.62f));
			});
	}

	// ─────────────────────────────────────────────────────────────
	//  UI 辅助
	// ─────────────────────────────────────────────────────────────

	private static void AddTitle(VBoxContainer parent, string text)
	{
		var lbl = new Label { Text = text };
		lbl.AddThemeFontSizeOverride("font_size", 14);
		lbl.AddThemeColorOverride("font_color", new Color(0.95f, 0.95f, 0.95f));
		parent.AddChild(lbl);
	}

	private static void AddSection(VBoxContainer parent, string text)
	{
		AddSpacer(parent, 6);
		var lbl = new Label { Text = text };
		lbl.AddThemeFontSizeOverride("font_size", 12);
		lbl.AddThemeColorOverride("font_color", new Color(0.95f, 0.80f, 0.45f));
		parent.AddChild(lbl);

		var sep = new HSeparator();
		sep.AddThemeConstantOverride("separation", 2);
		parent.AddChild(sep);
	}

	private static void AddSpacer(VBoxContainer parent, float height)
	{
		var sp = new Control { CustomMinimumSize = new Vector2(0, height) };
		parent.AddChild(sp);
	}

	private static HSlider AddSlider(
		VBoxContainer parent,
		string label,
		float min, float max, float value, float step,
		Action<float> onChange)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 8);
		parent.AddChild(row);

		var lbl = new Label
		{
			Text = label,
			CustomMinimumSize = new Vector2(118, 0),
			VerticalAlignment = VerticalAlignment.Center,
		};
		lbl.AddThemeFontSizeOverride("font_size", 12);
		lbl.AddThemeColorOverride("font_color", new Color(0.80f, 0.85f, 0.90f));
		row.AddChild(lbl);

		var slider = new HSlider
		{
			MinValue = min,
			MaxValue = max,
			Value    = Math.Clamp(value, min, max),
			Step     = step,
			CustomMinimumSize = new Vector2(160, 18),
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		row.AddChild(slider);

		var readout = new Label
		{
			Text = FormatValue(Math.Clamp(value, min, max), step),
			CustomMinimumSize = new Vector2(54, 0),
			HorizontalAlignment = HorizontalAlignment.Right,
			VerticalAlignment   = VerticalAlignment.Center,
		};
		readout.AddThemeFontSizeOverride("font_size", 11);
		readout.AddThemeColorOverride("font_color", new Color(0.95f, 0.85f, 0.55f));
		row.AddChild(readout);

		slider.ValueChanged += v =>
		{
			readout.Text = FormatValue((float)v, step);
			onChange((float)v);
		};
		return slider;
	}

	private static CheckBox AddCheckRow(VBoxContainer parent, string label, bool initial, Action<bool> onToggle)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 8);
		parent.AddChild(row);

		var lbl = new Label
		{
			Text = label,
			CustomMinimumSize = new Vector2(118, 0),
			VerticalAlignment = VerticalAlignment.Center,
		};
		lbl.AddThemeFontSizeOverride("font_size", 12);
		lbl.AddThemeColorOverride("font_color", new Color(0.80f, 0.85f, 0.90f));
		row.AddChild(lbl);

		var check = new CheckBox
		{
			ButtonPressed = initial,
			Text          = initial ? "开" : "关",
		};
		check.Toggled += on =>
		{
			check.Text = on ? "开" : "关";
			onToggle(on);
		};
		row.AddChild(check);
		return check;
	}

	private static OptionButton AddOptionRow(
		VBoxContainer parent,
		string label,
		string[] items,
		int defaultIndex,
		Action<int> onSelect)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 8);
		parent.AddChild(row);

		var lbl = new Label
		{
			Text = label,
			CustomMinimumSize = new Vector2(118, 0),
			VerticalAlignment = VerticalAlignment.Center,
		};
		lbl.AddThemeFontSizeOverride("font_size", 12);
		lbl.AddThemeColorOverride("font_color", new Color(0.80f, 0.85f, 0.90f));
		row.AddChild(lbl);

		var opt = new OptionButton
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		foreach (var it in items) opt.AddItem(it);
		opt.Select(Math.Clamp(defaultIndex, 0, items.Length - 1));
		opt.ItemSelected += idx => onSelect((int)idx);
		row.AddChild(opt);
		return opt;
	}

	private static Button AddButtonRow(VBoxContainer parent, string label, Action onPressed)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 8);
		parent.AddChild(row);

		var spacer = new Control { CustomMinimumSize = new Vector2(118, 0) };
		row.AddChild(spacer);

		var btn = new Button
		{
			Text = label,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		btn.Pressed += onPressed;
		row.AddChild(btn);
		return btn;
	}

	private static string FormatValue(float v, float step)
	{
		if (step >= 1f) return ((int)Math.Round(v)).ToString();
		if (step >= 0.1f) return v.ToString("0.00");
		return v.ToString("0.000");
	}

	private static int IndexOfRenderMode(GrassRenderMode mode) => mode switch
	{
		GrassRenderMode.Shader3D => 0,
		GrassRenderMode.Legacy   => 1,
		_                        => 2,
	};

	private static int IndexOfForce(GrassOverlayForceMode force) => force switch
	{
		GrassOverlayForceMode.Auto => 0,
		GrassOverlayForceMode.On   => 1,
		_                          => 2,
	};
}
