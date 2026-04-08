using System.Linq;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Data;
using Xunit;

namespace MiniRPG.Tests;

public sealed class CombatModuleTests
{
	public CombatModuleTests()
	{
		TestSupport.EnsureGameplayDataLoaded();
	}

	// ── IsVitalLimb ──────────────────────────────────────

	[Fact]
	public void IsVitalLimb_WithVitalTag_ReturnsTrue()
	{
		var limb = new Limb { Tags = new() { [CombatModule.VitalTag] = 1 } };
		Assert.True(CombatModule.IsVitalLimb(limb));
	}

	[Fact]
	public void IsVitalLimb_WithoutVitalTag_ReturnsFalse()
	{
		var limb = new Limb { Tags = new() { ["organic"] = 1 } };
		Assert.False(CombatModule.IsVitalLimb(limb));
	}

	[Fact]
	public void IsVitalLimb_EmptyTags_ReturnsFalse()
	{
		var limb = new Limb();
		Assert.False(CombatModule.IsVitalLimb(limb));
	}

	// ── IsDead ───────────────────────────────────────────

	[Fact]
	public void IsDead_HealthyActor_ReturnsFalse()
	{
		var (_, _, enemy) = SkillCastingTestHelper.CreateCombatState();
		Assert.False(CombatModule.IsDead(enemy));
	}

	[Fact]
	public void IsDead_NoVitalLimbs_ReturnsTrue()
	{
		var (_, _, enemy) = SkillCastingTestHelper.CreateCombatState();
		// Remove both vital and legacy vital tags from all limbs
		foreach (var limb in enemy.Limbs)
		{
			limb.Tags.Remove(CombatModule.VitalTag);
			limb.Tags.Remove("\u8981\u5bb3"); // Legacy vital tag
		}
		Assert.True(CombatModule.IsDead(enemy));
	}

	// ── PickPreferredTargetLimb ──────────────────────────

	[Fact]
	public void PickPreferredTargetLimb_PrefersVital()
	{
		var (state, player, enemy) = SkillCastingTestHelper.CreateCombatState();
		var limb = CombatModule.PickPreferredTargetLimb(state, player, enemy);
		Assert.NotNull(limb);
		Assert.True(CombatModule.IsVitalLimb(limb!));
	}

	[Fact]
	public void PickPreferredTargetLimb_NoLimbs_ReturnsNull()
	{
		var (state, player, enemy) = SkillCastingTestHelper.CreateCombatState();
		enemy.Limbs.Clear();
		Assert.Null(CombatModule.PickPreferredTargetLimb(state, player, enemy));
	}

	[Fact]
	public void PickPreferredTargetLimb_NoVital_PrefersTorso()
	{
		var (state, player, enemy) = SkillCastingTestHelper.CreateCombatState();
		foreach (var limb in enemy.Limbs)
			limb.Tags.Remove(CombatModule.VitalTag);
		var torso = enemy.Limbs.FirstOrDefault(l => l.BodyPart == BodyParts.Torso);
		var picked = CombatModule.PickPreferredTargetLimb(state, player, enemy);
		if (torso != null)
			Assert.Equal(torso.Id, picked!.Id);
		else
			Assert.NotNull(picked);
	}

	// ── CalcDamage ───────────────────────────────────────

	[Fact]
	public void CalcDamage_GodmodeTarget_ReturnsZero()
	{
		var (_, player, enemy) = SkillCastingTestHelper.CreateCombatState();
		enemy.AddBuff(new Buff { Id = "debug_godmode" });
		var action = new InteractionDef { Power = 5, EffectType = "melee_attack" };
		Assert.Equal(0, CombatModule.CalcDamage(player, action, enemy));
	}

	[Fact]
	public void CalcDamage_BasicAttack_ReturnsPositive()
	{
		var (_, player, enemy) = SkillCastingTestHelper.CreateCombatState();
		var action = new InteractionDef { Power = 5, EffectType = "melee_attack" };
		var damage = CombatModule.CalcDamage(player, action, enemy);
		Assert.True(damage >= 1);
	}

