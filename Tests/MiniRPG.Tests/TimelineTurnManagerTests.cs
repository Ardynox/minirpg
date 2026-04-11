using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Data;
using MiniRPG.Core.Weather;
using Xunit;

namespace MiniRPG.Tests;

public sealed class TimelineTurnManagerTests
{
	[Fact]
	public void Reset_PlayerStartsReady()
	{
		var state = new GameState
		{
			PlayerId = "player",
			Actors = new Dictionary<string, Actor>
			{
				["player"] = CreateActor("player", Factions.Player, moving: 1f, consciousness: 1f, metabolism: 1f),
				["enemy"] = CreateActor("enemy", Factions.Hostile, moving: 1f, consciousness: 1f, metabolism: 1f),
			},
		};

		TimelineTurnManager.Reset(state);

		Assert.True(TimelineTurnManager.IsPlayerTurn(state));
		Assert.Equal("player", state.Timeline.CurrentActorId);
		Assert.Equal(2, state.Timeline.Actors.Count);
		Assert.Equal(TimelineTurnManager.ActionThreshold,
			Assert.Single(state.Timeline.Actors.FindAll(entry => entry.ActorId == "player")).Charge);
	}

	[Fact]
	public void CalculateSpeed_UsesMovingConsciousnessAndMetabolism()
	{
		var actor = CreateActor("player", Factions.Player, moving: 1.5f, consciousness: 0.5f, metabolism: 0.25f);
		var caps = actor.ComputeCapacities();

		var speed = TimelineTurnManager.CalculateSpeed(actor);

		Assert.Equal(
			caps.GetValueOrDefault(Caps.Moving)
			* caps.GetValueOrDefault(Caps.Consciousness)
			* caps.GetValueOrDefault(Caps.Metabolism),
			speed,
			4);
	}

	[Fact]
	public void SyncActors_RemovesDeadEntriesAndAddsMissingEntries()
	{
		var state = new GameState
		{
			PlayerId = "player",
			Actors = new Dictionary<string, Actor>
			{
				["player"] = CreateActor("player", Factions.Player, moving: 1f, consciousness: 1f, metabolism: 1f),
				["enemy"] = CreateActor("enemy", Factions.Hostile, moving: 1f, consciousness: 1f, metabolism: 1f),
			},
			Timeline = new TimelineState
			{
				CurrentActorId = "ghost",
				LastActorId = "ghost",
				Actors =
				[
					new TimelineActorState { ActorId = "ghost", Charge = 12f },
					new TimelineActorState { ActorId = "player", Charge = 34f },
				],
			},
		};

		TimelineTurnManager.SyncActors(state);

		Assert.Null(state.Timeline.CurrentActorId);
		Assert.Null(state.Timeline.LastActorId);
		Assert.Equal(2, state.Timeline.Actors.Count);
		Assert.Contains(state.Timeline.Actors, entry => entry.ActorId == "player" && entry.Charge == 34f);
		Assert.Contains(state.Timeline.Actors, entry => entry.ActorId == "enemy" && entry.Charge == 0f);
	}

	[Fact]
	public void CreateDebugSnapshot_SortsCurrentThenEtaThenStableTieBreak()
	{
		var state = new GameState
		{
			PlayerId = "player",
			Actors = new Dictionary<string, Actor>
			{
				["player"] = CreateActor("player", Factions.Player, moving: 1f, consciousness: 1f, metabolism: 1f),
				["brute"] = CreateActor("brute", Factions.Hostile, moving: 1f, consciousness: 1f, metabolism: 1f),
				["scout"] = CreateActor("scout", Factions.Hostile, moving: 1f, consciousness: 1f, metabolism: 1f),
				["alpha"] = CreateActor("alpha", Factions.Hostile, moving: 1f, consciousness: 1f, metabolism: 1f),
				["omega"] = CreateActor("omega", Factions.Hostile, moving: 1f, consciousness: 1f, metabolism: 1f),
			},
			Timeline = new TimelineState
			{
				CurrentActorId = "brute",
				LastActorId = "player",
				Actors =
				[
					new TimelineActorState { ActorId = "player", Charge = 40f },
					new TimelineActorState { ActorId = "brute", Charge = 100f },
					new TimelineActorState { ActorId = "scout", Charge = 80f },
					new TimelineActorState { ActorId = "alpha", Charge = 50f },
					new TimelineActorState { ActorId = "omega", Charge = 50f },
				],
			},
		};

		var snapshot = TimelineTurnManager.CreateDebugSnapshot(state, playerDead: false, watchModeEnabled: false);

		Assert.Equal(TimelineDebugPhase.AutoAdvance, snapshot.Phase);
		Assert.True(snapshot.HasPendingAutoAdvance);
		Assert.Equal(TimelineInputLockReason.OtherActorsActing, snapshot.InputLockedReason);
		Assert.Equal(["brute", "scout", "alpha", "omega"], snapshot.Entries.Select(entry => entry.ActorName).ToArray());
		Assert.True(snapshot.Entries[0].IsCurrent);
		Assert.Equal(0f, snapshot.Entries[0].EtaToAct);
	}

