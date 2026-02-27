using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Reflection;
using System.Runtime.InteropServices;
using GB;

public class Program
{
  enum DisplayBackend
  {
      Sdl,
      Gtk
  }

  public static void Main(string[] args) {
   Console.WriteLine("dmg starting");
   bool headless = false;
   int maxCycles = 20_000_000;
   string loadStatePath = null;
   string saveStatePath = null;
   string romPath = "./roms/pkred.gb";
      DisplayBackend backend = DisplayBackend.Sdl;
   if (args != null) {

      foreach (var arg in args)
      {
          if (arg == "--gtk")
              backend = DisplayBackend.Gtk;
          else if (arg == "--sdl")
              backend = DisplayBackend.Sdl;
      }
     foreach (var arg in args) {
       if (arg == "--cpu2" || arg == "--cpu2-structured") {
         // legacy flags; structured is now always used.
       } else if (arg == "--headless") {
         headless = true;
       } else if (arg.StartsWith("--load-state=")) {
         loadStatePath = arg.Substring("--load-state=".Length);
       } else if (arg.StartsWith("--save-state=")) {
         saveStatePath = arg.Substring("--save-state=".Length);
       } else if (arg.StartsWith("--max-cycles=")) {
         int.TryParse(arg.Substring("--max-cycles=".Length), out maxCycles);
       } else if (!arg.StartsWith("-")) {
         romPath = arg;
       }
     }
   }
   var cpuBackend = CpuBackend.Cpu2Structured;
   Console.WriteLine("cpu: " + cpuBackend);
   var gb = new Gameboy(romPath, cpuBackend);
   if (!string.IsNullOrEmpty(loadStatePath) && File.Exists(loadStatePath)) {
     var s = EmulatorStateFile.Load(loadStatePath);
     gb.LoadState(s);
     Console.WriteLine("state loaded: " + loadStatePath);
   }

   if (!string.IsNullOrEmpty(saveStatePath)) {
     AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
     {
       try {
         EmulatorStateFile.Save(saveStatePath, gb.SaveState());
       } catch { }
     };
   }

   if (!string.IsNullOrEmpty(saveStatePath)) {
     Action saveAction = () => {
       EmulatorStateFile.Save(saveStatePath, gb.SaveState());
       Console.WriteLine("state saved: " + saveStatePath);
     };
     gb.BindHotkey(InputKeySource.Sdl, (int) 'i', saveAction);
     gb.BindHotkey(InputKeySource.Gtk, 65474, saveAction); // Gdk.Key.F5
   }
   if (!string.IsNullOrEmpty(loadStatePath)) {
     Action loadAction = () => {
       if (File.Exists(loadStatePath)) {
         gb.LoadState(EmulatorStateFile.Load(loadStatePath));
         Console.WriteLine("state loaded: " + loadStatePath);
       }
     };
     gb.BindHotkey(InputKeySource.Sdl, (int) 'o', loadAction);
     gb.BindHotkey(InputKeySource.Gtk, 65478, loadAction); // Gdk.Key.F9
   }

   // Fast-forward toggle (Tab): 1x <-> 3x emulation speed.
   const int SDL_KEY_TAB = 9;
   const int GTK_KEY_TAB = 65289;
   double speedMultiplier = 1.0;
   Action toggleFastForward = () => {
     speedMultiplier = speedMultiplier > 1.0 ? 1.0 : 10.0;
     Console.WriteLine(speedMultiplier > 1.0 ? "speed: 10x" : "speed: 1x");
   };
   gb.BindHotkey(InputKeySource.Sdl, SDL_KEY_TAB, toggleFastForward);
   gb.BindHotkey(InputKeySource.Gtk, GTK_KEY_TAB, toggleFastForward);

   if (headless) {
     RunHeadless(gb, maxCycles);
     if (!string.IsNullOrEmpty(saveStatePath)) {
       EmulatorStateFile.Save(saveStatePath, gb.SaveState());
       Console.WriteLine("state saved: " + saveStatePath);
     }
     return;
   }

  if (backend == DisplayBackend.Gtk)
  {
      Gtk.Application.Init();
  }
  IDisplay display =
    backend == DisplayBackend.Gtk
        ? CreateDisplayGtk(gb)
        : CreateDisplaySdl(gb);

  display.SetFrameBuffer(gb.ppu.GetFrameBuffer());

// Refresh display whenever a frame is ready
gb.ppu.OnFrameReady += fb => display.Update(fb);

    const double CPU_HZ = 4194304.0;
    double cycleRemainder = 0;

    display.RunLoop(deltaSeconds =>
    {
        double cycles = (deltaSeconds * CPU_HZ) * speedMultiplier + cycleRemainder;
        int whole = (int)cycles;
        cycleRemainder = cycles - whole;

        if (whole > 70224)
            whole = 70224;

        gb.TickCycles(whole);
    });
  }


  static IDisplay CreateDisplaySdl(Gameboy gb)
  {
    return new GBDisplaySdl(pixelSize: 4, input: gb.bus.Joypad, keyEventHandler: gb.HandleSdlKey);
  }

  static IDisplay CreateDisplayGtk(Gameboy gb)
  {
    var asm = typeof(Program).Assembly;
    var t = asm.GetType("GBDisplay");
    if (t == null)
      throw new Exception("GBDisplay type not found. Compile with GBDisplay.cs and gtk-sharp.");
    return (IDisplay)Activator.CreateInstance(t, new object[] { 4, gb.bus.Joypad, null, new Func<uint, bool, bool>(gb.HandleGtkKey) });
  }

  static void RunHeadless(Gameboy gb, int maxCycles)
  {
    var serial = new StringBuilder();
    int batch = 256;
    int ran = 0;
    while (ran < maxCycles) {
      gb.TickCycles(batch);
      ran += batch;
      DrainSerial(gb.bus, serial);

      string s = serial.ToString();
      if (s.IndexOf("Passed", StringComparison.OrdinalIgnoreCase) >= 0 ||
          s.IndexOf("Failed", StringComparison.OrdinalIgnoreCase) >= 0) {
        break;
      }
    }

    Console.WriteLine("headless cycles: " + ran);
    if (serial.Length > 0) {
      Console.WriteLine("serial:");
      Console.WriteLine(serial.ToString().TrimEnd());
    } else {
      Console.WriteLine("serial: <empty>");
    }
  }

  static void DrainSerial(Bus bus, StringBuilder serial)
  {
    if (bus.Read(0xFF02) == 0x81) {
      serial.Append((char)bus.Read(0xFF01));
      bus.Write(0xFF02, 0);
    }
  }
}

