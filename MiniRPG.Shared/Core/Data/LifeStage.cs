namespace MiniRPG.Core.Data;

/// <summary>
/// 生命阶段。按年龄阈值由 <c>LifeStageCatalog.GetLifeStage</c> 计算。
/// 放在 <c>MiniRPG.Core.Data</c> 命名空间是为了让 <see cref="Actor"/> 不需要新增 using。
/// </summary>
public enum LifeStage
{
	Infant,
	Child,
	Adolescent,
	Adult,
	Elder,
}
