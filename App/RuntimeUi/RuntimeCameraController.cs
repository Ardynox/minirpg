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

	public RuntimeCameraController(GameState state)
	{
		_state = state;
		ResetForSession();
	}

	public RuntimeCameraMode Mode { get; private set; }

	public bool IsPanDragActive => _panDragActive;

	public RuntimeCameraSnapshot BuildSnapshot()
	{
		if (Mode == RuntimeCameraMode.FollowActor)
		{
			var (x, y, z) = ResolveActiveActorPosition();
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

	public void ResetForSession()
	{
		Mode = RuntimeCameraMode.FollowActor;
		_panDragActive = false;
		CenterOnActiveActor();
	}

	public bool ToggleMode()
	{
		Mode = Mode == RuntimeCameraMode.FollowActor
			? RuntimeCameraMode.LayerPan
			: RuntimeCameraMode.FollowActor;
		EnsurePanCameraInitialized();
		_panDragActive = false;
		return true;
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

	public bool BeginPanDrag()
	{
		if (Mode != RuntimeCameraMode.LayerPan)
			return false;

		EnsurePanCameraInitialized();
		_panDragActive = true;
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
