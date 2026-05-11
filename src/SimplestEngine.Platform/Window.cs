using SimplestEngine.Abi;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;

namespace SimplestEngine.Platform;

/// <summary>
/// Window flags driven by the Godot project file [display] section.
/// Maps 1:1 onto <see cref="WindowState"/> + Sdl2Window properties:
/// <list type="bullet">
///   <item><c>Fullscreen &amp;&amp; Borderless</c> → <see cref="WindowState.BorderlessFullScreen"/></item>
///   <item><c>Fullscreen</c> alone → <see cref="WindowState.FullScreen"/> (videomode change)</item>
///   <item><c>Borderless</c> in windowed mode → <c>Sdl.BorderVisible = false</c></item>
///   <item><c>Resizable</c> → <c>Sdl.Resizable</c></item>
/// </list>
/// </summary>
public sealed class WindowOptions
{
    public bool Resizable { get; init; } = true;
    public bool Borderless { get; init; }
    public bool Fullscreen { get; init; }
}

/// <summary>Engine window + Veldrid GraphicsDevice + input snapshot per frame.</summary>
public sealed class Window : IDisposable
{
    public Sdl2Window Sdl { get; }
    public GraphicsDevice GraphicsDevice { get; }

    public event Action<int, int>? Resized;
    public event Action<KeyEvent>? KeyEvent;
    public event Action<MouseEvent>? MouseEvent;
    public event Action<MouseMoveEventArgs>? MouseMove;
    public event Action? Closing;

    public bool Running { get; private set; }

    public Window(string title, int width, int height, GraphicsBackend? backend = null)
        : this(title, width, height, options: null, backend)
    { }

    public Window(string title, int width, int height,
                  WindowOptions? options, GraphicsBackend? backend = null)
    {
        options ??= new WindowOptions();

        var initialState = (options.Fullscreen, options.Borderless) switch
        {
            (true,  true)  => WindowState.BorderlessFullScreen,
            (true,  false) => WindowState.FullScreen,
            _              => WindowState.Normal,
        };

        var wci = new WindowCreateInfo
        {
            X = 100, Y = 100, WindowWidth = width, WindowHeight = height,
            WindowTitle = title,
            WindowInitialState = initialState,
        };
        var opts = new GraphicsDeviceOptions(
            debug: false,
            swapchainDepthFormat: null,
            syncToVerticalBlank: true,
            resourceBindingModel: ResourceBindingModel.Improved,
            preferDepthRangeZeroToOne: true,
            preferStandardClipSpaceYDirection: true);

        if (backend.HasValue)
        {
            VeldridStartup.CreateWindowAndGraphicsDevice(wci, opts, backend.Value, out var sdl, out var gd);
            Sdl = sdl; GraphicsDevice = gd;
        }
        else
        {
            VeldridStartup.CreateWindowAndGraphicsDevice(wci, opts, out var sdl, out var gd);
            Sdl = sdl; GraphicsDevice = gd;
        }

        // Apply the Sdl2Window-level flags. Resizable/BorderVisible affect both
        // windowed and (some platforms') fullscreen handling, so set them always.
        Sdl.Resizable     = options.Resizable;
        Sdl.BorderVisible = !options.Borderless;

        Sdl.Resized += OnResized;
        Sdl.KeyDown += e => KeyEvent?.Invoke(e);
        Sdl.KeyUp += e => KeyEvent?.Invoke(e);
        Sdl.MouseDown += e => MouseEvent?.Invoke(e);
        Sdl.MouseUp += e => MouseEvent?.Invoke(e);
        Sdl.MouseMove += e => MouseMove?.Invoke(e);
        Sdl.Closing += () => { Running = false; Closing?.Invoke(); };

        Running = true;
    }

    private void OnResized()
    {
        GraphicsDevice.ResizeMainWindow((uint)Sdl.Width, (uint)Sdl.Height);
        Resized?.Invoke(Sdl.Width, Sdl.Height);
    }

    public InputSnapshot PumpEvents() => Sdl.PumpEvents();

    public Vector2i Size => new(Sdl.Width, Sdl.Height);

    public void Dispose()
    {
        GraphicsDevice.Dispose();
        Sdl.Close();
    }
}