	[Fact]
	public void CreateDebugSnapshot_ReportsPlayerTurnWithoutLock()
	{
		var state = new GameState
		{
			PlayerId = "player",
			Actors = new Dictionary<string, Actor>
			{
				["player"] = CreateActor("player", Factions.Player, moving: 1f, consciousness: 1f, metabolism: 1f),
				["enemy"] = CreateActor("enemy", Factions.Hostile, moving: 0.5f, consciousness: 1f, metabolism: 1f),
			},
			Timeline = new TimelineState
			{
				CurrentActorId = "player",
				LastActorId = "enemy",
				Actors =
				[
					new TimelineActorState { ActorId = "player", Charge = 100f },
					new TimelineActorState { ActorId = "enemy", Charge = 25f },
				],
			},
		};

		var snapshot = TimelineTurnManager.CreateDebugSnapshot(state, playerDead: false, watchModeEnabled: false);

		Assert.Equal(TimelineDebugPhase.PlayerTurn, snapshot.Phase);
		Assert.True(snapshot.IsPlayerTurn);
		Assert.False(snapshot.HasPendingAutoAdvance);
		Assert.Equal(TimelineInputLockReason.None, snapshot.InputLockedReason);
		Assert.Equal("player", snapshot.CurrentActorName);
		Assert.True(snapshot.Entries[0].IsPlayer);
		Assert.True(snapshot.Entries[0].IsCurrent);
	}

	[Fact]
	public void CreateDebugSnapshot_ReportsWatchModeLock()
	{
		var state = new GameState
		{
			PlayerId = "player",
			Actors = new Dictionary<string, Actor>
			{
				["player"] = CreateActor("player", Factions.Player, moving: 1f, consciousness: 1f, metabolism: 1f),
				["enemy"] = CreateActor("enemy", Factions.Hostile, moving: 1f, consciousness: 1f, metabolism: 1f),
			},
			Timeline = new TimelineState
			{
				CurrentActorId = "player",
				Actors =
				[
					new TimelineActorState { ActorId = "player", Charge = 100f },
					new TimelineActorState { ActorId = "enemy", Charge = 90f },
				],
			},
		};

		var snapshot = TimelineTurnManager.CreateDebugSnapshot(state, playerDead: false, watchModeEnabled: true);

		Assert.Equal(TimelineDebugPhase.WatchMode, snapshot.Phase);
		Assert.True(snapshot.HasPendingAutoAdvance);
		Assert.Equal(TimelineInputLockReason.WatchMode, snapshot.InputLockedReason);
	}

	[Fact]
	public void CreateDebugSnapshot_ReportsDeadState()
	{
		var state = new GameState
		{
			PlayerId = "player",
			Actors = new Dictionary<string, Actor>
			{
				["player"] = CreateActor("player", Factions.Player, moving: 1f, consciousness: 1f, metabolism: 1f),
				["enemy"] = CreateActor("enemy", Factions.Hostile, moving: 1f, consciousness: 1f, metabolism: 1f),
			},
			Timeline = new TimelineState
			{
				CurrentActorId = "enemy",
				Actors =
				[
					new TimelineActorState { ActorId = "player", Charge = 80f },
					new TimelineActorState { ActorId = "enemy", Charge = 100f },
				],
			},
		};

		var snapshot = TimelineTurnManager.CreateDebugSnapshot(state, playerDead: true, watchModeEnabled: false);

		Assert.Equal(TimelineDebugPhase.Dead, snapshot.Phase);
		Assert.False(snapshot.HasPendingAutoAdvance);
		Assert.Equal(TimelineInputLockReason.Dead, snapshot.InputLockedReason);
	}

	[Fact]
	public void CreateDebugSnapshot_ReportsNoActiveActorWhenRosterIsInactive()
	{
		var inactivePlayer = new Actor
		{
			Id = "player",
			DisplayName = "player",
			Faction = Factions.Player,
		};
		var state = new GameState
		{
			PlayerId = "player",
			Actors = new Dictionary<string, Actor>
			{
				["player"] = inactivePlayer,
			},
			Timeline = new TimelineState
			{
				Actors =
				[
					new TimelineActorState { ActorId = "player", Charge = 100f },
				],
			},
		};

		var snapshot = TimelineTurnManager.CreateDebugSnapshot(state, playerDead: false, watchModeEnabled: false);

		Assert.Equal(TimelineDebugPhase.NoActiveActor, snapshot.Phase);
		Assert.Equal(TimelineInputLockReason.NoActiveActor, snapshot.InputLockedReason);
		Assert.Empty(snapshot.Entries);
	}

