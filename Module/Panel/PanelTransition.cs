using Godot;

namespace MiniRPG.Module.Panel;

/// <summary>
/// 面板打开/关闭的通用淡入淡出 + 轻微缩放过渡。
/// <para>
/// 每次启动新 tween 时会递增一个 sequence，<see cref="Tween.Finished"/> 回调会检查自己是否仍是
/// 最新的那一段——如果中途被其它 FadeIn/FadeOut 抢走，回调直接 no-op，
/// 避免"FadeOut 的 Finished 在新 FadeIn 完成后覆盖 Visible/Modulate"这类边界问题。
/// </para>
/// </summary>
public static class PanelTransition
{
	private const float DefaultDuration = 0.22f;
	private const float SlidePx = 14f;
	private const string TweenMeta = "_panel_tween";
	private const string SeqMeta = "_panel_tween_seq";

	public static void FadeIn(Control target, float duration = DefaultDuration, bool slide = true)
	{
		if (target == null) return;

		var seq = NextSeq(target);
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
		if (target == null || !target.Visible)
			return;

		var seq = NextSeq(target);
		KillActiveTween(target);
		target.PivotOffset = target.Size * 0.5f;
		var tween = target.CreateTween();
		tween.SetTrans(Tween.TransitionType.Quad);
		tween.SetEase(Tween.EaseType.In);
		tween.TweenProperty(target, "modulate:a", 0.0f, duration * 0.8f);
		tween.Parallel().TweenProperty(target, "scale", new Vector2(0.97f, 0.97f), duration * 0.8f);
		tween.Finished += () => OnFadeOutFinished(target, seq);

		StoreTween(target, tween);
	}

	public static void ShowImmediate(Control target)
	{
		if (target == null) return;
		NextSeq(target);
		KillActiveTween(target);
		target.Modulate = new Color(1f, 1f, 1f, 1f);
		target.Scale = Vector2.One;
		target.Visible = true;
	}

	public static void HideImmediate(Control target)
	{
		if (target == null) return;
		NextSeq(target);
		KillActiveTween(target);
		target.Visible = false;
		target.Modulate = new Color(1f, 1f, 1f, 1f);
		target.Scale = Vector2.One;
	}

	private static void OnFadeOutFinished(Control target, int seq)
	{
		if (!GodotObject.IsInstanceValid(target))
			return;
		if (CurrentSeq(target) != seq)
			return;

		target.Visible = false;
		target.Modulate = new Color(1f, 1f, 1f, 1f);
		target.Scale = Vector2.One;
	}

	private static void StoreTween(Control target, Tween tween)
	{
		target.SetMeta(TweenMeta, Variant.From(tween));
	}

	private static void KillActiveTween(Control target)
	{
		Variant existingTween = default;
		if (target.HasMeta(TweenMeta))
			existingTween = target.GetMeta(TweenMeta);
		if (existingTween.VariantType == Variant.Type.Object
			&& existingTween.AsGodotObject() is Tween activeTween
			&& activeTween.IsRunning())
		{
			activeTween.Kill();
		}
	}

	private static int NextSeq(Control target)
	{
		var next = CurrentSeq(target) + 1;
		target.SetMeta(SeqMeta, next);
		return next;
	}

	private static int CurrentSeq(Control target)
	{
		if (!target.HasMeta(SeqMeta))
			return 0;
		return target.GetMeta(SeqMeta).AsInt32();
	}
}
