using MiniRPG.Core.Combat;
using MiniRPG.Core.Job;

namespace MiniRPG.Core.AI.Utility;

public sealed class JobExecuteExecutor : IUtilityExecutor
{
	public ActionExecutionResult Execute(GameState state, Actor actor, Perception perception, UtilityEvalResult eval)
	{
		var ticket = eval.TargetTicket;
		if (ticket == null)
		{
			ticket = JobScheduler.GetReservedTicket(state, actor) ?? JobScheduler.FindBestTicket(state, actor);
			if (ticket == null) return new ActionExecutionResult();
		}

		if (!JobScheduler.TryReserve(state, ticket, actor))
			return new ActionExecutionResult();

		return JobExecutor.Execute(state, actor, ticket);
	}
}
