using System;
using System.Collections.Generic;

namespace MiniRPG;

internal sealed class ClientPredictionState
{
	private readonly Dictionary<string, PredictedMove> _predictedByRequestId = new(StringComparer.Ordinal);
	private readonly Queue<PredictedMove> _orderedPredictions = new();
	private readonly Dictionary<string, long> _rollbackCountByReason = new(StringComparer.Ordinal);

	public long NextClientTick { get; private set; }
	public long TotalRollbackCount { get; private set; }
	public long LastRollbackDistanceManhattan { get; private set; }

	public PredictionConfig Config { get; private set; } = PredictionConfig.Default;

	public void Configure(PredictionConfig config)
	{
		Config = config;
	}

	public PredictedMove CreateMovePrediction(string requestId, int dx, int dy, int predictedX, int predictedY, int predictedZ)
	{
		var prediction = new PredictedMove(
			requestId,
			NextClientTick++,
			dx,
			dy,
			predictedX,
			predictedY,
			predictedZ);
		_predictedByRequestId[requestId] = prediction;
		_orderedPredictions.Enqueue(prediction);
		TrimHistory();
		return prediction;
	}

	public ReconcileDecision Reconcile(string? acknowledgedRequestId, int authoritativeX, int authoritativeY, int authoritativeZ)
	{
		if (!string.IsNullOrWhiteSpace(acknowledgedRequestId) && _predictedByRequestId.TryGetValue(acknowledgedRequestId, out var acknowledged))
		{
			PruneThrough(acknowledged.ClientTick);
		}

		if (_orderedPredictions.Count == 0)
			return ReconcileDecision.NoPending(authoritativeX, authoritativeY, authoritativeZ);

		var baselineX = authoritativeX;
		var baselineY = authoritativeY;
		var baselineZ = authoritativeZ;
		foreach (var prediction in _orderedPredictions)
		{
			baselineX += prediction.Dx;
			baselineY += prediction.Dy;
		}

		var latestPredicted = GetLatestPending();
		if (!latestPredicted.HasValue)
			return ReconcileDecision.NoPending(authoritativeX, authoritativeY, authoritativeZ);

		var latest = latestPredicted.Value;
		var dx = latest.PredictedX - baselineX;
		var dy = latest.PredictedY - baselineY;
		var dz = latest.PredictedZ - baselineZ;
		var manhattan = Math.Abs(dx) + Math.Abs(dy) + Math.Abs(dz);
		if (manhattan <= Config.RollbackThresholdManhattan)
			return ReconcileDecision.InTolerance(authoritativeX, authoritativeY, authoritativeZ, baselineX, baselineY, baselineZ, manhattan);

		TotalRollbackCount++;
		LastRollbackDistanceManhattan = manhattan;
		AccumulateRollbackReason("movement");
		return ReconcileDecision.WithRollback(authoritativeX, authoritativeY, authoritativeZ, baselineX, baselineY, baselineZ, manhattan);
	}

	public IReadOnlyDictionary<string, long> GetRollbackBreakdown() => _rollbackCountByReason;

	private void TrimHistory()
	{
		while (_orderedPredictions.Count > Config.MaxPendingCommands)
		{
			var removed = _orderedPredictions.Dequeue();
			_predictedByRequestId.Remove(removed.RequestId);
		}
	}

	private void PruneThrough(long tick)
	{
		while (_orderedPredictions.Count > 0 && _orderedPredictions.Peek().ClientTick <= tick)
		{
			var removed = _orderedPredictions.Dequeue();
			_predictedByRequestId.Remove(removed.RequestId);
		}
	}

	private PredictedMove? GetLatestPending()
	{
		PredictedMove? latest = null;
		foreach (var prediction in _orderedPredictions)
			latest = prediction;
		return latest;
	}

	private void AccumulateRollbackReason(string reason)
	{
		if (_rollbackCountByReason.TryGetValue(reason, out var count))
			_rollbackCountByReason[reason] = count + 1;
		else
			_rollbackCountByReason[reason] = 1;
	}
}

internal readonly record struct PredictedMove(
	string RequestId,
	long ClientTick,
	int Dx,
	int Dy,
	int PredictedX,
	int PredictedY,
	int PredictedZ);

internal readonly record struct PredictionConfig(
	int RollbackThresholdManhattan,
	int MaxPendingCommands)
{
	public static PredictionConfig Default => new(
		RollbackThresholdManhattan: 0,
		MaxPendingCommands: 64);
}

internal readonly record struct ReconcileDecision(
	bool RollbackNeeded,
	bool HasPending,
	int AuthoritativeX,
	int AuthoritativeY,
	int AuthoritativeZ,
	int ReplayedX,
	int ReplayedY,
	int ReplayedZ,
	int ManhattanDistance)
{
	public static ReconcileDecision NoPending(int authoritativeX, int authoritativeY, int authoritativeZ) => new(
		RollbackNeeded: false,
		HasPending: false,
		AuthoritativeX: authoritativeX,
		AuthoritativeY: authoritativeY,
		AuthoritativeZ: authoritativeZ,
		ReplayedX: authoritativeX,
		ReplayedY: authoritativeY,
		ReplayedZ: authoritativeZ,
		ManhattanDistance: 0);

	public static ReconcileDecision InTolerance(
		int authoritativeX,
		int authoritativeY,
		int authoritativeZ,
		int replayedX,
		int replayedY,
		int replayedZ,
		int distance) => new(
		RollbackNeeded: false,
		HasPending: true,
		AuthoritativeX: authoritativeX,
		AuthoritativeY: authoritativeY,
		AuthoritativeZ: authoritativeZ,
		ReplayedX: replayedX,
		ReplayedY: replayedY,
		ReplayedZ: replayedZ,
		ManhattanDistance: distance);

	public static ReconcileDecision WithRollback(
		int authoritativeX,
		int authoritativeY,
		int authoritativeZ,
		int replayedX,
		int replayedY,
		int replayedZ,
		int distance) => new(
		RollbackNeeded: true,
		HasPending: true,
		AuthoritativeX: authoritativeX,
		AuthoritativeY: authoritativeY,
		AuthoritativeZ: authoritativeZ,
		ReplayedX: replayedX,
		ReplayedY: replayedY,
		ReplayedZ: replayedZ,
		ManhattanDistance: distance);
}
