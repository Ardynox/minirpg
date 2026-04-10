using System;
using System.Collections.Generic;

namespace MiniRPG.Core.Multiplayer;

public enum ServerAuditCategory
{
	Request,
	Combat,
	Economy,
}

public sealed record ServerAuditLogEntry
{
	public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
	public ServerAuditCategory Category { get; init; } = ServerAuditCategory.Request;
	public string Action { get; init; } = string.Empty;
	public string RoomId { get; init; } = string.Empty;
	public string? PlayerSessionId { get; init; }
	public string? ActorId { get; init; }
	public string? RequestId { get; init; }
	public long ServerTick { get; init; }
	public string Result { get; init; } = "ok";
	public string? Code { get; init; }
	public int EventCount { get; init; }
	public Dictionary<string, string> Metadata { get; init; } = [];
}
