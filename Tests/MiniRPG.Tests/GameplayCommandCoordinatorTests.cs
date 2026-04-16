using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;
using MiniRPG.Core.Multiplayer;
using MiniRPG.Core.World;
using MiniRPG.Module;
using Xunit;

namespace MiniRPG.Tests;

public sealed class GameplayCommandCoordinatorTests
{
	public GameplayCommandCoordinatorTests()
	{
		SkillCastingTestHelper.EnsureGameDataLoaded();
	}

	[Fact]
	public void DoMove_PredictedMoveConsumed_DoesNotSubmitTimelineAction()
	{
		var harness = new Harness(CreateActor("player", Factions.Player, 1, 1));

		harness.Coordinator.DoMove(1, 0, (_, _) => true);

		Assert.Empty(harness.PlayerActions);
	}

	[Fact]
	public void StartDig_NoPlayer_DoesNothing()
	{
		var harness = new Harness(player: null);

		harness.Coordinator.StartDig();

		Assert.Equal(InputFocus.Action, harness.Input.Focus);
		Assert.Null(harness.Log.LastLine);
	}

	[Fact]
	public void StartDig_NoCellSkills_LogsAndDoesNotEnterDirectionMode()
	{
		var player = CreateActor("player", Factions.Player, 1, 1, manipulation: 0f);
		var harness = new Harness(player);

		harness.Coordinator.StartDig();

		Assert.Equal(LocalizationService.T("dig.no_skills"), harness.Log.LastLine);
		Assert.Equal(InputFocus.Action, harness.Input.Focus);
	}

	[Fact]
	public void StartDig_NoBreakableTargets_LogsAndDoesNotEnterDirectionMode()
	{
		var harness = new Harness(CreateActor("player", Factions.Player, 1, 1));

		harness.Coordinator.StartDig();

		Assert.Equal(LocalizationService.T("dig.no_targets"), harness.Log.LastLine);
		Assert.Equal(InputFocus.Action, harness.Input.Focus);
	}

	[Fact]
	public void HandleDigDirection_InvalidDirection_DoesNotSubmitAction()
	{
		var harness = new Harness(CreateActor("player", Factions.Player, 1, 1));

		harness.Coordinator.HandleDigDirection("bad");

		Assert.Empty(harness.PlayerActions);
		Assert.Null(harness.Log.LastLine);
	}

	[Fact]
	public void HandleDigDirection_UnbreakableTarget_DoesNotSubmitAction()
	{
		var harness = new Harness(CreateActor("player", Factions.Player, 1, 1));
		harness.State.World!.SetTerrain(1, 0, 0, "floor");
		harness.State.World.SetHardness(1, 0, 0, 0);

		harness.Coordinator.HandleDigDirection("n");

		Assert.Empty(harness.PlayerActions);
		Assert.Equal(LocalizationService.T("dig.invalid_target"), harness.Log.LastLine);
	}

	[Fact]
	public void DoInteract_NoTargetsAndNoGroundItems_LogsNoNearbyInteraction()
	{
		var harness = new Harness(CreateActor("player", Factions.Player, 1, 1));

		harness.Coordinator.DoInteract();

		Assert.Equal(LocalizationService.T("ui.interaction.none_nearby"), harness.Log.LastLine);
		Assert.Empty(harness.ClientCommands);
		Assert.Equal(0, harness.DoGroundInteractSelectedCalls);
	}

	[Fact]
	public void DoInteract_MultipleTargets_InvalidSelection_LogsInvalidSelection()
	{
		var harness = new Harness(CreateActor("player", Factions.Player, 1, 1));
		harness.AddActor(CreateActor("ally_a", Factions.Friendly, 1, 0));
		harness.AddActor(CreateActor("ally_b", Factions.Friendly, 2, 1));

		harness.Coordinator.DoInteract();
		InvokeSelection(harness.Input, 9);

		Assert.Equal(InputFocus.Action, harness.Input.Focus);
		Assert.Equal(LocalizationService.T("ui.selection.invalid"), harness.Log.LastLine);
		Assert.Empty(harness.ClientCommands);
	}

	[Fact]
	public void DoInteract_SingleTarget_CancelSelection_LogsSelectionCanceled()
	{
		var harness = new Harness(CreateActor("player", Factions.Player, 1, 1));
		harness.AddActor(CreateActor("ally", Factions.Friendly, 1, 0));

		harness.Coordinator.DoInteract();
		CancelSelection(harness.Input);

		Assert.Equal(InputFocus.Action, harness.Input.Focus);
		Assert.Equal(LocalizationService.T("ui.selection.canceled"), harness.Log.LastLine);
		Assert.Empty(harness.ClientCommands);
	}

