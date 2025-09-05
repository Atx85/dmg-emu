using System;
using System.IO;
using System.Collections.Generic;
using Gtk;
using Cairo;
using System.Reflection;
using System.Runtime.InteropServices;

public class Program
{
  public static void Main(string[] args) {

    Application.Init ();
    Gtk.Window w = new Gtk.Window ("Mono-Cairo Rounded Rectangles");

    DrawingArea a = new CairoGraphic ();

    Box box = new HBox (true, 0);
    box.Add (a);
    w.Add (box);
    w.Resize (500, 500);
    w.DeleteEvent += close_window;
    w.ShowAll ();

    Application.Run ();
  }

  static void close_window (object obj, DeleteEventArgs args)
  {
    Application.Quit ();
  }
}

public class CairoGraphic : DrawingArea
{
  static double min (params double[] arr)
  {
    int minp = 0;
    for (int i = 1; i < arr.Length; i++)
      if (arr[i] < arr[minp])
        minp = i;

    return arr[minp];
  }

  void DrawText(Context g, string text, double x, double y, double fontSize, Color color)
  {
      g.SetSourceColor(color);
      g.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Normal);
      g.SetFontSize(fontSize);
      g.MoveTo(x, y);
      g.ShowText(text);
      g.Stroke(); // optional if you want stroke effect
  }
  void DrawSmiley(Context g, int startX, int startY, int pixelSize)
  {
    // 8x8 smiley face pattern (1 = draw, 0 = skip)
    int[,] smiley = new int[8, 8] {
      {0,1,1,0,0,1,1,0},
        {1,0,0,1,1,0,0,1},
        {1,0,0,1,1,0,0,1},
        {1,0,0,0,0,0,0,1},
        {1,0,1,0,0,1,0,1},
        {1,0,0,0,0,0,0,1},
        {0,1,0,0,0,0,1,0},
        {0,0,1,1,1,1,0,0}
    };

    Color color = new Color(1, 1, 0); // yellow

    for (int y = 0; y < 8; y++)
    {
      for (int x = 0; x < 8; x++)
      {
        if (smiley[y, x] == 1)
        {
          DrawPixel(g, startX + x * pixelSize, startY + y * pixelSize, color);
        }
      }
    }
  }
  static void DrawRoundedRectangle (Cairo.Context gr, double x, double y, double width, double height, double radius)
  {
    gr.Save ();

    if ((radius > height / 2) || (radius > width / 2))
      radius = min (height / 2, width / 2);

    gr.MoveTo (x, y + radius);
    gr.Arc (x + radius, y + radius, radius, Math.PI, -Math.PI / 2);
    gr.LineTo (x + width - radius, y);
    gr.Arc (x + width - radius, y + radius, radius, -Math.PI / 2, 0);
    gr.LineTo (x + width, y + height - radius);
    gr.Arc (x + width - radius, y + height - radius, radius, 0, Math.PI / 2);
    gr.LineTo (x + radius, y + height);
    gr.Arc (x + radius, y + height - radius, radius, Math.PI / 2, Math.PI);
    gr.ClosePath ();
    gr.Restore ();
  }

  static void DrawCurvedRectangle (Cairo.Context gr, double x, double y, double width, double height)
  {
    gr.Save ();
    gr.MoveTo (x, y + height / 2);
    gr.CurveTo (x, y, x, y, x + width / 2, y);
    gr.CurveTo (x + width, y, x + width, y, x + width, y + height / 2);
    gr.CurveTo (x + width, y + height, x + width, y + height, x + width / 2, y + height);
    gr.CurveTo (x, y + height, x, y + height, x, y + height / 2);
    gr.Restore ();
  }

  void DrawPixel(Context g, int x, int y, Color color)
  {
    g.SetSourceColor(color);
    g.Rectangle(x, y, 5, 5); // 1x1 rectangle = a pixel
    g.Fill();
  }



  protected override bool OnExposeEvent(Gdk.EventExpose args)
  {
    using (Context g = Gdk.CairoHelper.Create(args.Window))
    {
      // Fill background with dark grey (#2e2e2e)
      g.SetSourceColor(new Color(0.18, 0.18, 0.18)); // Dark grey
      g.Rectangle(0, 0, Allocation.Width, Allocation.Height);
      g.Fill();

      // Draw your other stuff here
      DrawCurvedRectangle(g, 30, 30, 300, 200);
      DrawRoundedRectangle(g, 70, 250, 300, 200, 40);
      g.SetSourceColor(new Color(0.1, 0.6, 1, 1));
      g.FillPreserve();
      g.SetSourceColor(new Color(0.2, 0.8, 1, 1));
      g.LineWidth = 5;
      g.Stroke();

      DrawText(g, "Hello, Cairo!", 50, 400, 24, new Color(1, 1, 1)); // White text
      // Draw pixel art
      DrawSmiley(g, 50, 50, 10);
    }

    return true;
  }
}

