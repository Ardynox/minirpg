using System;
using System.Collections.Generic;
using Godot;

namespace MiniRPG.Module.Panel;

/// <summary>
/// 富文本 Tooltip 浮层。不依赖 Godot 原生 tooltip 机制，允许 BBCode、最大宽度、自动换行。
/// <para>
/// 用法：
/// <code>
///   _richTooltips.Attach(button, () => $"[b]{skill.Name}[/b]\n[color=#cccccc]{skill.Desc}[/color]");
/// </code>
/// 其中 Factory 函数每次 hover 时调用一次，可以基于运行时状态生成内容。
/// </para>
/// <para>
/// 单例由 <c>Main.Startup</c> 挂到 <c>OverlayLayer</c>。整个 overlay 监听全局 hover 时序，
/// 控件进入 <see cref="Delay"/> 秒后显示，离开立即隐藏。不与 Godot 原生 tooltip 同时工作：
/// 调用 <see cref="Attach"/> 时会把原生 <c>TooltipText</c> 清空。
/// </para>
/// </summary>
public sealed partial class RichTooltipLayer : Control
{
	private const int MaxWidth = 420;
	private const float Padding = 12f;
	private const double Delay = 0.25;
	private const double FadeInSec = 0.12;

	private readonly Dictionary<Control, Func<string>> _sources = new();
	// 存储每个 control 的 lambda 委托引用，Detach 时调用 -= 真正解绑，避免信号堆积。
	private readonly Dictionary<Control, (Action Enter, Action Exit, Action Exiting)> _signalCallbacks = new();

	private PanelContainer? _panel;
	private RichTextLabel? _label;
	private Control? _currentHovered;
	// 取代旧的 _pendingTimer 直接持有 timer 实例：每次 HoverStart/HoverEnd/Detach 自增后，旧 timer 的 Timeout 回调通过 guard 早退。
	private int _hoverGeneration;

	public RichTooltipLayer()
	{
		Name = "RichTooltipLayer";
		MouseFilter = MouseFilterEnum.Ignore;
		AnchorsPreset = (int)LayoutPreset.FullRect;
		ZIndex = 200;
	}

	public override void _Ready()
	{
		base._Ready();
		_panel = new PanelContainer
		{
			Name = "RichTooltipPanel",
			MouseFilter = MouseFilterEnum.Ignore,
			ThemeTypeVariation = "TooltipPanel",
			Visible = false,
			Modulate = new Color(1f, 1f, 1f, 0f),
		};
		_label = new RichTextLabel
		{
			BbcodeEnabled = true,
			FitContent = true,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			CustomMinimumSize = new Vector2(0, 0),
			MouseFilter = MouseFilterEnum.Ignore,
		};
		_label.AddThemeColorOverride("default_color", UIColors.TextNormal);
		_panel.AddChild(_label);
		AddChild(_panel);
	}

	/// <summary>
	/// 给一个 Control 挂 rich tooltip。Factory 每次 hover 时调用一次。
	/// 传 null factory 或同一个 control 再次 Attach 会覆盖。
	/// 原生 TooltipText 会被清空，避免双 tooltip。
	/// </summary>
	public void Attach(Control control, Func<string>? bbcodeFactory)
	{
		if (control == null)
			return;

		control.TooltipText = string.Empty;

		if (bbcodeFactory == null)
		{
			Detach(control);
			return;
		}

		var wasAttached = _sources.ContainsKey(control);
		_sources[control] = bbcodeFactory;

		if (wasAttached)
			return;

		Action enter = () => OnHoverStart(control);
		Action exit = () => OnHoverEnd(control);
		Action exiting = () => Detach(control);
		_signalCallbacks[control] = (enter, exit, exiting);

		control.MouseEntered += enter;
		control.MouseExited += exit;
		control.TreeExiting += exiting;
	}

	public void Detach(Control control)
	{
		if (control == null) return;
		_sources.Remove(control);
		if (_signalCallbacks.Remove(control, out var callbacks)
			&& GodotObject.IsInstanceValid(control))
		{
			control.MouseEntered -= callbacks.Enter;
			control.MouseExited -= callbacks.Exit;
			control.TreeExiting -= callbacks.Exiting;
		}
		if (_currentHovered == control)
		{
			_currentHovered = null;
			_hoverGeneration++;
			HideTooltip();
		}
	}

	private void OnHoverStart(Control control)
	{
		if (!_sources.ContainsKey(control)) return;
		_currentHovered = control;
		var tree = GetTree();
		if (tree == null) return;

		_hoverGeneration++;
		var myGeneration = _hoverGeneration;
		var pending = control;
		var timer = tree.CreateTimer(Delay, processAlways: true);
		timer.Timeout += () =>
		{
			if (myGeneration != _hoverGeneration) return;
			if (_currentHovered != pending) return;
			ShowTooltipFor(pending);
		};
	}

	private void OnHoverEnd(Control control)
	{
		if (_currentHovered == control)
		{
			_currentHovered = null;
			_hoverGeneration++;
			HideTooltip();
		}
	}

	private void ShowTooltipFor(Control control)
	{
		if (_panel == null || _label == null) return;
		if (!_sources.TryGetValue(control, out var factory)) return;

		string text;
		try { text = factory(); }
		catch { return; }

		if (string.IsNullOrWhiteSpace(text)) return;

		_label.Text = text;
		// 修：原公式 Math.Min(MaxWidth, Math.Max(160, MaxWidth)) 恒等于 MaxWidth。
		// 现按文本自然宽度在 [160, MaxWidth] 区间 clamp，让短 tooltip 不强行被拉到 420。
		var measured = (int)Math.Ceiling(_label.GetContentWidth());
		_label.CustomMinimumSize = new Vector2(Math.Min(MaxWidth, Math.Max(160, measured)), 0);
		_panel.Visible = true;
		PositionNearMouse();

		var tween = _panel.CreateTween();
		tween.SetTrans(Tween.TransitionType.Quad);
		tween.SetEase(Tween.EaseType.Out);
		tween.TweenProperty(_panel, "modulate:a", 1.0f, FadeInSec);
	}

	private void HideTooltip()
	{
		if (_panel == null) return;
		_panel.Visible = false;
		_panel.Modulate = new Color(1f, 1f, 1f, 0f);
	}

	public override void _Process(double delta)
	{
		if (_panel != null && _panel.Visible)
			PositionNearMouse();
	}

	private void PositionNearMouse()
	{
		if (_panel == null) return;
		var viewport = GetViewport();
		if (viewport == null) return;

		var mouse = viewport.GetMousePosition();
		var size = _panel.Size;
		if (size.X <= 0 || size.Y <= 0)
			size = _panel.CustomMinimumSize;

		var viewportRect = viewport.GetVisibleRect();
		var target = new Vector2(mouse.X + 18f, mouse.Y + 18f);
		if (target.X + size.X > viewportRect.End.X - 8f)
			target.X = mouse.X - size.X - 12f;
		if (target.Y + size.Y > viewportRect.End.Y - 8f)
			target.Y = mouse.Y - size.Y - 12f;
		target.X = Math.Max(8f, target.X);
		target.Y = Math.Max(8f, target.Y);
		_panel.Position = target;
	}
}
