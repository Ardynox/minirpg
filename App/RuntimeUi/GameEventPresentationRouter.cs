using System;
using System.Collections.Generic;
using MiniRPG.Core.Combat;

namespace MiniRPG;

internal sealed class GameEventPresentationRouter
{
	private readonly GameState _state;
	private readonly LogModule _log;
	private readonly CombatUIModule _combatUi;
	private readonly GroundPanelModule _ground;
	private readonly IncidentAlertModule _incidentAlerts;
	private readonly Action<GameEvent> _playCombatFx;
	private readonly Action<GameEvent> _playWeatherLightningFx;
	private readonly Action<GameEvent> _presentActorMotion;
	private readonly Action<string> _handlePlayerDeath;
	private readonly Action<Actor, PlayerTargetSource> _setCurrentTarget;
	private readonly Action _closeDialogPanel;
	private readonly Action _closeTradePanel;
	private readonly Func<TradeUIModule> _ensureTradeUi;
	private readonly Func<DialogUIModule> _ensureDialogUi;
	private readonly Action _onPlayerRestCompleted;
	private readonly Action<string> _showInfoToast;
	private readonly Action<string> _showWarningToast;
	private readonly Action _flushMap;

	public GameEventPresentationRouter(
		GameState state,
		LogModule log,
		CombatUIModule combatUi,
		GroundPanelModule ground,
		IncidentAlertModule incidentAlerts,
		Action<GameEvent> playCombatFx,
		Action<GameEvent> playWeatherLightningFx,
		Action<GameEvent> presentActorMotion,
		Action<string> handlePlayerDeath,
		Action<Actor, PlayerTargetSource> setCurrentTarget,
		Action closeDialogPanel,
		Action closeTradePanel,
		Func<TradeUIModule> ensureTradeUi,
		Func<DialogUIModule> ensureDialogUi,
		Action onPlayerRestCompleted,
		Action<string> showInfoToast,
		Action<string> showWarningToast,
		Action flushMap)
	{
		_state = state;
		_log = log;
		_combatUi = combatUi;
		_ground = ground;
		_incidentAlerts = incidentAlerts;
		_playCombatFx = playCombatFx;
		_playWeatherLightningFx = playWeatherLightningFx;
		_presentActorMotion = presentActorMotion;
		_handlePlayerDeath = handlePlayerDeath;
		_setCurrentTarget = setCurrentTarget;
		_closeDialogPanel = closeDialogPanel;
		_closeTradePanel = closeTradePanel;
		_ensureTradeUi = ensureTradeUi;
		_ensureDialogUi = ensureDialogUi;
		_onPlayerRestCompleted = onPlayerRestCompleted;
		_showInfoToast = showInfoToast;
		_showWarningToast = showWarningToast;
		_flushMap = flushMap;
	}

	public void Dispatch(List<GameEvent> events)
	{
		foreach (var e in events)
		{
			_log.DispatchEvent(e, _state);
			DispatchSingle(e);
		}

		_incidentAlerts.ProcessEvents(events);
	}

