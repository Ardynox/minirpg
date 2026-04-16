using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using MiniRPG.Core.Data;
using MiniRPG.Module;
using MiniRPG.Module.Render;
using Xunit;

namespace MiniRPG.Tests;

public sealed class AutoNavigationCoordinatorTests
{
	public AutoNavigationCoordinatorTests()
	{
		SkillCastingTestHelper.EnsureGameDataLoaded();
	}

	[Fact]
	public void ActorMotionTiming_ManualPlayerTier_UsesSlowDuration()
	{
		Assert.Equal(ActorMotionTimingTier.PlayerSlow, ActorMotionTiming.ResolveManualPlayerTier());
		Assert.Equal(0.16f, ActorMotionTiming.ResolveDurationSeconds(ActorMotionTimingTier.PlayerSlow), 3);
		Assert.Equal(0.08f, ActorMotionTiming.ResolveDurationSeconds(ActorMotionTimingTier.NpcFast), 3);
	}

	[Fact]
	public void AutoNavigationMotionTiming_ShortPath_UsesPlayerSlowTier()
	{
		Assert.Equal(
			ActorMotionTimingTier.PlayerSlow,
			AutoNavigationCoordinator.ResolveAutoNavigationMotionTimingTier(pathLength: 3));
	}

	[Fact]
	public void Confirm_LongPath_KeepsPlayerFastTierForWholeRun()
	{
		var harness = new Harness();
		var target = new Vector3I(5, 1, 0);

		Assert.True(harness.Coordinator.StartOrRetarget(target));
		Assert.True(harness.Coordinator.StartOrRetarget(target));
		Assert.True(harness.Coordinator.IsActive);
		Assert.Equal(ActorMotionTimingTier.PlayerFast, harness.Coordinator.CurrentPlayerMotionTimingTier);

		harness.Player.X = 4;
		harness.State.PlayerX = 4;
		Assert.Equal(ActorMotionTimingTier.PlayerFast, harness.Coordinator.CurrentPlayerMotionTimingTier);
	}

	private sealed class Harness
	{
		public Harness()
		{
			(State, Player, _) = SkillCastingTestHelper.CreateCombatState(enemyX: 20, enemyY: 20);
			PartyModule.Initialize(State);
			Log = new LogModule(_ => { }, () => { }, _ => { });
			var session = (GameSessionModule)RuntimeHelpers.GetUninitializedObject(typeof(GameSessionModule));
			SetAutoProperty(session, "GameStarted", true);
			var menu = (MenuModule)RuntimeHelpers.GetUninitializedObject(typeof(MenuModule));
			SetAutoProperty(menu, "CurrentScreen", MenuModule.Screen.InGame);

			Coordinator = new AutoNavigationCoordinator(
				State,
				session,
				Log,
				() => menu,
				playerDead: () => false,
				mapEditorActive: () => false,
				layoutEditActive: () => false,
				renderReady: () => true,
				isMultiplayerSession: () => false,
				watchModeEnabled: () => false,
				skillTargetCursorActive: () => false,
				skillTargetWorldCell: () => null,
				doMove: (_, _) => { },
				submitPlayerAction: _ => { },
				flushMap: () => { },
				syncSettingsUiState: () => { },
				getMultiplayerActivityVersion: () => 0L,
				getMultiplayerPendingPredictionCount: () => 0);
		}

		public GameState State { get; }
		public Actor Player { get; }
		public LogModule Log { get; }
		public AutoNavigationCoordinator Coordinator { get; }

		private static void SetAutoProperty<T>(object target, string propertyName, T value)
		{
			var field = target.GetType().GetField($"<{propertyName}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.NotNull(field);
			field!.SetValue(target, value);
		}
	}
}
