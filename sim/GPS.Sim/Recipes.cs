namespace GPS.Sim;

public sealed record RecipeInput(string Item, int Count, QualityTier MinTier);
public sealed record RecipeOutput(string Item, int Count);

/// <summary>レシピ。Machine は設備の kind。ScrapRate は失敗時に回収する原材料の割合(1/10,000。既定 7,000 = 7 割)。品質の倍率はパーセント。</summary>
public sealed record RecipeDef(string Id, string Name, string Machine, int TechLevel, IReadOnlyList<RecipeInput> Inputs, IReadOnlyList<RecipeOutput> Outputs,
                               int Ticks, int ScrapRate = 7000, int QualityTopMul = 100, int QualityHighMul = 100, int QualityDefectMul = 100,
                               int MechGeneration = 0, string? PowerSource = null);
