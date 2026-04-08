using System;
using System.Collections.Generic;
using MiniRPG.Core.Data;
using MiniRPG.Core.Dialog;
using Xunit;

namespace MiniRPG.Tests;

public sealed class DialogRuleEngineTests
{
	private static readonly Random Rng = new(42);

	private static DialogContext MakeCtx(
		HashSet<string>? boolTags = null,
		Dictionary<string, float>? numTags = null)
	{
		TestSupport.EnsureGameplayDataLoaded();
		var player = PresetDB.SpawnActor("player", "p");
		var npc = PresetDB.SpawnActor("player", "n");
		return new DialogContext
		{
			Player = player,
			Npc = npc,
			BoolTags = boolTags ?? [],
			NumTags = numTags ?? new(),
		};
	}

	// ── Matches ──────────────────────────────────────────

	[Fact]
	public void Matches_NullCondition_ReturnsTrue()
	{
		var ctx = MakeCtx();
		Assert.True(DialogRuleEngine.Matches(ctx, null));
	}

	[Fact]
	public void Matches_EmptyCondition_ReturnsTrue()
	{
		var ctx = MakeCtx();
		Assert.True(DialogRuleEngine.Matches(ctx, new DialogCondition()));
	}

	[Fact]
	public void Matches_RequiresTags_AllPresent_ReturnsTrue()
	{
		var ctx = MakeCtx(boolTags: ["prof:merchant", "race:human"]);
		var cond = new DialogCondition { RequiresTags = ["prof:merchant", "race:human"] };
		Assert.True(DialogRuleEngine.Matches(ctx, cond));
	}

	[Fact]
	public void Matches_RequiresTags_Missing_ReturnsFalse()
	{
		var ctx = MakeCtx(boolTags: ["prof:merchant"]);
		var cond = new DialogCondition { RequiresTags = ["prof:merchant", "race:elf"] };
		Assert.False(DialogRuleEngine.Matches(ctx, cond));
	}

	[Fact]
	public void Matches_ForbidsTags_Present_ReturnsFalse()
	{
		var ctx = MakeCtx(boolTags: ["npc_angry"]);
		var cond = new DialogCondition { ForbidsTags = ["npc_angry"] };
		Assert.False(DialogRuleEngine.Matches(ctx, cond));
	}

	[Fact]
	public void Matches_ForbidsTags_Absent_ReturnsTrue()
	{
		var ctx = MakeCtx(boolTags: ["npc_happy"]);
		var cond = new DialogCondition { ForbidsTags = ["npc_angry"] };
		Assert.True(DialogRuleEngine.Matches(ctx, cond));
	}

	[Fact]
	public void Matches_MinTags_Met_ReturnsTrue()
	{
		var ctx = MakeCtx(numTags: new() { ["affinity"] = 50 });
		var cond = new DialogCondition { MinTags = new() { ["affinity"] = 30 } };
		Assert.True(DialogRuleEngine.Matches(ctx, cond));
	}

	[Fact]
	public void Matches_MinTags_NotMet_ReturnsFalse()
	{
		var ctx = MakeCtx(numTags: new() { ["affinity"] = 10 });
		var cond = new DialogCondition { MinTags = new() { ["affinity"] = 30 } };
		Assert.False(DialogRuleEngine.Matches(ctx, cond));
	}

	[Fact]
	public void Matches_MinTags_MissingKey_ReturnsFalse()
	{
		var ctx = MakeCtx();
		var cond = new DialogCondition { MinTags = new() { ["affinity"] = 30 } };
		Assert.False(DialogRuleEngine.Matches(ctx, cond));
	}

	[Fact]
	public void Matches_MaxTags_Met_ReturnsTrue()
	{
		var ctx = MakeCtx(numTags: new() { ["mood"] = 0.2f });
		var cond = new DialogCondition { MaxTags = new() { ["mood"] = 0.5f } };
		Assert.True(DialogRuleEngine.Matches(ctx, cond));
	}

	[Fact]
	public void Matches_MaxTags_Exceeded_ReturnsFalse()
	{
		var ctx = MakeCtx(numTags: new() { ["mood"] = 0.8f });
		var cond = new DialogCondition { MaxTags = new() { ["mood"] = 0.5f } };
		Assert.False(DialogRuleEngine.Matches(ctx, cond));
	}

	[Fact]
	public void Matches_MaxTags_MissingKey_ReturnsFalse()
	{
		var ctx = MakeCtx();
		var cond = new DialogCondition { MaxTags = new() { ["mood"] = 0.5f } };
		Assert.False(DialogRuleEngine.Matches(ctx, cond));
	}

