using System;
using System.Collections.Concurrent;
using System.Globalization;

namespace MiniRPG.Core.Multiplayer;

public sealed class MultiplayerTelemetryStore
{
	private readonly ConcurrentDictionary<string, RoomTelemetryMetrics> _rooms = new(StringComparer.Ordinal);

	public RoomTelemetryMetrics GetOrCreateRoom(string roomId)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(roomId);
		return _rooms.GetOrAdd(roomId, static _ => new RoomTelemetryMetrics());
	}

	public void RecordCommand(string roomId, bool accepted)
	{
		var metrics = GetOrCreateRoom(roomId);
		metrics.RecordCommand(accepted);
	}

	public void RecordRttSample(string roomId, long clientTick, long serverTick)
	{
		var metrics = GetOrCreateRoom(roomId);
		metrics.RecordRttSample(Math.Abs(serverTick - clientTick));
	}

	public void RecordRollback(string roomId) => GetOrCreateRoom(roomId).RollbackCount++;

	public void RecordResync(string roomId) => GetOrCreateRoom(roomId).ResyncCount++;

	public void SetPlayerCount(string roomId, int count) => GetOrCreateRoom(roomId).RoomPlayerCount = count;

	public static void AttachMetricsMetadata(ServerAuditLogEntry entry, RoomTelemetryMetrics metrics)
	{
		entry.Metadata["metric.rttTicks.avg"] = metrics.AverageRttTicks.ToString("0.###", CultureInfo.InvariantCulture);
		entry.Metadata["metric.rttTicks.last"] = metrics.LastRttTicks.ToString(CultureInfo.InvariantCulture);
		entry.Metadata["metric.rollbackCount"] = metrics.RollbackCount.ToString(CultureInfo.InvariantCulture);
		entry.Metadata["metric.resyncCount"] = metrics.ResyncCount.ToString(CultureInfo.InvariantCulture);
		entry.Metadata["metric.commandRejectRate"] = metrics.CommandRejectRate.ToString("0.####", CultureInfo.InvariantCulture);
		entry.Metadata["metric.roomPlayerCount"] = metrics.RoomPlayerCount.ToString(CultureInfo.InvariantCulture);
	}
}

public sealed class RoomTelemetryMetrics
{
	private long _totalCommandCount;
	private long _rejectedCommandCount;
	private long _rttSampleCount;
	private double _rttTickSum;

	public long LastRttTicks { get; private set; }
	public long RollbackCount { get; set; }
	public long ResyncCount { get; set; }
	public int RoomPlayerCount { get; set; }

	public double AverageRttTicks => _rttSampleCount <= 0 ? 0d : _rttTickSum / _rttSampleCount;
	public double CommandRejectRate => _totalCommandCount <= 0 ? 0d : (double)_rejectedCommandCount / _totalCommandCount;

	public void RecordCommand(bool accepted)
	{
		_totalCommandCount++;
		if (!accepted)
			_rejectedCommandCount++;
	}

	public void RecordRttSample(long rttTicks)
	{
		LastRttTicks = rttTicks;
		_rttSampleCount++;
		_rttTickSum += rttTicks;
	}
}
