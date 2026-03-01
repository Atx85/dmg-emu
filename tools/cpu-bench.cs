using System;
using System.Diagnostics;
using DmgEmu.Core;

public static class CpuBenchProgram
{
    private const double DmgCpuHz = 4_194_304.0;

    public static int Main(string[] args)
    {
        int seconds = 3;

        if (args != null)
        {
            foreach (var arg in args)
            {
                if (arg.StartsWith("--seconds="))
                {
                    int.TryParse(arg.Substring("--seconds=".Length), out seconds);
                }
                else if (arg.StartsWith("--bench-seconds="))
                {
                    int.TryParse(arg.Substring("--bench-seconds=".Length), out seconds);
                }
            }
        }

        if (seconds < 1) seconds = 1;

        Console.WriteLine("cpu benchmark (Cpu2Structured)");
        Console.WriteLine("duration: " + seconds + "s");
        Console.WriteLine();

        RunKernel(
            "NOP loop",
            new byte[]
            {
                0x00,
                0x00,
                0x00,
                0x00,
                0xC3, 0x00, 0x01
            },
            seconds);

        RunKernel(
            "Mixed ALU/load loop",
            new byte[]
            {
                0x06, 0x12,
                0x0E, 0x34,
                0x80,
                0xA1,
                0xB0,
                0xAF,
                0x04,
                0x0D,
                0x50,
                0x59,
                0x23,
                0x77,
                0x7E,
                0x81,
                0xB8,
                0x00,
                0xC3, 0x00, 0x01
            },
            seconds);

        return 0;
    }

    private static void RunKernel(string name, byte[] kernel, int seconds)
    {
        var bus = new FlatMemoryBus();
        bus.Load(0x0100, kernel);

        var cpu = new Cpu2Structured(bus, new BenchClock(), new NoInterrupts());
        Warmup(cpu, 500);

        long steps = 0;
        long cycles = 0;

        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalSeconds < seconds)
        {
            int used = cpu.Step();
            steps++;
            cycles += used;
        }
        sw.Stop();

        double elapsed = sw.Elapsed.TotalSeconds;
        double ips = steps / elapsed;
        double cyclesPerSec = cycles / elapsed;
        double emulatedMhz = cyclesPerSec / 1_000_000.0;
        double realtime = cyclesPerSec / DmgCpuHz;

        Console.WriteLine(name + ":");
        Console.WriteLine("  steps/s: " + ips.ToString("N0"));
        Console.WriteLine("  cycles/s: " + cyclesPerSec.ToString("N0"));
        Console.WriteLine("  emulated MHz: " + emulatedMhz.ToString("N2"));
        Console.WriteLine("  realtime vs DMG: " + realtime.ToString("N2") + "x");
        Console.WriteLine();
    }

    private static void Warmup(Cpu2Structured cpu, int milliseconds)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < milliseconds)
        {
            cpu.Step();
        }
    }

    private sealed class FlatMemoryBus : ICpuBus
    {
        private readonly byte[] memory = new byte[0x10000];

        public byte Read(ushort addr) => memory[addr];

        public void Write(ushort addr, byte value)
        {
            memory[addr] = value;
        }

        public void Load(ushort start, byte[] data)
        {
            if (data == null) return;
            int i = 0;
            while (i < data.Length && start + i < memory.Length)
            {
                memory[start + i] = data[i];
                i++;
            }
        }
    }

    private sealed class BenchClock : IClock
    {
        public void Advance(int cycles) { }
    }

    private sealed class NoInterrupts : IInterruptController
    {
        public InterruptFlags Pending() => InterruptFlags.None;

        public int HighestPendingBit(InterruptFlags flags) => 0;

        public void Clear(int bit) { }
    }
}
