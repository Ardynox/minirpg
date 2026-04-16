using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Godot;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;
using MiniRPG.Module;
using MiniRPG.Module.Panel;
using MiniRPG.Module.Render;
using Xunit;

namespace MiniRPG.Tests;

public sealed class GameEventPresentationRouterTests
{
	[Fact]
	public void Dispatch_EmptyEventList_DoesNotThrow()
	{
		using var harness = new Harness();

		var exception = Record.Exception(() => harness.Router.Dispatch([]));

		Assert.Null(exception);
		Assert.Null(harness.Log.LastLine);
		Assert.Empty(harness.CurrentTargets);
	}

	[Fact]
	public void Dispatch_CombatAttack_ByNonPlayer_DoesNotSetCurrentTarget()
	{
		using var harness = new Harness();
		var animatable = new FakeAnimatable();
		harness.RegisterPlayerAnimatable(animatable);
		var villager = CreateActor("villager", Factions.Friendly, 2, 1);
		harness.AddActor(villager);

		harness.Router.Dispatch(
		[
			new GameEvent("combat_attack")
			{
				InitiatorId = harness.Enemy.Id,
				InitiatorActorName = harness.Enemy.DisplayName,
				TargetId = villager.Id,
				TargetActorName = villager.DisplayName,
				ActionName = "Slash",
				LimbName = "Head",
				Damage = 3,
			},
		]);

		Assert.Empty(harness.CurrentTargets);
		Assert.Single(harness.CombatFxEvents);
		Assert.Empty(animatable.PlayCalls);
		Assert.Empty(animatable.PlayOneShotCalls);
	}

	[Fact]
	public void Dispatch_InteractionTalk_MissingTarget_LogsFallbackLine()
	{
		using var harness = new Harness();

		harness.Router.Dispatch(
		[
			new GameEvent("interaction")
			{
				EffectType = "talk",
				TargetId = "missing_npc",
				TargetActorName = "Missing NPC",
			},
		]);

		Assert.Equal(
			LocalizationService.T("dialog.fallback.line", ("target", "Missing NPC")),
			harness.Log.LastLine);
		Assert.Equal(0, harness.CloseTradeCalls);
		Assert.False(harness.EnsureDialogUiCalled);
	}

	[Fact]
	public void Dispatch_Interaction_DefaultType_LogsGenericInteractionMessage()
	{
		using var harness = new Harness();

		harness.Router.Dispatch(
		[
			new GameEvent("interaction")
			{
				EffectType = "wave",
				InteractionName = "Wave",
				TargetActorName = "Villager",
			},
		]);

		Assert.Equal(
			LocalizationService.T("log.interaction.default", ("name", "Wave"), ("target", "Villager")),
			harness.Log.LastLine);
		Assert.False(harness.EnsureTradeUiCalled);
		Assert.False(harness.EnsureDialogUiCalled);
	}

	[Theory]
	[InlineData("actor_moved")]
	[InlineData("actor_climbed")]
	public void Dispatch_MovementEvents_PresentActorMotion(string eventType)
	{
		using var harness = new Harness();

		var motionEvent = new GameEvent(eventType)
		{
			InitiatorId = harness.Player.Id,
			SourceX = 1,
			SourceY = 1,
			SourceZ = 0,
			TargetX = 2,
			TargetY = 1,
			TargetZ = eventType == "actor_climbed" ? 1 : 0,
		};

		harness.Router.Dispatch([motionEvent]);

		Assert.Same(motionEvent, Assert.Single(harness.MotionEvents));
	}

	[Theory]
	[InlineData("actor_killed", "killed")]
	[InlineData("death_blood_loss", "blood_loss")]
	[InlineData("death_infection", "infection")]
	[InlineData("actor_incapacitated", "incapacitated")]
	public void Dispatch_PlayerDeathEvents_CallCorrectDeathReason(string eventType, string expectedReason)
	{
		using var harness = new Harness();
		var animatable = new FakeAnimatable();
		harness.RegisterPlayerAnimatable(animatable);

		harness.Router.Dispatch(
		[
			new GameEvent(eventType)
			{
				TargetId = harness.Player.Id,
				TargetActorName = harness.Player.DisplayName,
			},
		]);

		Assert.Equal([expectedReason], harness.DeathReasons);
		Assert.Equal([("Die", false)], animatable.PlayCalls);
	}

