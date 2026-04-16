using Godot;

namespace MiniRPG.Module.Panel;

/// <summary>
/// 按键按下时的短促金色闪光，作为"我按到了"的瞬时反馈。
/// 比粒子系统更轻（一个 ColorRect + 2 段 tween，约 23ms 峰值 + 180ms 衰减），
/// 不影响按钮 stylebox 本身的 pressed 样式，闪光放在按钮 Control 子节点上由 Godot 自动裁剪到按钮矩形内。
/// <para>
/// 典型调用点：<c>button.Pressed += () => ButtonPressFlash.Flash(button);</c>
/// 或在 SelectIndex / Activate 等"玩家意图落点"上触发。
/// </para>
/// </summary>
public static class ButtonPressFlash
{
	private static readonly Color DefaultColor = new(1f, 0.88f, 0.45f, 0f);
	private const float PeakAlpha = 0.55f;
	private const float AttackSeconds = 0.035f;
	private const float DecaySeconds = 0.18f;

	/// <summary>
	/// 在按钮上叠一个 full-rect 金色闪光，~220ms 后自动 QueueFree。
	/// 可传 color 覆盖默认金色（例如战斗技能可用橙红）。
	/// </summary>
	public static void Flash(Control target, Color? color = null)
	{
		if (target == null || !GodotObject.IsInstanceValid(target))
			return;

		var flash = new ColorRect
		{
			Color = new Color((color ?? DefaultColor).R, (color ?? DefaultColor).G, (color ?? DefaultColor).B, 0f),
			MouseFilter = Control.MouseFilterEnum.Ignore,
			AnchorsPreset = (int)Control.LayoutPreset.FullRect,
		};
		target.AddChild(flash);

		var tween = flash.CreateTween();
		tween.SetTrans(Tween.TransitionType.Cubic);
		tween.SetEase(Tween.EaseType.Out);
		tween.TweenProperty(flash, "color:a", PeakAlpha, AttackSeconds);
		tween.TweenProperty(flash, "color:a", 0f, DecaySeconds);
		tween.Finished += () =>
		{
			if (GodotObject.IsInstanceValid(flash))
				flash.QueueFree();
		};
	}
}
