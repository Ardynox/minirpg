using System.Collections.Generic;

namespace MiniRPG.Core;

/// <summary>
/// 动作定义注册表：从 PresetDB 读取，不再硬编码。
/// </summary>
public static class ActionDefs
{
	public static List<ActionDef> All => PresetDB.Actions;
}
