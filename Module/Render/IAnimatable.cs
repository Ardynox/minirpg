using Godot;

namespace MiniRPG.Module.Render;

/// <summary>
/// 可动画实体的统一抽象。业务层通过此接口控制动画，
/// 不关心底层是 SpineSprite、AnimatedSprite2D 还是静态 Sprite2D。
/// </summary>
public interface IAnimatable
{
	Node2D Node { get; }

	/// <summary>播放动画。loop=true 为循环动画（Idle/Walk），false 为单次。</summary>
	void Play(string animName, bool loop = true);

	/// <summary>播放一次性动画，结束后自动切回 <paramref name="thenAnim"/>。</summary>
	void PlayOneShot(string animName, string thenAnim = "Idle");

	/// <summary>停止当前动画。</summary>
	void Stop();

	/// <summary>根据移动方向设置朝向（正值朝右，负值朝左，0 不变）。</summary>
	void SetFacing(int dx);

	/// <summary>当前是否已就绪（资源已加载可以播放）。</summary>
	bool Ready { get; }
}
