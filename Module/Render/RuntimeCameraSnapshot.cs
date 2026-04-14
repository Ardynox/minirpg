using Godot;

namespace MiniRPG.Module.Render;

internal enum RuntimeCameraMode
{
	FollowActor,
	LayerPan,
}

internal readonly record struct RuntimeCameraSnapshot(
	RuntimeCameraMode Mode,
	int CenterX,
	int CenterY,
	int CenterZ,
	Vector2 ScreenCenterTarget)
{
	public static RuntimeCameraSnapshot Create(RuntimeCameraMode mode, int centerX, int centerY, int centerZ, Vector2 screenCenterTarget) =>
		new(mode, centerX, centerY, centerZ, screenCenterTarget);

	public static RuntimeCameraSnapshot Create(RuntimeCameraMode mode, int centerX, int centerY, int centerZ) =>
		new(mode, centerX, centerY, centerZ, IsoCoordUtil.WorldToScreen(centerX, centerY, centerZ));
}
