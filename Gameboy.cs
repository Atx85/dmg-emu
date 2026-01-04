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
        int remaining = cycles;

        while (remaining > 0)
        {
            int used = cpu.Step();    // returns cycles used
            remaining -= used;

            for (int i = 0; i < used; i++)
            {
                ppu.Tick();
                bus.timer.Tick(1);
            }
        }
    }

}

}
