using Godot;

namespace MiniRPG.Module.Panel;

public static class ButtonHoverEffect
{
	private const float HoverScale = 1.03f;
	private const float Duration = 0.12f;
	private const string TweenMeta = "__btn_hover_tween";

	public static void Attach(Button button)
	{
		button.PivotOffset = button.Size * 0.5f;
		button.MouseEntered += () => AnimateScale(button, HoverScale);
		button.MouseExited += () => AnimateScale(button, 1f);
		button.Resized += () => button.PivotOffset = button.Size * 0.5f;
	}

	public static void AttachAll(Node root)
	{
		foreach (var child in root.GetChildren())
		{
			if (child is Button btn && btn.ThemeTypeVariation == "ActionButton")
				Attach(btn);

			if (child is Node node)
				AttachAll(node);
		}
	}

	private static void AnimateScale(Button button, float targetScale)
	{
		if (!GodotObject.IsInstanceValid(button))
			return;

		Variant existing = default;
		if (button.HasMeta(TweenMeta))
			existing = button.GetMeta(TweenMeta);
		if (existing.VariantType == Variant.Type.Object
			&& existing.AsGodotObject() is Tween oldTween
			&& oldTween.IsRunning())
		{
			oldTween.Kill();
		}

		button.PivotOffset = button.Size * 0.5f;
		var tween = button.CreateTween();
		tween.SetTrans(Tween.TransitionType.Quad);
		tween.SetEase(Tween.EaseType.Out);
		tween.TweenProperty(button, "scale", new Vector2(targetScale, targetScale), Duration);
		button.SetMeta(TweenMeta, Variant.From(tween));
	}
}
