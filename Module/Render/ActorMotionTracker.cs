using System;
using System.Collections.Generic;
using Godot;

namespace MiniRPG.Module.Render;

/// <summary>
/// Owns the live motion tweens that <see cref="IsometricVoxelRenderer"/>
/// uses to interpolate an actor's visual position between its previous
/// tile and its next tile. Keeps the motion dictionary, the blocking-flag
/// state and the pure timing arithmetic together so the renderer only
/// coordinates the scene (camera, visibility, drawing) and not the state
/// machine of in-flight tweens.
/// </summary>
internal sealed class ActorMotionTracker
{
	private const float MotionDampingStrength = 1.5f;
	private const int MaxContinuousSegmentsPerActor = 3;
	private readonly Dictionary<string, ActorMotionState> _motions = new(StringComparer.Ordinal);
	private bool _hasBlocking;
	private double _lastKnownClockSeconds;

	public bool HasAny => _motions.Count > 0;

	public bool HasBlocking => _hasBlocking;

	public void Record(
		ActorMotionPresentationRequest request,
		double startTimeSeconds,
		float durationSeconds)
	{
		_lastKnownClockSeconds = startTimeSeconds;

		var blockingGateSeconds = ResolveBlockingGateSeconds(request, durationSeconds);
		if (TryAppendContinuousSegment(request, startTimeSeconds, durationSeconds, blockingGateSeconds))
		{
			UpdateBlockingFlag(startTimeSeconds);
			return;
		}

		var source = ResolveSourcePosition(request, startTimeSeconds);
		var state = new ActorMotionState(request.UsesContinuousPresentation);
		state.Segments.Add(CreateSegment(source, request, startTimeSeconds, durationSeconds));
		state.BlockingGateEndTimeSeconds = request.Blocking
			? startTimeSeconds + blockingGateSeconds
			: double.NegativeInfinity;
		_motions[request.ActorId] = state;
		UpdateBlockingFlag(startTimeSeconds);
	}

	public void Remove(string actorId)
	{
		if (string.IsNullOrWhiteSpace(actorId))
			return;
		if (_motions.Remove(actorId))
			UpdateBlockingFlag(lastClockSeconds: null);
	}

	public void Clear()
	{
		_motions.Clear();
		_hasBlocking = false;
	}

	/// <summary>Drop finished tweens and refresh the blocking flag.</summary>
	public void Prune(double clockSeconds)
	{
		_lastKnownClockSeconds = clockSeconds;
		if (_motions.Count == 0)
			return;

		List<string>? completed = null;
		foreach (var (actorId, motion) in _motions)
		{
			PruneState(motion, clockSeconds);
			if (motion.Segments.Count == 0)
				(completed ??= new List<string>()).Add(actorId);
		}

		if (completed != null)
		{
			for (var i = 0; i < completed.Count; i++)
				_motions.Remove(completed[i]);
		}

		UpdateBlockingFlag(clockSeconds);
	}

	/// <summary>Re-evaluate whether any blocking tween is still in flight.</summary>
	public void UpdateBlockingFlag(double? lastClockSeconds)
	{
		var referenceClock = lastClockSeconds ?? _lastKnownClockSeconds;
		if (_motions.Count == 0)
		{
			_hasBlocking = false;
			return;
		}

		_hasBlocking = false;
		foreach (var motion in _motions.Values)
		{
			if (motion.Segments.Count == 0)
				continue;
			if (motion.BlockingGateEndTimeSeconds > referenceClock)
			{
				_hasBlocking = true;
				break;
			}
		}
	}

	/// <summary>
	/// When an in-flight tween exists for <paramref name="actorId"/>, fill
	/// <paramref name="position"/> with the interpolated world-space
	/// coordinates and return <c>true</c>; otherwise return <c>false</c>
	/// and let the caller fall back to the actor's static tile.
	/// </summary>
	public bool TryGetInterpolatedPosition(
		string actorId,
		double clockSeconds,
		out Vector3 position)
	{
		_lastKnownClockSeconds = clockSeconds;
		if (!_motions.TryGetValue(actorId, out var motion))
		{
			position = default;
			return false;
		}

		PruneState(motion, clockSeconds);
		if (motion.Segments.Count == 0)
		{
			_motions.Remove(actorId);
			position = default;
			UpdateBlockingFlag(clockSeconds);
			return false;
		}

		var segment = motion.Segments[0];
		var progress = ResolvePresentationProgress(segment, clockSeconds);
		position = new Vector3(
			Mathf.Lerp(segment.SourceX, segment.TargetX, progress),
			Mathf.Lerp(segment.SourceY, segment.TargetY, progress),
			Mathf.Lerp(segment.SourceZ, segment.TargetZ, progress));
		return true;
	}

	/// <summary>
	/// True when <paramref name="request"/> represents a standard 4-way
	/// tile step or a single-Z step. Multi-tile teleports do not tween.
	/// </summary>
	public static bool IsStandardStep(ActorMotionPresentationRequest request)
	{
		var dx = Math.Abs(request.TargetX - request.SourceX);
		var dy = Math.Abs(request.TargetY - request.SourceY);
		var dz = Math.Abs(request.TargetZ - request.SourceZ);
		return (dz == 0 && dx + dy == 1) || (dx == 0 && dy == 0 && dz == 1);
	}

