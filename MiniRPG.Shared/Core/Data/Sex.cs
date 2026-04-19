namespace MiniRPG.Core.Data;

/// <summary>
/// 生物性别。当前只用于受孕 / 出生 / 人口学统计。
/// 放在 <c>MiniRPG.Core.Data</c> 命名空间是为了让 <see cref="Actor"/> 不需要新增 using。
/// </summary>
public enum Sex
{
	Female = 0,
	Male = 1,
}
