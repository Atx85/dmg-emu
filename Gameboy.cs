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
        
        var fb = new FrameBuffer();
        var regs = new BusRegistersAdapter(bus);
        var ints = new BusInterruptsAdapter(bus);
        var timing = new PpuTiming();
        var mem = new BusMemoryAdapter(bus);
        var counter = new SpriteCounter(mem, regs);
        var sm = new PpuStateMachine(regs, ints, timing, counter);

        var bg = new BackgroundRenderer(mem, regs, fb);
        var window = new WindowRenderer(mem, regs, fb);
        var sprite = new SpriteRenderer(mem, regs, fb);

        ppu = new Ppu(sm, fb, bg, window, sprite);
        // ppu = new Ppu(bus);
    }

    public Gameboy(string path)
    {
        var cart = new Cartridge();
        cart.load(path);
        bus = new Bus(ref cart);
        cpu = new Cpu(bus);
        var fb = new FrameBuffer();
        var regs = new BusRegistersAdapter(bus);
        var ints = new BusInterruptsAdapter(bus);
        var timing = new PpuTiming();
        var mem = new BusMemoryAdapter(bus);
        var counter = new SpriteCounter(mem, regs);
        var sm = new PpuStateMachine(regs, ints, timing, counter);

        var bg = new BackgroundRenderer(mem, regs, fb);
        var window = new WindowRenderer(mem, regs, fb);
        var sprite = new SpriteRenderer(mem, regs, fb);

        ppu = new Ppu(sm, fb, bg, window, sprite);
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
                ppu.Step(1);
                bus.timer.Tick(1);
                bus.TickDma(1);
            }
        }
    }

}

}
