using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Data;
using Xunit;

namespace MiniRPG.Tests;

public sealed class CombatModuleFxPayloadTests
{
	[Fact]
	public void Attack_PopulatesCombatFxPayloadFields()
	{
		var state = new GameState { Turn = 4, WorldSeed = 9 };
		var attacker = CreateActor("attacker", Factions.Player, 3, 4);
		var target = CreateActor("target", Factions.Hostile, 5, 4);
		var action = new InteractionDef
		{
			Id = "bow_shoot",
			Name = "Shoot",
			EffectType = "ranged_attack",
			DamageType = DamageTypes.Sharp,
			Power = 5,
		};

		var events = CombatModule.Attack(state, attacker, target, action, target.Limbs[0]);
		var attackEvent = Assert.Single(events, static e => e.Type == "combat_attack");

		Assert.Equal("ranged_attack", attackEvent.EffectType);
		Assert.Equal(DamageTypes.Sharp, attackEvent.DamageType);
		Assert.Equal(attacker.X, attackEvent.SourceX);
		Assert.Equal(attacker.Y, attackEvent.SourceY);
		Assert.Equal(target.X, attackEvent.TargetX);
		Assert.Equal(target.Y, attackEvent.TargetY);
	}

	[Fact]
	public void Block_PopulatesCombatFxPayloadFields()
	{
		var state = new GameState { Turn = 1, WorldSeed = 3 };
		var attacker = CreateActor("attacker", Factions.Player, 3, 4);
		var target = CreateActor("target", Factions.Hostile, 5, 4);
		var block = new InteractionDef
		{
			Id = "block",
			Name = "Block",
			EffectType = "block",
			Power = 1,
		};

		var events = CombatModule.Attack(state, attacker, target, block, target.Limbs[0]);
		var blockEvent = Assert.Single(events);

		Assert.Equal("combat_block", blockEvent.Type);
		Assert.Equal("block", blockEvent.EffectType);
		Assert.Equal(attacker.X, blockEvent.SourceX);
		Assert.Equal(attacker.Y, blockEvent.SourceY);
	}

	private static Actor CreateActor(string id, string faction, int x, int y)
	{
		return new Actor
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
					Id = $"{id}_core",
					Name = "Core",
					MaxDurability = 10,
					Durability = 10,
					BodyPart = "torso",
					Material = "flesh",
				}
			]
		};
	}
}
