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
	private readonly Dictionary<string, ActorMotionState> _motions = new(StringComparer.Ordinal);
	private bool _hasBlocking;

	public bool HasAny => _motions.Count > 0;

	public bool HasBlocking => _hasBlocking;

	public void Record(
		ActorMotionPresentationRequest request,
		double startTimeSeconds,
		float durationSeconds)
	{
		var source = ResolveSourcePosition(request, startTimeSeconds);
		_motions[request.ActorId] = new ActorMotionState(
			source.X,
			source.Y,
			source.Z,
			request.TargetX,
			request.TargetY,
			request.TargetZ,
			startTimeSeconds,
			ResolveDurationSeconds(durationSeconds, source, request),
			request.Blocking,
			request.UsesAsyncPresentation);
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
		if (_motions.Count == 0)
			return;

		List<string>? completed = null;
		foreach (var (actorId, motion) in _motions)
		{
			if (GetProgress(motion, clockSeconds) >= 1f)
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
		if (_motions.Count == 0)
		{
			_hasBlocking = false;
			return;
		}

		_hasBlocking = false;
		foreach (var motion in _motions.Values)
		{
			if (!motion.Blocking)
				continue;
			var progress = lastClockSeconds is { } clock
				? GetProgress(motion, clock)
				: GetProgress(motion, motion.StartTimeSeconds);
			if (progress < 1f)
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
		if (!_motions.TryGetValue(actorId, out var motion))
		{
			position = default;
			return false;
		}

		var progress = ResolvePresentationProgress(motion, clockSeconds);
		position = new Vector3(
			Mathf.Lerp(motion.SourceX, motion.TargetX, progress),
			Mathf.Lerp(motion.SourceY, motion.TargetY, progress),
			Mathf.Lerp(motion.SourceZ, motion.TargetZ, progress));
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

	private static float GetProgress(ActorMotionState motion, double clockSeconds)
	{
		if (motion.DurationSeconds <= 0f)
			return 1f;

		var elapsed = (float)(clockSeconds - motion.StartTimeSeconds);
		return Mathf.Clamp(elapsed / motion.DurationSeconds, 0f, 1f);
	}

	private Vector3 ResolveSourcePosition(ActorMotionPresentationRequest request, double clockSeconds)
	{
		if (request.UsesAsyncPresentation
			&& TryGetInterpolatedPosition(request.ActorId, clockSeconds, out var currentPosition))
		{
			return currentPosition;
		}

		return new Vector3(request.SourceX, request.SourceY, request.SourceZ);
	}

	private static float ResolveDurationSeconds(
		float baseDurationSeconds,
		Vector3 source,
		ActorMotionPresentationRequest request)
	{
		if (!request.UsesAsyncPresentation || baseDurationSeconds <= 0f)
			return baseDurationSeconds;

		var distance = MathF.Abs(request.TargetX - source.X)
			+ MathF.Abs(request.TargetY - source.Y)
			+ MathF.Abs(request.TargetZ - source.Z);
		if (distance <= 0.001f)
			return 0f;

		return baseDurationSeconds * MathF.Max(distance, 1f);
	}

	private static float ResolvePresentationProgress(ActorMotionState motion, double clockSeconds)
	{
		var progress = GetProgress(motion, clockSeconds);
		return motion.AsyncPresentation ? progress : ApplyDampedProgress(progress);
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

	internal readonly record struct ActorMotionState(
		float SourceX,
		float SourceY,
		float SourceZ,
		float TargetX,
		float TargetY,
		float TargetZ,
		double StartTimeSeconds,
		float DurationSeconds,
		bool Blocking,
		bool AsyncPresentation);
}
