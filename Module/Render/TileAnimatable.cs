using Godot;

namespace MiniRPG.Module.Render;

/// <summary>
/// 静态 tile 图片的 IAnimatable 空实现（no-op）。
/// 用于尚未替换为 Spine 的实体，保持接口统一。
/// Play / Stop 等方法为空操作，不会报错。
/// </summary>
public class TileAnimatable : IAnimatable
{
	public static readonly TileAnimatable Null = new(null!);

	private readonly Node2D? _node;

	public Node2D Node => _node!;
	public bool Ready => _node != null;

	public TileAnimatable(Node2D? node)
	{
		_node = node;
	}

	public void Play(string animName, bool loop = true) { }
	public void PlayOneShot(string animName, string thenAnim = "Idle") { }
	public void Stop() { }

	public void SetFacing(int dx)
	{
		if (_node == null || dx == 0) return;
		var s = _node.Scale;
		_node.Scale = new Vector2(dx < 0 ? -Mathf.Abs(s.X) : Mathf.Abs(s.X), s.Y);
	}
}
