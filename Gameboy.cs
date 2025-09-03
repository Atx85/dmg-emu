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
      int i = 10000;
      while(true) {
        i--;
        cpu.Step();

          // ppu.Tick(cycles);
          // timer.Tick(cycles);
          cpu.handleInterrupts();
         // if (cpu.eiPending) {
         //   cpu.IME = true;
         //   cpu.eiPending = false;
         // }
      }
    }
  }
}
