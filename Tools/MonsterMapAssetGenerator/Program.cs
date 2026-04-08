using System.IO;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
MonsterMapAssetGenerator.Generate(root);
MonsterMapAssetGenerator.GenerateAnomalyPack(root);
