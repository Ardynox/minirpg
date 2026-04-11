using System;
using System.Linq;
using Godot;

namespace MiniRPG;

public partial class Main
{
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
		CloseLimbTargetPanel();
		if (_skillCastCursorActive)
			EndInspectMode();

		RefreshArmedSkillUi();
	}

	private void ClearArmedSkill(bool restoreFocus = true)
	{
		_armedSkillId = null;
		CloseLimbTargetPanel();
		RefreshArmedSkillUi();
		if (_skillCastCursorActive)
			EndInspectMode(restoreFocus);
	}

	private void RefreshArmedSkillUi()
	{
		_skillBar.ArmedSkillId = _armedSkillId;
		_skillMgr.ArmedSkillId = _armedSkillId;
		_skillBarDirty = true;
		_skillMgr.Dirty = true;
	}

	private void OpenLimbTargetPanel(Actor target, InteractionDef skill) =>
		OpenLimbTargetPanel(new LimbTargetPanelModule.LimbTargetRequest
		{
			TargetActor = target,
			Skill = skill,
			TargetName = IdentificationModule.GetActorDisplayName(_state, target),
			Options = target.Limbs
				.Select(limb => new LimbTargetPanelModule.LimbTargetOption
				{
					LimbId = limb.Id,
					Label = limb.Name,
					CurrentDurability = limb.Durability,
					MaxDurability = limb.MaxDurability,
					IsVital = CombatModule.IsVitalLimb(limb),
					IsMissing = limb.Durability <= 0,
				})
				.ToList(),
		});

	private void OpenLimbTargetPanel(LimbTargetPanelModule.LimbTargetRequest request)
	{
		var panel = EnsureLimbTargetPanel();
		panel.Open(_state, request);
		_panels.PushFocus(panel);
	}

	private void OpenOperationTargetPanel(Actor surgeon, Actor target, InteractionDef skill)
	{
		var limbIds = SurgeryModule.GetLiveOperationLimbIds(surgeon, target);
		if (limbIds.Count == 0)
		{
			_log.Add(LocalizationService.TOrFallback("log.surgery.no_operation_targets", "No valid operation is available for that target."));
			return;
		}

		var request = new LimbTargetPanelModule.LimbTargetRequest
		{
			TargetActor = target,
			Skill = skill,
			TargetName = IdentificationModule.GetActorDisplayName(_state, target),
			Options = limbIds
				.Select(limbId => CreateOperationOption(target, limbId))
				.ToList(),
		};
		OpenLimbTargetPanel(request);
	}

	private void OpenCorpseHarvest(Item corpseItem)
	{
		var player = ActorModule.GetPlayer(_state);
		if (player == null)
			return;

		var skill = InteractionDefs.Get("harvest_corpse");
		if (skill == null)
			return;

		var limbIds = SurgeryModule.GetCorpseHarvestableLimbIds(corpseItem);
		if (limbIds.Count == 0)
		{
			_log.Add(LocalizationService.TOrFallback("log.corpse.no_harvest_targets", "Nothing useful remains to harvest."));
			return;
		}

		var request = new LimbTargetPanelModule.LimbTargetRequest
		{
			TargetItem = corpseItem,
			Skill = skill,
			TargetName = ItemFormatHelper.GetDisplayName(_state, corpseItem),
			Options = limbIds
				.Select(limbId => CreateCorpseHarvestOption(limbId))
				.ToList(),
		};
		OpenLimbTargetPanel(request);
	}

	private LimbTargetPanelModule.LimbTargetOption CreateOperationOption(Actor target, string limbId)
	{
		var current = target.Limbs.FirstOrDefault(limb => string.Equals(limb.Id, limbId, StringComparison.Ordinal));
		var preset = PresetDB.Limbs.GetValueOrDefault(limbId);
		return new LimbTargetPanelModule.LimbTargetOption
		{
			LimbId = limbId,
			Label = current?.Name ?? preset?.Name ?? limbId,
			CurrentDurability = current?.Durability ?? 0,
			MaxDurability = current?.MaxDurability ?? preset?.MaxDurability ?? 0,
			IsVital = current != null
				? CombatModule.IsVitalLimb(current)
				: (preset?.Tags.ContainsKey(CombatModule.VitalTag) ?? false) || (preset?.Tags.ContainsKey("要害") ?? false),
			IsMissing = current == null || current.Durability <= 0,
		};
	}

	private static LimbTargetPanelModule.LimbTargetOption CreateCorpseHarvestOption(string limbId)
	{
		var preset = PresetDB.Limbs.GetValueOrDefault(limbId);
		return new LimbTargetPanelModule.LimbTargetOption
		{
			LimbId = limbId,
			Label = preset?.Name ?? limbId,
			CurrentDurability = preset?.MaxDurability ?? 0,
			MaxDurability = preset?.MaxDurability ?? 0,
			IsVital = (preset?.Tags.ContainsKey(CombatModule.VitalTag) ?? false) || (preset?.Tags.ContainsKey("要害") ?? false),
			IsMissing = false,
		};
	}

	private void SubmitCorpseOperation(string skillId, Item corpseItem)
	{
		var player = ActorModule.GetPlayer(_state);
		if (player == null)
			return;

		SubmitPlayerAction(TimelinePlayerAction.CastSkill(
			skillId,
			SkillTargetType.Item,
			targetItemId: corpseItem.InstanceId,
			targetX: player.X,
			targetY: player.Y,
			targetZ: player.Z));
	}

	private void CloseLimbTargetPanel()
	{
		if (_limbTargetPanel == null || !_limbTargetPanel.Visible)
			return;

		_limbTargetPanel.Close();
		_panels.OnPanelClosed(_limbTargetPanel);
	}

	private void HandleLimbTargetConfirmed(LimbTargetPanelModule.LimbTargetRequest request, LimbTargetPanelModule.LimbTargetOption option)
	{
		if (request.TargetItem != null)
		{
			CloseLimbTargetPanel();
			var player = ActorModule.GetPlayer(_state);
			if (player == null)
				return;

			SubmitPlayerAction(TimelinePlayerAction.CastSkill(
				request.Skill.Id,
				SkillTargetType.Item,
				targetLimbId: option.LimbId,
				targetItemId: request.TargetItem.InstanceId,
				targetX: player.X,
				targetY: player.Y,
				targetZ: player.Z));
			return;
		}

		var target = request.TargetActor;
		var limb = target?.Limbs.Find(candidate => string.Equals(candidate.Id, option.LimbId, StringComparison.Ordinal));
		if (target == null)
		{
			CloseLimbTargetPanel();
			return;
		}

		CloseLimbTargetPanel();
		SubmitPlayerAction(TimelinePlayerAction.CastSkill(
			request.Skill.Id,
			SkillTargetType.Actor,
			targetActorId: target.Id,
			targetLimbId: limb?.Id ?? option.LimbId,
			targetX: target.X,
			targetY: target.Y,
			targetZ: target.Z));
	}

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

	private void StartSkillCastCursorMode()
	{
		var skill = GetArmedSkill();
		if (skill == null || !_session.GameStarted || _menu.InMenu || _mapRender == null)
			return;

		var player = ActorModule.GetPlayer(_state);
		if (player == null)
			return;

		_inspectModeActive = true;
		_skillCastCursorActive = true;
		_inspectWorldCell = ResolveSkillCursorOriginCell(player);
		_inspectPreviousFocusId = _panels.FocusedId;
		CloseActorInspectPanel();
		_panels.SetFocus("map");
		_log.Add(LocalizationService.T("ui.skill.targeting.entered", ("skill", skill.Name)));
		FlushMap();
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

		var targetType = ActionModule.ResolveSkillTargetType(skill);
		var targetActor = targetType == SkillTargetType.Actor
			? ActorModule.GetAt(_state, worldCell.X, worldCell.Y, worldCell.Z)
			: null;
		var restoreFocus = _skillCastCursorActive;
		if (_inspectModeActive)
			EndInspectMode(restoreFocus);

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
