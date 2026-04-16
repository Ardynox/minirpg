using Godot;

namespace MiniRPG.Module.Panel;

public static class PanelTransition
{
	private const float DefaultDuration = 0.22f;
	private const float SlidePx = 14f;

	public static void FadeIn(Control target, float duration = DefaultDuration, bool slide = true)
	{
		KillActiveTween(target);
		target.Modulate = new Color(1f, 1f, 1f, 0f);
		target.PivotOffset = target.Size * 0.5f;
		target.Scale = new Vector2(0.97f, 0.97f);
		target.Visible = true;

		var tween = target.CreateTween();
		tween.SetTrans(Tween.TransitionType.Back);
		tween.SetEase(Tween.EaseType.Out);
		tween.TweenProperty(target, "modulate:a", 1.0f, duration);
		tween.Parallel().TweenProperty(target, "scale", Vector2.One, duration);

		if (slide)
		{
			var startOffset = target.Position.Y - SlidePx;
			var endY = target.Position.Y;
			target.Position = new Vector2(target.Position.X, startOffset);
			tween.Parallel().TweenProperty(target, "position:y", endY, duration);
		}

		StoreTween(target, tween);
	}

	public static void FadeOut(Control target, float duration = DefaultDuration)
	{
		if (!target.Visible)
			return;

		KillActiveTween(target);
		target.PivotOffset = target.Size * 0.5f;
		var tween = target.CreateTween();
		tween.SetTrans(Tween.TransitionType.Quad);
		tween.SetEase(Tween.EaseType.In);
		tween.TweenProperty(target, "modulate:a", 0.0f, duration * 0.8f);
		tween.Parallel().TweenProperty(target, "scale", new Vector2(0.97f, 0.97f), duration * 0.8f);
		tween.Finished += () =>
		{
			if (!GodotObject.IsInstanceValid(target))
				return;
			target.Visible = false;
			target.Modulate = new Color(1f, 1f, 1f, 1f);
			target.Scale = Vector2.One;
		};

		StoreTween(target, tween);
	}

	public static void ShowImmediate(Control target)
	{
		KillActiveTween(target);
		target.Modulate = new Color(1f, 1f, 1f, 1f);
		target.Scale = Vector2.One;
		target.Visible = true;
	}

	public static void HideImmediate(Control target)
	{
		KillActiveTween(target);
		target.Visible = false;
		target.Modulate = new Color(1f, 1f, 1f, 1f);
		target.Scale = Vector2.One;
	}

	private static void StoreTween(Control target, Tween tween)
	{
		target.SetMeta("_panel_tween", Variant.From(tween));
	}

	private static void KillActiveTween(Control target)
	{
		Variant existingTween = default;
		if (target.HasMeta("_panel_tween"))
			existingTween = target.GetMeta("_panel_tween");
		if (existingTween.VariantType == Variant.Type.Object
			&& existingTween.AsGodotObject() is Tween activeTween
			&& activeTween.IsRunning())
		{
			activeTween.Kill();
		}
	}
}
