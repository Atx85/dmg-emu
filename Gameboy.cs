using System;
using System.IO;
using System.Collections.Generic;
namespace GB {
  public class Gameboy
  {
    public Bus bus;
    public Ppu ppu;
    public Gameboy () {
      var cart = new Cartridge();
      bus = new Bus(ref cart);
      Cpu cpu = new Cpu(bus);
      ppu = new Ppu(bus);
    }
    public Gameboy (string path) {
      // gb.bppTest();
      var cart = new Cartridge();
      cart.load(path);
      bus = new Bus(ref cart);
      Cpu cpu = new Cpu(bus);
      ppu = new Ppu(bus);
      var dbg = new Dbg(ref bus);
      while(true) {
        int cycles = cpu.Step();
        ppu.Tick();
        for (int c = 0; c < cycles; c++)
          bus.timer.Tick(1);
        dbg.Update();
          // cpu.handleInterrupts();
         // if (cpu.eiPending) {
         //   cpu.IME = true;
         //   cpu.eiPending = false;
         // }
      }
    }
  }
}
