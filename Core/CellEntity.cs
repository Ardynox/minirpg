using System.Collections.Generic;

namespace MiniRPG.Core;

/// <summary>
/// 格子上的实体类型，数值 = 渲染优先级（越大越靠上）。
/// 同一格子可堆叠多个不同类型的实体，渲染时取优先级最高的 Glyph。
/// </summary>
// REVIEW: Actor = 50 在 MAP_REFACTOR.md 中定义但枚举里缺失。
//         按设计文档 Actor 不存在栈中所以枚举里不需要，
//         但 MAP_REFACTOR.md 第 64 行列了 Actor = 50，文档与代码不一致应同步修正。
public enum CellEntityType
{
	Terrain = 0,    // 地形：墙、地面、水、岩浆
	Fixture   = 10, // 设施：楼梯、巢穴、门、房屋
	Container = 15, // 容器：宝箱、箱子
	Item      = 20, // 掉落物：地上的物品
	Hazard  = 30,   // 危险物：陷阱、毒雾、火焰
	Corpse  = 40,   // 尸体/残骸
	Effect  = 60,   // 视觉效果：爆炸、魔法光环（临时）
}

/// <summary>
/// 格子栈中的一个实体。轻量值对象，可 JSON 序列化。
/// Terrain/Fixture 是内联数据（自包含）；Item 的 EntityId 引用掉落物表（未来扩展）。
/// Actor 不存在栈中——渲染时从 GameState.Actors 动态查询。
/// </summary>
// REVIEW: CellEntity 是 class 而非 struct / record，
//         深拷贝时需要手动复制每个字段（见 SaveModule.CopyCellEntity）。
//         如果改为 record 可自动获得值语义的相等比较，且拷贝更简洁。
public class CellEntity
{
	public CellEntityType Type { get; set; }

	/// <summary>渲染用字符（"#" / "." / ">" / "N" 等）。</summary>
	public string Glyph { get; set; } = "";

	/// <summary>
	/// 语义标识，用于逻辑判断（不是显示字符）。
	/// Terrain: "wall" / "floor"
	/// Fixture: "stair_down" / "stair_up" / "nest" / "door" / "house" / "item"
	/// </summary>
	// REVIEW: EntityId 使用裸字符串，没有枚举或常量约束。
	//         拼写错误（如 "stair_Down"）不会在编译期被捕获，
	//         建议引入 const 字符串集或 EntityId 枚举。
	public string EntityId { get; set; } = "";

	/// <summary>可选附加数据（如巢穴的额外配置）。目前未被使用。</summary>
	public Dictionary<string, string>? Meta { get; set; }
}
