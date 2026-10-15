using Godot;
using GPS.Sim;
using System.Linq;

/// <summary>Godot 側の唯一の入口。シミュレーションの状態を描き、入力をコマンドに変換して渡すだけ。</summary>
public partial class Main : Node3D
{
    Simulation _sim = null!;
    TerrainRenderer _terrain = null!;
    Camera3D _cam = null!;
    Label _hud = null!;

    Vector3 _pivot = new(32, 6, 32);
    float _yaw = 0.8f, _elev = 0.9f, _dist = 60;
    byte _current = 1;
    int _frame;

    public override void _Ready()
    {
        Engine.PhysicsTicksPerSecond = Simulation.TicksPerSecond;
        _sim = new Simulation(BlockRegistry.FromJson(FileAccess.GetFileAsString("res://data/blocks.json")), GameData.Load(ProjectSettings.GlobalizePath("res://data")));
        WorldGen.Terrain(_sim.World, _sim.Blocks, 64, 64, 1);

        _terrain = new TerrainRenderer();
        AddChild(_terrain);
        _terrain.Init(_sim.World, _sim.Blocks);
        _terrain.BuildAll();

        _cam = new Camera3D { Far = 600 };
        AddChild(_cam);
        AddChild(SceneSetup.Lighting());
        _hud = new Label { Position = new Vector2(8, 8) };
        AddChild(_hud);
        UpdateCamera();
    }

    public override void _PhysicsProcess(double delta)
    {
        _sim.Step();
        _terrain.BuildDirty();
    }

    public override void _Process(double delta)
    {
        var move = Vector2.Zero;
        if (Input.IsPhysicalKeyPressed(Key.W)) move.Y += 1;
        if (Input.IsPhysicalKeyPressed(Key.S)) move.Y -= 1;
        if (Input.IsPhysicalKeyPressed(Key.D)) move.X += 1;
        if (Input.IsPhysicalKeyPressed(Key.A)) move.X -= 1;
        if (move != Vector2.Zero)
        {
            var fwd = -new Vector3(Mathf.Sin(_yaw), 0, Mathf.Cos(_yaw));
            var right = new Vector3(-fwd.Z, 0, fwd.X);
            _pivot += (fwd * move.Y + right * move.X) * (float)delta * _dist * 0.6f;
            UpdateCamera();
        }
        _hud.Text = $"Tick {_sim.Tick}  Block: {_sim.Blocks[_current].Name}  (1-{_sim.Blocks.Solid.Count()}: select / LMB: place / RMB: remove / MMB drag: orbit / wheel: zoom / WASD: pan)";

        // 動作確認用: `++ --screenshot <path> [--click x,y] [--rclick x,y]` でクリックを再生し、画面を保存して終了する
        var args = OS.GetCmdlineUserArgs();
        int si = System.Array.IndexOf(args, "--screenshot");
        if (si < 0) return;
        _frame++;
        if (_frame == 2)
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] is "--click" or "--rclick")
                {
                    var p = args[i + 1].Split(',');
                    Click(new Vector2(float.Parse(p[0]), float.Parse(p[1])), place: args[i] == "--click");
                }
        if (_frame == 30)
        {
            GetViewport().GetTexture().GetImage().SavePng(args[si + 1]);
            GetTree().Quit();
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { Pressed: true } mb:
                switch (mb.ButtonIndex)
                {
                    case MouseButton.WheelUp: _dist = Mathf.Max(5, _dist * 0.9f); UpdateCamera(); break;
                    case MouseButton.WheelDown: _dist = Mathf.Min(300, _dist / 0.9f); UpdateCamera(); break;
                    case MouseButton.Left: Click(mb.Position, place: true); break;
                    case MouseButton.Right: Click(mb.Position, place: false); break;
                }
                break;
            case InputEventMouseMotion mm when Input.IsMouseButtonPressed(MouseButton.Middle):
                _yaw -= mm.Relative.X * 0.005f;
                _elev = Mathf.Clamp(_elev + mm.Relative.Y * 0.005f, 0.15f, 1.5f);
                UpdateCamera();
                break;
            case InputEventKey { Pressed: true, Echo: false } k when k.Keycode >= Key.Key1 && k.Keycode <= Key.Key9:
                byte id = (byte)(k.Keycode - Key.Key1 + 1);
                if (_sim.Blocks.Solid.Any(d => d.Id == id)) _current = id;
                break;
        }
    }

    void Click(Vector2 screen, bool place)
    {
        var o = _cam.ProjectRayOrigin(screen);
        var d = _cam.ProjectRayNormal(screen);
        if (!Raycast.Cast(_sim.World, new System.Numerics.Vector3(o.X, o.Y, o.Z), new System.Numerics.Vector3(d.X, d.Y, d.Z), 600, out var hit, out var before)) return;
        _sim.Enqueue(place ? new PlaceBlock(before, _current) : new RemoveBlock(hit));
    }

    void UpdateCamera()
    {
        var offset = new Vector3(Mathf.Cos(_elev) * Mathf.Sin(_yaw), Mathf.Sin(_elev), Mathf.Cos(_elev) * Mathf.Cos(_yaw)) * _dist;
        _cam.Position = _pivot + offset;
        _cam.LookAt(_pivot);
    }
}
