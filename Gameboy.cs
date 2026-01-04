using System;
using System.IO;
using System.Collections.Generic;
namespace GB {

public class Gameboy
{
    public Bus bus;
    public Ppu ppu;
    public Cpu cpu;

    public Gameboy()
    {
        var cart = new Cartridge();
        bus = new Bus(ref cart);
        cpu = new Cpu(bus);
        ppu = new Ppu(bus);
    }

    public Gameboy(string path)
    {
        var cart = new Cartridge();
        cart.load(path);
        bus = new Bus(ref cart);
        cpu = new Cpu(bus);
        ppu = new Ppu(bus);
    }

    /// <summary>
    /// Tick the Game Boy for n CPU cycles
    /// </summary>
    public void TickCycles(int cycles)
    {
        while (cycles > 0)
        {
            int ticked = cpu.Step();  // execute 1 instruction, returns cycles it took
            // ppu.Tick(ticked);         // advance PPU by same number of cycles
            ppu.Tick();         // advance PPU by same number of cycles
            for (int c = 0; c < ticked; c++)
                bus.timer.Tick(1);    // advance timers
            cycles -= ticked;
        }
    }
}

}
