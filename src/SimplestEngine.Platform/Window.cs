using SimplestEngine.Abi;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;

namespace SimplestEngine.Platform;

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
    {
        var wci = new WindowCreateInfo
        {
            X = 100, Y = 100, WindowWidth = width, WindowHeight = height,
            WindowTitle = title,
            WindowInitialState = WindowState.Normal,
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