	private sealed class Harness : IDisposable
	{
		public Harness()
		{
			SkillCastingTestHelper.EnsureGameDataLoaded();
			(State, Player, Enemy) = SkillCastingTestHelper.CreateCombatState();
			PartyModule.Initialize(State);
			Log = new LogModule(_ => { }, () => { }, _ => { });
			IncidentAlerts = (IncidentAlertModule)RuntimeHelpers.GetUninitializedObject(typeof(IncidentAlertModule));

			Router = new GameEventPresentationRouter(
				State,
				Log,
				(CombatUIModule)RuntimeHelpers.GetUninitializedObject(typeof(CombatUIModule)),
				(GroundPanelModule)RuntimeHelpers.GetUninitializedObject(typeof(GroundPanelModule)),
				IncidentAlerts,
				playCombatFx: e => CombatFxEvents.Add(e),
				playWeatherLightningFx: e => LightningFxEvents.Add(e),
				presentActorMotion: e => MotionEvents.Add(e),
				handlePlayerDeath: reason => DeathReasons.Add(reason),
				setCurrentTarget: (actor, _) => CurrentTargets.Add(actor),
				closeDialogPanel: () => CloseDialogCalls++,
				closeTradePanel: () => CloseTradeCalls++,
				ensureTradeUi: () =>
				{
					EnsureTradeUiCalled = true;
					throw new InvalidOperationException("Trade UI should not be opened in this test.");
				},
				ensureDialogUi: () =>
				{
					EnsureDialogUiCalled = true;
					throw new InvalidOperationException("Dialog UI should not be opened in this test.");
				},
				onPlayerRestCompleted: () => RestCompletedCalls++);
		}

		public GameState State { get; }
		public Actor Player { get; }
		public Actor Enemy { get; }
		public LogModule Log { get; }
		public IncidentAlertModule IncidentAlerts { get; }
		public GameEventPresentationRouter Router { get; }
		public List<GameEvent> CombatFxEvents { get; } = [];
		public List<GameEvent> LightningFxEvents { get; } = [];
		public List<GameEvent> MotionEvents { get; } = [];
		public List<string> DeathReasons { get; } = [];
		public List<Actor> CurrentTargets { get; } = [];
		public bool EnsureTradeUiCalled { get; private set; }
		public bool EnsureDialogUiCalled { get; private set; }
		public int CloseDialogCalls { get; private set; }
		public int CloseTradeCalls { get; private set; }
		public int RestCompletedCalls { get; private set; }

		public void AddActor(Actor actor)
		{
			ActorModule.Add(State, actor);
		}

		public void RegisterPlayerAnimatable(IAnimatable animatable)
		{
			ResAccess.RegisterAnimatable(State.PlayerId, animatable);
		}

		public void Dispose()
		{
			ResAccess.Reset();
		}
	}

	private sealed class FakeAnimatable : IAnimatable
	{
		public Node2D Node { get; } = (Node2D)RuntimeHelpers.GetUninitializedObject(typeof(Node2D));
		public bool Ready => true;
		public List<(string Name, bool Loop)> PlayCalls { get; } = [];
		public List<(string Name, string ThenAnim)> PlayOneShotCalls { get; } = [];

		public void Play(string animName, bool loop = true)
		{
			PlayCalls.Add((animName, loop));
		}

		public void PlayOneShot(string animName, string thenAnim = "Idle")
		{
			PlayOneShotCalls.Add((animName, thenAnim));
		}

		public void SetFacing(int dx)
		{
		}

		public void Stop()
		{
		}
	}

	private static Actor CreateActor(string id, string faction, int x, int y) => new()
	{
		Id = id,
		DisplayName = id,
		Faction = faction,
		X = x,
		Y = y,
		Z = 0,
		Limbs =
		[
			new Limb
			{
				Id = $"{id}_head",
				Name = "Head",
				MaxDurability = 10,
				Durability = 10,
				Material = "flesh",
				BodyPart = BodyParts.Head,
				Capacities = new Dictionary<string, float>
				{
					[Caps.Consciousness] = 1f,
					[Caps.Manipulation] = 1f,
					[Caps.Moving] = 1f,
					[Caps.Sight] = 1f,
				},
				Tags = new Dictionary<string, int>(),
			},
		],
	};
}
