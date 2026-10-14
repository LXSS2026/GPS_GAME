using Godot;

/// <summary>光と環境(方向光の影・空・SSAO・グロー・霧・トーンマップ)。G8 第 7 節の陰影を軽量な設定で入れる。</summary>
public static class SceneSetup
{
    public static Node3D Lighting()
    {
        var root = new Node3D { Name = "Lighting" };
        root.AddChild(new DirectionalLight3D
        {
            ShadowEnabled = true,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel2Splits,
            DirectionalShadowMaxDistance = 220,
            LightEnergy = 1.6f,
            LightColor = new Color(1.0f, 0.96f, 0.9f),
            RotationDegrees = new Vector3(-52, 38, 0),
        });
        var sky = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color(0.33f, 0.52f, 0.78f),
            SkyHorizonColor = new Color(0.72f, 0.80f, 0.90f),
            GroundBottomColor = new Color(0.40f, 0.52f, 0.66f),
            GroundHorizonColor = new Color(0.62f, 0.74f, 0.86f),
        };
        root.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Sky,
                Sky = new Sky { SkyMaterial = sky },
                AmbientLightSource = Godot.Environment.AmbientSource.Sky,
                AmbientLightSkyContribution = 0.6f,
                AmbientLightEnergy = 1.0f,
                TonemapMode = Godot.Environment.ToneMapper.Filmic,
                TonemapExposure = 1.15f,
                SsaoEnabled = true,
                SsaoRadius = 1.5f,
                SsaoIntensity = 2.0f,
                GlowEnabled = true,
                GlowIntensity = 0.5f,
                GlowBloom = 0.05f,
                GlowHdrThreshold = 1.0f,
                FogEnabled = true,
                FogLightColor = new Color(0.72f, 0.80f, 0.90f),
                FogDensity = 0.0007f,
                FogSkyAffect = 0.0f,
            }
        });
        return root;
    }
}
