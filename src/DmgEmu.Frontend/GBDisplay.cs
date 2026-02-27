using Gtk;
using Cairo;
using System;
using DmgEmu.Core;

namespace DmgEmu.Frontend
{
public class GBDisplay : IDisplay
{
    private Window window;
    private DrawingArea canvas;
    private IFrameBuffer framebuffer;
    private int pixelSize = 3;
    private DateTime lastFrame = DateTime.MinValue;
    private readonly TimeSpan frameTime = TimeSpan.FromSeconds(1.0 / 59.73);

    // DMG palette colors (white to black)
    private static readonly Color[] Colors = new Color[]
    {
        new Color(1, 1, 1),       // White
        new Color(0.7, 0.7, 0.7), // Light gray
        new Color(0.4, 0.4, 0.4), // Dark gray
        new Color(0, 0, 0)        // Black
    };

    private readonly IInputHandler input;
    private readonly IKeyMapper keyMapper;
    private readonly Func<uint, bool, bool> keyEventHandler;

    public GBDisplay(int pixelSize = 3, IInputHandler input = null, IKeyMapper keyMapper = null, Func<uint, bool, bool> keyEventHandler = null)
    {
        this.pixelSize = pixelSize;
        this.input = input;
        this.keyMapper = keyMapper ?? new DefaultKeyMapper();
        this.keyEventHandler = keyEventHandler;

        Application.Init();

        window = new Window("Game Boy Display");
        window.SetDefaultSize(160 * pixelSize, 144 * pixelSize);
        window.DeleteEvent += (obj, args) => Application.Quit();

        window.AddEvents((int)Gdk.EventMask.KeyPressMask | (int)Gdk.EventMask.KeyReleaseMask);
        window.KeyPressEvent += Window_KeyPressEvent;
        window.KeyReleaseEvent += Window_KeyReleaseEvent;

        canvas = new DrawingArea();
        canvas.Drawn += Canvas_Drawn;

        window.Add(canvas);
        window.ShowAll();
    }

    public void Start()
    {
        Application.Run();
    }

    public void RunLoop(Action<double> onTick)
    {
        DateTime last = DateTime.UtcNow;

        GLib.Timeout.Add(1, () =>
        {
            var now = DateTime.UtcNow;
            double delta = (now - last).TotalSeconds;
            last = now;

            onTick(delta);
            return true;
        });

        Start();
    }

    public void SetFrameBuffer(IFrameBuffer fb)
    {
        framebuffer = fb;
    }

    public void Update(IFrameBuffer fb)
    {
        var now = DateTime.UtcNow;
        if (now - lastFrame < frameTime)
            return; // skip frames to maintain ~59.7 FPS

        lastFrame = now;
        framebuffer = fb;
        canvas.QueueDraw();
    }

    private void DrawPixel(Context g, int x, int y, Color color)
    {
        g.SetSourceColor(color);
        g.Rectangle(x, y, pixelSize, pixelSize);
        g.Fill();
    }

    private void Canvas_Drawn(object o, DrawnArgs args)
    {
        Context g = args.Cr;

        // Fill background
        g.SetSourceColor(Colors[0]);
        g.Rectangle(0, 0, canvas.Allocation.Width, canvas.Allocation.Height);
        g.Fill();

        if (framebuffer == null)
            return;

        for (int y = 0; y < 144; y++)
        {
            for (int x = 0; x < 160; x++)
            {
                int colorIndex = framebuffer.GetPixel(x, y);
                if (colorIndex < 0 || colorIndex > 3)
                    colorIndex = 0; // safety
                DrawPixel(g, x * pixelSize, y * pixelSize, Colors[colorIndex]);
            }
        }
    }

    private void Window_KeyPressEvent(object o, KeyPressEventArgs args)
    {
        if (keyEventHandler != null && keyEventHandler(args.Event.KeyValue, true)) return;
        if (input == null) return;
        if (keyMapper.TryMapGtkKey(args.Event.KeyValue, out JoypadButton btn))
            input.SetButton(btn, true);
    }

    private void Window_KeyReleaseEvent(object o, KeyReleaseEventArgs args)
    {
        if (keyEventHandler != null && keyEventHandler(args.Event.KeyValue, false)) return;
        if (input == null) return;
        if (keyMapper.TryMapGtkKey(args.Event.KeyValue, out JoypadButton btn))
            input.SetButton(btn, false);
    }
}

}