	[Fact]
	public void Matches_CombinedConditions_AllMet()
	{
		var ctx = MakeCtx(
			boolTags: ["prof:merchant", "is_friend"],
			numTags: new() { ["affinity"] = 60, ["mood"] = 0.3f });
		var cond = new DialogCondition
		{
			RequiresTags = ["prof:merchant"],
			ForbidsTags = ["npc_angry"],
			MinTags = new() { ["affinity"] = 50 },
			MaxTags = new() { ["mood"] = 0.5f },
		};
		Assert.True(DialogRuleEngine.Matches(ctx, cond));
	}

	[Fact]
	public void Matches_CombinedConditions_OneFails()
	{
		var ctx = MakeCtx(
			boolTags: ["prof:merchant", "npc_angry"],
			numTags: new() { ["affinity"] = 60 });
		var cond = new DialogCondition
		{
			RequiresTags = ["prof:merchant"],
			ForbidsTags = ["npc_angry"],
		};
		Assert.False(DialogRuleEngine.Matches(ctx, cond));
	}

	// ── SelectEntry ──────────────────────────────────────

	[Fact]
	public void SelectEntry_NoMatch_ReturnsNull()
	{
		var ctx = MakeCtx();
		var candidates = new List<DialogEntry>
		{
			new() { Id = "a", Condition = new() { RequiresTags = ["nonexistent"] } },
		};
		Assert.Null(DialogRuleEngine.SelectEntry(ctx, candidates, Rng));
	}

	[Fact]
	public void SelectEntry_SingleMatch_ReturnsThat()
	{
		var ctx = MakeCtx(boolTags: ["prof:merchant"]);
		var entry = new DialogEntry
		{
			Id = "greet_merchant",
			Condition = new() { RequiresTags = ["prof:merchant"] },
			Priority = 1,
		};
		var result = DialogRuleEngine.SelectEntry(ctx, [entry], Rng);
		Assert.NotNull(result);
		Assert.Equal("greet_merchant", result!.Id);
	}

	[Fact]
	public void SelectEntry_HigherPriority_Wins()
	{
		var ctx = MakeCtx(boolTags: ["prof:merchant"]);
		var low = new DialogEntry { Id = "low", Priority = 1 };
		var high = new DialogEntry { Id = "high", Priority = 10 };
		var result = DialogRuleEngine.SelectEntry(ctx, [low, high], Rng);
		Assert.Equal("high", result!.Id);
	}

	[Fact]
	public void SelectEntry_SamePriority_RandomTieBreak()
	{
		var ctx = MakeCtx();
		var a = new DialogEntry { Id = "a", Priority = 5 };
		var b = new DialogEntry { Id = "b", Priority = 5 };
		var c = new DialogEntry { Id = "c", Priority = 5 };

		var seen = new HashSet<string>();
		for (int i = 0; i < 100; i++)
		{
			var rng = new Random(i);
			var result = DialogRuleEngine.SelectEntry(ctx, [a, b, c], rng);
			seen.Add(result!.Id);
		}
		// With 100 random seeds, we should see at least 2 different entries
		Assert.True(seen.Count >= 2, $"Only saw {string.Join(",", seen)}");
	}

	[Fact]
	public void SelectEntry_EmptyCandidates_ReturnsNull()
	{
		var ctx = MakeCtx();
		Assert.Null(DialogRuleEngine.SelectEntry(ctx, [], Rng));
	}

	// ── FilterOptions ────────────────────────────────────

	[Fact]
	public void FilterOptions_KeepsMatchingOptions()
	{
		var ctx = MakeCtx(boolTags: ["is_friend"]);
		var options = new List<DialogOption>
		{
			new() { Text = "Friend option", Condition = new() { RequiresTags = ["is_friend"] } },
			new() { Text = "Enemy option", Condition = new() { RequiresTags = ["is_enemy"] } },
			new() { Text = "Always", Condition = null },
		};
		var filtered = DialogRuleEngine.FilterOptions(ctx, options);
		Assert.Equal(2, filtered.Count);
		Assert.Contains(filtered, o => o.Text == "Friend option");
		Assert.Contains(filtered, o => o.Text == "Always");
	}

	[Fact]
	public void FilterOptions_EmptyList_ReturnsEmpty()
	{
		var ctx = MakeCtx();
		Assert.Empty(DialogRuleEngine.FilterOptions(ctx, []));
	}

	[Fact]
	public void FilterOptions_AllFiltered_ReturnsEmpty()
	{
		var ctx = MakeCtx();
		var options = new List<DialogOption>
		{
			new() { Text = "A", Condition = new() { RequiresTags = ["x"] } },
			new() { Text = "B", Condition = new() { RequiresTags = ["y"] } },
		};
		Assert.Empty(DialogRuleEngine.FilterOptions(ctx, options));
	}
}
