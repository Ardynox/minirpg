using System;
using System.Collections.Generic;
using Godot;

namespace MiniRPG.Module.Panel;

public enum ToastKind
{
	Info,
	Success,
	Warning,
	Error,
	/// <summary>
	/// "大字头条"——用于死亡仪式感 / 全队覆灭等需要瞬间抓住玩家眼球的事件。
	/// 字号 ~2.6×、白色描边、停留时间默认 3 秒（仍走相同的 fade in/out 队列）。
	/// </summary>
	Heading,
}

/// <summary>
/// 屏幕顶部短暂消息浮层。不阻塞输入，自动 4 秒后消失，同时可并存最多 3 条。
/// <para>
/// 典型调用点：存档失败 / 多人联机断线 / 资源耗尽等"想让玩家瞟到但不打断"的事件。
/// 正常的 `log.*` 条目仍然走 <see cref="LogModule"/>，Toast 只负责短时高优先级可见性。
/// </para>
/// <para>
/// 挂点：作为 <c>OverlayLayer</c> 的子节点（Control），使用 TopWide anchors 铺满顶部。
/// 内部 VBoxContainer 水平居中、垂直自顶部堆叠，多条时从上往下排（最新在下）。
/// </para>
/// </summary>
public sealed partial class ToastOverlay : Control
{
	private const float TopMargin = 24f;
	private const float ToastSpacing = 8f;
	private const int MaxVisible = 3;
	private const double DefaultDurationSec = 4.0;
	private const double DefaultHeadingDurationSec = 3.0;
	private const double FadeInSec = 0.18;
	private const double FadeOutSec = 0.32;
	private const int HeadingFontSize = 44;
	private const float HeadingMinWidth = 720f;

	private readonly Queue<PendingToast> _pending = new();
	private readonly List<ActiveToast> _active = [];
	private VBoxContainer? _column;

	public ToastOverlay()
	{
		Name = "ToastOverlay";
		MouseFilter = MouseFilterEnum.Ignore;
		AnchorsPreset = (int)LayoutPreset.TopWide;
		OffsetTop = TopMargin;
		GrowHorizontal = GrowDirection.Both;
		GrowVertical = GrowDirection.End;
	}

	public override void _Ready()
	{
		base._Ready();
		_column = new VBoxContainer
		{
			Name = "ToastColumn",
			MouseFilter = MouseFilterEnum.Ignore,
			AnchorsPreset = (int)LayoutPreset.TopWide,
			GrowHorizontal = GrowDirection.Both,
			GrowVertical = GrowDirection.End,
			Alignment = BoxContainer.AlignmentMode.Begin,
		};
		_column.AddThemeConstantOverride("separation", (int)ToastSpacing);
		AddChild(_column);
	}

	public void Show(string text, ToastKind kind = ToastKind.Info, double durationSec = DefaultDurationSec)
	{
		if (string.IsNullOrWhiteSpace(text))
			return;

		_pending.Enqueue(new PendingToast(text, kind, durationSec));
		DispatchPending();
	}

	public void ShowWarning(string text, double durationSec = DefaultDurationSec) =>
		Show(text, ToastKind.Warning, durationSec);

	public void ShowError(string text, double durationSec = DefaultDurationSec) =>
		Show(text, ToastKind.Error, durationSec);

	/// <summary>
	/// 弹一条"大字头条"。专为死亡仪式感 / 全队覆灭等高优先级演出准备：字号比普通 toast 大 ~2.6×、
	/// 白底高对比，默认 3 秒停留。仍排在普通 toast 队列里，超过 <see cref="MaxVisible"/> 会按入队顺序排队。
	/// </summary>
	public void ShowHeading(string text, double durationSec = DefaultHeadingDurationSec) =>
		Show(text, ToastKind.Heading, durationSec);

	public void Clear()
	{
		_pending.Clear();
		for (var i = _active.Count - 1; i >= 0; i--)
			RetireToast(_active[i]);
	}

	private void DispatchPending()
	{
		if (_column == null) return;

		while (_pending.Count > 0 && _active.Count < MaxVisible)
		{
			var item = _pending.Dequeue();
			SpawnToast(item);
		}
	}

	private void SpawnToast(PendingToast item)
	{
		if (_column == null) return;

		var isHeading = item.Kind == ToastKind.Heading;
		var panel = new PanelContainer
		{
			MouseFilter = MouseFilterEnum.Ignore,
			SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
			Modulate = new Color(1f, 1f, 1f, 0f),
			ThemeTypeVariation = "TooltipPanel",
		};

		var label = new Label
		{
			Text = item.Text,
			MouseFilter = MouseFilterEnum.Ignore,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			CustomMinimumSize = new Vector2(isHeading ? HeadingMinWidth : 320f, 0f),
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		label.AddThemeColorOverride("font_color", ColorForKind(item.Kind));
		if (isHeading)
		{
			label.AddThemeFontSizeOverride("font_size", HeadingFontSize);
			label.AddThemeConstantOverride("outline_size", 6);
			label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.85f));
		}
		panel.AddChild(label);
		_column.AddChild(panel);

		var tween = panel.CreateTween();
		tween.SetTrans(Tween.TransitionType.Quad);
		tween.SetEase(Tween.EaseType.Out);
		tween.TweenProperty(panel, "modulate:a", 1.0f, FadeInSec);

		var active = new ActiveToast(panel, label);
		_active.Add(active);

		var tree = GetTree();
		if (tree == null)
		{
			RetireToast(active);
			return;
		}

		var lifetime = tree.CreateTimer(item.DurationSec, processAlways: true);
		lifetime.Timeout += () => RetireToast(active);
	}

	private void RetireToast(ActiveToast toast)
	{
		if (!_active.Remove(toast))
			return;

		if (!GodotObject.IsInstanceValid(toast.Panel))
		{
			DispatchPending();
			return;
		}

		var tween = toast.Panel.CreateTween();
		tween.SetTrans(Tween.TransitionType.Quad);
		tween.SetEase(Tween.EaseType.In);
		tween.TweenProperty(toast.Panel, "modulate:a", 0f, FadeOutSec);
		tween.Finished += () =>
		{
			if (GodotObject.IsInstanceValid(toast.Panel))
				toast.Panel.QueueFree();
			DispatchPending();
		};
	}

	private static Color ColorForKind(ToastKind kind) => kind switch
	{
		ToastKind.Success => UIColors.TextSuccess,
		ToastKind.Warning => UIColors.TextWarning,
		ToastKind.Error => UIColors.TextCombat,
		ToastKind.Heading => Colors.White,
		_ => UIColors.TextNormal,
	};

	private readonly record struct PendingToast(string Text, ToastKind Kind, double DurationSec);

	private sealed class ActiveToast(PanelContainer panel, Label label)
	{
		public PanelContainer Panel { get; } = panel;
		public Label Label { get; } = label;
	}
}
