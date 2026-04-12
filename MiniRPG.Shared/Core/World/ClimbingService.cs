using System;
using System.Collections.Generic;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;

namespace MiniRPG.Core.World;

/// <summary>
/// Server-authoritative climbing and gravity rules.
/// Replaces the old VerticalTraversalService with a unified climb/fall model.
/// </summary>
public static class ClimbingService
{
	/// <summary>Fixture entity ID for explicitly climbable surfaces (walls, cliffs).</summary>
	public const string ClimbableWallFixture = "climbable_wall";

	/// <summary>
	/// Can the actor auto-climb at (x,y,z) in direction dz without a skill check?
	/// True when a ladder, stair_down (dz=+1) or stair_up (dz=-1) is present
	/// and the target layer is walkable.
	/// </summary>
	public static bool CanAutoClimb(WorldMap world, int x, int y, int z, int dz)
	{
		if (dz == 0)
			return false;

		var targetZ = z + dz;
		if (!world.IsWalkable(x, y, targetZ))
			return false;

		if (world.HasFixture(x, y, z, Entities.Ladder))
			return true;

		return dz > 0
			? world.HasFixture(x, y, z, Entities.StairDown)
			: world.HasFixture(x, y, z, Entities.StairUp);
	}

	/// <summary>
	/// Can the actor attempt a checked climb (may fail) at (x,y,z) in direction dz?
	/// True when a climbable surface is present and target layer is walkable.
	/// </summary>
	public static bool CanAttemptClimb(WorldMap world, int x, int y, int z, int dz)
	{
		if (dz == 0)
			return false;

		var targetZ = z + dz;
		return world.IsWalkable(x, y, targetZ)
			&& world.HasFixture(x, y, z, ClimbableWallFixture);
	}

	/// <summary>
	/// Returns the climb difficulty at position (x,y,z). 0 = trivial (auto-climb fixtures).
	/// Higher values make the check harder.
	/// </summary>
	public static int GetClimbDifficulty(WorldMap world, int x, int y, int z)
	{
		if (world.HasFixture(x, y, z, Entities.Ladder)
			|| world.HasFixture(x, y, z, Entities.StairDown)
			|| world.HasFixture(x, y, z, Entities.StairUp))
		{
			return 0;
		}

		if (world.HasFixture(x, y, z, ClimbableWallFixture))
			return 5;

		return 10;
	}

	/// <summary>
	/// Roll a simple probability check for climbing.
	/// Returns true on success. Difficulty 0 always succeeds.
	/// </summary>
	public static bool RollClimbCheck(Actor actor, int difficulty)
	{
		if (difficulty <= 0)
			return true;

		var climbingBonus = actor.GetTag("climbing");
		var successPercent = Math.Clamp(80 + climbingBonus * 2 - difficulty * 10, 10, 100);
		return Random.Shared.Next(100) < successPercent;
	}

	/// <summary>
	/// Move actor vertically by dz layers. Updates position, player tracking, and chunk map.
	/// </summary>
	public static void MoveActorVertical(GameState state, Actor actor, int dz)
	{
		ActorModule.MoveActor(state, actor.Id, actor.X, actor.Y, actor.Z + dz);
	}

	/// <summary>
	/// Apply gravity to a single actor. Returns number of layers fallen.
	/// Identical logic to old VerticalTraversalService.ApplyGravity.
	/// </summary>
	public static int ApplyGravity(GameState state, Actor actor, int maxFallPerStep)
	{
		if (state.World == null)
			return 0;

		var world = state.World;
		var x = actor.X;
		var y = actor.Y;
		var z = actor.Z;
		var fellLayers = 0;
		var cappedMax = Math.Clamp(maxFallPerStep, 1, 128);

		while (fellLayers < cappedMax)
		{
			var belowZ = z + 1;
			if (world.HasFixture(x, y, z, Entities.Ladder)
				|| world.HasFixture(x, y, z, Entities.StairDown)
				|| world.HasFixture(x, y, belowZ, Entities.Ladder)
				|| world.HasFixture(x, y, belowZ, Entities.StairUp))
			{
				break;
			}

			if (!world.IsWalkable(x, y, belowZ))
				break;

			z = belowZ;
			fellLayers++;
		}

		if (fellLayers <= 0)
			return 0;

		ActorModule.MoveActor(state, actor.Id, x, y, z);
		return fellLayers;
	}

	/// <summary>
	/// Apply gravity to the player actor and emit fall damage events if applicable.
	/// Designed to be called server-side after each consumed timeline action.
	/// </summary>
	public static void ApplyPlayerGravityAndDamage(GameState state, List<GameEvent> events)
	{
		var player = ActorModule.GetPlayer(state);
		if (player == null)
			return;

		var runtime = GameConfig.WorldRuntime;
		var fellLayers = ApplyGravity(state, player, runtime.MaxFallLayersPerStep);
		if (fellLayers <= 0)
			return;

		events.Add(new GameEvent("actor_fell")
		{
			InitiatorId = player.Id,
			Damage = fellLayers,
		});

		var freeLayers = Math.Clamp(runtime.FallDamageFreeLayers, 0, 16);
		if (fellLayers > freeLayers && player.Limbs.Count > 0)
		{
			var impact = player.Limbs.Find(limb => limb.BodyPart == BodyParts.Leg)
				?? player.Limbs.Find(limb => limb.BodyPart == BodyParts.Foot)
				?? player.Limbs.Find(limb => limb.BodyPart == BodyParts.Torso)
				?? player.Limbs[0];
			var damagePerLayer = Math.Clamp(runtime.FallDamagePerLayer, 1, 100);
			var fallDamage = (fellLayers - freeLayers) * damagePerLayer;
			var damageEvents = CombatModule.ApplyEnvironmentalDamage(state, player, impact, fallDamage, DamageTypes.Blunt);
			events.AddRange(damageEvents);
		}
	}
}
