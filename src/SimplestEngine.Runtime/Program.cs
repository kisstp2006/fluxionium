using System.Diagnostics;
using SimplestEngine;
using SimplestEngine.Abi;
using SimplestEngine.Physics.Aether;
using SimplestEngine.Platform;
using SimplestEngine.Rendering.Veldrid;
using SimplestEngine.Resources;
using SimplestEngine.Scripting;
using SimplestEngine.Scripting.Lua;
using SimplestEngine.Servers;
using SixLabors.ImageSharp;

// --- 1. Locate project ------------------------------------------------------
BuiltInClasses.Register();

// Wire the engine default font hook (Noto Sans Regular, embedded in
// SimplestEngine.Resources) into Scene's Font abstract so Label etc. resolve
// .GetDefault(size) to a real TTF instead of the emergency 5x7 bitmap.
SimplestEngine.Font.DefaultFontFactory = size => EngineDefaults.GetDefaultFont(size);
Console.WriteLine("[engine] default font: Noto Sans Regular (embedded, OFL)");

var projectRoot = args.Length > 0 ? args[0]
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "hello"));
Console.WriteLine($"[engine] project root: {projectRoot}");

// Generate demo-only sprite asset if it is missing. Keeps the godot_proj sample
// self-contained without shipping a binary PNG inside the source repo.
EnsureSampleSprite(projectRoot);

// Detect a Godot-style project.godot. If absent we still run as a flat folder.
var projectFile = Path.Combine(projectRoot, "project.godot");
var cfg = File.Exists(projectFile)
    ? GodotProjectConfig.Load(projectFile)
    : new GodotProjectConfig();
if (File.Exists(projectFile))
    Console.WriteLine($"[engine] project.godot detected - title='{cfg.Title}', main_scene='{cfg.MainScene}'");

// Godot 3 historical default window size. Only used if project.godot has no
// [display] section at all - otherwise cfg.WindowWidth/Height come from the file.
var windowW = cfg.HasDisplaySection ? cfg.WindowWidth : GodotProjectConfig.Godot3DefaultWidth;
var windowH = cfg.HasDisplaySection ? cfg.WindowHeight : GodotProjectConfig.Godot3DefaultHeight;
if (!cfg.HasDisplaySection && File.Exists(projectFile))
    Console.WriteLine($"[engine] no [display] section - falling back to Godot 3 default {windowW}x{windowH}");

// --- 2. Bootstrap servers ---------------------------------------------------
var winOpts = new WindowOptions
{
    Resizable  = cfg.WindowResizable,
    Borderless = cfg.WindowBorderless,
    Fullscreen = cfg.WindowFullscreen,
};
using var window = new Window(cfg.Title, windowW, windowH, winOpts);
Console.WriteLine($"[engine] window mode: fullscreen={cfg.WindowFullscreen} borderless={cfg.WindowBorderless} resizable={cfg.WindowResizable} size={window.Size.X}x{window.Size.Y}");

using var backend = new VeldridBackend(window.GraphicsDevice);
var renderingServer = new RenderingServerDefault(backend);

// Boot splash: per Godot 3 semantics paint the framebuffer with
// [application] boot_splash/bg_color while autoloads + main scene initialize,
// then switch to [rendering] environment/default_clear_color for normal play.
if (cfg.BootSplashBgColor is { } splash)
{
    renderingServer.SetClearColor(splash);
    window.PumpEvents();
    renderingServer.Frame();
    Console.WriteLine($"[engine] boot splash bg_color=({splash.R:0.##}, {splash.G:0.##}, {splash.B:0.##}, {splash.A:0.##}) painted");
}

var physics = new AetherBackend();
var input = new InputServer();

