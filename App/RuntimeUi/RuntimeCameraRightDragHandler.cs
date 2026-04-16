using Godot;
using MiniRPG.Module.Panel;

namespace MiniRPG;

internal sealed class RuntimeCameraRightDragHandler
{
	private const float DragStartThreshold = 4f;

	private readonly PanelManager _panels;

	private bool _pending;
	private bool _startedOnMap;
	private bool _promotedToPan;
	private Vector2 _pressGlobalPosition;

	public RuntimeCameraRightDragHandler(PanelManager panels)
	{
		_panels = panels;
	}

	public bool IsPending => _pending;
	public bool IsPromotedToPan => _promotedToPan;
	public Vector2 PressGlobalPosition => _pressGlobalPosition;

	public bool TryBegin(
		Vector2 globalPosition,
		IPanel? hit,
		IsometricVoxelRenderer? mapRender)
	{
		if (mapRender == null)
			return false;
		if (hit != null && hit.PanelId != "map")
			return false;
		if (!mapRender.TryGetWorldCellFromGlobalPosition(globalPosition, out _))
			return false;

		_pending = true;
		_startedOnMap = true;
		_promotedToPan = false;
		_pressGlobalPosition = globalPosition;
		return true;
	}

	public bool TryPromoteToPan(
		InputEventMouseMotion motion,
		RuntimeCameraController? cameraController)
	{
		if (cameraController == null
			|| !_pending
			|| !_startedOnMap
			|| _promotedToPan)
		{
			return false;
		}

		if (!IsRightMouseButtonPressed(motion.ButtonMask)
			&& !Input.IsMouseButtonPressed(MouseButton.Right))
		{
			return false;
		}

		if (motion.GlobalPosition.DistanceSquaredTo(_pressGlobalPosition)
			< DragStartThreshold * DragStartThreshold)
		{
			return false;
		}

		_promotedToPan = cameraController.BeginPanDragFromCurrentView();
		if (_promotedToPan)
			_panels.SetFocus("map");
		return _promotedToPan;
	}

	public void Reset(RuntimeCameraController? cameraController, bool endPanDrag = false)
	{
		if (endPanDrag)
			cameraController?.EndPanDrag();

		_pending = false;
		_startedOnMap = false;
		_promotedToPan = false;
		_pressGlobalPosition = Vector2.Zero;
	}

	private static bool IsRightMouseButtonPressed(MouseButtonMask buttonMask) =>
		(buttonMask & MouseButtonMask.Right) != 0;
}
