namespace MiniRPG.Core.AI.Utility;

public sealed class ConsiderationDef
{
	public string Input { get; set; } = "";
	public ResponseCurve Curve { get; set; } = new();
}