// Strict Godot parity: project.godot [input] is the single source of truth for any
// action it lists. We only add the engine's built-in ui_* fallbacks for actions
// the project did NOT define - matching Godot 3's behaviour where InputMap entries
// override the default bindings completely (even when the project-defined event
// list is empty).
var engineDefaults = new (string Name, KeyCode Key)[]
{
    ("ui_left",   KeyCode.Left),
    ("ui_right",  KeyCode.Right),
    ("ui_up",     KeyCode.Up),
    ("ui_down",   KeyCode.Down),
    ("ui_accept", KeyCode.Space),
    ("ui_cancel", KeyCode.Escape),
};
foreach (var (name, key) in engineDefaults)
{
    if (cfg.InputActions.ContainsKey(name)) continue;
    input.AddKeyEvent(StringName.Get(name), key);
}

// Apply user-defined Godot InputMap (project.godot [input] section).
if (cfg.InputActions.Count > 0)
{
    InputMapImporter.Apply(cfg, input);
    Console.WriteLine($"[engine] imported {cfg.InputActions.Count} InputMap action(s) from project.godot");
}

var resources = new ResourceLoader(projectRoot) { Rendering = renderingServer };
var tree = new SceneTree
{
    RenderingServer = renderingServer,
    PhysicsBackend = physics,
    InputServer = input,
};
var engineAPI = new EngineAPI(tree);

var scriptServer = new ScriptServer();
var luaLang = new LuaScriptLanguage { Engine = engineAPI };
scriptServer.Register(luaLang);

