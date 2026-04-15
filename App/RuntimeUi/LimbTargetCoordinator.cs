using System;
using System.Collections.Generic;
using System.Linq;

namespace MiniRPG;

internal sealed class LimbTargetCoordinator
{
	private readonly GameState _state;
	private readonly LogModule _log;
	private readonly PanelManager _panels;
	private readonly Func<LimbTargetPanelModule> _ensureLimbTargetPanel;
	private readonly Action<TimelinePlayerAction> _submitPlayerAction;

	public LimbTargetCoordinator(
		GameState state,
		LogModule log,
		PanelManager panels,
		Func<LimbTargetPanelModule> ensureLimbTargetPanel,
		Action<TimelinePlayerAction> submitPlayerAction)
	{
		_state = state;
		_log = log;
		_panels = panels;
		_ensureLimbTargetPanel = ensureLimbTargetPanel;
		_submitPlayerAction = submitPlayerAction;
	}

	public LimbTargetPanelModule? LimbTargetPanel { get; set; }

	public void OpenLimbTargetPanel(Actor target, InteractionDef skill) =>
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

	public void OpenLimbTargetPanel(LimbTargetPanelModule.LimbTargetRequest request)
	{
		var panel = _ensureLimbTargetPanel();
		panel.Open(_state, request);
		_panels.PushFocus(panel);
	}

	public void OpenOperationTargetPanel(Actor surgeon, Actor target, InteractionDef skill)
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

	public void OpenCorpseHarvest(Item corpseItem)
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

	public void SubmitCorpseOperation(string skillId, Item corpseItem)
	{
		var player = ActorModule.GetPlayer(_state);
		if (player == null)
			return;

		_submitPlayerAction(TimelinePlayerAction.CastSkill(
			skillId,
			SkillTargetType.Item,
			targetItemId: corpseItem.InstanceId,
			targetX: player.X,
			targetY: player.Y,
			targetZ: player.Z));
	}

	public void CloseLimbTargetPanel()
	{
		if (LimbTargetPanel == null || !LimbTargetPanel.Visible)
			return;

		LimbTargetPanel.Close();
		_panels.OnPanelClosed(LimbTargetPanel);
	}

	public void HandleLimbTargetConfirmed(LimbTargetPanelModule.LimbTargetRequest request, LimbTargetPanelModule.LimbTargetOption option)
	{
		if (request.TargetItem != null)
		{
			CloseLimbTargetPanel();
			var player = ActorModule.GetPlayer(_state);
			if (player == null)
				return;

			_submitPlayerAction(TimelinePlayerAction.CastSkill(
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
		_submitPlayerAction(TimelinePlayerAction.CastSkill(
			request.Skill.Id,
			SkillTargetType.Actor,
			targetActorId: target.Id,
			targetLimbId: limb?.Id ?? option.LimbId,
			targetX: target.X,
			targetY: target.Y,
			targetZ: target.Z));
	}

	private static LimbTargetPanelModule.LimbTargetOption CreateOperationOption(Actor target, string limbId)
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
}
