using System;
using System.Collections.Generic;

namespace MiniRPG.Core.AI.Utility;

public static class ExecutorRegistry
{
	private static readonly Dictionary<string, IUtilityExecutor> _executors = new(StringComparer.Ordinal);
	private static bool _initialized;

	public static void EnsureInitialized()
	{
		if (_initialized) return;
		Initialize();
	}

	public static void Initialize()
	{
		_executors.Clear();

		Register("self_extinguish", new SelfExtinguishExecutor());
		Register("flee_fire", new FleeFireExecutor());
		Register("extinguish_nearby_fire", new ExtinguishNearbyFireExecutor());
		Register("equip_warm_clothing", new EquipWarmClothingExecutor());
		Register("seek_campfire", new SeekCampfireExecutor());
		Register("light_fire", new LightFireExecutor());
		Register("seek_cool_area", new SeekCoolAreaExecutor());

		Register("attack_enemy", new AttackEnemyExecutor());
		Register("chase_enemy", new ChaseEnemyExecutor());
		Register("flee_combat", new FleeCombatExecutor());
		Register("investigate_suspicious", new InvestigateSuspiciousExecutor());
		Register("search_area", new SearchAreaExecutor());

		Register("eat_food", new EatFoodExecutor());
		Register("drink_water", new DrinkWaterExecutor());
		Register("rest_sleep", new RestSleepExecutor());
		Register("tend_self", new TendSelfExecutor());
		Register("tend_other", new TendOtherExecutor());

		Register("job_execute", new JobExecuteExecutor());

		Register("rescue_ally_fire", new RescueAllyFireExecutor());
		Register("social_interact", new SocialInteractExecutor());

		Register("follow_leader", new FollowLeaderExecutor());
		Register("return_home", new ReturnHomeExecutor());
		Register("wander", new WanderExecutor());
		Register("idle", new IdleExecutor());

		Register("mental_break_berserk", new MentalBreakBerserkExecutor());
		Register("mental_break_catatonic", new MentalBreakCatatonicExecutor());
		Register("mental_break_binge_eat", new MentalBreakBingeEatExecutor());
		Register("mental_break_flee", new MentalBreakFleeExecutor());

		_initialized = true;
	}

	public static void Register(string id, IUtilityExecutor executor) =>
		_executors[id] = executor;

	public static IUtilityExecutor? Get(string id) =>
		_executors.TryGetValue(id, out var executor) ? executor : null;
}
