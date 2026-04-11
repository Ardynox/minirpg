using System;
using System.Collections.Generic;
using Godot;

namespace MiniRPG;

internal enum StartupState
{
	LoadingHeavyAssets,
	CompletingHeavyAssetsSynchronously,
	Finalizing,
	Ready,
	Failed,
}

internal readonly record struct StartupHeavyLoadStep(
	string Path,
	float ProgressStart,
	float ProgressEnd,
	string StatusKey);

internal sealed class MainStartupCoordinator
{
	private readonly StartupHeavyLoadStep[] _heavyLoadSteps;
	private readonly float _visualFloor;
	private readonly float _visualPlateau;
	private readonly float _visualProgressPerSecond;
	private readonly float _threadedLoadTimeoutSeconds;
	private readonly float _syncFallbackProgress;
	private readonly Action _onStartupReady;
	private readonly Action _onStartupFailed;
	private readonly Action<string> _onWarning;
	private readonly Action _refreshStartupUi;
	private readonly Action _prewarmDeferredUiScenes;

	private readonly Godot.Collections.Array _startupThreadProgress = new();
	private readonly Queue<Action> _postStartupTasks = new();
	private bool _postStartupTasksQueued;
	private int _postStartupTaskDelayFrames;
	private int _startupLoadIndex = -1;
	private string? _startupLoadPath;
	private ulong _startupLoadStartedAtMsec;

	public StartupState State { get; private set; } = StartupState.LoadingHeavyAssets;
	public string StartupStatusKey { get; private set; } = "ui.startup.status.tileset";
	public float StartupProgress { get; private set; }
	public string? LastFailedPath { get; private set; }

	public bool ResourcesReady => State == StartupState.Ready;

	public MainStartupCoordinator(
		StartupHeavyLoadStep[] heavyLoadSteps,
		float visualFloor,
		float visualPlateau,
		float visualProgressPerSecond,
		float threadedLoadTimeoutSeconds,
		float syncFallbackProgress,
		Action onStartupReady,
		Action onStartupFailed,
		Action<string> onWarning,
		Action refreshStartupUi,
		Action prewarmDeferredUiScenes)
	{
		_heavyLoadSteps = heavyLoadSteps;
		_visualFloor = visualFloor;
		_visualPlateau = visualPlateau;
		_visualProgressPerSecond = visualProgressPerSecond;
		_threadedLoadTimeoutSeconds = threadedLoadTimeoutSeconds;
		_syncFallbackProgress = syncFallbackProgress;
		_onStartupReady = onStartupReady;
		_onStartupFailed = onStartupFailed;
		_onWarning = onWarning;
		_refreshStartupUi = refreshStartupUi;
		_prewarmDeferredUiScenes = prewarmDeferredUiScenes;
	}

	public void BeginHeavyStartupLoad()
	{
		State = StartupState.LoadingHeavyAssets;
		_startupLoadIndex = -1;
		_startupLoadPath = null;
		_startupLoadStartedAtMsec = 0;
		StartupProgress = 0f;
		StartNextHeavyStartupLoad();
	}

	public void PollHeavyStartupLoad(Func<string, bool> storeLoadedResource, Action finalizeHeavyStartupLoad)
	{
		switch (State)
		{
			case StartupState.LoadingHeavyAssets:
				PollCurrentHeavyStartupLoad(storeLoadedResource);
				break;
			case StartupState.CompletingHeavyAssetsSynchronously:
				CompleteCurrentHeavyStartupLoadSynchronously(storeLoadedResource);
				break;
			case StartupState.Finalizing:
				finalizeHeavyStartupLoad();
				break;
		}
	}

	public void PollPostStartupTasks()
	{
		if (State != StartupState.Ready || _postStartupTasks.Count == 0)
			return;

		if (_postStartupTaskDelayFrames > 0)
		{
			_postStartupTaskDelayFrames--;
			return;
		}

		var task = _postStartupTasks.Dequeue();
		try
		{
			task();
		}
		catch (Exception ex)
		{
			_onWarning($"[Startup] Deferred task failed: {ex.Message}");
		}
	}

