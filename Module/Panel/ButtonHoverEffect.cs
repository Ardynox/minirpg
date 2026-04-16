using System;
using Godot;

namespace MiniRPG.Module.Panel;

public static class ButtonHoverEffect
{
	private const float HoverScale = 1.03f;
	private const float Duration = 0.12f;
	private const string TweenMeta = "__btn_hover_tween";
	private const string HookedMeta = "__btn_hover_hooked";

	/// <summary>
	/// 全局 hover 进入回调。默认 null；AudioModule 等可以在启动时注册一次来挂音效。
	/// 不影响按钮本身视觉反馈，只在 hover 进入瞬间触发一次。
	/// </summary>
	public static Action<Button>? OnHoverEnter { get; set; }

	/// <summary>
	/// 全局按键按下回调。默认 null；AudioModule 等可以在启动时注册一次来挂 click 音效。
	/// </summary>
	public static Action<Button>? OnPressed { get; set; }

	public static void Attach(Button button)
	{
		if (button == null || button.HasMeta(HookedMeta))
		{
			AttachPressHook(button!);
			return;
		}

		button.PivotOffset = button.Size * 0.5f;
		button.MouseEntered += () =>
		{
			AnimateScale(button, HoverScale);
			OnHoverEnter?.Invoke(button);
		};
		button.MouseExited += () => AnimateScale(button, 1f);
		button.Resized += () => button.PivotOffset = button.Size * 0.5f;
		AttachPressHook(button);
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

	/// <summary>
	/// 只挂按下回调（用于不需要 hover 缩放但需要音效的 RowButton/TabButton 等）。
	/// 重复调用幂等。
	/// </summary>
	public static void AttachPressHook(Button button)
	{
		if (button == null || button.HasMeta(HookedMeta))
			return;

		button.SetMeta(HookedMeta, true);
		button.Pressed += () => OnPressed?.Invoke(button);
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
