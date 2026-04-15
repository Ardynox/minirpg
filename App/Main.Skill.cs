using System;
using System.Linq;
using Godot;

namespace MiniRPG;

public partial class Main
{
	private LimbTargetCoordinator _limbTargetCoordinator = null!;

	private void HandleSkillConfirmRequested(InteractionDef skill)
	{
		if (!SkillQuery.IsUnifiedCastSkill(skill))
		{
			_log.Add(LocalizationService.T("log.skill_cast_failed.unsupported", ("skill", skill.Name)));
			return;
		}

		if (string.Equals(_armedSkillId, skill.Id, StringComparison.Ordinal))
		{
			ClearArmedSkill();
			return;
		}

		if (IsTimelineInputLocked())
			return;

		InterruptAutoNavigationForManualInput();
		ArmSkill(skill.Id);
	}

	private void ArmSkill(string skillId)
	{
		if (string.IsNullOrWhiteSpace(skillId))
		{
			ClearArmedSkill();
			return;
		}

		_armedSkillId = skillId;
		_limbTargetCoordinator.CloseLimbTargetPanel();
		if (_skillTargetCursorActive)
			EndSkillTargetCursorMode();

		RefreshArmedSkillUi();
	}

	private void ClearArmedSkill(bool restoreFocus = true)
	{
		_armedSkillId = null;
		_limbTargetCoordinator.CloseLimbTargetPanel();
		RefreshArmedSkillUi();
		if (_skillTargetCursorActive)
			EndSkillTargetCursorMode(restoreFocus);
	}

	private void RefreshArmedSkillUi()
	{
		_skillBar.ArmedSkillId = _armedSkillId;
		_skillMgr.ArmedSkillId = _armedSkillId;
		_skillBarDirty = true;
		_skillMgr.Dirty = true;
	}

	private void OpenLimbTargetPanel(Actor target, InteractionDef skill) =>
		_limbTargetCoordinator.OpenLimbTargetPanel(target, skill);

	private void OpenOperationTargetPanel(Actor surgeon, Actor target, InteractionDef skill) =>
		_limbTargetCoordinator.OpenOperationTargetPanel(surgeon, target, skill);

	private void OpenCorpseHarvest(Item corpseItem) =>
		_limbTargetCoordinator.OpenCorpseHarvest(corpseItem);

	private void SubmitCorpseOperation(string skillId, Item corpseItem) =>
		_limbTargetCoordinator.SubmitCorpseOperation(skillId, corpseItem);

	private void CloseLimbTargetPanel() =>
		_limbTargetCoordinator.CloseLimbTargetPanel();

	private void HandleLimbTargetConfirmed(LimbTargetPanelModule.LimbTargetRequest request, LimbTargetPanelModule.LimbTargetOption option) =>
		_limbTargetCoordinator.HandleLimbTargetConfirmed(request, option);

	private InteractionDef? GetArmedSkill()
	{
		if (string.IsNullOrWhiteSpace(_armedSkillId))
			return null;

		var player = ActorModule.GetPlayer(_state);
		if (player == null)
		{
			ClearArmedSkill(restoreFocus: false);
			return null;
		}

		foreach (var skill in SkillQuery.GetAll(player))
		{
			if (!string.Equals(skill.Id, _armedSkillId, StringComparison.Ordinal))
				continue;
			if (SkillQuery.IsUnifiedCastSkill(skill))
				return skill;
			break;
		}

		ClearArmedSkill();
		return null;
	}

	private bool TryCastArmedSkillAtMouse(Vector2 globalPosition)
	{
		if (_mapRender == null || !(_mapRender.TryGetWorldCellFromGlobalPosition(globalPosition, out var worldCell)))
			return false;

		return TryCastArmedSkillAtWorldCell(worldCell);
	}

	private bool TryCastArmedSkillAtWorldCell(Vector3I worldCell)
	{
		var skill = GetArmedSkill();
		if (skill == null || IsTimelineInputLocked())
			return false;

		var player = ActorModule.GetPlayer(_state);
		if (player == null)
			return false;

		InterruptAutoNavigationForManualInput();

		var targetType = ActionModule.ResolveSkillTargetType(skill);
		var targetActor = targetType == SkillTargetType.Actor
			? ActorModule.GetAt(_state, worldCell.X, worldCell.Y, worldCell.Z)
			: null;
		var restoreFocus = _skillTargetCursorActive;
		if (_skillTargetCursorActive)
			EndSkillTargetCursorMode(restoreFocus);

		if (IsIdentifySkill(skill))
			return TryHandleIdentifyActorTarget(targetActor);

		switch (targetType)
		{
			case SkillTargetType.Self:
				SubmitPlayerAction(TimelinePlayerAction.CastSkill(skill.Id, SkillTargetType.Self));
				return true;

			case SkillTargetType.Cell:
				SubmitPlayerAction(TimelinePlayerAction.CastSkill(
					skill.Id,
					SkillTargetType.Cell,
					targetX: worldCell.X,
					targetY: worldCell.Y,
					targetZ: worldCell.Z));
				return true;

			case SkillTargetType.Actor:
				if (targetActor != null && string.Equals(skill.EffectType, "operate", StringComparison.Ordinal))
				{
					SetCurrentTarget(targetActor, PlayerTargetSource.Explicit);
					OpenOperationTargetPanel(player, targetActor, skill);
					return true;
				}

				if (targetActor != null
					&& SkillQuery.IsAttackSkill(skill)
					&& ActionModule.CanCastSkill(
						_state,
						player,
						skill.Id,
						SkillTargetType.Actor,
						targetActor: targetActor,
						targetX: targetActor.X,
						targetY: targetActor.Y,
						targetZ: targetActor.Z))
				{
					SetCurrentTarget(targetActor, PlayerTargetSource.Explicit);
					OpenLimbTargetPanel(targetActor, skill);
					return true;
				}

				SubmitPlayerAction(TimelinePlayerAction.CastSkill(
					skill.Id,
					SkillTargetType.Actor,
					targetActorId: targetActor?.Id,
					targetX: worldCell.X,
					targetY: worldCell.Y,
					targetZ: worldCell.Z));
				return true;

			default:
				return false;
		}
	}

	private static bool IsIdentifySkill(InteractionDef skill) =>
		string.Equals(skill.EffectType, "identify", StringComparison.Ordinal);

	private bool TryHandleIdentifyActorTarget(Actor? targetActor)
	{
		if (targetActor == null)
		{
			_log.Add(LocalizationService.T("ui.inspect.no_actor"));
			return true;
		}

		var identified = IdentificationModule.IdentifyActor(_state, targetActor);
		var actorName = IdentificationModule.GetActorDisplayName(_state, targetActor);
		_log.Add(LocalizationService.T(
			identified ? "log.identify.actor_identified" : "log.identify.actor_known",
			("target", actorName)));

		OpenActorInspectPanel(targetActor);
		RefreshVisiblePanels();
		FlushMap();
		return true;
	}

	private bool TryHandleIdentifyItemTarget(Item item)
	{
		var skill = GetArmedSkill();
		if (skill == null || !IsIdentifySkill(skill))
			return false;

		IdentificationModule.IdentifyItem(_state, item);
		RefreshVisiblePanels();
		FlushMap();
		return true;
	}
}
