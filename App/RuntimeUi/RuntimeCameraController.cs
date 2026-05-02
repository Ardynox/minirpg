using Godot;
using MiniRPG.Core.Data;
using MiniRPG.Module.Render;

namespace MiniRPG;

internal sealed class RuntimeCameraController
{
	private const int MaxCameraLayer = 20;

	private readonly GameState _state;

	private bool _panCameraInitialized;
	private bool _panDragActive;
	private float _panCameraFloatX;
	private float _panCameraFloatY;
	private int _panCameraX;
	private int _panCameraY;
	private int _panCameraZ;

	// 死亡 / 焦点切换演出用的"镜头缓动"覆盖：在 FollowActor 模式下，BuildSnapshot 会把
	// 摄像头中心在 from→当前 active actor 之间按 ease-out cubic 插值。仅 0.4~1.5s 短期使用，
	// 由 Main._Process 调 Tick(delta) 推进，结束自动复位。不影响 LayerPan 路径。
	private (int X, int Y, int Z)? _cinematicFromCell;
	private double _cinematicElapsedSec;
	private double _cinematicDurationSec;

	public RuntimeCameraController(GameState state)
	{
		_state = state;
		ResetForSession();
	}

	public RuntimeCameraMode Mode { get; private set; }

	public bool IsPanDragActive => _panDragActive;

	public bool IsCinematicSweepActive => _cinematicFromCell != null;

	public RuntimeCameraSnapshot BuildSnapshot()
	{
		if (Mode == RuntimeCameraMode.FollowActor)
		{
			var (x, y, z) = ResolveActiveActorPosition();
			if (_cinematicFromCell is { } from && _cinematicDurationSec > 0)
			{
				var raw = (float)Math.Clamp(_cinematicElapsedSec / _cinematicDurationSec, 0.0, 1.0);
				var eased = 1f - Mathf.Pow(1f - raw, 3f);
				var lerpX = Mathf.Lerp((float)from.X, (float)x, eased);
				var lerpY = Mathf.Lerp((float)from.Y, (float)y, eased);
				var lerpZ = Mathf.Lerp((float)from.Z, (float)z, eased);
				return RuntimeCameraSnapshot.Create(
					RuntimeCameraMode.FollowActor,
					Mathf.RoundToInt(lerpX),
					Mathf.RoundToInt(lerpY),
					Mathf.RoundToInt(lerpZ),
					IsoCoordUtil.WorldToScreen(lerpX, lerpY, lerpZ));
			}
			return RuntimeCameraSnapshot.Create(RuntimeCameraMode.FollowActor, x, y, z);
		}

		EnsurePanCameraInitialized();
		return RuntimeCameraSnapshot.Create(
			RuntimeCameraMode.LayerPan,
			_panCameraX,
			_panCameraY,
			_panCameraZ,
			IsoCoordUtil.WorldToScreen(_panCameraFloatX, _panCameraFloatY, _panCameraZ));
	}

	/// <summary>
	/// 给"焦点切换 / 死亡"演出用：把摄像头从 (fromX,fromY,fromZ) 在 durationSec 秒内
	/// ease-out 缓动回到当前 active actor 位置。重复调用会重置为新一段缓动。
	/// </summary>
	public void BeginCinematicSweep(int fromX, int fromY, int fromZ, double durationSec)
	{
		_cinematicFromCell = (fromX, fromY, fromZ);
		_cinematicElapsedSec = 0;
		_cinematicDurationSec = Math.Max(0.05, durationSec);
	}

	public void TickCinematicSweep(double delta)
	{
		if (_cinematicFromCell == null) return;
		_cinematicElapsedSec += delta;
		if (_cinematicElapsedSec >= _cinematicDurationSec)
			CancelCinematicSweep();
	}

	public void CancelCinematicSweep()
	{
		_cinematicFromCell = null;
		_cinematicElapsedSec = 0;
		_cinematicDurationSec = 0;
	}

	public void ResetForSession()
	{
		Mode = RuntimeCameraMode.FollowActor;
		_panDragActive = false;
		CancelCinematicSweep();
		CenterOnActiveActor();
	}

	public bool BeginPanDragFromCurrentView()
	{
		if (Mode == RuntimeCameraMode.FollowActor)
			CenterOnActiveActor();
		else
			EnsurePanCameraInitialized();

		Mode = RuntimeCameraMode.LayerPan;
		_panDragActive = true;
		return true;
	}

	public bool ReturnToFollowActor()
	{
		var changed = Mode != RuntimeCameraMode.FollowActor || _panDragActive;
		CenterOnActiveActor();
		Mode = RuntimeCameraMode.FollowActor;
		_panDragActive = false;
		return changed;
	}

	public void CenterOnActiveActor()
	{
		var (x, y, z) = ResolveActiveActorPosition();
		_panCameraFloatX = x;
		_panCameraFloatY = y;
		_panCameraX = x;
		_panCameraY = y;
		_panCameraZ = z;
		_panCameraInitialized = true;
		_panDragActive = false;
	}

	public bool AdjustPanZ(int delta)
	{
		if (Mode != RuntimeCameraMode.LayerPan || delta == 0)
			return false;

		EnsurePanCameraInitialized();
		var next = Math.Clamp(_panCameraZ + delta, -MaxCameraLayer, MaxCameraLayer);
		if (next == _panCameraZ)
			return false;

		_panCameraZ = next;
		return true;
	}

	public bool PanByScreenDelta(Vector2 screenDelta)
	{
		if (!_panDragActive || Mode != RuntimeCameraMode.LayerPan)
			return false;

		EnsurePanCameraInitialized();
		if (screenDelta.LengthSquared() <= 0.0001f)
			return false;

		var currentScreen = IsoCoordUtil.WorldToScreen(_panCameraFloatX, _panCameraFloatY, _panCameraZ);
		var nextScreen = currentScreen - screenDelta;
		var (nextX, nextY) = IsoCoordUtil.ScreenToWorld(nextScreen, _panCameraZ);
		var previousCellX = _panCameraX;
		var previousCellY = _panCameraY;

		_panCameraFloatX = nextX;
		_panCameraFloatY = nextY;
		_panCameraX = Mathf.RoundToInt(nextX);
		_panCameraY = Mathf.RoundToInt(nextY);
		return previousCellX != _panCameraX
			|| previousCellY != _panCameraY
			|| !Mathf.IsEqualApprox(nextX, previousCellX)
			|| !Mathf.IsEqualApprox(nextY, previousCellY);
	}

	public bool EndPanDrag()
	{
		if (!_panDragActive)
			return false;

		_panDragActive = false;
		return true;
	}

	private void EnsurePanCameraInitialized()
	{
		if (_panCameraInitialized)
			return;

		CenterOnActiveActor();
	}

	private (int X, int Y, int Z) ResolveActiveActorPosition()
	{
		var actor = PartyModule.GetActiveActor(_state) ?? ActorModule.GetPlayer(_state);
		return actor == null
			? (_state.PlayerX, _state.PlayerY, _state.PlayerZ)
			: (actor.X, actor.Y, actor.Z);
	}
}
