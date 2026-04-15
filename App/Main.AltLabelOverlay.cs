namespace MiniRPG;

public partial class Main
{
	private void BindAltLabelOverlay()
	{
		_altLabelOverlayController.Bind();
	}

	private void TickAltLabelOverlay(RuntimeUiModeSnapshot snapshot)
	{
		_altLabelOverlayController.Tick(snapshot);
	}
}
