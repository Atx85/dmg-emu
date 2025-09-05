using System;
using System.IO;
using System.Collections.Generic;
namespace GB {
  public class Gameboy
  {
    public Gameboy (string path) {
      // gb.bppTest();
      var cart = new Cartridge();
      cart.load(path);
      var bus = new Bus(ref cart);
      Cpu cpu = new Cpu(bus);
      var dbg = new Dbg(ref bus);
      int i = 10000;
      while(true) {
        i--;
        int cycles = cpu.Step();
          // ppu.Tick(cycles);
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
