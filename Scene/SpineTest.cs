using Godot;

public partial class SpineTest : Node2D
{
	public override void _Ready()
	{
		var sprite = GetNode<Node2D>("Balin");
		var dataRes = (GodotObject)sprite.Get("skeleton_data_res");
		var animations = (Godot.Collections.Array)dataRes.Call("get_animations");
		GD.Print($"[SpineTest] 动画数量: {animations.Count}");
		foreach (var anim in animations)
		{
			var name = (string)((GodotObject)anim).Call("get_name");
			GD.Print($"  - {name}");
		}

		var skins = (Godot.Collections.Array)dataRes.Call("get_skins");
		GD.Print($"[SpineTest] 皮肤数量: {skins.Count}");
		foreach (var skin in skins)
		{
			var name = (string)((GodotObject)skin).Call("get_name");
			GD.Print($"  - {name}");
		}
	}
}
