using System.Text.Json;

namespace GPS.Sim;

/// <summary>定義データ(game/data の json)。コードに定数を埋めない。</summary>
public sealed class GameData
{
    public readonly IReadOnlyDictionary<string, ItemDef> Items;
    public readonly IReadOnlyDictionary<string, StructureDef> Structures;
    public readonly IReadOnlyDictionary<string, RecipeDef> Recipes;
    public readonly IReadOnlyDictionary<string, int> Infrastructure;   // id → 解放される段階(techLevel)
    public readonly int ReactorPrecisionMin;        // 品質 0 のときの出力(%)
    public readonly int DefectReductionPerSkill;    // 担当者の技能 1 につき失敗率を下げる割合(%)
    public readonly int BatteryChargeEfficiency;     // 充電効率(%)
    public readonly int[] TierPriceMul = { 30, 100, 300, 1000 };   // 階級ごとの価格倍率(%)
    public int ImportMul = 150;             // 輸入価格の倍率(%)
    public int ImportQuality = 60;          // 輸入品の品質
    public int RobotNormalBias = 70;        // 製造ロボットの通常品質への偏り(%)
    public int OverloadMax = 150, OverloadLimit = 900_000, OverloadRecoveryMul = 1;   // 過負荷の上限(%)、爆発する負荷(%·ティック)、回復の倍率
    public double BlastRadiusPerKw = 0.02;   // 爆発の半径(セル / kW)
    public long MechBatteryCapacity = 1_800_000;   // 機体のバッテリー容量(kW·ティック。mech_parts.json の powerSources.battery)

    static readonly JsonSerializerOptions Opt = new() { PropertyNameCaseInsensitive = true };

    GameData(Dictionary<string, ItemDef> items, Dictionary<string, StructureDef> structures, Dictionary<string, RecipeDef> recipes, Dictionary<string, int> infra, int precisionMin, int defectRed, int chargeEff)
    { Items = items; Structures = structures; Recipes = recipes; Infrastructure = infra; ReactorPrecisionMin = precisionMin; DefectReductionPerSkill = defectRed; BatteryChargeEfficiency = chargeEff; }

    public static GameData Load(string dir)
    {
        var items = JsonSerializer.Deserialize<List<ItemDef>>(File.ReadAllText(Path.Combine(dir, "items.json")), Opt)!.ToDictionary(i => i.Id);
        using var sdoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "structures.json")));
        var structures = new Dictionary<string, StructureDef>();
        foreach (var s in sdoc.RootElement.GetProperty("structures").EnumerateArray())
        {
            var def = JsonSerializer.Deserialize<StructureDef>(s.GetRawText(), Opt)!;
            structures[def.Id] = def;
        }
        int precisionMin = sdoc.RootElement.GetProperty("reactorPrecision").GetProperty("precisionMin").GetInt32();
        int defectRed = sdoc.RootElement.GetProperty("staff").GetProperty("defectReductionPerSkill").GetInt32();
        int chargeEff = sdoc.RootElement.TryGetProperty("batteryChargeEfficiency", out var ce) ? ce.GetInt32() : 100;
        using var rdoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "recipes.json")));
        var recipes = new Dictionary<string, RecipeDef>();
        foreach (var r in rdoc.RootElement.GetProperty("recipes").EnumerateArray())
        {
            var def = JsonSerializer.Deserialize<RecipeDef>(r.GetRawText(), Opt)!;
            foreach (var i in def.Inputs) if (!items.ContainsKey(i.Item)) throw new InvalidDataException($"recipe {def.Id}: unknown item {i.Item}");
            foreach (var o in def.Outputs) if (!items.ContainsKey(o.Item)) throw new InvalidDataException($"recipe {def.Id}: unknown item {o.Item}");
            recipes[def.Id] = def;
        }
        using var idoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "infrastructure.json")));
        var infra = idoc.RootElement.GetProperty("infrastructure").EnumerateArray().ToDictionary(e => e.GetProperty("id").GetString()!, e => e.TryGetProperty("techLevel", out var tl) ? tl.GetInt32() : 1);
        var gd = new GameData(items, structures, recipes, infra, precisionMin, defectRed, chargeEff);
        if (sdoc.RootElement.TryGetProperty("trade", out var tr))
        {
            foreach (var kv in tr.GetProperty("tierMul").EnumerateObject()) gd.TierPriceMul[int.Parse(kv.Name)] = kv.Value.GetInt32();
            gd.ImportMul = tr.GetProperty("importMul").GetInt32(); gd.ImportQuality = tr.GetProperty("importQuality").GetInt32();
        }
        if (sdoc.RootElement.TryGetProperty("robot", out var rb)) gd.RobotNormalBias = rb.GetProperty("normalBias").GetInt32();
        if (sdoc.RootElement.TryGetProperty("overload", out var ov))
        {
            gd.OverloadMax = ov.GetProperty("max").GetInt32(); gd.OverloadLimit = ov.GetProperty("limit").GetInt32();
            gd.OverloadRecoveryMul = ov.GetProperty("recoveryMul").GetInt32(); gd.BlastRadiusPerKw = ov.GetProperty("radiusPerKw").GetDouble();
        }
        var mp = Path.Combine(dir, "mech_parts.json");
        if (File.Exists(mp)) { using var md = JsonDocument.Parse(File.ReadAllText(mp)); if (md.RootElement.TryGetProperty("powerSources", out var psrc)) gd.MechBatteryCapacity = psrc.GetProperty("battery").GetProperty("capacity").GetInt64(); }
        return gd;
    }

    /// <summary>テストや道具から: カレントから上へ辿って game/data を探す。</summary>
    public static string FindDataDir(string? start = null)
    {
        var dir = new DirectoryInfo(start ?? AppContext.BaseDirectory);
        while (dir != null)
        {
            var cand = Path.Combine(dir.FullName, "game", "data", "items.json");
            if (File.Exists(cand)) return Path.GetDirectoryName(cand)!;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("game/data not found above " + (start ?? AppContext.BaseDirectory));
    }
}
