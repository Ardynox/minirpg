using Godot;

namespace MiniRPG.Module;

internal readonly record struct ModalInputDecision(
	bool Handled,
	bool ConfirmRequested,
	bool CancelRequested);

internal static class ModalInputLogic
{
	public static ModalInputDecision HandleKey(InputEventKey key, bool blockConfirm)
	{
		return HandleKey(
			key.Pressed,
			key.Echo,
			key.AltPressed,
			key.CtrlPressed,
			key.MetaPressed,
			key.Keycode,
			blockConfirm);
	}

	internal static ModalInputDecision HandleKey(
		bool pressed,
		bool echo,
		bool altPressed,
		bool ctrlPressed,
		bool metaPressed,
		Key keycode,
		bool blockConfirm)
	{
		if (!pressed || echo || altPressed || ctrlPressed || metaPressed)
			return default;

		if (keycode == Key.Escape)
			return new ModalInputDecision(true, ConfirmRequested: false, CancelRequested: true);

		if (keycode is not (Key.Enter or Key.KpEnter))
			return default;

		if (blockConfirm)
			return new ModalInputDecision(true, ConfirmRequested: false, CancelRequested: false);

		return new ModalInputDecision(true, ConfirmRequested: true, CancelRequested: false);
	}
}
