using System;
using System.Globalization;
using System.Text;
using Godot;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Config;

namespace MiniRPG.Module.Panel;

/// <summary>
/// 死亡报告面板：玩家死后的"复盘 modal"，显示死因 + 死亡回合 + 最近 N 条事件。
/// </summary>
/// <remarks>
/// <para>由 <c>App/RuntimeUi/PlayerDeathPresenter</c> 通过委托调用 <see cref="ShowReport"/> 弹出；
/// 不参与 <c>PanelManager</c> 焦点链——它是 modal 覆盖层，类似 <see cref="ToastOverlay"/>。</para>
///
/// <para>两种 reason 分支表现不同：
/// <list type="bullet">
/// <item>非 party_wipe（焦点死、incapacitated）：右上角"继续"按钮 → 关闭 panel，下一焦点接管。</item>
/// <item>party_wipe（全队覆灭）：底部按钮变"回主菜单"，由外部 onBackToMenu 委托接管真正的会话退出。</item>
/// </list></para>
/// </remarks>
public sealed partial class PlayerDeathReportPanel : PanelContainer
{
	private const float MinWidth = 480f;
	private const float MinHeight = 320f;
	private const int MaxEventsToShow = 30;

	private readonly Action _onBackToMenu;
	private RichTextLabel? _content;
	private Button? _actionButton;
	private bool _isPartyWipe;

	public PlayerDeathReportPanel(Action onBackToMenu)
	{
		_onBackToMenu = onBackToMenu ?? throw new ArgumentNullException(nameof(onBackToMenu));
		Name = "PlayerDeathReportPanel";
		Visible = false;
		MouseFilter = MouseFilterEnum.Stop;
		AnchorsPreset = (int)LayoutPreset.Center;
		CustomMinimumSize = new Vector2(MinWidth, MinHeight);
		ThemeTypeVariation = "TooltipPanel";
	}

	public override void _Ready()
	{
		base._Ready();

		var vbox = new VBoxContainer
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill,
		};
		vbox.AddThemeConstantOverride("separation", 8);
		AddChild(vbox);

		_content = new RichTextLabel
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill,
			BbcodeEnabled = true,
			ScrollActive = true,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			CustomMinimumSize = new Vector2(MinWidth - 32f, MinHeight - 80f),
		};
		vbox.AddChild(_content);

		_actionButton = new Button
		{
			Text = LocalizationService.TOrFallback("ui.common.close", "Close"),
			SizeFlagsHorizontal = SizeFlags.ShrinkEnd,
		};
		_actionButton.Pressed += OnActionPressed;
		vbox.AddChild(_actionButton);
	}

	/// <summary>
	/// 显示一份 <see cref="DeathReport"/>。<paramref name="isPartyWipe"/>=true 时按钮文案变"回主菜单"，
	/// 点按钮回调外部传入的 onBackToMenu。
	/// </summary>
	public void ShowReport(DeathReport report, bool isPartyWipe)
	{
		if (report == null) return;
		_isPartyWipe = isPartyWipe;

		EnsureContent();
		if (_content == null || _actionButton == null) return;

		_content.Clear();
		_content.AppendText(BuildReportText(report, isPartyWipe));

		_actionButton.Text = isPartyWipe
			? LocalizationService.TOrFallback("ui.common.back_to_menu", "Back to Main Menu")
			: LocalizationService.TOrFallback("ui.common.continue", "Continue");

		Visible = true;
		MoveToFront();
	}

	private void OnActionPressed()
	{
		Visible = false;
		if (_isPartyWipe)
			_onBackToMenu();
	}

	private void EnsureContent()
	{
		// 防御：在某些测试 / headless 路径里 _Ready 可能没被调（例如直接 new + ShowReport）。
		// 创建最小可工作的 _content / _actionButton 占位。
		if (_content == null)
		{
			_content = new RichTextLabel { BbcodeEnabled = true };
			AddChild(_content);
		}
		if (_actionButton == null)
		{
			_actionButton = new Button();
			_actionButton.Pressed += OnActionPressed;
			AddChild(_actionButton);
		}
	}

	private static string BuildReportText(DeathReport report, bool isPartyWipe)
	{
		var sb = new StringBuilder();
		sb.AppendLine(LocalizationService.TOrFallback(
			"death.report.title",
			"Death Report"));
		sb.AppendLine(LocalizationService.TOrFallback(
			"death.report.actor",
			"Actor: {actor}",
			("actor", string.IsNullOrWhiteSpace(report.ActorName) ? report.ActorId : report.ActorName)));
		sb.AppendLine(LocalizationService.TOrFallback(
			"death.report.turn",
			"Death turn: {turn}",
			("turn", report.DeathTurn.ToString(CultureInfo.InvariantCulture))));
		sb.AppendLine(LocalizationService.TOrFallback(
			"death.report.cause",
			"Cause: {cause}",
			("cause", LocalizeCause(report.DeathCause, report.KillerName))));

		var count = Math.Min(report.RecentEvents.Count, MaxEventsToShow);
		if (count > 0)
		{
			sb.AppendLine();
			sb.AppendLine(LocalizationService.TOrFallback(
				"death.report.recent_events_header",
				"Recent events ({count}):",
				("count", count.ToString(CultureInfo.InvariantCulture))));

			var start = report.RecentEvents.Count - count;
			for (var i = start; i < report.RecentEvents.Count; i++)
			{
				var e = report.RecentEvents[i];
				sb.AppendLine(LocalizationService.TOrFallback(
					"death.report.event_line",
					"  T{turn} - {kind} {detail}",
					("turn", e.Turn.ToString(CultureInfo.InvariantCulture)),
					("kind", e.EventType),
					("detail", BuildEventDetail(e))));
			}
		}

		if (isPartyWipe)
		{
			sb.AppendLine();
			sb.AppendLine(LocalizationService.TOrFallback(
				"death.party_wiped",
				"══════  Party Wiped ══════"));
		}

		return sb.ToString();
	}

	private static string BuildEventDetail(RecentEventEntry e)
	{
		var bits = new StringBuilder();
		if (!string.IsNullOrWhiteSpace(e.InitiatorActorName))
			bits.Append("by ").Append(e.InitiatorActorName).Append(' ');
		if (!string.IsNullOrWhiteSpace(e.LimbName))
			bits.Append('[').Append(e.LimbName).Append("] ");
		if (!string.IsNullOrWhiteSpace(e.ActionName))
			bits.Append(e.ActionName).Append(' ');
		if (e.Damage > 0)
			bits.Append('-').Append(e.Damage.ToString(CultureInfo.InvariantCulture)).Append(' ');
		if (!string.IsNullOrWhiteSpace(e.EffectType))
			bits.Append('(').Append(e.EffectType).Append(')');
		return bits.ToString().TrimEnd();
	}

	private static string LocalizeCause(string causeId, string? killer)
	{
		if (string.Equals(causeId, "killed_by", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(killer))
		{
			return LocalizationService.TOrFallback(
				"death.cause.killed_by",
				"Killed by {killer}",
				("killer", killer));
		}

		var key = "death.cause." + (string.IsNullOrWhiteSpace(causeId) ? "unknown" : causeId);
		var fallback = causeId switch
		{
			"killed" => "Killed in combat",
			"incapacitated" => "Incapacitated",
			"blood_loss" => "Bled out",
			"infection" => "Died of infection",
			"food_poisoning" => "Died of food poisoning",
			_ => "Unknown",
		};
		return LocalizationService.TOrFallback(key, fallback);
	}
}
