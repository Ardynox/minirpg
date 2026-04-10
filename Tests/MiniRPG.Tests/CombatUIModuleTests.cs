using System;
using System.Collections.Generic;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;
using MiniRPG.Core.Multiplayer;
using MiniRPG.Module;
using Xunit;

namespace MiniRPG.Tests;

public sealed class CombatUIModuleTests
{
	[Fact]
	public void HandleActorKilled_UsesEventPayloadAndRewardsPlayerGold()
	{
		LocalizationService.Initialize();
		LocalizationService.SetLocale("en", notify: false);

		var (state, player, _) = SkillCastingTestHelper.CreateCombatState(enemyX: 2, enemyY: 1);
		var ui = new FakeGameUi(state);
		var module = new CombatUIModule(ui);

		module.HandleActorKilled(new GameEvent("actor_killed")
		{
			InitiatorId = player.Id,
			TargetId = "enemy",
			TargetActorName = "Unknown hostile",
			Damage = 9,
		});

		Assert.Equal(9, player.Gold);
		Assert.Contains(ui.Logs, line => line.Contains("Unknown hostile", StringComparison.Ordinal));
		Assert.Contains(ui.Logs, line => line.Contains("9", StringComparison.Ordinal));
	}

	private sealed class FakeGameUi(GameState state) : IGameUI
	{
		public List<string> Logs { get; } = [];

		public void AddLog(string msg) => Logs.Add(msg);
		public void EnterSelection(Action<int> callback) { }
		public void CancelSelection() { }
		public void FlushMap() { }
		public void Dispatch(List<GameEvent> events) { }
		public void SubmitPlayerAction(TimelinePlayerAction action) { }
		public bool TrySubmitClientCommand(ClientCommand command) => false;
		public bool TryHandleItemRightClick(Item item) => false;
		public GameState State => state;
		public bool PlayerDead { get; set; }
		public void HandlePlayerDeath(string reason) { }
	}
}
