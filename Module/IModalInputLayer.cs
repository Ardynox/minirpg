using Godot;

namespace MiniRPG.Module;

public interface IModalInputLayer
{
	bool Visible { get; }
	void Close();
	bool HandleKeyInput(InputEventKey key);
	bool HandleMouseInput(InputEvent @event) => false;
}
