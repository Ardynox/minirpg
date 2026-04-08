using Godot;

namespace MiniRPG.Module.Panel;

/// <summary>
/// 面板过渡动效工具。
/// 通过 Tween 为 Control 节点提供淡入/淡出和滑入/滑出效果。
/// 调用 Show/Hide 会自动管理 Visible 状态和正在进行的 Tween。
/// </summary>
public static class PanelTransition
{
	private const float DefaultDuration = 0.18f;
	private const float SlidePx = 12f;

	/// <summary>淡入显示：透明度 0→1，可选向下滑入。</summary>
	public static void FadeIn(Control target, float duration = DefaultDuration, bool slide = true)
	{
		KillActiveTween(target);
		target.Modulate = new Color(1f, 1f, 1f, 0f);
		target.Visible = true;

		var tween = target.CreateTween();
		tween.SetTrans(Tween.TransitionType.Quad);
		tween.SetEase(Tween.EaseType.Out);
		tween.TweenProperty(target, "modulate:a", 1.0f, duration);

		if (slide)
		{
			var startOffset = target.Position.Y - SlidePx;
			var endY = target.Position.Y;
			target.Position = new Vector2(target.Position.X, startOffset);
			tween.Parallel().TweenProperty(target, "position:y", endY, duration);
		}
	}

	/// <summary>淡出隐藏：透明度 1→0，完成后自动 Visible=false。</summary>
	public static void FadeOut(Control target, float duration = DefaultDuration)
	{
		if (!target.Visible)
			return;

		KillActiveTween(target);
		var tween = target.CreateTween();
		tween.SetTrans(Tween.TransitionType.Quad);
		tween.SetEase(Tween.EaseType.In);
		tween.TweenProperty(target, "modulate:a", 0.0f, duration);
		tween.Finished += () =>
		{
			if (!GodotObject.IsInstanceValid(target))
				return;
			target.Visible = false;
			target.Modulate = new Color(1f, 1f, 1f, 1f);
		};
	}

	/// <summary>立即显示，无动画。</summary>
	public static void ShowImmediate(Control target)
	{
		KillActiveTween(target);
		target.Modulate = new Color(1f, 1f, 1f, 1f);
		target.Visible = true;
	}

	/// <summary>立即隐藏，无动画。</summary>
	public static void HideImmediate(Control target)
	{
		KillActiveTween(target);
		target.Visible = false;
		target.Modulate = new Color(1f, 1f, 1f, 1f);
	}

	private static void KillActiveTween(Control target)
	{
		var existingTween = target.GetMeta("_panel_tween", default(Variant));
		if (existingTween.VariantType == Variant.Type.Object
			&& existingTween.AsGodotObject() is Tween activeTween
			&& activeTween.IsRunning())
		{
			activeTween.Kill();
		}
	}
}
