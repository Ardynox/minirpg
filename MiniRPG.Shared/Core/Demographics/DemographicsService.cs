using MiniRPG.Core.Data;

namespace MiniRPG.Core.Demographics;

/// <summary>
/// 薄壳门面：把 <see cref="LifeStageCatalog"/> 的查询接口暴露成"以 Actor 为主"的调用风格。
/// 给 UI / AI / 测试用，避免它们直接 import LifeStageCatalog 拿到 race bounds。
/// </summary>
public static class DemographicsService
{
	public static int GetAgeYears(Actor actor, GameState state) =>
		LifeStageCatalog.AgeYears(state, actor);

	public static LifeStage GetStage(Actor actor, GameState state) =>
		LifeStageCatalog.GetLifeStage(state, actor);

	public static bool IsAdultOrOlder(Actor actor, GameState state) =>
		LifeStageCatalog.IsAdultOrOlder(GetStage(actor, state));

	public static bool IsPregnant(Actor actor) =>
		actor.PregnancyTicksRemaining is > 0;

	public static int? GetGestationTurns() => LifeStageCatalog.Conception.GestationTurns;
}
