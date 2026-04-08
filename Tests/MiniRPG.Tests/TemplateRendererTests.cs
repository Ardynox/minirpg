using System;
using System.Collections.Generic;
using MiniRPG.Core.Data;
using MiniRPG.Core.Dialog;
using Xunit;

namespace MiniRPG.Tests;

public sealed class TemplateRendererTests
{
	public TemplateRendererTests()
	{
		TestSupport.EnsureGameplayDataLoaded();
	}

	private static DialogContext MakeCtx(
		string playerName = "Hero",
		string npcName = "Merchant",
		int playerGold = 100,
		int npcGold = 500,
		Dictionary<string, float>? numTags = null)
	{
		var player = PresetDB.SpawnActor("player", "p");
		player.DisplayName = playerName;
		player.Gold = playerGold;

		var npc = PresetDB.SpawnActor("player", "n");
		npc.DisplayName = npcName;
		npc.Gold = npcGold;

		return new DialogContext
		{
			Player = player,
			Npc = npc,
			BoolTags = [],
			NumTags = numTags ?? new(),
		};
	}

	// ── Built-in variables ───────────────────────────────

	[Fact]
	public void Render_PlayerName()
	{
		var ctx = MakeCtx(playerName: "Alice");
		var result = TemplateRenderer.Render("Hello {player_name}!", ctx);
		Assert.Equal("Hello Alice!", result);
	}

	[Fact]
	public void Render_NpcName()
	{
		var ctx = MakeCtx(npcName: "Bob");
		var result = TemplateRenderer.Render("{npc_name} says hi", ctx);
		Assert.Equal("Bob says hi", result);
	}

	[Fact]
	public void Render_Gold()
	{
		var ctx = MakeCtx(playerGold: 42);
		var result = TemplateRenderer.Render("You have {gold} gold", ctx);
		Assert.Equal("You have 42 gold", result);
	}

	[Fact]
	public void Render_NpcGold()
	{
		var ctx = MakeCtx(npcGold: 999);
		var result = TemplateRenderer.Render("I have {npc_gold} gold", ctx);
		Assert.Equal("I have 999 gold", result);
	}

	// ── Numeric tags ─────────────────────────────────────

	[Fact]
	public void Render_NumTag_Exists()
	{
		var ctx = MakeCtx(numTags: new() { ["affinity"] = 30 });
		var result = TemplateRenderer.Render("Affinity: {num:affinity}", ctx);
		Assert.Equal("Affinity: 30", result);
	}

	[Fact]
	public void Render_NumTag_Missing_ReturnsZero()
	{
		var ctx = MakeCtx();
		var result = TemplateRenderer.Render("Value: {num:nonexistent}", ctx);
		Assert.Equal("Value: 0", result);
	}

	[Fact]
	public void Render_NumTag_FloatValue()
	{
		var ctx = MakeCtx(numTags: new() { ["mood"] = 0.5f });
		var result = TemplateRenderer.Render("Mood: {num:mood}", ctx);
		Assert.Equal("Mood: 0.5", result);
	}

	// ── Pick from pool ───────────────────────────────────

	[Fact]
	public void Render_Pick_ReturnsSomething()
	{
		var ctx = MakeCtx(numTags: new() { ["p:greedy"] = 0.8f });
		var rng = new Random(42);
		var result = TemplateRenderer.Render("{pick:tone}", ctx, rng);
		Assert.NotEmpty(result);
		Assert.DoesNotContain("{pick:tone}", result);
	}

	[Fact]
	public void Render_Pick_Deterministic_WithSameRng()
	{
		var ctx = MakeCtx(numTags: new() { ["p:wise"] = 0.9f });
		var r1 = TemplateRenderer.Render("{pick:filler}", ctx, new Random(42));
		var r2 = TemplateRenderer.Render("{pick:filler}", ctx, new Random(42));
		Assert.Equal(r1, r2);
	}

	// ── Unknown variable ─────────────────────────────────

	[Fact]
	public void Render_UnknownVariable_PassedThrough()
	{
		var ctx = MakeCtx();
		var result = TemplateRenderer.Render("Hello {unknown_var}!", ctx);
		Assert.Equal("Hello {unknown_var}!", result);
	}

