using System;
using System.IO;
using System.Collections.Generic;
using Gtk;
using Cairo;
using System.Reflection;
using System.Runtime.InteropServices;

using GB;
public class Program
{
  public static void Main(string[] args) {
    //    Console.WriteLine(args[0]);
    Console.WriteLine("dmg starting");
    // new Gameboy("./roms/01-special.gb");
    // new Gameboy("./roms/05-op rp.gb");
    // new Gameboy("./roms/rom.gb");
    //var gb = new Gameboy("./roms/dmg-acid2.gb");
    var gb = new Gameboy();
    TestPpu(gb);
  }


  static void TestPpu(Gameboy gb)
  {
    var ppu = gb.ppu;

    // Make sure BG is enabled
    gb.bus.Write(0xFF40, 0x91);
    gb.bus.Write(0xFF47, 0xE4); // BGP palette

    // Fill VRAM & tile map
    for (ushort addr = 0x8000; addr < 0x9000; addr++)
      gb.bus.Write(addr, (byte)(addr & 0xFF));
    for (ushort addr = 0x9800; addr < 0x9C00; addr++)
      gb.bus.Write(addr, (byte)((addr - 0x9800) % 256));

    // Create display first
    var display = new GBDisplay(pixelSize: 4);
    gb.ppu.OnFrameReady += display.Update;

    // Step PPU in a timer so GTK main loop can run
    GLib.Timeout.Add(1600, () =>
        {
        // Tick enough dots for one frame (154 lines × 456 dots)
        for (int line = 0; line < 154; line++)
        for (int dot = 0; dot < 456; dot++)
        ppu.Tick();

        return true; // continue repeating if you want animation
        });

    // Start GTK main loop
    display.Start();
  }
}

public static class FramebufferTest {
  public static Pixel[,] CreateTestFramebuffer() {
    // 144 rows × 160 columns
    Pixel[,] framebuffer = new Pixel[144, 160];

    for (int y = 0; y < 144; y++) {
      for (int x = 0; x < 160; x++) {
        byte color;

        // Simple pattern: alternate colors every 8 pixels (like tiles)
        if (((x / 8) + (y / 8)) % 4 == 0)
          color = 0;
        else if (((x / 8) + (y / 8)) % 4 == 1)
          color = 1;
        else if (((x / 8) + (y / 8)) % 4 == 2)
          color = 2;
        else
          color = 3;

        framebuffer[y, x] = new Pixel(color, 0, 0, false);
      }
    }

    return framebuffer;
  }
}
