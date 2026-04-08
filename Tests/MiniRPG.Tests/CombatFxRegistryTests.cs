using System.Collections.Generic;
using System.IO;
using System.Linq;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;
using MiniRPG.Module.Render;
using Xunit;

namespace MiniRPG.Tests;

public sealed class CombatFxRegistryTests
{
	[Fact]
	public void LoadConfig_FromProjectData_ParsesCoreMappings()
	{
		var json = File.ReadAllText(Path.Combine(ResolveRepoRoot(), "Data", "combat_fx.json"));

		var registry = CombatFxRegistry.FromJson(json);
		var commands = registry.Resolve(new GameEvent("combat_attack")
		{
			EffectType = "melee_attack",
			SourceX = 4,
			SourceY = 5,
			TargetX = 5,
			TargetY = 5,
			Damage = 7,
		}, sourceVisible: true, targetVisible: true);

		Assert.Equal(3, commands.Count);
		Assert.Contains(commands, command => command.Kind == CombatFxCommandKind.Sprite && command.ResourceId == "fx_slash_arc");
		Assert.Contains(commands, command => command.Kind == CombatFxCommandKind.Sprite && command.ResourceId == "fx_hit_blunt");
		Assert.Contains(commands, command => command.Kind == CombatFxCommandKind.Text && command.Text == "7");
	}

	[Fact]
	public void Resolve_UsesDefaultAttackFallback_WhenEffectTypeIsUnknown()
	{
		var registry = CombatFxRegistry.FromJson("""
		{
		  "defaultAttack": "default_attack",
		  "effects": {
		    "default_attack": {
		      "hit": {
		        "kind": "sprite",
		        "anchor": "target",
		        "resourceId": "fx_hit_blunt",
		        "duration": 0.18,
		        "scale": 0.3
		      },
		      "text": {
		        "mode": "damage",
		        "anchor": "target",
		        "duration": 0.5,
		        "risePixels": 50
		      }
		    }
		  }
		}
		""");

		var commands = registry.Resolve(new GameEvent("combat_attack")
		{
			EffectType = "unknown_attack",
			SourceX = 1,
			SourceY = 2,
			TargetX = 2,
			TargetY = 2,
			Damage = 4,
		}, sourceVisible: true, targetVisible: true);

		Assert.Equal(
			[CombatFxCommandKind.Sprite, CombatFxCommandKind.Text],
			commands.Select(static command => command.Kind).ToArray());
		Assert.Equal("fx_hit_blunt", commands[0].ResourceId);
		Assert.Equal("4", commands[1].Text);
	}

	[Fact]
	public void Resolve_RangedAttackFallsBackToHitAndText_WhenOnlyTargetIsVisible()
	{
		var registry = CombatFxRegistry.FromJson(File.ReadAllText(Path.Combine(ResolveRepoRoot(), "Data", "combat_fx.json")));

		var commands = registry.Resolve(new GameEvent("combat_attack")
		{
			EffectType = "ranged_attack",
			SourceX = 1,
			SourceY = 1,
			TargetX = 8,
			TargetY = 3,
			Damage = 11,
		}, sourceVisible: false, targetVisible: true);

		Assert.DoesNotContain(commands, static command => command.Kind == CombatFxCommandKind.Projectile);
		Assert.Contains(commands, static command => command.Kind == CombatFxCommandKind.Sprite);
		Assert.Contains(commands, static command => command.Kind == CombatFxCommandKind.Text && command.Text == "11");
	}

	[Fact]
	public void Resolve_RangedAttackUsesProjectile_WhenBothEndpointsVisible()
	{
		var registry = CombatFxRegistry.FromJson(File.ReadAllText(Path.Combine(ResolveRepoRoot(), "Data", "combat_fx.json")));

		var commands = registry.Resolve(new GameEvent("combat_attack")
		{
			EffectType = "ranged_attack",
			SourceX = 1,
			SourceY = 1,
			TargetX = 8,
			TargetY = 3,
			Damage = 11,
		}, sourceVisible: true, targetVisible: true);

		Assert.Contains(commands, static command => command.Kind == CombatFxCommandKind.Projectile && command.ResourceId == "fx_arrow_projectile");
		Assert.Contains(commands, static command => command.Kind == CombatFxCommandKind.Sprite && command.ResourceId == "fx_hit_blunt");
		Assert.Contains(commands, static command => command.Kind == CombatFxCommandKind.Text && command.Text == "11");
	}

	[Fact]
	public void Resolve_BlockProducesBuffFlashAndLocalizedText()
	{
		var registry = CombatFxRegistry.FromJson(File.ReadAllText(Path.Combine(ResolveRepoRoot(), "Data", "combat_fx.json")));
		LocalizationService.Initialize();

		var commands = registry.Resolve(new GameEvent("combat_block")
		{
			ActionName = "格挡",
			EffectType = "block",
			SourceX = 3,
			SourceY = 7,
		}, sourceVisible: true, targetVisible: false);

		Assert.Equal(2, commands.Count);
		Assert.Contains(commands, static command => command.Kind == CombatFxCommandKind.Sprite && command.ResourceId == "fx_buff_flash");
		Assert.Contains(commands, static command => command.Kind == CombatFxCommandKind.Text && command.Text == "格挡");
	}

	private static string ResolveRepoRoot()
	{
		var current = new DirectoryInfo(AppContext.BaseDirectory);
		while (current != null)
		{
			if (File.Exists(Path.Combine(current.FullName, "project.godot")))
				return current.FullName;

			current = current.Parent;
		}

		throw new DirectoryNotFoundException("Failed to locate repository root.");
	}
}
