using MiniRPG.Core.Combat;
using MiniRPG.Core.Multiplayer;

namespace MiniRPG.Module.Session;

/// <summary>
/// Translate a <see cref="TimelinePlayerAction"/> into the equivalent
/// <see cref="ClientCommand"/> payload for backend submission.
/// </summary>
/// <remarks>
/// Kept separate from <c>Main</c> / <c>GameSessionModule</c> so that the
/// mapping stays a pure data translation with no runtime dependencies.
/// </remarks>
public static class TimelineActionClientCommandFactory
{
	public static bool TryBuild(
		TimelinePlayerAction action,
		string actorId,
		out ClientCommand command)
	{
		command = action.Type switch
		{
			TimelinePlayerActionType.Move => new MoveClientCommand
			{
				ActorId = actorId,
				Dx = action.Dx,
				Dy = action.Dy,
			},
			TimelinePlayerActionType.Dig => new DigClientCommand
			{
				ActorId = actorId,
				Dx = action.Dx,
				Dy = action.Dy,
				SkillId = action.SkillId ?? string.Empty,
			},
			TimelinePlayerActionType.Attack => new AttackClientCommand
			{
				ActorId = actorId,
				SkillId = action.SkillId,
				TargetActorId = action.TargetActorId ?? string.Empty,
				TargetLimbId = action.TargetLimbId,
			},
			TimelinePlayerActionType.CastSkill => new CastSkillClientCommand
			{
				ActorId = actorId,
				SkillId = action.SkillId ?? string.Empty,
				TargetType = action.TargetType,
				TargetActorId = action.TargetActorId,
				TargetLimbId = action.TargetLimbId,
				TargetItemId = action.TargetItemId,
				TargetX = action.TargetX,
				TargetY = action.TargetY,
				TargetZ = action.TargetZ,
			},
			TimelinePlayerActionType.EatInventory => new EatInventoryClientCommand
			{
				ActorId = actorId,
				InventoryIndex = action.InventoryIndex,
			},
			TimelinePlayerActionType.Rest => new RestClientCommand
			{
				ActorId = actorId,
			},
			TimelinePlayerActionType.TerrainBuild => new TerrainBuildClientCommand
			{
				ActorId = actorId,
				TerrainId = action.TerrainId ?? string.Empty,
				TargetX = action.TargetX,
				TargetY = action.TargetY,
				TargetZ = action.TargetZ,
			},
			TimelinePlayerActionType.TerrainDemolish => new TerrainDemolishClientCommand
			{
				ActorId = actorId,
				TargetX = action.TargetX,
				TargetY = action.TargetY,
				TargetZ = action.TargetZ,
			},
			TimelinePlayerActionType.FacilityPlaceBlueprint => new FacilityPlaceBlueprintClientCommand
			{
				ActorId = actorId,
				FacilityDefId = action.FacilityDefId ?? string.Empty,
				TargetX = action.TargetX,
				TargetY = action.TargetY,
				TargetZ = action.TargetZ,
				Rotation = action.FacilityRotation,
			},
			TimelinePlayerActionType.FacilityDemolish => new FacilityDemolishClientCommand
			{
				ActorId = actorId,
				FacilityId = action.FacilityId ?? string.Empty,
			},
			TimelinePlayerActionType.FacilityDeliver => new FacilityDeliverClientCommand
			{
				ActorId = actorId,
				FacilityId = action.FacilityId ?? string.Empty,
			},
			TimelinePlayerActionType.FacilityConstruct => new FacilityConstructClientCommand
			{
				ActorId = actorId,
				FacilityId = action.FacilityId ?? string.Empty,
			},
			TimelinePlayerActionType.Climb => new ClimbClientCommand
			{
				ActorId = actorId,
				Dz = action.Dz,
			},
			_ => null!,
		};

		return command != null;
	}
}
