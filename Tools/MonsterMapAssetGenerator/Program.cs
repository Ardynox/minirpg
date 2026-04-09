using System;
using System.IO;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var command = args.Length > 0 ? args[0].Trim().ToLowerInvariant() : "generate";

switch (command)
{
    case "generate":
        MonsterMapAssetGenerator.Generate(root);
        MonsterMapAssetGenerator.GenerateAnomalyPack(root);
        Console.WriteLine("Generated base and anomaly monster map assets.");
        break;

    case "validate-iso8":
        var reportPath = MonsterMapAssetGenerator.ValidateIso8Assets(root);
        Console.WriteLine($"ISO8 validation report generated: {reportPath}");
        break;

    default:
        Console.Error.WriteLine("Unknown command. Use one of: generate, validate-iso8");
        Environment.ExitCode = 1;
        break;
}
