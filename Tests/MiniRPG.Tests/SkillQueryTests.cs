using System.Linq;
using MiniRPG.Core.Data;
using Xunit;

namespace MiniRPG.Tests;

public sealed class SkillQueryTests
{
	[Fact]
	public void GetAttackSkills_IncludesRangedAttack()
	{
		var (_, player, _) = SkillCastingTestHelper.CreateCombatState();

		var attackSkills = SkillQuery.GetAttackSkills(player);

		Assert.Contains(attackSkills, skill => skill.Id == "bow_shoot");
		Assert.Contains(attackSkills, skill => skill.EffectType == "ranged_attack");
	}

	[Fact]
	public void SocialSkills_DoNotEnterUnifiedCastFlow()
	{
		SkillCastingTestHelper.EnsureGameDataLoaded();

		var socialSkillIds = new[] { "talk", "trade", "tame" };
		foreach (var skillId in socialSkillIds)
		{
			var skill = Assert.Single(InteractionDefs.All, def => def.Id == skillId);
			Assert.True(SkillQuery.IsSocialSkill(skill));
			Assert.False(SkillQuery.IsUnifiedCastSkill(skill));
		}
	}

	[Fact]
	public void GetCellSkills_IncludesFireControlSkills()
	{
		var (_, player, _) = SkillCastingTestHelper.CreateCombatState();

		var cellSkills = SkillQuery.GetCellSkills(player);

		Assert.Contains(cellSkills, skill => skill.Id == "light_fire");
		Assert.Contains(cellSkills, skill => skill.Id == "extinguish_fire");
		Assert.True(SkillQuery.IsUnifiedCastSkill(Assert.Single(cellSkills, skill => skill.Id == "extinguish_fire")));
	}
}