	private void DispatchSingle(GameEvent e)
	{
		switch (e.Type)
		{
			case "combat_attack":
				if (e.InitiatorId == _state.PlayerId && e.TargetId != null)
				{
					var attackedTarget = ActorModule.GetById(_state, e.TargetId);
					if (attackedTarget != null)
						_setCurrentTarget(attackedTarget, PlayerTargetSource.Explicit);
				}
				_playCombatFx(e);
				if (e.InitiatorId == _state.PlayerId)
					ResAccess.GetAnimatable(_state.PlayerId)?.PlayOneShot("Attack_1");
				if (e.TargetId == _state.PlayerId)
					ResAccess.GetAnimatable(_state.PlayerId)?.PlayOneShot("Pain");
				break;
			case "combat_block":
				_playCombatFx(e);
				break;
			case "weather_lightning_strike":
				_playWeatherLightningFx(e);
				if (e.TargetId == _state.PlayerId)
					ResAccess.GetAnimatable(_state.PlayerId)?.PlayOneShot("Pain");
				break;
			case "actor_killed" when e.TargetId == ActiveActorAccess.GetActiveId(_state):
				ResAccess.GetAnimatable(_state.PlayerId)?.Play("Die", false);
				// 焦点角色死亡：HandlePlayerDeath 自带"队伍仍有活人就拒绝进终局"的守卫，
				// 实际终局走 ActiveActorDeathHandler → party_wiped 路径；这里保留调用作为 1v1 兜底。
				_handlePlayerDeath("killed");
				break;
			case "death_blood_loss" when e.TargetId == ActiveActorAccess.GetActiveId(_state):
				ResAccess.GetAnimatable(_state.PlayerId)?.Play("Die", false);
				_handlePlayerDeath("blood_loss");
				break;
			case "death_infection" when e.TargetId == ActiveActorAccess.GetActiveId(_state):
				ResAccess.GetAnimatable(_state.PlayerId)?.Play("Die", false);
				_handlePlayerDeath("infection");
				break;
			case "actor_killed":
				_state.KillCount++;
				_combatUi.HandleActorKilled(e);
				break;
			case "actor_moved":
				_presentActorMotion(e);
				if (e.InitiatorId == _state.PlayerId)
					ResAccess.GetAnimatable(_state.PlayerId)?.PlayOneShot("Walk");
				break;
			case "actor_climbed":
				_presentActorMotion(e);
				break;
			case "actor_incapacitated" when e.TargetId == ActiveActorAccess.GetActiveId(_state):
				ResAccess.GetAnimatable(_state.PlayerId)?.Play("Die", false);
				_handlePlayerDeath("incapacitated");
				break;
			case "party_wiped":
				// 由 ActiveActorDeathHandler 在全队都死后派出；走"回主菜单"终局。
				// HandlePlayerDeath 自带"还有活人就拒绝进终局"的守卫，这里安全地无条件转发。
				_handlePlayerDeath("party_wiped");
				break;
			case "party_member_lost":
				// 队员倒下：弹个 Toast 让玩家立刻看到（日志由 LogModule 同步翻译）。
				_showWarningToast(LocalizationService.T(
					string.Equals(e.EffectType, "was_active", StringComparison.Ordinal)
						? "log.party.member_lost.was_active"
						: "log.party.member_lost",
					("member", e.TargetActorName ?? "?")));
				break;
			case "active_actor_switched":
				// PartyModule.ActiveId 已经被 ActiveActorDeathHandler 改完；
				// FlushMap 内部的 SyncViewToActiveActor 会把相机和 PlayerX/Y/Z 同步到新焦点，
				// 这里立刻 flush 一次让相机不要等到下一帧才切。
				_showInfoToast(LocalizationService.T(
					"log.party.active_switched",
					("member", e.TargetActorName ?? "?")));
				_flushMap();
				break;
			case "actor_revived":
				// 复活通道暂时还没人发，先备好翻译；ReviveService 接通时直接派 GameEvent 即可。
				_showInfoToast(LocalizationService.T(
					"log.party.revived",
					("member", e.TargetActorName ?? "?")));
				break;
			case "interaction":
				DispatchInteraction(e);
				break;
			case "rest_completed" when e.TargetId == _state.PlayerId:
				_onPlayerRestCompleted();
				break;
			case "item_picked_up" or "item_dropped":
				_ground.Invalidate();
				break;
		}
	}

	private void DispatchInteraction(GameEvent e)
	{
		switch (e.EffectType)
		{
			case "trade":
				if (e.TargetId != null && ActorModule.GetById(_state, e.TargetId) != null)
					_closeDialogPanel();
				_ensureTradeUi().OpenTradeMenu(e);
				break;
			case "talk":
				if (e.TargetId != null)
				{
					var talkTarget = ActorModule.GetById(_state, e.TargetId);
					if (talkTarget != null)
					{
						_closeTradePanel();
						_ensureDialogUi().OpenDialog(talkTarget);
						break;
					}
				}
				_log.Add(LocalizationService.T("dialog.fallback.line", ("target", e.TargetActorName)));
				break;
			case "tame":
				_log.Add(LocalizationService.T("log.interaction.tame_success", ("target", e.TargetActorName)));
				break;
			default:
				_log.Add(LocalizationService.T("log.interaction.default", ("name", e.InteractionName), ("target", e.TargetActorName)));
				break;
		}
	}
}