	// ── Empty template ───────────────────────────────────

	[Fact]
	public void Render_EmptyTemplate_ReturnsEmpty()
	{
		var ctx = MakeCtx();
		Assert.Equal("", TemplateRenderer.Render("", ctx));
	}

	// ── No variables ─────────────────────────────────────

	[Fact]
	public void Render_NoVariables_ReturnsOriginal()
	{
		var ctx = MakeCtx();
		Assert.Equal("Plain text", TemplateRenderer.Render("Plain text", ctx));
	}

	// ── Multiple variables ───────────────────────────────

	[Fact]
	public void Render_MultipleVariables()
	{
		var ctx = MakeCtx(playerName: "Alice", npcName: "Bob", playerGold: 50);
		var result = TemplateRenderer.Render("{player_name} meets {npc_name} with {gold} gold", ctx);
		Assert.Equal("Alice meets Bob with 50 gold", result);
	}

	// ── Affinity level ───────────────────────────────────

	[Fact]
	public void Render_AffinityLevel_Stranger()
	{
		var ctx = MakeCtx(numTags: new() { ["affinity"] = 5 });
		var result = TemplateRenderer.Render("{affinity_level}", ctx);
		Assert.NotEmpty(result);
		Assert.DoesNotContain("{affinity_level}", result);
	}

	[Fact]
	public void Render_AffinityLevel_Dearest()
	{
		var ctx = MakeCtx(numTags: new() { ["affinity"] = 90 });
		var result = TemplateRenderer.Render("{affinity_level}", ctx);
		Assert.NotEmpty(result);
	}

	// ── Mood desc ────────────────────────────────────────

	[Fact]
	public void Render_MoodDesc_Calm()
	{
		var ctx = MakeCtx(numTags: new() { ["mood"] = 0.1f });
		var result = TemplateRenderer.Render("{mood_desc}", ctx);
		Assert.NotEmpty(result);
		Assert.DoesNotContain("{mood_desc}", result);
	}

	// ── Floor / kill_count / talk_count ───────────────────

	[Fact]
	public void Render_Floor()
	{
		var ctx = MakeCtx(numTags: new() { ["floor"] = 3 });
		var result = TemplateRenderer.Render("Floor {floor}", ctx);
		Assert.Equal("Floor 3", result);
	}

	[Fact]
	public void Render_KillCount()
	{
		var ctx = MakeCtx(numTags: new() { ["kill_count"] = 10 });
		var result = TemplateRenderer.Render("Kills: {kill_count}", ctx);
		Assert.Equal("Kills: 10", result);
	}

	// ── Self ref / player ref ────────────────────────────

	[Fact]
	public void Render_SelfRef_Default()
	{
		var ctx = MakeCtx();
		var result = TemplateRenderer.Render("{self_ref}", ctx);
		Assert.NotEmpty(result);
		Assert.DoesNotContain("{self_ref}", result);
	}

	[Fact]
	public void Render_PlayerRef_Default()
	{
		var ctx = MakeCtx();
		var result = TemplateRenderer.Render("{player_ref}", ctx);
		Assert.NotEmpty(result);
		Assert.DoesNotContain("{player_ref}", result);
	}

	[Fact]
	public void Render_PlayerRef_OldFriend()
	{
		var ctx = MakeCtx(numTags: new() { ["affinity"] = 70 });
		var result = TemplateRenderer.Render("{player_ref}", ctx);
		Assert.NotEmpty(result);
	}

	// ── Tone / filler ────────────────────────────────────

	[Fact]
	public void Render_Tone()
	{
		var ctx = MakeCtx(numTags: new() { ["p:cautious"] = 0.8f });
		var result = TemplateRenderer.Render("{tone}", ctx, new Random(42));
		Assert.NotEmpty(result);
	}

	[Fact]
	public void Render_Filler()
	{
		var ctx = MakeCtx(numTags: new() { ["p:friendly"] = 0.7f });
		var result = TemplateRenderer.Render("{filler}", ctx, new Random(42));
		Assert.NotEmpty(result);
	}
}
