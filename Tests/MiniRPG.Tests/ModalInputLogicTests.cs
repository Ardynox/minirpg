using Godot;
using MiniRPG.Module;
using Xunit;

namespace MiniRPG.Tests;

public sealed class ModalInputLogicTests
{
	[Fact]
	public void HandleKey_RequestsCancelOnEscape()
	{
		var result = ModalInputLogic.HandleKey(
			pressed: true,
			echo: false,
			altPressed: false,
			ctrlPressed: false,
			metaPressed: false,
			keycode: Key.Escape,
			blockConfirm: false);

		Assert.True(result.Handled);
		Assert.True(result.CancelRequested);
		Assert.False(result.ConfirmRequested);
	}

	[Theory]
	[InlineData(Key.Enter)]
	[InlineData(Key.KpEnter)]
	public void HandleKey_RequestsConfirmWhenEnterIsNotBlocked(Key keycode)
	{
		var result = ModalInputLogic.HandleKey(
			pressed: true,
			echo: false,
			altPressed: false,
			ctrlPressed: false,
			metaPressed: false,
			keycode: keycode,
			blockConfirm: false);

		Assert.True(result.Handled);
		Assert.True(result.ConfirmRequested);
		Assert.False(result.CancelRequested);
	}

	[Fact]
	public void HandleKey_DoesNotConfirmWhenBlocked()
	{
		var result = ModalInputLogic.HandleKey(
			pressed: true,
			echo: false,
			altPressed: false,
			ctrlPressed: false,
			metaPressed: false,
			keycode: Key.Enter,
			blockConfirm: true);

		Assert.True(result.Handled);
		Assert.False(result.ConfirmRequested);
		Assert.False(result.CancelRequested);
	}

	[Fact]
	public void HandleKey_IgnoresEchoAndModifierKeys()
	{
		var echo = ModalInputLogic.HandleKey(
			pressed: true,
			echo: true,
			altPressed: false,
			ctrlPressed: false,
			metaPressed: false,
			keycode: Key.Enter,
			blockConfirm: false);
		var ctrl = ModalInputLogic.HandleKey(
			pressed: true,
			echo: false,
			altPressed: false,
			ctrlPressed: true,
			metaPressed: false,
			keycode: Key.Enter,
			blockConfirm: false);

		Assert.False(echo.Handled);
		Assert.False(ctrl.Handled);
	}
}
