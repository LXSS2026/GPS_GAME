using Godot;

/// <summary>光と環境。ハード SF 調: 低い夕刻の太陽と冷たい環境光、暗い空、厚めの霧、彩度を落としコントラストを上げる。</summary>
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
            LightEnergy = 2.2f,
            LightColor = new Color(1.0f, 0.78f, 0.58f),   // 夕刻の暖色
            RotationDegrees = new Vector3(-36, 52, 0),     // 低い太陽 = 長い影
        });
        var sky = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color(0.05f, 0.07f, 0.12f),
            SkyHorizonColor = new Color(0.36f, 0.30f, 0.30f),
            GroundBottomColor = new Color(0.08f, 0.09f, 0.12f),
            GroundHorizonColor = new Color(0.30f, 0.27f, 0.28f),
            SunAngleMax = 30, SunCurve = 0.2f,
        };
        root.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Sky,
                Sky = new Sky { SkyMaterial = sky },
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.42f, 0.50f, 0.66f),   // 冷たい環境光
                AmbientLightEnergy = 1.0f,
                TonemapMode = Godot.Environment.ToneMapper.Aces,
                TonemapExposure = 1.0f,
                SsaoEnabled = true,
                SsaoRadius = 1.8f,
                SsaoIntensity = 2.6f,
                GlowEnabled = true,
                GlowIntensity = 0.9f,
                GlowBloom = 0.08f,
                GlowHdrThreshold = 0.9f,
                FogEnabled = true,
                FogLightColor = new Color(0.22f, 0.25f, 0.32f),
                FogDensity = 0.0024f,
                FogSkyAffect = 0.3f,
                AdjustmentEnabled = true,
                AdjustmentSaturation = 0.68f,
                AdjustmentContrast = 1.18f,
                AdjustmentBrightness = 1.05f,
            }
        });
        // 周辺減光(画面端を暗くする薄い板)
        var layer = new CanvasLayer();
        var rect = new ColorRect { AnchorRight = 1, AnchorBottom = 1, MouseFilter = Control.MouseFilterEnum.Ignore };
        var shader = new Shader
        {
            Code = "shader_type canvas_item;\nvoid fragment(){ vec2 d = UV - vec2(0.5); float v = smoothstep(0.35, 0.95, length(d) * 1.45); COLOR = vec4(0.0, 0.0, 0.02, v * 0.55); }"
        };
        rect.Material = new ShaderMaterial { Shader = shader };
        layer.AddChild(rect);
        root.AddChild(layer);
        return root;
    }
}