	private static void InvokeSelection(InputModule input, int value)
	{
		var callbackField = typeof(InputModule).GetField("_selectionCallback", BindingFlags.Instance | BindingFlags.NonPublic)
			?? throw new InvalidOperationException("InputModule selection callback field was not found.");
		var cancelField = typeof(InputModule).GetField("_onSelectionCancel", BindingFlags.Instance | BindingFlags.NonPublic)
			?? throw new InvalidOperationException("InputModule selection cancel field was not found.");
		var callback = (Action<int>?)callbackField.GetValue(input)
			?? throw new InvalidOperationException("InputModule selection callback was not set.");

		callbackField.SetValue(input, null);
		cancelField.SetValue(input, null);
		input.EnterActionMode();
		callback(value);
	}

	private static void CancelSelection(InputModule input)
	{
		var cancelField = typeof(InputModule).GetField("_onSelectionCancel", BindingFlags.Instance | BindingFlags.NonPublic)
			?? throw new InvalidOperationException("InputModule selection cancel field was not found.");
		var cancel = (Action?)cancelField.GetValue(input)
			?? throw new InvalidOperationException("InputModule selection cancel callback was not set.");

		input.CancelSelection();
		cancel();
	}

	private sealed class Harness
	{
		public Harness(Actor? player)
		{
			State = new GameState
			{
				WorldSeed = 12345,
				PlayerId = player?.Id ?? "player",
				PlayerX = player?.X ?? 1,
				PlayerY = player?.Y ?? 1,
				PlayerZ = player?.Z ?? 0,
				Actors = new Dictionary<string, Actor>(),
				World = new WorldMap(12345, new FlatFloorGenerator()),
				GeneratorId = "test",
				ViewModeId = "single_layer",
			};

			if (player != null)
			{
				ActorModule.Add(State, player);
				PartyModule.Initialize(State);
			}

			Input = new InputModule(new InputBindingService(Path.Combine(
				Path.GetTempPath(),
				$"mini-rpg-keybindings-{Guid.NewGuid():N}.json")));
			Log = new LogModule(_ => { }, () => { }, _ => { });
			Coordinator = new GameplayCommandCoordinator(
				State,
				Input,
				Log,
				isMultiplayerSession: () => false,
				submitClientCommand: command => ClientCommands.Add(command),
				submitPlayerAction: action => PlayerActions.Add(action),
				dispatch: _ => { },
				flushMap: () => FlushMapCalls++,
				invalidateGround: () => InvalidateGroundCalls++,
				refreshGround: () => RefreshGroundCalls++,
				doGroundInteractSelected: () => DoGroundInteractSelectedCalls++);
		}

		public GameState State { get; }
		public InputModule Input { get; }
		public LogModule Log { get; }
		public GameplayCommandCoordinator Coordinator { get; }
		public List<ClientCommand> ClientCommands { get; } = [];
		public List<TimelinePlayerAction> PlayerActions { get; } = [];
		public int FlushMapCalls { get; private set; }
		public int InvalidateGroundCalls { get; private set; }
		public int RefreshGroundCalls { get; private set; }
		public int DoGroundInteractSelectedCalls { get; private set; }

		public void AddActor(Actor actor)
		{
			ActorModule.Add(State, actor);
		}
	}

	private static Actor CreateActor(
		string id,
		string faction,
		int x,
		int y,
		float consciousness = 1f,
		float manipulation = 1f,
		float moving = 1f,
		float sight = 1f) => new()
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
					[Caps.Consciousness] = consciousness,
					[Caps.Manipulation] = manipulation,
					[Caps.Moving] = moving,
					[Caps.Sight] = sight,
				},
				Tags = new Dictionary<string, int>(),
			},
		],
	};

	private sealed class FlatFloorGenerator : IMapGenerator
	{
		public string Id => "flat_floor";
		public string Name => "Flat Floor";

		public void GenerateChunk(ChunkData chunk, int worldSeed)
		{
			var terrainId = chunk.Coord.Cz == 0
				? TerrainRegistry.GetId("floor")
				: chunk.Coord.Cz > 0
					? TerrainRegistry.GetId("wall_stone")
					: TerrainRegistry.GetId("air");
			chunk.Fill(terrainId);
		}

		public void PopulateChunk(ChunkData chunk, int worldSeed)
		{
		}
	}
}