	private static float GetProgress(ActorMotionSegment segment, double clockSeconds)
	{
		if (segment.DurationSeconds <= 0f)
			return 1f;

		var elapsed = (float)(clockSeconds - segment.StartTimeSeconds);
		return Mathf.Clamp(elapsed / segment.DurationSeconds, 0f, 1f);
	}

	private Vector3 ResolveSourcePosition(ActorMotionPresentationRequest request, double clockSeconds)
	{
		if (request.UsesContinuousPresentation
			&& TryGetInterpolatedPosition(request.ActorId, clockSeconds, out var currentPosition))
		{
			return currentPosition;
		}

		return new Vector3(request.SourceX, request.SourceY, request.SourceZ);
	}

	private bool TryAppendContinuousSegment(
		ActorMotionPresentationRequest request,
		double clockSeconds,
		float baseDurationSeconds,
		float blockingGateSeconds)
	{
		if (!request.UsesContinuousPresentation
			|| !_motions.TryGetValue(request.ActorId, out var motion)
			|| !motion.ContinuousPresentation)
		{
			return false;
		}

		PruneState(motion, clockSeconds);
		if (motion.Segments.Count == 0 || motion.Segments.Count >= MaxContinuousSegmentsPerActor)
			return false;

		var tail = motion.Segments[^1];
		if (!MatchesSegmentTarget(request, tail))
			return false;

		var source = new Vector3(tail.TargetX, tail.TargetY, tail.TargetZ);
		var segmentStartTimeSeconds = tail.StartTimeSeconds + tail.DurationSeconds;
		motion.Segments.Add(CreateSegment(source, request, segmentStartTimeSeconds, baseDurationSeconds));
		if (request.Blocking)
		{
			motion.BlockingGateEndTimeSeconds = Math.Max(
				motion.BlockingGateEndTimeSeconds,
				clockSeconds + blockingGateSeconds);
		}

		return true;
	}

	private static bool MatchesSegmentTarget(
		ActorMotionPresentationRequest request,
		ActorMotionSegment tail)
	{
		return Math.Abs(tail.TargetX - request.SourceX) <= 0.001f
			&& Math.Abs(tail.TargetY - request.SourceY) <= 0.001f
			&& Math.Abs(tail.TargetZ - request.SourceZ) <= 0.001f;
	}

	private static float ResolveBlockingGateSeconds(
		ActorMotionPresentationRequest request,
		float baseDurationSeconds)
	{
		if (!request.Blocking)
			return 0f;

		var fallback = MathF.Max(baseDurationSeconds, 0f);
		var requested = request.BlockingGateSecondsOverride ?? fallback;
		if (fallback <= 0f)
			return MathF.Max(requested, 0f);

		return Math.Clamp(requested, 0f, fallback);
	}

	private static ActorMotionSegment CreateSegment(
		Vector3 source,
		ActorMotionPresentationRequest request,
		double startTimeSeconds,
		float baseDurationSeconds)
	{
		return new ActorMotionSegment(
			source.X,
			source.Y,
			source.Z,
			request.TargetX,
			request.TargetY,
			request.TargetZ,
			startTimeSeconds,
			ResolveDurationSeconds(baseDurationSeconds, source, request),
			request.UsesContinuousPresentation);
	}

	private static float ResolveDurationSeconds(
		float baseDurationSeconds,
		Vector3 source,
		ActorMotionPresentationRequest request)
	{
		if (!request.UsesContinuousPresentation || baseDurationSeconds <= 0f)
			return baseDurationSeconds;

		var distance = MathF.Abs(request.TargetX - source.X)
			+ MathF.Abs(request.TargetY - source.Y)
			+ MathF.Abs(request.TargetZ - source.Z);
		if (distance <= 0.001f)
			return 0f;

		return baseDurationSeconds * MathF.Max(distance, 1f);
	}

	private static void PruneState(ActorMotionState motion, double clockSeconds)
	{
		while (motion.Segments.Count > 0)
		{
			var segment = motion.Segments[0];
			if (GetProgress(segment, clockSeconds) < 1f)
				break;

			motion.Segments.RemoveAt(0);
		}
	}

	private static float ResolvePresentationProgress(ActorMotionSegment segment, double clockSeconds)
	{
		var progress = GetProgress(segment, clockSeconds);
		return segment.ContinuousPresentation ? progress : ApplyDampedProgress(progress);
	}

	internal static float ApplyDampedProgress(float progress)
	{
		progress = Mathf.Clamp(progress, 0f, 1f);
		if (progress <= 0f || progress >= 1f)
			return progress;

		// Exponential ease-out keeps the step readable at the start and lets it
		// settle into the destination tile instead of sliding at constant speed.
		var normalization = 1f - Mathf.Exp(-MotionDampingStrength);
		if (normalization <= 0f)
			return progress;

		return (1f - Mathf.Exp(-MotionDampingStrength * progress)) / normalization;
	}

	internal sealed class ActorMotionState
	{
		public ActorMotionState(bool continuousPresentation)
		{
			ContinuousPresentation = continuousPresentation;
		}

		public List<ActorMotionSegment> Segments { get; } = new();

		public bool ContinuousPresentation { get; }

		public double BlockingGateEndTimeSeconds { get; set; } = double.NegativeInfinity;
	}

	internal readonly record struct ActorMotionSegment(
		float SourceX,
		float SourceY,
		float SourceZ,
		float TargetX,
		float TargetY,
		float TargetZ,
		double StartTimeSeconds,
		float DurationSeconds,
		bool ContinuousPresentation);
}