// --- 3. Autoloads (Godot project [autoload]) --------------------------------
// We instantiate them BEFORE the main scene so they behave like singletons that
// outlive scene changes (matching Godot semantics). Godot supports two flavours:
//   - scene autoload:  GlobalState="res://global_state.tscn"      (PackedScene)
//   - script autoload: GlobalState="*res://global_state.gd"       (bare Node + script)
foreach (var auto in cfg.Autoloads)
{
    try
    {
        var res = resources.Load(auto.Path);
        Node? autoloadRoot = res switch
        {
            PackedScene ps        => InstantiateScene(ps),
            ScriptResource sres   => InstantiateScriptAutoload(sres),
            ScriptSourceResource ss => WarnDormantScript(auto.Name, ss),
            _ => null,
        };
        if (autoloadRoot is not null)
        {
            autoloadRoot.Name = StringName.Get(auto.Name);
            tree.Root.AddChild(autoloadRoot);
            Console.WriteLine($"[engine] autoload '{auto.Name}' loaded from {auto.Path}");
        }
        else
        {
            Console.WriteLine($"[engine] autoload '{auto.Name}' could not be instantiated (path={auto.Path}).");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[engine] autoload '{auto.Name}' failed: {ex.Message}");
    }
}

// --- 4. Load main scene -----------------------------------------------------
var mainScenePath = cfg.MainScene ?? "res://main.tscn";
var loaded = resources.Load(mainScenePath);
if (loaded is PackedScene packed)
{
    var root = InstantiateScene(packed);
    if (root is not null)
    {
        tree.ChangeSceneTo(root);
        Console.WriteLine($"[engine] loaded {mainScenePath} root='{root.DisplayName}' children={root.Children.Count}");
    }
}
else
{
    var fallback = new Node2D { Name = StringName.Get("Root") };
    fallback.RenderingServer = renderingServer;
    tree.ChangeSceneTo(fallback);
    Console.WriteLine($"[engine] no main scene at {mainScenePath} - empty scene started.");
}

// Boot splash is over: switch to the runtime clear color (Godot's
// [rendering] environment/default_clear_color, falls through to dark grey
// when the project file omitted the key).
if (cfg.ClearColor is { } cc)
{
    renderingServer.SetClearColor(cc);
    Console.WriteLine($"[engine] applied [rendering] default_clear_color ({cc.R:0.##}, {cc.G:0.##}, {cc.B:0.##}, {cc.A:0.##})");
}

// --- 5. Main loop -----------------------------------------------------------
var stopwatch = Stopwatch.StartNew();
double lastTime = 0;
int frame = 0;

while (window.Sdl.Exists)
{
    var snap = window.PumpEvents();
    input.UpdateFromVeldrid(snap);

    double now = stopwatch.Elapsed.TotalSeconds;
    var delta = (float)Math.Min(0.1, now - lastTime);
    lastTime = now;

    tree.Process(delta);

    frame++;
    if (frame % 60 == 0)
        window.Sdl.Title = $"{cfg.Title} - {1.0 / Math.Max(0.0001, delta):F0} FPS";
}

Node? InstantiateScene(PackedScene ps)
{
    ps.ScriptServer = scriptServer;
    return ps.Instance(renderingServer, resources);
}

Node? InstantiateScriptAutoload(ScriptResource sres)
{
    // Godot's '*res://x.gd' autoload form: spawn a bare Node and attach the script.
    // Matches scene autoload semantics so the resulting tree position is identical.
    var holder = new Node();
    var instance = sres.Script.Instantiate(holder);
    holder.SetScript(instance);
    return holder;
}

static Node? WarnDormantScript(string autoloadName, ScriptSourceResource src)
{
    // Source-only script (e.g. a .gd file we can't run yet): keep a stub Node
    // so other code referencing autoload-by-name still resolves, but log loudly
    // that the singleton is dormant.
    Console.WriteLine($"[engine] autoload '{autoloadName}' has a {src.Language} script ({src.SourceFile}); language not active. Stub Node attached.");
    return new Node();
}

static void EnsureSampleSprite(string root)
{
    var target = Path.Combine(root, "player.png");
    if (File.Exists(target)) return;
    try
    {
        // Generate a 16x16 pixel-art "knight" head so the demo has a visible Sprite
        // without checking a binary file into the repo. The 1/0/2 grid below maps to
        // (transparent / outline / fill) - simple but recognisably a character.
        var sprite = new[,]
        {
            {0,0,0,0,1,1,1,1,1,1,1,1,0,0,0,0},
            {0,0,0,1,2,2,2,2,2,2,2,2,1,0,0,0},
            {0,0,1,2,2,2,2,2,2,2,2,2,2,1,0,0},
            {0,1,2,2,2,2,2,2,2,2,2,2,2,2,1,0},
            {0,1,2,2,1,1,2,2,2,2,1,1,2,2,1,0},
            {0,1,2,2,1,0,2,2,2,2,0,1,2,2,1,0},
            {0,1,2,2,2,2,2,2,2,2,2,2,2,2,1,0},
            {0,1,2,2,2,2,2,2,2,2,2,2,2,2,1,0},
            {0,1,2,2,1,2,2,2,2,2,2,1,2,2,1,0},
            {0,1,2,2,2,1,1,1,1,1,1,2,2,2,1,0},
            {0,0,1,2,2,2,2,2,2,2,2,2,2,1,0,0},
            {0,0,0,1,2,2,2,2,2,2,2,2,1,0,0,0},
            {0,0,0,1,2,2,1,1,1,1,2,2,1,0,0,0},
            {0,0,0,1,2,2,1,0,0,1,2,2,1,0,0,0},
            {0,0,0,1,2,2,1,0,0,1,2,2,1,0,0,0},
            {0,0,0,0,1,1,0,0,0,0,1,1,0,0,0,0},
        };
        using var img = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(16, 16);
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
            {
                var px = sprite[y, x] switch
                {
                    1 => new SixLabors.ImageSharp.PixelFormats.Rgba32(20, 30, 50, 255),
                    2 => new SixLabors.ImageSharp.PixelFormats.Rgba32(220, 180, 90, 255),
                    _ => new SixLabors.ImageSharp.PixelFormats.Rgba32(0, 0, 0, 0),
                };
                img[x, y] = px;
            }
        img.SaveAsPng(target);
        Console.WriteLine($"[engine] generated sample sprite -> {target}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[engine] sample sprite generation failed: {ex.Message}");
    }
}