	[Fact]
	public void SubmitPlayerAction_CastSkillConsumesOnlyWhenValid()
	{
		var (state, _, _) = SkillCastingTestHelper.CreateCombatState();
		TimelineTurnManager.Reset(state);

		var result = TimelineTurnManager.SubmitPlayerAction(
			state,
			TimelinePlayerAction.CastSkill("sword_parry", SkillTargetType.Self));

		Assert.True(result.ActionConsumed);
		Assert.Contains(result.Events, evt => evt.Type == "combat_block");
	}

	[Fact]
	public void SubmitPlayerAction_CastSkillStaysOnPlayerTurnWhenInvalid()
	{
		var (state, _, enemy) = SkillCastingTestHelper.CreateCombatState(enemyX: 7, enemyY: 1);
		TimelineTurnManager.Reset(state);

		var result = TimelineTurnManager.SubmitPlayerAction(
			state,
			TimelinePlayerAction.CastSkill(
				"bow_shoot",
				SkillTargetType.Actor,
				targetActorId: enemy.Id,
				targetX: enemy.X,
				targetY: enemy.Y,
				targetZ: enemy.Z));

		Assert.False(result.ActionConsumed);
		var failure = Assert.Single(result.Events);
		Assert.Equal("skill_cast_failed", failure.Type);
		Assert.Equal("out_of_range", failure.FailureReason);
		Assert.True(TimelineTurnManager.IsPlayerTurn(state));
	}

	[Fact]
	public void SkillCooldowns_TickOnlyOnOwnersConsumedTurns()
	{
		var (state, player, _) = SkillCastingTestHelper.CreateCombatState(enemyX: 2, enemyY: 1);
		TimelineTurnManager.Reset(state);

		var castResult = TimelineTurnManager.SubmitPlayerAction(
			state,
			TimelinePlayerAction.CastSkill("sword_parry", SkillTargetType.Self));
		Assert.True(castResult.ActionConsumed);
		Assert.Equal(1, player.GetSkillCooldown("sword_parry"));

		var autoResult = TimelineTurnManager.AdvanceAuto(state, watchModeEnabled: false, fastTurnModeEnabled: false);
		Assert.True(autoResult.ActionConsumed);
		Assert.Equal(1, player.GetSkillCooldown("sword_parry"));

		var moveResult = TimelineTurnManager.SubmitPlayerAction(state, TimelinePlayerAction.Move(0, -1));
		Assert.True(moveResult.ActionConsumed);
		Assert.Equal(0, player.GetSkillCooldown("sword_parry"));
	}

	[Fact]
	public void CalculateSpeed_AppliesWeatherOnlyToExposedSurfaceActors()
	{
		var (state, player, _) = SkillCastingTestHelper.CreateCombatState(enemyX: 6, enemyY: 1);
		state.Weather = new WeatherState
		{
			FrontPhase = 0f,
			DebugOverride = new WeatherDebugOverride
			{
				Type = WeatherType.Sandstorm,
				Intensity = WeatherIntensity.Heavy,
			},
		};

		var baseSpeed = TimelineTurnManager.CalculateSpeed(player);
		var exposedSpeed = TimelineTurnManager.CalculateSpeed(state, player);
		Assert.InRange(exposedSpeed, baseSpeed * 0.79f, baseSpeed * 0.81f);

		state.World!.SetFixture(player.X, player.Y, player.Z, "H", Entities.House);
		var shelteredSpeed = TimelineTurnManager.CalculateSpeed(state, player);
		Assert.Equal(baseSpeed, shelteredSpeed, 4);

		state.World.RemoveEntity(player.X, player.Y, 0, CellEntityType.Fixture, Entities.House);
		player.Z = 1;
		state.PlayerZ = 1;
		var undergroundSpeed = TimelineTurnManager.CalculateSpeed(state, player);
		Assert.Equal(baseSpeed, undergroundSpeed, 4);
	}

	private static Actor CreateActor(string id, string faction, float moving, float consciousness, float metabolism)
	{
		return new Actor
		{
			Id = id,
			DisplayName = id,
			Faction = faction,
			Limbs =
			[
				new Limb
				{
					Id = $"{id}_core",
					Name = "Core",
					MaxDurability = 10,
					Durability = 10,
					Material = "flesh",
					BodyPart = "torso",
					Capacities = new Dictionary<string, float>
					{
						[Caps.BloodCirculation] = 1f,
						[Caps.Moving] = moving,
						[Caps.Consciousness] = consciousness,
						[Caps.Metabolism] = metabolism,
					},
					Tags = new Dictionary<string, int>
					{
						["瑕佸"] = 1,
					},
				},
			],
		};
	}
}