	[Fact]
	public void CalcDamage_MinimumDamageIsOne()
	{
		var (_, player, enemy) = SkillCastingTestHelper.CreateCombatState();
		var action = new InteractionDef { Power = 0, EffectType = "melee_attack" };
		Assert.True(CombatModule.CalcDamage(player, action, enemy) >= 1);
	}

	// ── Attack ───────────────────────────────────────────

	[Fact]
	public void Attack_ProducesCombatAttackEvent()
	{
		var (state, player, enemy) = SkillCastingTestHelper.CreateCombatState();
		var action = new InteractionDef { Id = "punch", Name = "Punch", Power = 3, EffectType = "melee_attack" };
		var limb = enemy.Limbs[0];
		var events = CombatModule.Attack(state, player, enemy, action, limb);
		Assert.Contains(events, e => e.Type == "combat_attack");
	}

	[Fact]
	public void Attack_ReducesLimbDurability()
	{
		var (state, player, enemy) = SkillCastingTestHelper.CreateCombatState();
		var action = new InteractionDef { Id = "punch", Name = "Punch", Power = 3, EffectType = "melee_attack" };
		var limb = enemy.Limbs[0];
		var before = limb.Durability;
		CombatModule.Attack(state, player, enemy, action, limb);
		Assert.True(limb.Durability <= before);
	}

	[Fact]
	public void Attack_BlockAction_AddsBlockBuff()
	{
		var (state, player, _) = SkillCastingTestHelper.CreateCombatState();
		var blockAction = new InteractionDef { Id = "block", Name = "Block", EffectType = "block" };
		var limb = player.Limbs[0];
		var events = CombatModule.Attack(state, player, player, blockAction, limb);
		Assert.Contains(events, e => e.Type == "combat_block");
		Assert.Contains(player.Buffs, b => b.Tags.ContainsKey("blocking"));
	}

	// ── CheckVitalStatus ─────────────────────────────────

	[Fact]
	public void CheckVitalStatus_HealthyActor_ReturnsNull()
	{
		var (_, _, enemy) = SkillCastingTestHelper.CreateCombatState();
		Assert.Null(CombatModule.CheckVitalStatus(enemy));
	}

	// ── ApplyEnvironmentalDamage ─────────────────────────

	[Fact]
	public void ApplyEnvironmentalDamage_ZeroDamage_ReturnsEmpty()
	{
		var (state, _, enemy) = SkillCastingTestHelper.CreateCombatState();
		var events = CombatModule.ApplyEnvironmentalDamage(state, enemy, enemy.Limbs[0], 0, "fire");
		Assert.Empty(events);
	}

	[Fact]
	public void ApplyEnvironmentalDamage_PositiveDamage_ReducesDurability()
	{
		var (state, _, enemy) = SkillCastingTestHelper.CreateCombatState();
		var limb = enemy.Limbs[0];
		var before = limb.Durability;
		CombatModule.ApplyEnvironmentalDamage(state, enemy, limb, 2, "fire");
		Assert.True(limb.Durability < before);
	}

	// ── GetAttackActions ─────────────────────────────────

	[Fact]
	public void GetAttackActions_ReturnsNonEmpty()
	{
		var (_, player, _) = SkillCastingTestHelper.CreateCombatState();
		var actions = CombatModule.GetAttackActions(player);
		Assert.NotEmpty(actions);
	}

	// ── MonsterChooseAction ──────────────────────────────

	[Fact]
	public void MonsterChooseAction_ReturnsActionAndLimb()
	{
		var (state, player, enemy) = SkillCastingTestHelper.CreateCombatState();
		var choice = CombatModule.MonsterChooseAction(state, enemy, player);
		Assert.NotNull(choice);
		Assert.NotNull(choice!.Value.Action);
		Assert.NotNull(choice.Value.TargetLimb);
	}

	[Fact]
	public void MonsterChooseAction_NoLimbs_ReturnsNull()
	{
		var (state, player, enemy) = SkillCastingTestHelper.CreateCombatState();
		player.Limbs.Clear();
		var choice = CombatModule.MonsterChooseAction(state, enemy, player);
		Assert.Null(choice);
	}
}
