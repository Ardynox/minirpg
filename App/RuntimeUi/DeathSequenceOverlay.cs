using Godot;
using MiniRPG.Module.Panel;

namespace MiniRPG;

/// <summary>
/// 死亡 / 焦点切换演出叠加层：屏幕短暂减饱和 + 大字 heading toast + 镜头缓动期间的输入抑制。
/// 配合 <see cref="PlayerDeathPresenter"/> 与 <see cref="RuntimeCameraController.BeginCinematicSweep"/> 一起用，
/// 让"焦点角色倒下→切到下一个队员"不再是瞬切。
/// </summary>
/// <remarks>
/// 设计意图（见 <c>Docs/产品愿景.md</c> "死亡与复活 / 仪式感要求"第 1 条）：
/// <list type="bullet">
/// <item>1.5 秒减饱和 vignette（黑色 modulate fade in 0.25s → hold 1.0s → fade out 0.25s）。</item>
/// <item>大字 heading toast 显示"X 倒下了" / "全队覆灭"，由 <see cref="ToastOverlay.ShowHeading"/> 渲染。</item>
/// <item>同时驱动 <c>IsActive</c>=true，让 <c>Main.UiMode</c> 把玩家输入暂时锁住（reason key
///   <c>timeline.lock.death_sequence</c>），避免 1.5s 内误操作。</item>
/// </list>
/// 本类只关心"屏幕看到什么"——镜头缓动由 <see cref="RuntimeCameraController.BeginCinematicSweep"/> 单独负责，
/// heading 文字由调用方传入（中文 / 英文已在 i18n 落地）。
/// </remarks>
public sealed partial class DeathSequenceOverlay : ColorRect
{
	private const double FadeInSec = 0.25;
	private const double FadeOutSec = 0.25;
	private const float TargetAlpha = 0.55f;

	private double _elapsedSec;
	private double _totalDurationSec;
	private bool _active;

	public DeathSequenceOverlay()
	{
		Name = "DeathSequenceOverlay";
		MouseFilter = MouseFilterEnum.Ignore;
		Color = new Color(0f, 0f, 0f, 0f);
		Visible = false;
		ZIndex = 200;
		AnchorsPreset = (int)LayoutPreset.FullRect;
	}

	/// <summary>演出是否还在进行中（含 fade in / hold / fade out）。</summary>
	public bool IsActive => _active;

	/// <summary>
	/// 触发一次演出。<paramref name="totalDurationSec"/> 包含 fade in + hold + fade out。
	/// 重复调用会重置进度（最新一次覆盖前一次）。
	/// </summary>
	public void Begin(double totalDurationSec = 1.5)
	{
		_totalDurationSec = System.Math.Max(FadeInSec + FadeOutSec + 0.05, totalDurationSec);
		_elapsedSec = 0;
		_active = true;
		Visible = true;
		Color = new Color(0f, 0f, 0f, 0f);
	}

	public override void _Process(double delta)
	{
		base._Process(delta);
		if (!_active) return;

		_elapsedSec += delta;
		var alpha = ResolveAlpha(_elapsedSec, _totalDurationSec);
		Color = new Color(0f, 0f, 0f, alpha);

		if (_elapsedSec >= _totalDurationSec)
		{
			_active = false;
			Visible = false;
			Color = new Color(0f, 0f, 0f, 0f);
		}
	}

	internal static float ResolveAlpha(double elapsed, double total)
	{
		if (elapsed <= 0 || total <= 0) return 0f;
		var fadeInEnd = FadeInSec;
		var fadeOutStart = total - FadeOutSec;
		if (elapsed < fadeInEnd)
			return TargetAlpha * (float)(elapsed / FadeInSec);
		if (elapsed < fadeOutStart)
			return TargetAlpha;
		var fadeOutProgress = System.Math.Clamp((elapsed - fadeOutStart) / FadeOutSec, 0.0, 1.0);
		return TargetAlpha * (1f - (float)fadeOutProgress);
	}
}
