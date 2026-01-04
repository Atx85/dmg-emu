
using Gtk;
using Cairo;
using System;
using GB;
public class GBDisplay
{
    private Window window;
    private DrawingArea canvas;
    private Pixel[,] framebuffer;
    private int pixelSize = 3;
    DateTime lastFrame = DateTime.MinValue;
    readonly TimeSpan frameTime = TimeSpan.FromSeconds(1.0 / 59.73);
    // DMG palette colors
    private static readonly Color[] Colors = new Color[]
    {
        new Color(1, 1, 1),
        new Color(0.7, 0.7, 0.7),
        new Color(0.4, 0.4, 0.4),
        new Color(0, 0, 0)
    };

    public GBDisplay(int pixelSize = 3)
    {
        this.pixelSize = pixelSize;

        Application.Init();

        window = new Window("Game Boy Display");
        canvas = new DrawingArea();
        canvas.ExposeEvent += Canvas_ExposeEvent;

        window.Add(canvas);
        window.Resize(160 * pixelSize, 144 * pixelSize);
        window.DeleteEvent += (obj, args) => Application.Quit();
        window.ShowAll();
    }

    public void Start()
    {
        Application.Run();
    }

 
    public void Update(Pixel[,] fb)
    {
        var now = DateTime.UtcNow;

        if (now - lastFrame < frameTime)
            return; // skip drawing to maintain real framerate

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

    private void Canvas_ExposeEvent(object o, ExposeEventArgs args)
    {
        using (Context g = Gdk.CairoHelper.Create(canvas.GdkWindow))
        {
            // Fill background
            g.SetSourceColor(new Color(1, 1, 1));
            g.Rectangle(0, 0, canvas.Allocation.Width, canvas.Allocation.Height);
            g.Fill();

            if (framebuffer == null)
                return;

            for (int y = 0; y < 144; y++)
            {
                for (int x = 0; x < 160; x++)
                {
                    var px = framebuffer[y, x];
                    DrawPixel(g, x * pixelSize, y * pixelSize, Colors[px.Color]);
                }
            }
        }
    }
}