	public void MarkReady()
	{
		StartupProgress = 1f;
		State = StartupState.Ready;
		_refreshStartupUi();
		QueuePostStartupTasks();
		_onStartupReady();
	}

	public void FailHeavyStartupLoad(string path, string reason)
	{
		GD.PrintErr($"[Startup] Heavy resource load failed: {path} ({reason})");
		TransitionToStartupFailed(path);
	}

	public void FailStartupBootstrap(string path, Exception ex)
	{
		GD.PrintErr($"[Startup] Bootstrap failed: {path} ({ex.GetType().Name}: {ex.Message})");
		GD.PrintErr($"[Startup] Bootstrap exception detail: {ex}");
		TransitionToStartupFailed(path);
	}

	private void TransitionToStartupFailed(string path)
	{
		State = StartupState.Failed;
		LastFailedPath = path;
		_startupLoadPath = null;
		_startupLoadStartedAtMsec = 0;
		StartupStatusKey = "ui.startup.status.failed";
		_refreshStartupUi();
		_onStartupFailed();
	}

	private void StartNextHeavyStartupLoad()
	{
		_startupLoadIndex++;
		if (_startupLoadIndex >= _heavyLoadSteps.Length)
		{
			BeginHeavyStartupFinalization();
			return;
		}

		var step = _heavyLoadSteps[_startupLoadIndex];
		_startupLoadPath = step.Path;
		StartupStatusKey = step.StatusKey;
		_startupLoadStartedAtMsec = Time.GetTicksMsec();
		StartupProgress = Mathf.Lerp(step.ProgressStart, step.ProgressEnd, _visualFloor);
		_startupThreadProgress.Clear();

		var err = ResourceLoader.LoadThreadedRequest(step.Path, string.Empty, useSubThreads: true, ResourceLoader.CacheMode.Reuse);
		if (err != Error.Ok)
		{
			FailHeavyStartupLoad(step.Path, err.ToString());
			return;
		}

		_refreshStartupUi();
	}

	private void BeginHeavyStartupFinalization()
	{
		State = StartupState.Finalizing;
		_startupLoadPath = null;
		_startupLoadStartedAtMsec = 0;
		StartupProgress = 0.95f;
		StartupStatusKey = "ui.startup.status.finalizing";
		_refreshStartupUi();
	}

	private void PollCurrentHeavyStartupLoad(Func<string, bool> storeLoadedResource)
	{
		if (string.IsNullOrEmpty(_startupLoadPath)
			|| _startupLoadIndex < 0
			|| _startupLoadIndex >= _heavyLoadSteps.Length)
			return;

		var step = _heavyLoadSteps[_startupLoadIndex];
		_startupThreadProgress.Clear();
		var status = ResourceLoader.LoadThreadedGetStatus(_startupLoadPath, _startupThreadProgress);
		UpdateHeavyStartupProgress(step);

		switch (status)
		{
			case ResourceLoader.ThreadLoadStatus.InProgress:
				if (ShouldSwitchToSynchronousHeavyLoad())
					BeginSynchronousHeavyStartupCompletion(step);
				return;
			case ResourceLoader.ThreadLoadStatus.Loaded:
				_startupLoadStartedAtMsec = 0;
				StartupProgress = step.ProgressEnd;
				if (!storeLoadedResource(_startupLoadPath))
					return;
				_startupLoadPath = null;
				StartNextHeavyStartupLoad();
				return;
			case ResourceLoader.ThreadLoadStatus.Failed:
			case ResourceLoader.ThreadLoadStatus.InvalidResource:
				FailHeavyStartupLoad(_startupLoadPath, $"status={status}");
				return;
		}
	}

