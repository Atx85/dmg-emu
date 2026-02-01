using System;
using System.IO;
using System.Collections.Generic;
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
   var gb = new Gameboy("./roms/pkred.gb");
   // // var gb = new Gameboy();
   // // TestPpu(gb);
    // Choose one:
    IDisplay display = CreateDisplaySdl(gb);
    // IDisplay display = CreateDisplayGtk(gb);
// Give the display the PPU framebuffer
display.SetFrameBuffer(gb.ppu.GetFrameBuffer());

// Refresh display whenever a frame is ready
gb.ppu.OnFrameReady += fb => display.Update(fb);

    const double CPU_HZ = 4194304.0;
    double cycleRemainder = 0;

    display.RunLoop(deltaSeconds =>
    {
        double cycles = deltaSeconds * CPU_HZ + cycleRemainder;
        int whole = (int)cycles;
        cycleRemainder = cycles - whole;

        if (whole > 70224)
            whole = 70224;

        gb.TickCycles(whole);
    });
  }


  static IDisplay CreateDisplaySdl(Gameboy gb)
  {
    return new GBDisplaySdl(pixelSize: 4, input: gb.bus.Joypad);
  }

  static IDisplay CreateDisplayGtk(Gameboy gb)
  {
    var asm = typeof(Program).Assembly;
    var t = asm.GetType("GBDisplay");
    if (t == null)
      throw new Exception("GBDisplay type not found. Compile with GBDisplay.cs and gtk-sharp.");
    return (IDisplay)Activator.CreateInstance(t, new object[] { 4, gb.bus.Joypad, null });
  }
}
/*
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
*/
