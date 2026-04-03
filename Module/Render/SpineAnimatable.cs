using Godot;

namespace MiniRPG.Module.Render;

/// <summary>
/// Spine 骨骼动画的 IAnimatable 实现。
/// 包装 SpineSprite 节点，通过 GodotObject.Call 调用 Spine GDExtension API。
/// </summary>
public class SpineAnimatable : IAnimatable
{
	private readonly Node2D _spine;
	private string _currentAnim = "";
	private bool _ready;

	public Node2D Node => _spine;
	public bool Ready => _ready;

	public SpineAnimatable(Node2D spineNode)
	{
		_spine = spineNode;
		_ready = spineNode != null && IsSpineValid(spineNode);
	}

	public void Play(string animName, bool loop = true)
	{
		if (!_ready) return;
		if (animName == _currentAnim && loop) return;

		_currentAnim = animName;
		var animState = (GodotObject)_spine.Call("get_animation_state");
		animState.Call("set_animation", animName, loop, 0);
	}

	public void PlayOneShot(string animName, string thenAnim = "Idle")
	{
		Play(animName, false);
		if (!_ready) return;

		var animState = (GodotObject)_spine.Call("get_animation_state");
		animState.Call("add_animation", thenAnim, 0f, true, 0);
		_currentAnim = thenAnim;
	}

	public void Stop()
	{
		if (!_ready) return;
		var animState = (GodotObject)_spine.Call("get_animation_state");
		animState.Call("clear_tracks");
		_currentAnim = "";
	}

	public void SetFacing(int dx)
	{
		if (dx == 0) return;
		var s = _spine.Scale;
		_spine.Scale = new Vector2(dx < 0 ? -Mathf.Abs(s.X) : Mathf.Abs(s.X), s.Y);
	}

	private static bool IsSpineValid(Node2D node)
	{
		try
		{
			node.Call("get_animation_state");
			return true;
		}
		catch
		{
			return false;
		}
	}
}
