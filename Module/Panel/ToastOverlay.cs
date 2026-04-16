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
	private const double FadeInSec = 0.18;
	private const double FadeOutSec = 0.32;

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
			CustomMinimumSize = new Vector2(320f, 0f),
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		label.AddThemeColorOverride("font_color", ColorForKind(item.Kind));
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
		_ => UIColors.TextNormal,
	};

	private readonly record struct PendingToast(string Text, ToastKind Kind, double DurationSec);

	private sealed class ActiveToast(PanelContainer panel, Label label)
	{
		public PanelContainer Panel { get; } = panel;
		public Label Label { get; } = label;
	}
}
