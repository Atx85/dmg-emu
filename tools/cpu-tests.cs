using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Drawing;
using DmgEmu.Core;

public static class CpuTestsProgram
{
    private sealed class TestFailure : Exception
    {
        public TestFailure(string message) : base(message) { }
    }

    private sealed class FlatBus : ICpuBus
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
            for (int i = 0; i < data.Length && start + i < memory.Length; i++)
                memory[start + i] = data[i];
        }
    }

    private sealed class NullClock : IClock
    {
        public void Advance(int cycles) { }
    }

    private sealed class TestInterrupts : IInterruptController
    {
        public InterruptFlags PendingFlags = InterruptFlags.None;
        public int ClearCount;
        public int LastClearedBit = -1;

        public InterruptFlags Pending() => PendingFlags;

        public int HighestPendingBit(InterruptFlags flags)
        {
            int value = (int)flags;
            for (int i = 0; i < 5; i++)
            {
                if (((value >> i) & 1) != 0) return i;
            }
            return 0;
        }

        public void Clear(int bit)
        {
            ClearCount++;
            LastClearedBit = bit;
            PendingFlags = (InterruptFlags)((int)PendingFlags & ~(1 << bit));
        }
    }

    public static int Main(string[] args)
    {
        bool runUnit = true;
        bool runRom = true;
        bool runAcid2 = false;
        bool runBlarggSuites = true;
        bool requireAcid2 = false;
        bool requireMemTiming = false;
        int maxCycles = 100_000_000;
        string romDir = FindDefaultRomDir();
        string gbRoot = FindDefaultGbRoot();
        string acid2Rom = FindDefaultAcid2Rom();
        string acid2Ref = FindDefaultAcid2Reference();
        int acid2Frames = 8;
        int acid2Tolerance = 0;
        int acid2MaxDiffPixels = 0;
        int acid2MaxCycles = 60_000_000;

        if (args != null)
        {
            foreach (var arg in args)
            {
                if (arg == "--unit-only")
                {
                    runUnit = true;
                    runRom = false;
                }
                else if (arg == "--rom-only")
                {
                    runUnit = false;
                    runRom = true;
                }
                else if (arg == "--no-blargg-suites")
                {
                    runBlarggSuites = false;
                }
                else if (arg == "--require-mem-timing")
                {
                    runBlarggSuites = true;
                    requireMemTiming = true;
                }
                else if (arg.StartsWith("--max-cycles="))
                {
                    int.TryParse(arg.Substring("--max-cycles=".Length), out maxCycles);
                    if (maxCycles < 1) maxCycles = 100_000_000;
                }
                else if (arg.StartsWith("--rom-dir="))
                {
                    romDir = arg.Substring("--rom-dir=".Length);
                }
                else if (arg.StartsWith("--gb-root="))
                {
                    gbRoot = arg.Substring("--gb-root=".Length);
                }
                else if (arg == "--acid2")
                {
                    runAcid2 = true;
                }
                else if (arg == "--require-acid2")
                {
                    runAcid2 = true;
                    requireAcid2 = true;
                }
                else if (arg.StartsWith("--acid2-rom="))
                {
                    acid2Rom = arg.Substring("--acid2-rom=".Length);
                    runAcid2 = true;
                }
                else if (arg.StartsWith("--acid2-ref="))
                {
                    acid2Ref = arg.Substring("--acid2-ref=".Length);
                    runAcid2 = true;
                }
                else if (arg.StartsWith("--acid2-frames="))
                {
                    int.TryParse(arg.Substring("--acid2-frames=".Length), out acid2Frames);
                    if (acid2Frames < 1) acid2Frames = 1;
                }
                else if (arg.StartsWith("--acid2-tolerance="))
                {
                    int.TryParse(arg.Substring("--acid2-tolerance=".Length), out acid2Tolerance);
                    if (acid2Tolerance < 0) acid2Tolerance = 0;
                }
                else if (arg.StartsWith("--acid2-max-diff="))
                {
                    int.TryParse(arg.Substring("--acid2-max-diff=".Length), out acid2MaxDiffPixels);
                    if (acid2MaxDiffPixels < 0) acid2MaxDiffPixels = 0;
                }
                else if (arg.StartsWith("--acid2-max-cycles="))
                {
                    int.TryParse(arg.Substring("--acid2-max-cycles=".Length), out acid2MaxCycles);
                    if (acid2MaxCycles < 1) acid2MaxCycles = 60_000_000;
                }
            }
        }

        try
        {
            if (runUnit) RunUnitTests();
            if (runRom) RunRomSmokeTests(romDir, maxCycles);
            if (runBlarggSuites) RunBlarggSuites(gbRoot, maxCycles, requireMemTiming);
            if (runAcid2) RunAcid2VisualTest(acid2Rom, acid2Ref, acid2Frames, acid2Tolerance, acid2MaxDiffPixels, acid2MaxCycles, requireAcid2);
            Console.WriteLine();
            Console.WriteLine("CPU test suite: PASS");
            return 0;
        }
        catch (TestFailure ex)
        {
            Console.WriteLine();
            Console.WriteLine("CPU test suite: FAIL");
            Console.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void RunUnitTests()
    {
        var tests = new List<Action>
        {
            TestHaltExitWhenIme0AndInterruptPending,
            TestInterruptServiceWhenIme1,
            TestInterruptPriorityHighestBitWins,
            TestEiDelayedEnableAndReti,
            TestJrAndJpConditionalCycles,
            TestConditionalCallAndRetCycles,
            TestCallAndRetCycles,
            TestDaaAndAdcFlagEdges,
            TestAddSpSignedAndLdHlSpSignedFlags,
            TestCbRegisterAndHlBehavior,
            TestCbBitResSetOps,
            TestLdRrMatrixCyclesAndDataflow,
            TestAluRegisterMatrixCycles,
            TestOpcodeOracleSamples,
            TestPushPopAfMasksLowFlagsNibble,
            TestLdhAndAbsoluteLoadStorePaths,
            TestStopInstructionStateAndCycle,
            TestCycleTraceRegression,
            TestDeterministicTraceDigest
        };

        Console.WriteLine("unit tests:");
        int passed = 0;
        var sw = Stopwatch.StartNew();
        foreach (var test in tests)
        {
            test();
            passed++;
        }
        sw.Stop();
        Console.WriteLine("  passed: " + passed + "/" + tests.Count + " in " + sw.ElapsedMilliseconds + " ms");
    }

    private static Cpu2Structured MakeCpu(FlatBus bus, TestInterrupts ints)
    {
        return new Cpu2Structured(bus, new NullClock(), ints);
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition) throw new TestFailure(message);
    }

    private static void AssertEq<T>(T actual, T expected, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(actual, expected))
            throw new TestFailure(message + " (expected=" + expected + ", actual=" + actual + ")");
    }

    private static void TestHaltExitWhenIme0AndInterruptPending()
    {
        var bus = new FlatBus();
        var ints = new TestInterrupts { PendingFlags = InterruptFlags.VBlank };
        bus.Load(0x0101, new byte[] { 0x00 }); // NOP

        var cpu = MakeCpu(bus, ints);
        var s = cpu.GetState();
        s.IME = false;
        s.IsHalted = true;
        s.PC = 0x0101;
        cpu.SetState(s);

        int cycles = cpu.Step();
        var next = cpu.GetState();

        AssertEq(cycles, 4, "HALT exit path should consume 4 cycles for resumed NOP");
        AssertTrue(!next.IsHalted, "CPU should leave HALT when interrupt is pending and IME=0");
        AssertEq(next.PC, (ushort)0x0102, "PC should advance to next instruction");
        AssertEq(ints.ClearCount, 0, "Interrupt must not be serviced when IME=0");
    }

    private static void TestInterruptServiceWhenIme1()
    {
        var bus = new FlatBus();
        var ints = new TestInterrupts { PendingFlags = InterruptFlags.VBlank };
        var cpu = MakeCpu(bus, ints);

        var s = cpu.GetState();
        s.IME = true;
        s.IsHalted = true;
        s.SP = 0xFFFE;
        s.PC = 0x1234;
        cpu.SetState(s);

        int cycles = cpu.Step();
        var next = cpu.GetState();

        AssertEq(cycles, 20, "Interrupt service should consume 20 cycles");
        AssertEq(next.PC, (ushort)0x0040, "VBlank vector should be selected");
        AssertEq(next.SP, (ushort)0xFFFC, "PC should be pushed on stack");
        AssertEq(bus.Read(0xFFFC), (byte)0x34, "Stack low byte mismatch");
        AssertEq(bus.Read(0xFFFD), (byte)0x12, "Stack high byte mismatch");
        AssertTrue(!next.IME, "IME should be cleared during interrupt service");
        AssertEq(ints.ClearCount, 1, "Interrupt should be acknowledged once");
        AssertEq(ints.LastClearedBit, 0, "Wrong interrupt bit cleared");
    }

    private static void TestEiDelayedEnableAndReti()
    {
        var bus = new FlatBus();
        var ints = new TestInterrupts();
        bus.Load(0x0100, new byte[] { 0xFB, 0x00, 0xD9 }); // EI; NOP; RETI

        // RETI returns to 0x3456.
        bus.Write(0xC000, 0x56);
        bus.Write(0xC001, 0x34);

        var cpu = MakeCpu(bus, ints);
        var s = cpu.GetState();
        s.IME = false;
        s.SP = 0xC000;
        cpu.SetState(s);

        int c1 = cpu.Step(); // EI
        var s1 = cpu.GetState();
        AssertEq(c1, 4, "EI cycle mismatch");
        AssertTrue(!s1.IME, "IME should not enable until next step");
        AssertTrue(s1.EiPending, "EI should set delayed-enable latch");

        int c2 = cpu.Step(); // NOP (EI latch applies at start)
        var s2 = cpu.GetState();
        AssertEq(c2, 4, "NOP cycle mismatch after EI");
        AssertTrue(s2.IME, "IME should be enabled on step after EI");
        AssertTrue(!s2.EiPending, "EI latch should clear after enable");

        int c3 = cpu.Step(); // RETI
        var s3 = cpu.GetState();
        AssertEq(c3, 16, "RETI cycle mismatch");
        AssertEq(s3.PC, (ushort)0x3456, "RETI target mismatch");
        AssertEq(s3.SP, (ushort)0xC002, "RETI SP mismatch");
        AssertTrue(s3.IME, "RETI should enable IME");
    }

    private static void TestInterruptPriorityHighestBitWins()
    {
        var bus = new FlatBus();
        var ints = new TestInterrupts { PendingFlags = InterruptFlags.Timer | InterruptFlags.VBlank | InterruptFlags.Joypad };
        var cpu = MakeCpu(bus, ints);

        var s = cpu.GetState();
        s.IME = true;
        s.SP = 0xFFFE;
        s.PC = 0x4000;
        cpu.SetState(s);

        int cycles = cpu.Step();
        var next = cpu.GetState();
        AssertEq(cycles, 20, "Interrupt priority service cycle mismatch");
        AssertEq(next.PC, (ushort)0x0040, "Lowest interrupt bit should have priority");
        AssertEq(ints.LastClearedBit, 0, "Wrong interrupt bit was cleared");
    }

    private static void TestJrAndJpConditionalCycles()
    {
        var bus = new FlatBus();
        var ints = new TestInterrupts();
        bus.Load(0x0100, new byte[] { 0x20, 0x02, 0xC2, 0x34, 0x12, 0x00, 0x00 }); // JR NZ,+2; JP NZ,1234
        var cpu = MakeCpu(bus, ints);

        var s = cpu.GetState();
        s.F = 0x80; // Z=1
        cpu.SetState(s);
        int c1 = cpu.Step();
        var s1 = cpu.GetState();
        AssertEq(c1, 8, "JR not-taken cycle mismatch");
        AssertEq(s1.PC, (ushort)0x0102, "JR not-taken PC mismatch");

        int c2 = cpu.Step();
        var s2 = cpu.GetState();
        AssertEq(c2, 12, "JP not-taken cycle mismatch");
        AssertEq(s2.PC, (ushort)0x0105, "JP not-taken PC mismatch");

        s2.PC = 0x0100;
        s2.F = 0x00; // Z=0
        cpu.SetState(s2);

        int c3 = cpu.Step();
        var s3 = cpu.GetState();
        AssertEq(c3, 12, "JR taken cycle mismatch");
        AssertEq(s3.PC, (ushort)0x0104, "JR taken PC mismatch");
    }

    private static void TestCallAndRetCycles()
    {
        var bus = new FlatBus();
        var ints = new TestInterrupts();
        bus.Load(0x0100, new byte[] { 0xCD, 0x00, 0x02, 0x00 }); // CALL 0x0200; NOP
        bus.Load(0x0200, new byte[] { 0xC9 }); // RET

        var cpu = MakeCpu(bus, ints);
        var s = cpu.GetState();
        s.SP = 0xFFFE;
        cpu.SetState(s);

        int c1 = cpu.Step();
        var s1 = cpu.GetState();
        AssertEq(c1, 24, "CALL cycle mismatch");
        AssertEq(s1.PC, (ushort)0x0200, "CALL target mismatch");
        AssertEq(s1.SP, (ushort)0xFFFC, "CALL stack pointer mismatch");
        AssertEq(bus.Read(0xFFFC), (byte)0x03, "CALL stack low byte mismatch");
        AssertEq(bus.Read(0xFFFD), (byte)0x01, "CALL stack high byte mismatch");

        int c2 = cpu.Step();
        var s2 = cpu.GetState();
        AssertEq(c2, 16, "RET cycle mismatch");
        AssertEq(s2.PC, (ushort)0x0103, "RET target mismatch");
        AssertEq(s2.SP, (ushort)0xFFFE, "RET stack pointer mismatch");
    }

    private static void TestConditionalCallAndRetCycles()
    {
        var bus = new FlatBus();
        var ints = new TestInterrupts();
        // CALL NZ,0200 ; RET NZ ; NOP
        bus.Load(0x0100, new byte[] { 0xC4, 0x00, 0x02, 0xC0, 0x00 });
        bus.Load(0x0200, new byte[] { 0xC0, 0x00 }); // RET NZ ; NOP

        var cpu = MakeCpu(bus, ints);
        var s = cpu.GetState();
        s.F = 0x80; // Z=1 => NZ false
        s.SP = 0xFFFE;
        cpu.SetState(s);

        int c1 = cpu.Step(); // CALL NZ not taken
        AssertEq(c1, 12, "CALL condition false cycle mismatch");
        AssertEq(cpu.GetState().PC, (ushort)0x0103, "CALL condition false PC mismatch");

        int c2 = cpu.Step(); // RET NZ not taken
        AssertEq(c2, 8, "RET condition false cycle mismatch");
        AssertEq(cpu.GetState().PC, (ushort)0x0104, "RET condition false PC mismatch");

        s = cpu.GetState();
        s.PC = 0x0100;
        s.F = 0x00; // Z=0 => NZ true
        s.SP = 0xFFFE;
        cpu.SetState(s);

        int c3 = cpu.Step(); // CALL NZ taken
        AssertEq(c3, 24, "CALL condition true cycle mismatch");
        AssertEq(cpu.GetState().PC, (ushort)0x0200, "CALL condition true PC mismatch");

        int c4 = cpu.Step(); // RET NZ taken
        AssertEq(c4, 20, "RET condition true cycle mismatch");
        AssertEq(cpu.GetState().PC, (ushort)0x0103, "RET condition true PC mismatch");
    }

    private static void TestDaaAndAdcFlagEdges()
    {
        // 45 + 38 = 83 in BCD after DAA
        var bus = new FlatBus();
        var ints = new TestInterrupts();
        bus.Load(0x0100, new byte[] { 0x3E, 0x45, 0xC6, 0x38, 0x27 }); // LD A,45; ADD A,38; DAA
        var cpu = MakeCpu(bus, ints);

        cpu.Step();
        cpu.Step();
        cpu.Step();
        var s = cpu.GetState();
        AssertEq(s.A, (byte)0x83, "DAA result mismatch");
        AssertEq((byte)(s.F & 0xF0), (byte)0x00, "DAA flags mismatch");

        // ADC half-carry edge: 0x0F + carry(1) -> 0x10 with H=1, C=0
        var bus2 = new FlatBus();
        bus2.Load(0x0100, new byte[] { 0x3E, 0x0F, 0x37, 0xCE, 0x00 }); // LD A,0F; SCF; ADC A,00
        var cpu2 = MakeCpu(bus2, new TestInterrupts());
        cpu2.Step();
        cpu2.Step();
        cpu2.Step();
        var s2 = cpu2.GetState();
        AssertEq(s2.A, (byte)0x10, "ADC result mismatch");
        AssertEq((byte)(s2.F & 0xF0), (byte)0x20, "ADC flags mismatch");
    }

    private static void TestAddSpSignedAndLdHlSpSignedFlags()
    {
        // E8: SP = 0x00FF + 1 => 0x0100 with H and C set
        var bus = new FlatBus();
        var ints = new TestInterrupts();
        bus.Load(0x0100, new byte[] { 0xE8, 0x01, 0xF8, 0xFF }); // ADD SP,+1 ; LD HL,SP-1

        var cpu = MakeCpu(bus, ints);
        var s = cpu.GetState();
        s.SP = 0x00FF;
        cpu.SetState(s);

        int c1 = cpu.Step();
        var s1 = cpu.GetState();
        AssertEq(c1, 16, "ADD SP,e8 cycle mismatch");
        AssertEq(s1.SP, (ushort)0x0100, "ADD SP,e8 result mismatch");
        AssertEq((byte)(s1.F & 0xF0), (byte)0x30, "ADD SP,e8 flags mismatch");

        int c2 = cpu.Step();
        var s2 = cpu.GetState();
        AssertEq(c2, 12, "LD HL,SP+e8 cycle mismatch");
        AssertEq((ushort)((s2.H << 8) | s2.L), (ushort)0x00FF, "LD HL,SP+e8 result mismatch");
        AssertEq((byte)(s2.F & 0xF0), (byte)0x00, "LD HL,SP+e8 flags mismatch");
    }

    private static void TestCbRegisterAndHlBehavior()
    {
        var bus = new FlatBus();
        var ints = new TestInterrupts();
        bus.Load(0x0100, new byte[]
        {
            0x21, 0x00, 0xC0, // LD HL,0xC000
            0x06, 0x80,       // LD B,0x80
            0x36, 0x80,       // LD (HL),0x80
            0xCB, 0x00,       // RLC B
            0xCB, 0x06        // RLC (HL)
        });
        var cpu = MakeCpu(bus, ints);

        AssertEq(cpu.Step(), 12, "LD HL cycle mismatch");
        AssertEq(cpu.Step(), 8, "LD B cycle mismatch");
        AssertEq(cpu.Step(), 12, "LD (HL) cycle mismatch");
        AssertEq(cpu.Step(), 8, "CB reg cycle mismatch");
        var s1 = cpu.GetState();
        AssertEq(s1.B, (byte)0x01, "RLC B result mismatch");
        AssertEq((byte)(s1.F & 0x10), (byte)0x10, "RLC B carry mismatch");

        AssertEq(cpu.Step(), 16, "CB (HL) cycle mismatch");
        AssertEq(bus.Read(0xC000), (byte)0x01, "RLC (HL) result mismatch");
    }

    private static void TestCbBitResSetOps()
    {
        var bus = new FlatBus();
        var ints = new TestInterrupts();
        bus.Load(0x0100, new byte[]
        {
            0x06, 0x10, // LD B,0x10
            0xCB, 0x40, // BIT 0,B -> Z=1
            0xCB, 0xC0, // SET 0,B -> B=0x11
            0xCB, 0x80  // RES 0,B -> B=0x10
        });

        var cpu = MakeCpu(bus, ints);
        cpu.Step();
        int c1 = cpu.Step();
        var s1 = cpu.GetState();
        AssertEq(c1, 8, "BIT opcode cycle mismatch");
        AssertEq((byte)(s1.F & 0xA0), (byte)0xA0, "BIT should set Z and H");

        int c2 = cpu.Step();
        var s2 = cpu.GetState();
        AssertEq(c2, 8, "SET opcode cycle mismatch");
        AssertEq(s2.B, (byte)0x11, "SET result mismatch");

        int c3 = cpu.Step();
        var s3 = cpu.GetState();
        AssertEq(c3, 8, "RES opcode cycle mismatch");
        AssertEq(s3.B, (byte)0x10, "RES result mismatch");
    }

    private static void TestLdRrMatrixCyclesAndDataflow()
    {
        // Build one instruction per LD r,r opcode except HALT.
        var bytes = new List<byte>();
        for (int op = 0x40; op <= 0x7F; op++)
        {
            if (op == 0x76) continue; // HALT
            bytes.Add((byte)op);
        }

        var bus = new FlatBus();
        bus.Load(0x0100, bytes.ToArray());
        bus.Write(0xC000, 0xA5); // value for (HL) reads

        var cpu = MakeCpu(bus, new TestInterrupts());
        var s = cpu.GetState();
        s.A = 0x11; s.B = 0x22; s.C = 0x33; s.D = 0x44; s.E = 0x55; s.H = 0xC0; s.L = 0x00;
        cpu.SetState(s);

        foreach (byte op in bytes)
        {
            var before = cpu.GetState();
            int dst = (op >> 3) & 0x07;
            int src = op & 0x07;
            byte expected = ReadRegOrHlForTest(before, src, bus);
            int expectedCycles = (dst == 6 || src == 6) ? 8 : 4;

            int gotCycles = cpu.Step();
            AssertEq(gotCycles, expectedCycles, "LD r,r cycle mismatch for opcode 0x" + op.ToString("X2"));

            var after = cpu.GetState();
            byte actual = ReadRegOrHlForTest(after, dst, bus);
            AssertEq(actual, expected, "LD r,r dataflow mismatch for opcode 0x" + op.ToString("X2"));
        }
    }

    private static void TestAluRegisterMatrixCycles()
    {
        var bus = new FlatBus();
        var opcodes = new List<byte>();
        for (int op = 0x80; op <= 0xBF; op++) opcodes.Add((byte)op);
        bus.Load(0x0100, opcodes.ToArray());
        bus.Write(0xC000, 0x01);

        var cpu = MakeCpu(bus, new TestInterrupts());
        var s = cpu.GetState();
        s.A = 0x10; s.B = 0x01; s.C = 0x02; s.D = 0x03; s.E = 0x04; s.H = 0xC0; s.L = 0x00;
        s.F = 0x00;
        cpu.SetState(s);

        foreach (byte op in opcodes)
        {
            int src = op & 0x07;
            int expectedCycles = (src == 6) ? 8 : 4;
            int got = cpu.Step();
            AssertEq(got, expectedCycles, "ALU A,r cycle mismatch for opcode 0x" + op.ToString("X2"));
        }
    }

    private static void TestPushPopAfMasksLowFlagsNibble()
    {
        var bus = new FlatBus();
        var ints = new TestInterrupts();
        bus.Load(0x0100, new byte[] { 0xF5, 0x3E, 0x00, 0xF1 }); // PUSH AF; LD A,0; POP AF

        var cpu = MakeCpu(bus, ints);
        var s = cpu.GetState();
        s.SP = 0xFFFE;
        s.A = 0xAB;
        s.F = 0xFF; // lower nibble should be ignored on restore
        cpu.SetState(s);

        AssertEq(cpu.Step(), 16, "PUSH AF cycle mismatch");
        AssertEq(cpu.Step(), 8, "LD A,d8 cycle mismatch");
        AssertEq(cpu.Step(), 12, "POP AF cycle mismatch");

        var end = cpu.GetState();
        AssertEq(end.A, (byte)0xAB, "POP AF A mismatch");
        AssertEq((byte)(end.F & 0x0F), (byte)0x00, "POP AF should clear low flag nibble");
    }

    private static void TestLdhAndAbsoluteLoadStorePaths()
    {
        var bus = new FlatBus();
        var ints = new TestInterrupts();
        bus.Load(0x0100, new byte[]
        {
            0x3E, 0x77,       // LD A,0x77
            0xE0, 0x42,       // LDH (0xFF42),A
            0x3E, 0x00,       // LD A,0
            0xF0, 0x42,       // LDH A,(0xFF42)
            0x0E, 0x80,       // LD C,0x80
            0xE2,             // LD (0xFF00+C),A
            0x3E, 0x00,       // LD A,0
            0xF2,             // LD A,(0xFF00+C)
            0xEA, 0x00, 0xC0, // LD (0xC000),A
            0x3E, 0x11,       // LD A,0x11
            0xFA, 0x00, 0xC0  // LD A,(0xC000)
        });

        var cpu = MakeCpu(bus, ints);

        AssertEq(cpu.Step(), 8, "LD A,d8 cycle mismatch");
        AssertEq(cpu.Step(), 12, "LDH (a8),A cycle mismatch");
        AssertEq(bus.Read(0xFF42), (byte)0x77, "LDH (a8),A data mismatch");
        AssertEq(cpu.Step(), 8, "LD A,d8 cycle mismatch");
        AssertEq(cpu.Step(), 12, "LDH A,(a8) cycle mismatch");
        AssertEq(cpu.GetState().A, (byte)0x77, "LDH A,(a8) data mismatch");
        AssertEq(cpu.Step(), 8, "LD C,d8 cycle mismatch");
        AssertEq(cpu.Step(), 8, "LD (FF00+C),A cycle mismatch");
        AssertEq(bus.Read(0xFF80), (byte)0x77, "LD (FF00+C),A data mismatch");
        AssertEq(cpu.Step(), 8, "LD A,d8 cycle mismatch");
        AssertEq(cpu.Step(), 8, "LD A,(FF00+C) cycle mismatch");
        AssertEq(cpu.GetState().A, (byte)0x77, "LD A,(FF00+C) data mismatch");
        AssertEq(cpu.Step(), 16, "LD (a16),A cycle mismatch");
        AssertEq(bus.Read(0xC000), (byte)0x77, "LD (a16),A data mismatch");
        AssertEq(cpu.Step(), 8, "LD A,d8 cycle mismatch");
        AssertEq(cpu.Step(), 16, "LD A,(a16) cycle mismatch");
        AssertEq(cpu.GetState().A, (byte)0x77, "LD A,(a16) data mismatch");
    }

    private static void TestStopInstructionStateAndCycle()
    {
        var bus = new FlatBus();
        bus.Load(0x0100, new byte[] { 0x10, 0x00, 0x00 }); // STOP, pad, NOP
        var cpu = MakeCpu(bus, new TestInterrupts());

        int c1 = cpu.Step();
        var s1 = cpu.GetState();
        AssertEq(c1, 4, "STOP cycle mismatch");
        AssertTrue(s1.IsStopped, "STOP should set stopped state");
        AssertEq(s1.PC, (ushort)0x0102, "STOP should advance PC by one extra byte");
    }

    private struct OracleCase
    {
        public string Name;
        public byte[] Program;
        public Action<Cpu2StructuredSnapshot, FlatBus> Validate;
    }

    private static void TestOpcodeOracleSamples()
    {
        var cases = new List<OracleCase>
        {
            new OracleCase
            {
                Name = "INC B flags",
                Program = new byte[] { 0x06, 0x0F, 0x04 },
                Validate = (s, bus) =>
                {
                    AssertEq(s.B, (byte)0x10, "INC B value mismatch");
                    AssertEq((byte)(s.F & 0xF0), (byte)0x20, "INC B flags mismatch");
                }
            },
            new OracleCase
            {
                Name = "DEC B flags",
                Program = new byte[] { 0x06, 0x10, 0x05 },
                Validate = (s, bus) =>
                {
                    AssertEq(s.B, (byte)0x0F, "DEC B value mismatch");
                    AssertEq((byte)(s.F & 0xF0), (byte)0x60, "DEC B flags mismatch");
                }
            },
            new OracleCase
            {
                Name = "ADD A,d8 carry",
                Program = new byte[] { 0x3E, 0xFF, 0xC6, 0x01 },
                Validate = (s, bus) =>
                {
                    AssertEq(s.A, (byte)0x00, "ADD A,d8 value mismatch");
                    AssertEq((byte)(s.F & 0xF0), (byte)0xB0, "ADD A,d8 flags mismatch");
                }
            },
            new OracleCase
            {
                Name = "SUB d8",
                Program = new byte[] { 0x3E, 0x10, 0xD6, 0x01 },
                Validate = (s, bus) =>
                {
                    AssertEq(s.A, (byte)0x0F, "SUB d8 value mismatch");
                    AssertEq((byte)(s.F & 0xF0), (byte)0x60, "SUB d8 flags mismatch");
                }
            },
            new OracleCase
            {
                Name = "XOR A",
                Program = new byte[] { 0x3E, 0x5A, 0xAF },
                Validate = (s, bus) =>
                {
                    AssertEq(s.A, (byte)0x00, "XOR A value mismatch");
                    AssertEq((byte)(s.F & 0xF0), (byte)0x80, "XOR A flags mismatch");
                }
            },
            new OracleCase
            {
                Name = "CP d8",
                Program = new byte[] { 0x3E, 0x10, 0xFE, 0x10 },
                Validate = (s, bus) =>
                {
                    AssertEq(s.A, (byte)0x10, "CP should not modify A");
                    AssertEq((byte)(s.F & 0xF0), (byte)0xC0, "CP flags mismatch");
                }
            },
            new OracleCase
            {
                Name = "JR relative negative",
                Program = new byte[] { 0x18, 0xFE },
                Validate = (s, bus) =>
                {
                    AssertEq(s.PC, (ushort)0x0100, "JR -2 should loop to start");
                }
            },
            new OracleCase
            {
                Name = "LD (HL),d8 side effect",
                Program = new byte[] { 0x21, 0x00, 0xC0, 0x36, 0x3C },
                Validate = (s, bus) =>
                {
                    AssertEq(bus.Read(0xC000), (byte)0x3C, "LD (HL),d8 memory mismatch");
                }
            },
            new OracleCase
            {
                Name = "LD A,(BC)",
                Program = new byte[] { 0x01, 0x00, 0xC0, 0x0A },
                Validate = (s, bus) =>
                {
                    AssertEq(s.A, (byte)0x66, "LD A,(BC) mismatch");
                }
            }
        };

        foreach (var tc in cases)
        {
            var bus = new FlatBus();
            bus.Load(0x0100, tc.Program);
            bus.Write(0xC000, 0x66);
            var cpu = MakeCpu(bus, new TestInterrupts());
            var s = cpu.GetState();
            s.F = 0x00;
            cpu.SetState(s);

            for (int i = 0; i < CountInstructions(tc.Program); i++)
            {
                cpu.Step();
            }

            tc.Validate(cpu.GetState(), bus);
        }
    }

    private static int CountInstructions(byte[] program)
    {
        // For small oracle snippets, one-byte approximation is enough
        // because each case executes known instruction counts.
        // We count explicit opcodes in each snippet manually by reading first byte stream.
        int pc = 0;
        int count = 0;
        while (pc < program.Length)
        {
            byte op = program[pc++];
            count++;
            if (op == 0x01 || op == 0x11 || op == 0x21 || op == 0x31 || op == 0xC3 || op == 0xC2 || op == 0xCA || op == 0xD2 || op == 0xDA || op == 0xCD || op == 0xC4 || op == 0xCC || op == 0xD4 || op == 0xDC || op == 0xEA || op == 0xFA || op == 0x08)
            {
                pc += 2;
            }
            else if (op == 0x06 || op == 0x0E || op == 0x16 || op == 0x1E || op == 0x26 || op == 0x2E || op == 0x3E || op == 0x18 || op == 0x20 || op == 0x28 || op == 0x30 || op == 0x38 || op == 0xC6 || op == 0xCE || op == 0xD6 || op == 0xDE || op == 0xE6 || op == 0xEE || op == 0xF6 || op == 0xFE || op == 0x36 || op == 0xE0 || op == 0xF0 || op == 0xE8 || op == 0xF8)
            {
                pc += 1;
            }
            else if (op == 0xCB)
            {
                pc += 1;
            }
        }
        return count;
    }

    private static void TestCycleTraceRegression()
    {
        var bus = new FlatBus();
        var ints = new TestInterrupts();
        bus.Load(0x0100, new byte[]
        {
            0x06, 0x01, // LD B,1
            0x04,       // INC B
            0x20, 0x02, // JR NZ,+2 (taken)
            0x00,       // NOP (skipped)
            0x00,       // NOP (skipped)
            0x05,       // DEC B
            0x20, 0xFE  // JR NZ,-2 (loop)
        });
        var cpu = MakeCpu(bus, ints);

        int[] expected = { 8, 4, 12, 4, 12, 12 };
        for (int i = 0; i < expected.Length; i++)
        {
            int got = cpu.Step();
            AssertEq(got, expected[i], "Cycle trace mismatch at step " + i);
        }

        var s = cpu.GetState();
        AssertEq(s.B, (byte)0x01, "Trace register B mismatch");
        AssertEq(s.PC, (ushort)0x0108, "Trace PC mismatch");
    }

    private static void TestDeterministicTraceDigest()
    {
        var bus = new FlatBus();
        var ints = new TestInterrupts();
        bus.Load(0x0100, new byte[]
        {
            0x3E, 0x10,       // LD A,10
            0x06, 0x01,       // LD B,1
            0x0E, 0x02,       // LD C,2
            0x80,             // ADD A,B
            0x81,             // ADD A,C
            0x05,             // DEC B
            0x20, 0xFA,       // JR NZ,-6
            0xAF,             // XOR A
            0xC3, 0x00, 0x01  // JP 0100
        });

        var cpu = MakeCpu(bus, ints);
        ulong hash = 1469598103934665603UL; // FNV-1a offset basis
        for (int i = 0; i < 128; i++)
        {
            var s = cpu.GetState();
            int cyc = cpu.Step();
            hash = Fnv1aMix(hash, (byte)(s.PC & 0xFF));
            hash = Fnv1aMix(hash, (byte)(s.PC >> 8));
            hash = Fnv1aMix(hash, s.A);
            hash = Fnv1aMix(hash, s.F);
            hash = Fnv1aMix(hash, s.B);
            hash = Fnv1aMix(hash, s.C);
            hash = Fnv1aMix(hash, s.D);
            hash = Fnv1aMix(hash, s.E);
            hash = Fnv1aMix(hash, s.H);
            hash = Fnv1aMix(hash, s.L);
            hash = Fnv1aMix(hash, (byte)cyc);
        }

        // Regression anchor for deterministic execution of the above trace.
        const ulong Expected = 0x5B462445E8455093UL;
        AssertEq(hash, Expected, "Deterministic trace digest mismatch");
    }

    private static string FindDefaultRomDir()
    {
        string a = "gb-test-roms/cpu_instrs/individual";
        if (Directory.Exists(a)) return a;

        string b = "roms/cpu_instrs/individual";
        if (Directory.Exists(b)) return b;

        return a;
    }

    private static string FindDefaultGbRoot()
    {
        string a = "gb-test-roms";
        if (Directory.Exists(a)) return a;
        string b = "roms";
        if (Directory.Exists(b)) return b;
        return a;
    }

    private static string FindDefaultAcid2Rom()
    {
        string[] candidates =
        {
            "gb-test-roms/dmg-acid2.gb",
            "dmg-acid2.gb",
            "roms/dmg-acid2.gb",
            "dmg-acid2/dmg-acid2.gb"
        };

        foreach (var c in candidates)
            if (File.Exists(c)) return c;

        return candidates[0];
    }

    private static string FindDefaultAcid2Reference()
    {
        string[] candidates =
        {
            "test-assets/dmg-acid2/reference-dmg.png",
            "dmg-acid2/img/reference-dmg.png",
            "img/reference-dmg.png"
        };

        foreach (var c in candidates)
            if (File.Exists(c)) return c;

        return candidates[0];
    }

    private static void RunRomSmokeTests(string romDir, int maxCycles)
    {
        if (!Directory.Exists(romDir))
        {
            throw new TestFailure("ROM directory not found: " + romDir);
        }

        var roms = Directory.GetFiles(romDir, "*.gb");
        Array.Sort(roms, StringComparer.OrdinalIgnoreCase);
        if (roms.Length == 0)
            throw new TestFailure("No ROM files found in: " + romDir);

        Console.WriteLine("rom smoke tests:");
        Console.WriteLine("  ROM                          result       cycles");

        foreach (var rom in roms)
        {
            var result = RunSingleRom(rom, maxCycles);
            Console.WriteLine("  " + Path.GetFileName(rom).PadRight(28) + " " + result.Status.PadRight(12) + " " + result.Cycles.ToString().PadRight(12));

            if (!string.Equals(result.Status, "Passed", StringComparison.OrdinalIgnoreCase))
            {
                throw new TestFailure("ROM did not pass: " + Path.GetFileName(rom) + " (status=" + result.Status + ")");
            }
        }
    }

    private sealed class BlarggSuiteSpec
    {
        public string Name;
        public string Pattern;
        public bool Required;
    }

    private static void RunBlarggSuites(string gbRoot, int maxCycles, bool requireMemTiming)
    {
        if (!Directory.Exists(gbRoot))
        {
            Console.WriteLine("blargg suites:");
            Console.WriteLine("  skipped: root not found: " + gbRoot);
            return;
        }

        var specs = new List<BlarggSuiteSpec>
        {
            new BlarggSuiteSpec { Name = "mem_timing", Pattern = "mem_timing/individual/*.gb", Required = requireMemTiming },
            new BlarggSuiteSpec { Name = "mem_timing-2", Pattern = "mem_timing-2/rom_singles/*.gb", Required = false },
            new BlarggSuiteSpec { Name = "oam_bug", Pattern = "oam_bug/rom_singles/*.gb", Required = false },
            new BlarggSuiteSpec { Name = "interrupt_time", Pattern = "interrupt_time/*.gb", Required = false },
            new BlarggSuiteSpec { Name = "halt_bug", Pattern = "halt_bug.gb", Required = false }
        };

        Console.WriteLine("blargg suites:");
        foreach (var spec in specs)
        {
            RunOneBlarggSuite(gbRoot, spec, maxCycles);
        }
    }

    private static void RunOneBlarggSuite(string gbRoot, BlarggSuiteSpec spec, int maxCycles)
    {
        var files = ResolvePattern(gbRoot, spec.Pattern);
        if (files.Count == 0)
        {
            Console.WriteLine("  " + spec.Name + ": skipped (no ROMs)");
            if (spec.Required) throw new TestFailure("Required suite has no ROMs: " + spec.Name);
            return;
        }

        int pass = 0;
        int fail = 0;
        int noResult = 0;
        var failed = new List<string>();

        foreach (var path in files)
        {
            var result = RunSingleRom(path, maxCycles);
            if (string.Equals(result.Status, "Passed", StringComparison.OrdinalIgnoreCase)) pass++;
            else if (string.Equals(result.Status, "Failed", StringComparison.OrdinalIgnoreCase))
            {
                fail++;
                failed.Add(Path.GetFileName(path));
            }
            else
            {
                noResult++;
            }
        }

        Console.WriteLine("  " + spec.Name + ": pass=" + pass + " fail=" + fail + " no-result=" + noResult);
        if (failed.Count > 0)
            Console.WriteLine("    failed: " + string.Join(", ", failed.ToArray()));

        if (spec.Required && (fail > 0 || noResult > 0))
            throw new TestFailure("Required suite failed: " + spec.Name + " (pass=" + pass + ", fail=" + fail + ", no-result=" + noResult + ")");
    }

    private static List<string> ResolvePattern(string gbRoot, string pattern)
    {
        string normalized = pattern.Replace('\\', '/');
        string dirPart = Path.GetDirectoryName(normalized.Replace('/', Path.DirectorySeparatorChar));
        string namePart = Path.GetFileName(normalized);

        string baseDir = string.IsNullOrEmpty(dirPart) ? gbRoot : Path.Combine(gbRoot, dirPart);
        var outFiles = new List<string>();
        if (!Directory.Exists(baseDir)) return outFiles;

        if (namePart.Contains("*") || namePart.Contains("?"))
        {
            outFiles.AddRange(Directory.GetFiles(baseDir, namePart, SearchOption.TopDirectoryOnly));
        }
        else
        {
            string full = Path.Combine(baseDir, namePart);
            if (File.Exists(full)) outFiles.Add(full);
        }

        outFiles.Sort(StringComparer.OrdinalIgnoreCase);
        return outFiles;
    }

    private static void RunAcid2VisualTest(
        string acid2RomPath,
        string referencePngPath,
        int targetFrames,
        int tolerance,
        int maxDiffPixels,
        int maxCycles,
        bool require)
    {
        Console.WriteLine("acid2 visual test:");

        if (!File.Exists(acid2RomPath))
        {
            string msg = "  skipped: ROM not found at " + acid2RomPath;
            if (require) throw new TestFailure("dmg-acid2 ROM not found: " + acid2RomPath);
            Console.WriteLine(msg);
            return;
        }

        if (!File.Exists(referencePngPath))
        {
            string msg = "  skipped: reference image not found at " + referencePngPath;
            if (require) throw new TestFailure("dmg-acid2 reference image not found: " + referencePngPath);
            Console.WriteLine(msg);
            return;
        }

        var gb = new Gameboy(acid2RomPath, CpuBackend.Cpu2Structured);
        int frames = 0;
        IFrameBuffer lastFrame = null;
        gb.ppu.OnFrameReady += fb =>
        {
            frames++;
            lastFrame = fb;
        };

        int ran = 0;
        int batch = 256;
        while (ran < maxCycles && frames < targetFrames)
        {
            gb.TickCycles(batch);
            ran += batch;
        }

        if (lastFrame == null || frames < targetFrames)
        {
            throw new TestFailure("dmg-acid2 did not produce enough frames (got " + frames + ", need " + targetFrames + ")");
        }

        try
        {
            using (var bmp = new Bitmap(referencePngPath))
            {
                if (bmp.Width != 160 || bmp.Height != 144)
                    throw new TestFailure("dmg-acid2 reference image must be 160x144, got " + bmp.Width + "x" + bmp.Height);

                int diff = 0;
                for (int y = 0; y < 144; y++)
                {
                    for (int x = 0; x < 160; x++)
                    {
                        int actualIndex = lastFrame.GetPixel(x, y);
                        if ((uint)actualIndex > 3) actualIndex = 0;

                        Color c = bmp.GetPixel(x, y);
                        int expectedIndex = QuantizeGrayToDmgIndex((c.R + c.G + c.B) / 3);
                        int actualGray = DmgGrayForIndex(actualIndex);
                        int expectedGray = DmgGrayForIndex(expectedIndex);

                        if (Math.Abs(actualGray - expectedGray) > tolerance)
                            diff++;
                    }
                }

                Console.WriteLine("  frames captured: " + frames);
                Console.WriteLine("  cycles run: " + ran);
                Console.WriteLine("  differing pixels: " + diff + " (max allowed " + maxDiffPixels + ")");

                if (diff > maxDiffPixels)
                    throw new TestFailure("dmg-acid2 mismatch: " + diff + " differing pixels");
            }
        }
        catch (TestFailure)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new TestFailure("dmg-acid2 image compare failed: " + ex.Message);
        }
    }

    private static int DmgGrayForIndex(int idx)
    {
        switch (idx)
        {
            case 0: return 255;
            case 1: return 170;
            case 2: return 85;
            default: return 0;
        }
    }

    private static int QuantizeGrayToDmgIndex(int gray)
    {
        int[] levels = { 255, 170, 85, 0 };
        int best = 0;
        int bestDist = int.MaxValue;

        for (int i = 0; i < levels.Length; i++)
        {
            int d = Math.Abs(gray - levels[i]);
            if (d < bestDist)
            {
                best = i;
                bestDist = d;
            }
        }

        return best;
    }

    private static byte ReadRegOrHlForTest(Cpu2StructuredSnapshot s, int regIndex, FlatBus bus)
    {
        switch (regIndex)
        {
            case 0: return s.B;
            case 1: return s.C;
            case 2: return s.D;
            case 3: return s.E;
            case 4: return s.H;
            case 5: return s.L;
            case 6: return bus.Read((ushort)((s.H << 8) | s.L));
            default: return s.A;
        }
    }

    private static ulong Fnv1aMix(ulong hash, byte value)
    {
        hash ^= value;
        hash *= 1099511628211UL;
        return hash;
    }

    private struct RomResult
    {
        public string Status;
        public int Cycles;
        public int ResultCode;
        public string Source;
    }

    private static RomResult RunSingleRom(string romPath, int maxCycles)
    {
        var gb = new Gameboy(romPath, CpuBackend.Cpu2Structured);
        var serial = new StringBuilder();
        int batch = 256;
        int ran = 0;
        string status = "no-result";
        int code = -1;
        string source = "none";

        while (ran < maxCycles)
        {
            gb.TickCycles(batch);
            ran += batch;
            DrainSerial(gb.bus, serial);

            if (TryReadBlarggMemoryResult(gb.bus, out int memCode))
            {
                code = memCode;
                status = memCode == 0 ? "Passed" : "Failed";
                source = "blargg-mem";
                break;
            }

            string s = serial.ToString();
            if (s.IndexOf("Passed", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                status = "Passed";
                source = "serial";
                code = 0;
                break;
            }
            if (s.IndexOf("Failed", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                status = "Failed";
                source = "serial";
                code = 1;
                break;
            }
        }

        return new RomResult { Status = status, Cycles = ran, ResultCode = code, Source = source };
    }

    private static bool TryReadBlarggMemoryResult(Bus bus, out int code)
    {
        code = -1;
        // Signature used by blargg suites: A001..A003 = DE B0 61.
        if (bus.Read(0xA001) != 0xDE) return false;
        if (bus.Read(0xA002) != 0xB0) return false;
        if (bus.Read(0xA003) != 0x61) return false;

        byte status = bus.Read(0xA000);
        if (status == 0x80) return false; // still running

        code = status;
        return true;
    }

    private static void DrainSerial(Bus bus, StringBuilder serial)
    {
        if (bus.Read(0xFF02) == 0x81)
        {
            serial.Append((char)bus.Read(0xFF01));
            bus.Write(0xFF02, 0);
        }
    }
}
