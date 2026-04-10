using MiniRPG;
using Xunit;

namespace MiniRPG.Tests;

public sealed class ClientPredictionStateTests
{
	[Fact]
	public void Reconcile_InTolerance_DoesNotRollback()
	{
		var state = new ClientPredictionState();
		state.Configure(new PredictionConfig(RollbackThresholdManhattan: 1, MaxPendingCommands: 32));

		state.CreateMovePrediction("req-1", 1, 0, predictedX: 11, predictedY: 10, predictedZ: 0);
		var decision = state.Reconcile("req-1", authoritativeX: 11, authoritativeY: 10, authoritativeZ: 0);

		Assert.False(decision.RollbackNeeded);
		Assert.False(decision.HasPending);
		Assert.Equal(0, state.TotalRollbackCount);
	}

	[Fact]
	public void Reconcile_OutOfTolerance_TriggersRollbackAndMetrics()
	{
		var state = new ClientPredictionState();
		state.Configure(new PredictionConfig(RollbackThresholdManhattan: 0, MaxPendingCommands: 32));

		state.CreateMovePrediction("req-1", 1, 0, predictedX: 5, predictedY: 5, predictedZ: 0);
		state.CreateMovePrediction("req-2", 1, 0, predictedX: 6, predictedY: 5, predictedZ: 0);

		var decision = state.Reconcile("req-1", authoritativeX: 3, authoritativeY: 5, authoritativeZ: 0);

		Assert.True(decision.HasPending);
		Assert.True(decision.RollbackNeeded);
		Assert.Equal(4, decision.ReplayedX);
		Assert.Equal(5, decision.ReplayedY);
		Assert.Equal(1, state.TotalRollbackCount);
		Assert.Equal(2, state.LastRollbackDistanceManhattan);
		Assert.Equal(1, state.GetRollbackBreakdown()["movement"]);
	}
}
