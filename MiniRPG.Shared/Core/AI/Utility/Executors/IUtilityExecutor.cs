using MiniRPG.Core.Combat;

namespace MiniRPG.Core.AI.Utility;

public interface IUtilityExecutor
{
	ActionExecutionResult Execute(GameState state, Actor actor, Perception perception, UtilityEvalResult eval);
}