	private bool ShouldSwitchToSynchronousHeavyLoad()
	{
		if (_startupLoadStartedAtMsec == 0)
			return false;
		var elapsedSeconds = (float)(Time.GetTicksMsec() - _startupLoadStartedAtMsec) / 1000f;
		return elapsedSeconds >= _threadedLoadTimeoutSeconds;
	}

	private void BeginSynchronousHeavyStartupCompletion(StartupHeavyLoadStep step)
	{
		if (string.IsNullOrEmpty(_startupLoadPath))
			return;

		State = StartupState.CompletingHeavyAssetsSynchronously;
		_startupLoadStartedAtMsec = 0;
		StartupProgress = Math.Max(step.ProgressEnd, _syncFallbackProgress);
		GD.Print($"[Startup] Threaded heavy load exceeded {_threadedLoadTimeoutSeconds:F1}s, switching to blocking completion: {_startupLoadPath}");
		_refreshStartupUi();
	}

	private void CompleteCurrentHeavyStartupLoadSynchronously(Func<string, bool> storeLoadedResource)
	{
		if (string.IsNullOrEmpty(_startupLoadPath)
			|| _startupLoadIndex < 0
			|| _startupLoadIndex >= _heavyLoadSteps.Length)
			return;

		var path = _startupLoadPath;
		var step = _heavyLoadSteps[_startupLoadIndex];

		try
		{
			_ = ResourceLoader.LoadThreadedGet(path);
			StartupProgress = step.ProgressEnd;
			if (!storeLoadedResource(path))
				return;

			State = StartupState.LoadingHeavyAssets;
			_startupLoadPath = null;
			StartNextHeavyStartupLoad();
		}
		catch (Exception ex)
		{
			FailHeavyStartupLoad(path, $"sync fallback failed: {ex.Message}");
		}
	}

	private void UpdateHeavyStartupProgress(StartupHeavyLoadStep step)
	{
		var progress = ReadStartupThreadProgress();
		if (_startupLoadStartedAtMsec > 0)
		{
			var elapsedSeconds = (float)(Time.GetTicksMsec() - _startupLoadStartedAtMsec) / 1000f;
			var fallbackProgress = Math.Min(_visualPlateau, _visualFloor + elapsedSeconds * _visualProgressPerSecond);
			progress = Math.Max(progress, fallbackProgress);
		}

		StartupProgress = Mathf.Lerp(step.ProgressStart, step.ProgressEnd, progress);
		_refreshStartupUi();
	}

	private float ReadStartupThreadProgress()
	{
		if (_startupThreadProgress.Count == 0)
			return 0f;
		return ClampProgressValue(_startupThreadProgress[0]);
	}

	private static float ClampProgressValue(object? rawProgress)
	{
		if (rawProgress == null)
			return 0f;

		switch (rawProgress)
		{
			case float single: return Math.Clamp(single, 0f, 1f);
			case double d: return Math.Clamp((float)d, 0f, 1f);
			case int i: return Math.Clamp(i, 0f, 1f);
			case long l: return Math.Clamp(l, 0f, 1f);
			case decimal m: return Math.Clamp((float)m, 0f, 1f);
			case string text when TryParseProgressText(text, out var parsed): return parsed;
		}

		return TryParseProgressText(Convert.ToString(rawProgress, System.Globalization.CultureInfo.InvariantCulture), out var fallback)
			? fallback
			: 0f;
	}

	private static bool TryParseProgressText(string? text, out float progress)
	{
		if (!string.IsNullOrWhiteSpace(text)
			&& (float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out progress)
				|| float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out progress)))
		{
			progress = Math.Clamp(progress, 0f, 1f);
			return true;
		}

		progress = 0f;
		return false;
	}

	private void QueuePostStartupTasks()
	{
		if (_postStartupTasksQueued)
			return;
		_postStartupTasksQueued = true;
		_postStartupTaskDelayFrames = 1;
		_postStartupTasks.Enqueue(_prewarmDeferredUiScenes);
	}
}
