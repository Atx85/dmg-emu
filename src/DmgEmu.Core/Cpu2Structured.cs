using System;
using System.Collections.Generic;

namespace DmgEmu.Core
{
    // Cpu2 is a structured sketch for a full CPU implementation.
    // It is intentionally incomplete and meant to guide a proper design.
    public sealed class Cpu2Structured
    {
        private readonly ICpuBus bus;
        private readonly IClock clock;
        private readonly IInterruptController interrupts;
        private readonly ITraceSink trace;
        private readonly bool traceEnabled;
        private int busCyclesThisStep;

        private Registers regs;
        private CpuState state;

        private readonly InstructionDecoder decoder;
        private readonly InstructionExecutor executor;
        private readonly OperandFetcher operandFetcher;

        public Cpu2Structured(ICpuBus bus, IClock clock, IInterruptController interrupts, ITraceSink trace = null)
        {
            this.bus = new TimedCpuBus(bus, this);
            this.clock = clock;
            this.interrupts = interrupts;
            this.trace = trace ?? new NullTraceSink();
            traceEnabled = !(this.trace is NullTraceSink);

            regs = Registers.PowerOn();
            state = CpuState.PowerOn();
            decoder = new InstructionDecoder();
            executor = new InstructionExecutor();
            operandFetcher = new OperandFetcher();
        }

        // Step one instruction (or interrupt entry) and return cycles used.
        public int Step()
        {
            busCyclesThisStep = 0;
            if (state.EiPending)
            {
                state.IME = true;
                state.EiPending = false;
            }

            if (TryHandleInterrupt(out int intCycles))
            {
                AdvanceRemainder(intCycles);
                return intCycles;
            }

            if (state.Halted)
            {
                AdvanceRemainder(4);
                return 4;
            }

            ushort pc = regs.PC;
            byte opcode = bus.Read(pc);
            regs.PC++;

            if (TryFastExecute(opcode, out int fastCycles))
            {
                AdvanceRemainder(fastCycles);
                return fastCycles;
            }

            Instruction instr = decoder.Decode(opcode, bus, ref regs);
            DecodedOperands operands = default(DecodedOperands);
            if (instr.Mode == AddressingMode.Imm8 ||
                instr.Mode == AddressingMode.Imm16 ||
                instr.Mode == AddressingMode.HighRamImm8 ||
                instr.Mode == AddressingMode.HighRamRegC)
            {
                operands = operandFetcher.Fetch(instr, bus, ref regs);
            }
            if (traceEnabled) trace.Trace(regs, instr);

            int cycles = executor.Execute(instr, operands, bus, ref regs, ref state, interrupts);
            AdvanceRemainder(cycles);
            return cycles;
        }

        public Registers Snapshot() => regs;

        public Cpu2StructuredSnapshot GetState()
        {
            return new Cpu2StructuredSnapshot
            {
                A = regs.A,
                F = regs.F,
                B = regs.B,
                C = regs.C,
                D = regs.D,
                E = regs.E,
                H = regs.H,
                L = regs.L,
                SP = regs.SP,
                PC = regs.PC,
                IME = state.IME,
                EiPending = state.EiPending,
                IsHalted = state.Halted,
                HaltBug = state.HaltBug,
                IsStopped = state.Stopped
            };
        }

        public void SetState(Cpu2StructuredSnapshot s)
        {
            regs.A = s.A;
            regs.F = (byte)(s.F & 0xF0);
            regs.B = s.B;
            regs.C = s.C;
            regs.D = s.D;
            regs.E = s.E;
            regs.H = s.H;
            regs.L = s.L;
            regs.SP = s.SP;
            regs.PC = s.PC;

            state.IME = s.IME;
            state.EiPending = s.EiPending;
            state.Halted = s.IsHalted;
            state.HaltBug = s.HaltBug;
            state.Stopped = s.IsStopped;
        }

        private bool TryHandleInterrupt(out int cycles)
        {
            cycles = 0;
            InterruptFlags pending = interrupts.Pending();
            if (pending == InterruptFlags.None)
                return false;

            if (!state.IME)
            {
                // With IME=0 and pending interrupt, HALT exits without servicing.
                state.Halted = false;
                state.HaltBug = false;
                return false;
            }

            // IME set: service highest priority interrupt
            state.IME = false;
            state.Halted = false;
            int bit = interrupts.HighestPendingBit(pending);
            interrupts.Clear(bit);

            Push16(regs.PC);
            regs.PC = InterruptController.VectorFor(bit);
            cycles = 20;
            return true;
        }

        private void Push16(ushort value)
        {
            regs.SP--;
            bus.Write(regs.SP, (byte)(value >> 8));
            regs.SP--;
            bus.Write(regs.SP, (byte)(value & 0xFF));
        }

        internal void OnBusAccess(int cycles)
        {
            busCyclesThisStep += cycles;
            clock.Advance(cycles);
        }

        private void AdvanceRemainder(int totalCycles)
        {
            int remaining = totalCycles - busCyclesThisStep;
            if (remaining > 0)
            {
                clock.Advance(remaining);
            }
            else if (remaining < 0)
            {
                // Clamp over-accounting to keep runtime stable while timing work is in progress.
                busCyclesThisStep = totalCycles;
            }
        }

        private bool TryFastExecute(byte opcode, out int cycles)
        {
            cycles = 0;

            // NOP is common and has no operand fetch path.
            if (opcode == 0x00)
            {
                cycles = 4;
                return true;
            }

            // LD r,r matrix except HALT.
            if (opcode >= 0x40 && opcode <= 0x7F && opcode != 0x76)
            {
                int dst = (opcode >> 3) & 0x07;
                int src = opcode & 0x07;
                byte value = ReadRegOrHlFast(src);
                WriteRegOrHlFast(dst, value);
                cycles = (dst == 6 || src == 6) ? 8 : 4;
                return true;
            }

            // ALU A,r matrix.
            if (opcode >= 0x80 && opcode <= 0xBF)
            {
                int src = opcode & 0x07;
                byte value = ReadRegOrHlFast(src);
                ExecAluAFast(opcode, value);
                cycles = (src == 6) ? 8 : 4;
                return true;
            }

            // LD r,d8 family used frequently in setup loops.
            if (opcode == 0x06 || opcode == 0x0E || opcode == 0x16 || opcode == 0x1E ||
                opcode == 0x26 || opcode == 0x2E || opcode == 0x3E)
            {
                byte imm = bus.Read(regs.PC);
                regs.PC++;
                switch (opcode)
                {
                    case 0x06: regs.B = imm; break;
                    case 0x0E: regs.C = imm; break;
                    case 0x16: regs.D = imm; break;
                    case 0x1E: regs.E = imm; break;
                    case 0x26: regs.H = imm; break;
                    case 0x2E: regs.L = imm; break;
                    case 0x3E: regs.A = imm; break;
                }
                cycles = 8;
                return true;
            }

            // Common register inc/dec and 16-bit pointer inc/dec.
            if (opcode == 0x04 || opcode == 0x05 || opcode == 0x0C || opcode == 0x0D ||
                opcode == 0x14 || opcode == 0x15 || opcode == 0x1C || opcode == 0x1D ||
                opcode == 0x24 || opcode == 0x25 || opcode == 0x2C || opcode == 0x2D ||
                opcode == 0x3C || opcode == 0x3D)
            {
                switch (opcode)
                {
                    case 0x04: regs.B = Inc8Fast(regs.B); break;
                    case 0x05: regs.B = Dec8Fast(regs.B); break;
                    case 0x0C: regs.C = Inc8Fast(regs.C); break;
                    case 0x0D: regs.C = Dec8Fast(regs.C); break;
                    case 0x14: regs.D = Inc8Fast(regs.D); break;
                    case 0x15: regs.D = Dec8Fast(regs.D); break;
                    case 0x1C: regs.E = Inc8Fast(regs.E); break;
                    case 0x1D: regs.E = Dec8Fast(regs.E); break;
                    case 0x24: regs.H = Inc8Fast(regs.H); break;
                    case 0x25: regs.H = Dec8Fast(regs.H); break;
                    case 0x2C: regs.L = Inc8Fast(regs.L); break;
                    case 0x2D: regs.L = Dec8Fast(regs.L); break;
                    case 0x3C: regs.A = Inc8Fast(regs.A); break;
                    case 0x3D: regs.A = Dec8Fast(regs.A); break;
                }
                cycles = 4;
                return true;
            }

            if (opcode == 0x03 || opcode == 0x0B || opcode == 0x13 || opcode == 0x1B ||
                opcode == 0x23 || opcode == 0x2B || opcode == 0x33 || opcode == 0x3B)
            {
                switch (opcode)
                {
                    case 0x03: regs.BC++; break;
                    case 0x0B: regs.BC--; break;
                    case 0x13: regs.DE++; break;
                    case 0x1B: regs.DE--; break;
                    case 0x23: regs.HL++; break;
                    case 0x2B: regs.HL--; break;
                    case 0x33: regs.SP++; break;
                    case 0x3B: regs.SP--; break;
                }
                cycles = 8;
                return true;
            }

            // JP a16 loop anchors.
            if (opcode == 0xC3)
            {
                byte lo = bus.Read(regs.PC++);
                byte hi = bus.Read(regs.PC++);
                ushort imm16 = (ushort)(lo | (hi << 8));
                regs.PC = imm16;
                cycles = 16;
                return true;
            }

            return false;
        }

        private byte ReadRegOrHlFast(int idx)
        {
            switch (idx)
            {
                case 0: return regs.B;
                case 1: return regs.C;
                case 2: return regs.D;
                case 3: return regs.E;
                case 4: return regs.H;
                case 5: return regs.L;
                case 6: return bus.Read(regs.HL);
                default: return regs.A;
            }
        }

        private void WriteRegOrHlFast(int idx, byte value)
        {
            switch (idx)
            {
                case 0: regs.B = value; break;
                case 1: regs.C = value; break;
                case 2: regs.D = value; break;
                case 3: regs.E = value; break;
                case 4: regs.H = value; break;
                case 5: regs.L = value; break;
                case 6: bus.Write(regs.HL, value); break;
                case 7: regs.A = value; break;
            }
        }

        private byte Inc8Fast(byte value)
        {
            byte result = (byte)(value + 1);
            byte f = (byte)(regs.F & 0x10); // preserve carry
            if (result == 0) f |= 0x80;
            if (((value & 0x0F) + 1) > 0x0F) f |= 0x20;
            regs.F = (byte)(f & 0xF0);
            return result;
        }

        private byte Dec8Fast(byte value)
        {
            byte result = (byte)(value - 1);
            byte f = (byte)((regs.F & 0x10) | 0x40); // preserve carry, set N
            if (result == 0) f |= 0x80;
            if ((value & 0x0F) == 0) f |= 0x20;
            regs.F = (byte)(f & 0xF0);
            return result;
        }

        private bool CarryFlagFast() => (regs.F & 0x10) != 0;

        private void SetFlagsZnHcFast(bool z, bool n, bool h, bool c)
        {
            byte f = 0;
            if (z) f |= 0x80;
            if (n) f |= 0x40;
            if (h) f |= 0x20;
            if (c) f |= 0x10;
            regs.F = (byte)(f & 0xF0);
        }

        private void ExecAluAFast(byte opcode, byte value)
        {
            byte a = regs.A;
            int group = (opcode >> 3) & 0x07;

            switch (group)
            {
                case 0: // ADD A,r
                {
                    int r = a + value;
                    regs.A = (byte)r;
                    SetFlagsZnHcFast(regs.A == 0, false, ((a & 0x0F) + (value & 0x0F)) > 0x0F, r > 0xFF);
                    return;
                }
                case 1: // ADC A,r
                {
                    int c = CarryFlagFast() ? 1 : 0;
                    int r = a + value + c;
                    regs.A = (byte)r;
                    SetFlagsZnHcFast(regs.A == 0, false, ((a & 0x0F) + (value & 0x0F) + c) > 0x0F, r > 0xFF);
                    return;
                }
                case 2: // SUB A,r
                {
                    int r = a - value;
                    regs.A = (byte)r;
                    SetFlagsZnHcFast(regs.A == 0, true, (a & 0x0F) < (value & 0x0F), r < 0);
                    return;
                }
                case 3: // SBC A,r
                {
                    int c = CarryFlagFast() ? 1 : 0;
                    int r = a - value - c;
                    regs.A = (byte)r;
                    SetFlagsZnHcFast(regs.A == 0, true, (a & 0x0F) < ((value & 0x0F) + c), a < (value + c));
                    return;
                }
                case 4: // AND A,r
                    regs.A = (byte)(a & value);
                    SetFlagsZnHcFast(regs.A == 0, false, true, false);
                    return;
                case 5: // XOR A,r
                    regs.A = (byte)(a ^ value);
                    SetFlagsZnHcFast(regs.A == 0, false, false, false);
                    return;
                case 6: // OR A,r
                    regs.A = (byte)(a | value);
                    SetFlagsZnHcFast(regs.A == 0, false, false, false);
                    return;
                case 7: // CP A,r
                {
                    int r = a - value;
                    SetFlagsZnHcFast(((byte)r) == 0, true, (a & 0x0F) < (value & 0x0F), r < 0);
                    return;
                }
            }
        }

    }

    internal sealed class TimedCpuBus : ICpuBus
    {
        private readonly ICpuBus inner;
        private readonly Cpu2Structured owner;

        public TimedCpuBus(ICpuBus inner, Cpu2Structured owner)
        {
            this.inner = inner;
            this.owner = owner;
        }

        public byte Read(ushort addr)
        {
            owner.OnBusAccess(4);
            return inner.Read(addr);
        }

        public void Write(ushort addr, byte value)
        {
            owner.OnBusAccess(4);
            inner.Write(addr, value);
        }
    }

    public interface ICpuBus
    {
        byte Read(ushort addr);
        void Write(ushort addr, byte value);
    }

    public interface IClock
    {
        void Advance(int cycles);
    }

    public interface IInterruptController
    {
        InterruptFlags Pending();
        int HighestPendingBit(InterruptFlags flags);
        void Clear(int bit);
    }

    [Flags]
    public enum InterruptFlags : byte
    {
        None = 0,
        VBlank = 1 << 0,
        Stat = 1 << 1,
        Timer = 1 << 2,
        Serial = 1 << 3,
        Joypad = 1 << 4
    }

    public static class InterruptController
    {
        public static ushort VectorFor(int bit)
        {
            switch (bit)
            {
                case 0: return 0x0040;
                case 1: return 0x0048;
                case 2: return 0x0050;
                case 3: return 0x0058;
                case 4: return 0x0060;
                default: return 0x0040;
            }
        }
    }

    public interface ITraceSink
    {
        void Trace(Registers regs, Instruction instr);
    }

    public sealed class NullTraceSink : ITraceSink
    {
        public void Trace(Registers regs, Instruction instr) { }
    }

    public struct Registers
    {
        public byte A;
        public byte F;
        public byte B;
        public byte C;
        public byte D;
        public byte E;
        public byte H;
        public byte L;

        public ushort SP;
        public ushort PC;

        public static Registers PowerOn()
        {
            return new Registers
            {
                A = 0x01,
                F = 0xB0,
                B = 0x00,
                C = 0x13,
                D = 0x00,
                E = 0xD8,
                H = 0x01,
                L = 0x4D,
                SP = 0xFFFE,
                PC = 0x0100
            };
        }

        public ushort AF
        {
            get { return (ushort)((A << 8) | (F & 0xF0)); }
            set { A = (byte)(value >> 8); F = (byte)(value & 0xF0); }
        }
        public ushort BC
        {
            get { return (ushort)((B << 8) | C); }
            set { B = (byte)(value >> 8); C = (byte)(value & 0xFF); }
        }
        public ushort DE
        {
            get { return (ushort)((D << 8) | E); }
            set { D = (byte)(value >> 8); E = (byte)(value & 0xFF); }
        }
        public ushort HL
        {
            get { return (ushort)((H << 8) | L); }
            set { H = (byte)(value >> 8); L = (byte)(value & 0xFF); }
        }
    }

    public struct CpuState
    {
        public bool IME;
        public bool Halted;
        public bool HaltBug;
        public bool Stopped;
        public bool EiPending;

        public static CpuState PowerOn()
        {
            return new CpuState
            {
                IME = true,
                Halted = false,
                HaltBug = false,
                Stopped = false,
                EiPending = false
            };
        }
    }

    public struct Instruction
    {
        public byte Opcode;
        public InstructionKind Kind;
        public ushort Imm16;
        public byte Imm8;
        public byte DstRegIndex;
        public byte SrcRegIndex;
        public int Cycles;
        public string Mnemonic;
        public AddressingMode Mode;
    }

    public enum AddressingMode
    {
        None,
        Reg,
        Imm8,
        Imm16,
        RegIndirect,
        Imm16Indirect,
        HighRamImm8,
        HighRamRegC
    }

    public enum InstructionKind
    {
        Nop,
        Ld,
        Add,
        Sub,
        Jp,
        Jr,
        Call,
        Ret,
        Rst,
        Bit,
        Res,
        Set,
        Misc
    }

    public sealed class InstructionDecoder
    {
        private readonly Instruction[] table;
        private readonly bool[] tableSet;

        public InstructionDecoder()
        {
            table = new Instruction[256];
            tableSet = new bool[256];
            SeedMinimal();
        }

        public Instruction Decode(byte opcode, ICpuBus bus, ref Registers regs)
        {
            if (opcode >= 0x40 && opcode <= 0x7F)
            {
                if (opcode == 0x76)
                {
                    return new Instruction { Opcode = opcode, Kind = InstructionKind.Misc, Cycles = 4, Mnemonic = "HALT", Mode = AddressingMode.None };
                }

                int dst = (opcode >> 3) & 0x07;
                int src = opcode & 0x07;
                int cycles = (dst == 6 || src == 6) ? 8 : 4;
                return new Instruction
                {
                    Opcode = opcode,
                    Kind = InstructionKind.Ld,
                    Cycles = cycles,
                    Mnemonic = "LD r,r",
                    Mode = AddressingMode.Reg,
                    DstRegIndex = (byte)dst,
                    SrcRegIndex = (byte)src
                };
            }

            if (opcode >= 0x80 && opcode <= 0xBF)
            {
                int src = opcode & 0x07;
                int cycles = (src == 6) ? 8 : 4;
                return new Instruction
                {
                    Opcode = opcode,
                    Kind = InstructionKind.Misc,
                    Cycles = cycles,
                    Mnemonic = "ALU A,r",
                    Mode = AddressingMode.Reg,
                    SrcRegIndex = (byte)src
                };
            }

            if (opcode == 0xC6 || opcode == 0xCE || opcode == 0xD6 || opcode == 0xDE ||
                opcode == 0xE6 || opcode == 0xEE || opcode == 0xF6 || opcode == 0xFE)
            {
                byte imm8 = bus.Read(regs.PC);
                regs.PC++;
                return new Instruction
                {
                    Opcode = opcode,
                    Kind = InstructionKind.Misc,
                    Cycles = 8,
                    Mnemonic = "ALU A,d8",
                    Mode = AddressingMode.Imm8,
                    Imm8 = imm8
                };
            }

            if (opcode == 0x18 || opcode == 0x20 || opcode == 0x28 || opcode == 0x30 || opcode == 0x38)
            {
                byte imm8 = bus.Read(regs.PC);
                regs.PC++;
                return new Instruction
                {
                    Opcode = opcode,
                    Kind = InstructionKind.Jr,
                    Cycles = 12,
                    Mnemonic = "JR e8",
                    Mode = AddressingMode.Imm8,
                    Imm8 = imm8
                };
            }

            if (opcode == 0xC2 || opcode == 0xC3 || opcode == 0xCA || opcode == 0xD2 || opcode == 0xDA)
            {
                byte lo = bus.Read(regs.PC++);
                byte hi = bus.Read(regs.PC++);
                ushort imm16 = (ushort)(lo | (hi << 8));
                return new Instruction
                {
                    Opcode = opcode,
                    Kind = InstructionKind.Jp,
                    Cycles = 16,
                    Mnemonic = "JP a16",
                    Mode = AddressingMode.Imm16,
                    Imm16 = imm16
                };
            }

            if (opcode == 0xE9)
            {
                return new Instruction
                {
                    Opcode = opcode,
                    Kind = InstructionKind.Jp,
                    Cycles = 4,
                    Mnemonic = "JP HL",
                    Mode = AddressingMode.None
                };
            }

            if (opcode == 0x27 || opcode == 0x2F || opcode == 0x37 || opcode == 0x3F ||
                opcode == 0x76 || opcode == 0xF3 || opcode == 0xFB)
            {
                return new Instruction
                {
                    Opcode = opcode,
                    Kind = InstructionKind.Misc,
                    Cycles = 4,
                    Mnemonic = "Misc flags/control",
                    Mode = AddressingMode.None
                };
            }

            if (opcode == 0xC0 || opcode == 0xC8 || opcode == 0xD0 || opcode == 0xD8 || opcode == 0xC9 || opcode == 0xD9)
            {
                return new Instruction
                {
                    Opcode = opcode,
                    Kind = InstructionKind.Ret,
                    Cycles = 16,
                    Mnemonic = "RET",
                    Mode = AddressingMode.None
                };
            }

            if (opcode == 0x34 || opcode == 0x35)
            {
                return new Instruction
                {
                    Opcode = opcode,
                    Kind = InstructionKind.Misc,
                    Cycles = 12,
                    Mnemonic = "INC/DEC (HL)",
                    Mode = AddressingMode.RegIndirect
                };
            }

            if (opcode == 0x36)
            {
                byte imm8 = bus.Read(regs.PC);
                regs.PC++;
                return new Instruction
                {
                    Opcode = opcode,
                    Kind = InstructionKind.Ld,
                    Cycles = 12,
                    Mnemonic = "LD (HL),d8",
                    Mode = AddressingMode.Imm8,
                    Imm8 = imm8
                };
            }

            if (opcode == 0x07 || opcode == 0x0F || opcode == 0x10 || opcode == 0x17 || opcode == 0x1F ||
                opcode == 0x09 || opcode == 0x19 || opcode == 0x29 || opcode == 0x39)
            {
                int cycles = (opcode == 0x09 || opcode == 0x19 || opcode == 0x29 || opcode == 0x39) ? 8 : 4;
                return new Instruction
                {
                    Opcode = opcode,
                    Kind = InstructionKind.Misc,
                    Cycles = cycles,
                    Mnemonic = "Remaining core ops",
                    Mode = AddressingMode.None
                };
            }

            if (opcode == 0xD3 || opcode == 0xDB || opcode == 0xDD || opcode == 0xE3 || opcode == 0xE4 ||
                opcode == 0xEB || opcode == 0xEC || opcode == 0xED || opcode == 0xF4 || opcode == 0xFC || opcode == 0xFD)
            {
                return new Instruction
                {
                    Opcode = opcode,
                    Kind = InstructionKind.Misc,
                    Cycles = 4,
                    Mnemonic = "Illegal",
                    Mode = AddressingMode.None
                };
            }

            if (opcode == 0xE0 || opcode == 0xF0)
            {
                byte imm8 = bus.Read(regs.PC);
                regs.PC++;
                return new Instruction
                {
                    Opcode = opcode,
                    Kind = InstructionKind.Ld,
                    Cycles = 12,
                    Mnemonic = "LDH a8",
                    Mode = AddressingMode.HighRamImm8,
                    Imm8 = imm8
                };
            }

            if (opcode == 0xE2 || opcode == 0xF2)
            {
                return new Instruction
                {
                    Opcode = opcode,
                    Kind = InstructionKind.Ld,
                    Cycles = 8,
                    Mnemonic = "LDH C",
                    Mode = AddressingMode.HighRamRegC
                };
            }

            if (opcode == 0xEA || opcode == 0xFA)
            {
                byte lo = bus.Read(regs.PC++);
                byte hi = bus.Read(regs.PC++);
                ushort imm16 = (ushort)(lo | (hi << 8));
                return new Instruction
                {
                    Opcode = opcode,
                    Kind = InstructionKind.Ld,
                    Cycles = 16,
                    Mnemonic = "LD a16",
                    Mode = AddressingMode.Imm16Indirect,
                    Imm16 = imm16
                };
            }

            if (opcode == 0x08)
            {
                byte lo = bus.Read(regs.PC++);
                byte hi = bus.Read(regs.PC++);
                ushort imm16 = (ushort)(lo | (hi << 8));
                return new Instruction
                {
                    Opcode = opcode,
                    Kind = InstructionKind.Ld,
                    Cycles = 20,
                    Mnemonic = "LD (a16),SP",
                    Mode = AddressingMode.Imm16Indirect,
                    Imm16 = imm16
                };
            }

            if (opcode == 0x02 || opcode == 0x0A || opcode == 0x12 || opcode == 0x1A ||
                opcode == 0x22 || opcode == 0x2A || opcode == 0x32 || opcode == 0x3A)
            {
                return new Instruction
                {
                    Opcode = opcode,
                    Kind = InstructionKind.Ld,
                    Cycles = 8,
                    Mnemonic = "LD A,(rr)/(HL+/-)",
                    Mode = AddressingMode.RegIndirect
                };
            }

            if (opcode == 0xE8 || opcode == 0xF8)
            {
                byte imm8 = bus.Read(regs.PC);
                regs.PC++;
                return new Instruction
                {
                    Opcode = opcode,
                    Kind = InstructionKind.Add,
                    Cycles = (opcode == 0xE8) ? 16 : 12,
                    Mnemonic = "SP/HL e8",
                    Mode = AddressingMode.Imm8,
                    Imm8 = imm8
                };
            }

            if (opcode == 0xF9)
            {
                return new Instruction
                {
                    Opcode = opcode,
                    Kind = InstructionKind.Ld,
                    Cycles = 8,
                    Mnemonic = "LD SP,HL",
                    Mode = AddressingMode.None
                };
            }

            if (opcode == 0xC4 || opcode == 0xCC || opcode == 0xD4 || opcode == 0xDC || opcode == 0xCD)
            {
                byte lo = bus.Read(regs.PC++);
                byte hi = bus.Read(regs.PC++);
                ushort imm16 = (ushort)(lo | (hi << 8));
                return new Instruction
                {
                    Opcode = opcode,
                    Kind = InstructionKind.Call,
                    Cycles = 24,
                    Mnemonic = "CALL a16",
                    Mode = AddressingMode.Imm16,
                    Imm16 = imm16
                };
            }

            if (opcode == 0xC7 || opcode == 0xCF || opcode == 0xD7 || opcode == 0xDF ||
                opcode == 0xE7 || opcode == 0xEF || opcode == 0xF7 || opcode == 0xFF)
            {
                return new Instruction
                {
                    Opcode = opcode,
                    Kind = InstructionKind.Rst,
                    Cycles = 16,
                    Mnemonic = "RST",
                    Mode = AddressingMode.None
                };
            }

            if (opcode == 0xC1 || opcode == 0xD1 || opcode == 0xE1 || opcode == 0xF1 ||
                opcode == 0xC5 || opcode == 0xD5 || opcode == 0xE5 || opcode == 0xF5)
            {
                return new Instruction
                {
                    Opcode = opcode,
                    Kind = InstructionKind.Misc,
                    Cycles = (opcode == 0xC1 || opcode == 0xD1 || opcode == 0xE1 || opcode == 0xF1) ? 12 : 16,
                    Mnemonic = "PUSH/POP",
                    Mode = AddressingMode.None
                };
            }

            if (opcode == 0xCB)
            {
                byte cb = bus.Read(regs.PC);
                regs.PC++;
                int regIndex = cb & 0x07;
                int cycles;
                if (cb <= 0x3F)
                {
                    cycles = (regIndex == 6) ? 16 : 8;
                }
                else if (cb <= 0x7F)
                {
                    cycles = (regIndex == 6) ? 12 : 8;
                }
                else
                {
                    cycles = (regIndex == 6) ? 16 : 8;
                }

                return new Instruction
                {
                    Opcode = 0xCB,
                    Kind = InstructionKind.Misc,
                    Cycles = cycles,
                    Mnemonic = "CB",
                    Mode = AddressingMode.Reg,
                    Imm8 = cb,
                    SrcRegIndex = (byte)regIndex
                };
            }

            Instruction instr;
            if (tableSet[opcode]) instr = table[opcode];
            else instr = new Instruction { Opcode = opcode, Kind = InstructionKind.Misc, Cycles = 4, Mnemonic = "??" };

            if (instr.Mnemonic.Contains("d8"))
            {
                instr.Imm8 = bus.Read(regs.PC);
                regs.PC++;
            }
            if (instr.Mnemonic.Contains("d16"))
            {
                byte lo = bus.Read(regs.PC++);
                byte hi = bus.Read(regs.PC++);
                instr.Imm16 = (ushort)(lo | (hi << 8));
            }
            return instr;
        }

        private void SeedMinimal()
        {
            // Implementation guide: expand this table to include all opcodes.
            // For each opcode, fill in Mnemonic, Kind, Cycles, and Mode.
            // Use Mode + operands to keep execution logic consistent.
            SetTable(0x00, new Instruction { Opcode = 0x00, Kind = InstructionKind.Nop, Cycles = 4, Mnemonic = "NOP", Mode = AddressingMode.None });
            SetTable(0x01, new Instruction { Opcode = 0x01, Kind = InstructionKind.Ld, Cycles = 12, Mnemonic = "LD BC,d16", Mode = AddressingMode.Imm16 });
            SetTable(0x03, new Instruction { Opcode = 0x03, Kind = InstructionKind.Misc, Cycles = 8, Mnemonic = "INC BC", Mode = AddressingMode.None });
            SetTable(0x04, new Instruction { Opcode = 0x04, Kind = InstructionKind.Misc, Cycles = 4, Mnemonic = "INC B", Mode = AddressingMode.None });
            SetTable(0x06, new Instruction { Opcode = 0x06, Kind = InstructionKind.Ld, Cycles = 8, Mnemonic = "LD B,d8", Mode = AddressingMode.Imm8 });
            SetTable(0x05, new Instruction { Opcode = 0x05, Kind = InstructionKind.Misc, Cycles = 4, Mnemonic = "DEC B", Mode = AddressingMode.None });
            SetTable(0x0B, new Instruction { Opcode = 0x0B, Kind = InstructionKind.Misc, Cycles = 8, Mnemonic = "DEC BC", Mode = AddressingMode.None });
            SetTable(0x0C, new Instruction { Opcode = 0x0C, Kind = InstructionKind.Misc, Cycles = 4, Mnemonic = "INC C", Mode = AddressingMode.None });
            SetTable(0x0E, new Instruction { Opcode = 0x0E, Kind = InstructionKind.Ld, Cycles = 8, Mnemonic = "LD C,d8", Mode = AddressingMode.Imm8 });
            SetTable(0x0D, new Instruction { Opcode = 0x0D, Kind = InstructionKind.Misc, Cycles = 4, Mnemonic = "DEC C", Mode = AddressingMode.None });
            SetTable(0x11, new Instruction { Opcode = 0x11, Kind = InstructionKind.Ld, Cycles = 12, Mnemonic = "LD DE,d16", Mode = AddressingMode.Imm16 });
            SetTable(0x13, new Instruction { Opcode = 0x13, Kind = InstructionKind.Misc, Cycles = 8, Mnemonic = "INC DE", Mode = AddressingMode.None });
            SetTable(0x14, new Instruction { Opcode = 0x14, Kind = InstructionKind.Misc, Cycles = 4, Mnemonic = "INC D", Mode = AddressingMode.None });
            SetTable(0x16, new Instruction { Opcode = 0x16, Kind = InstructionKind.Ld, Cycles = 8, Mnemonic = "LD D,d8", Mode = AddressingMode.Imm8 });
            SetTable(0x15, new Instruction { Opcode = 0x15, Kind = InstructionKind.Misc, Cycles = 4, Mnemonic = "DEC D", Mode = AddressingMode.None });
            SetTable(0x1B, new Instruction { Opcode = 0x1B, Kind = InstructionKind.Misc, Cycles = 8, Mnemonic = "DEC DE", Mode = AddressingMode.None });
            SetTable(0x1C, new Instruction { Opcode = 0x1C, Kind = InstructionKind.Misc, Cycles = 4, Mnemonic = "INC E", Mode = AddressingMode.None });
            SetTable(0x1E, new Instruction { Opcode = 0x1E, Kind = InstructionKind.Ld, Cycles = 8, Mnemonic = "LD E,d8", Mode = AddressingMode.Imm8 });
            SetTable(0x1D, new Instruction { Opcode = 0x1D, Kind = InstructionKind.Misc, Cycles = 4, Mnemonic = "DEC E", Mode = AddressingMode.None });
            SetTable(0x21, new Instruction { Opcode = 0x21, Kind = InstructionKind.Ld, Cycles = 12, Mnemonic = "LD HL,d16", Mode = AddressingMode.Imm16 });
            SetTable(0x23, new Instruction { Opcode = 0x23, Kind = InstructionKind.Misc, Cycles = 8, Mnemonic = "INC HL", Mode = AddressingMode.None });
            SetTable(0x24, new Instruction { Opcode = 0x24, Kind = InstructionKind.Misc, Cycles = 4, Mnemonic = "INC H", Mode = AddressingMode.None });
            SetTable(0x26, new Instruction { Opcode = 0x26, Kind = InstructionKind.Ld, Cycles = 8, Mnemonic = "LD H,d8", Mode = AddressingMode.Imm8 });
            SetTable(0x25, new Instruction { Opcode = 0x25, Kind = InstructionKind.Misc, Cycles = 4, Mnemonic = "DEC H", Mode = AddressingMode.None });
            SetTable(0x2B, new Instruction { Opcode = 0x2B, Kind = InstructionKind.Misc, Cycles = 8, Mnemonic = "DEC HL", Mode = AddressingMode.None });
            SetTable(0x2C, new Instruction { Opcode = 0x2C, Kind = InstructionKind.Misc, Cycles = 4, Mnemonic = "INC L", Mode = AddressingMode.None });
            SetTable(0x2E, new Instruction { Opcode = 0x2E, Kind = InstructionKind.Ld, Cycles = 8, Mnemonic = "LD L,d8", Mode = AddressingMode.Imm8 });
            SetTable(0x2D, new Instruction { Opcode = 0x2D, Kind = InstructionKind.Misc, Cycles = 4, Mnemonic = "DEC L", Mode = AddressingMode.None });
            SetTable(0x31, new Instruction { Opcode = 0x31, Kind = InstructionKind.Ld, Cycles = 12, Mnemonic = "LD SP,d16", Mode = AddressingMode.Imm16 });
            SetTable(0x33, new Instruction { Opcode = 0x33, Kind = InstructionKind.Misc, Cycles = 8, Mnemonic = "INC SP", Mode = AddressingMode.None });
            SetTable(0x3C, new Instruction { Opcode = 0x3C, Kind = InstructionKind.Misc, Cycles = 4, Mnemonic = "INC A", Mode = AddressingMode.None });
            SetTable(0x3E, new Instruction { Opcode = 0x3E, Kind = InstructionKind.Ld, Cycles = 8, Mnemonic = "LD A,d8", Mode = AddressingMode.Imm8 });
            SetTable(0x3D, new Instruction { Opcode = 0x3D, Kind = InstructionKind.Misc, Cycles = 4, Mnemonic = "DEC A", Mode = AddressingMode.None });
            SetTable(0x3B, new Instruction { Opcode = 0x3B, Kind = InstructionKind.Misc, Cycles = 8, Mnemonic = "DEC SP", Mode = AddressingMode.None });
            SetTable(0xC3, new Instruction { Opcode = 0xC3, Kind = InstructionKind.Jp, Cycles = 16, Mnemonic = "JP d16", Mode = AddressingMode.Imm16 });
        }

        private void SetTable(byte opcode, Instruction instr)
        {
            table[opcode] = instr;
            tableSet[opcode] = true;
        }
    }

    public struct DecodedOperands
    {
        public Operand Src;
        public Operand Dst;
    }

    public struct Operand
    {
        public OperandKind Kind;
        public Reg8 Reg8;
        public Reg16 Reg16;
        public ushort Address;
        public byte Imm8;
        public ushort Imm16;
    }

    public enum OperandKind
    {
        None,
        Register8,
        Register16,
        Immediate8,
        Immediate16,
        Memory
    }

    public enum Reg8 { A, B, C, D, E, H, L, F }
    public enum Reg16 { AF, BC, DE, HL, SP, PC }

    public sealed class OperandFetcher
    {
        // Implementation guide:
        // - Map AddressingMode to actual operand types and memory addresses.
        // - Use this to centralize how operands are read so execution code stays small.
        // - This is also where you add "HL+" and "HL-" post-increment/decrement behavior.
        public DecodedOperands Fetch(Instruction instr, ICpuBus bus, ref Registers regs)
        {
            var ops = new DecodedOperands();
            switch (instr.Mode)
            {
                case AddressingMode.Imm8:
                    ops.Src = new Operand { Kind = OperandKind.Immediate8, Imm8 = instr.Imm8 };
                    break;
                case AddressingMode.Imm16:
                    ops.Src = new Operand { Kind = OperandKind.Immediate16, Imm16 = instr.Imm16 };
                    break;
                case AddressingMode.HighRamImm8:
                    ops.Dst = new Operand { Kind = OperandKind.Memory, Address = (ushort)(0xFF00 + instr.Imm8) };
                    break;
                case AddressingMode.HighRamRegC:
                    ops.Dst = new Operand { Kind = OperandKind.Memory, Address = (ushort)(0xFF00 + regs.C) };
                    break;
                default:
                    break;
            }
            return ops;
        }
    }

    public sealed class InstructionExecutor
    {
        private static byte ReadRegOrHl(int idx, ICpuBus bus, ref Registers regs)
        {
            switch (idx)
            {
                case 0: return regs.B;
                case 1: return regs.C;
                case 2: return regs.D;
                case 3: return regs.E;
                case 4: return regs.H;
                case 5: return regs.L;
                case 6: return bus.Read(regs.HL);
                case 7: return regs.A;
                default: return 0;
            }
        }

        private static void WriteRegOrHl(int idx, byte value, ICpuBus bus, ref Registers regs)
        {
            switch (idx)
            {
                case 0: regs.B = value; break;
                case 1: regs.C = value; break;
                case 2: regs.D = value; break;
                case 3: regs.E = value; break;
                case 4: regs.H = value; break;
                case 5: regs.L = value; break;
                case 6: bus.Write(regs.HL, value); break;
                case 7: regs.A = value; break;
            }
        }

        private static byte Inc8(byte value, ref Registers regs)
        {
            byte result = (byte)(value + 1);
            byte f = (byte)(regs.F & 0x10); // preserve carry
            if (result == 0) f |= 0x80;
            if (((value & 0x0F) + 1) > 0x0F) f |= 0x20;
            regs.F = (byte)(f & 0xF0);
            return result;
        }

        private static byte Dec8(byte value, ref Registers regs)
        {
            byte result = (byte)(value - 1);
            byte f = (byte)((regs.F & 0x10) | 0x40); // preserve carry, set N
            if (result == 0) f |= 0x80;
            if ((value & 0x0F) == 0) f |= 0x20;
            regs.F = (byte)(f & 0xF0);
            return result;
        }

        private static bool CarryFlag(Registers regs) => (regs.F & 0x10) != 0;

        private static void SetFlagsZnHc(ref Registers regs, bool z, bool n, bool h, bool c)
        {
            byte f = 0;
            if (z) f |= 0x80;
            if (n) f |= 0x40;
            if (h) f |= 0x20;
            if (c) f |= 0x10;
            regs.F = (byte)(f & 0xF0);
        }

        private static void ExecAluA(byte opcode, byte value, ref Registers regs)
        {
            byte a = regs.A;
            int group = (opcode >> 3) & 0x07;

            switch (group)
            {
                case 0: // ADD A,r
                {
                    int r = a + value;
                    regs.A = (byte)r;
                    SetFlagsZnHc(ref regs, regs.A == 0, false, ((a & 0x0F) + (value & 0x0F)) > 0x0F, r > 0xFF);
                    return;
                }
                case 1: // ADC A,r
                {
                    int c = CarryFlag(regs) ? 1 : 0;
                    int r = a + value + c;
                    regs.A = (byte)r;
                    SetFlagsZnHc(ref regs, regs.A == 0, false, ((a & 0x0F) + (value & 0x0F) + c) > 0x0F, r > 0xFF);
                    return;
                }
                case 2: // SUB A,r
                {
                    int r = a - value;
                    regs.A = (byte)r;
                    SetFlagsZnHc(ref regs, regs.A == 0, true, (a & 0x0F) < (value & 0x0F), r < 0);
                    return;
                }
                case 3: // SBC A,r
                {
                    int c = CarryFlag(regs) ? 1 : 0;
                    int r = a - value - c;
                    regs.A = (byte)r;
                    SetFlagsZnHc(ref regs, regs.A == 0, true, (a & 0x0F) < ((value & 0x0F) + c), a < (value + c));
                    return;
                }
                case 4: // AND A,r
                    regs.A = (byte)(a & value);
                    SetFlagsZnHc(ref regs, regs.A == 0, false, true, false);
                    return;
                case 5: // XOR A,r
                    regs.A = (byte)(a ^ value);
                    SetFlagsZnHc(ref regs, regs.A == 0, false, false, false);
                    return;
                case 6: // OR A,r
                    regs.A = (byte)(a | value);
                    SetFlagsZnHc(ref regs, regs.A == 0, false, false, false);
                    return;
                case 7: // CP A,r
                {
                    int r = a - value;
                    SetFlagsZnHc(ref regs, ((byte)r) == 0, true, (a & 0x0F) < (value & 0x0F), r < 0);
                    return;
                }
            }
        }

        private static void ExecDaa(ref Registers regs)
        {
            byte a = regs.A;
            bool n = (regs.F & 0x40) != 0;
            bool h = (regs.F & 0x20) != 0;
            bool c = (regs.F & 0x10) != 0;

            int correction = 0;
            if (!n)
            {
                if (h || (a & 0x0F) > 9) correction |= 0x06;
                if (c || a > 0x99) { correction |= 0x60; c = true; }
                a = (byte)(a + correction);
            }
            else
            {
                if (h) correction |= 0x06;
                if (c) correction |= 0x60;
                a = (byte)(a - correction);
            }

            regs.A = a;
            byte f = 0;
            if (regs.A == 0) f |= 0x80;
            if (n) f |= 0x40;
            if (c) f |= 0x10;
            regs.F = (byte)(f & 0xF0);
        }

        private static ushort RotLCarry(byte value, out bool carry)
        {
            carry = (value & 0x80) != 0;
            return (ushort)(((value << 1) & 0xFF) | (carry ? 1 : 0));
        }

        private static ushort RotRCarry(byte value, out bool carry)
        {
            carry = (value & 0x01) != 0;
            return (ushort)((value >> 1) | (carry ? 0x80 : 0));
        }

        private static ushort RotLThroughCarry(byte value, bool oldCarry, out bool carry)
        {
            carry = (value & 0x80) != 0;
            return (ushort)(((value << 1) & 0xFF) | (oldCarry ? 1 : 0));
        }

        private static ushort RotRThroughCarry(byte value, bool oldCarry, out bool carry)
        {
            carry = (value & 0x01) != 0;
            return (ushort)((value >> 1) | (oldCarry ? 0x80 : 0));
        }

        private static void AddHl16(ref Registers regs, ushort value)
        {
            int hl = regs.HL;
            int r = hl + value;
            bool h = (((hl & 0x0FFF) + (value & 0x0FFF)) & 0x1000) != 0;
            bool c = r > 0xFFFF;

            regs.HL = (ushort)r;
            byte f = (byte)(regs.F & 0x80); // preserve Z
            if (h) f |= 0x20;
            if (c) f |= 0x10;
            regs.F = (byte)(f & 0xF0); // N=0
        }

        private static byte Rlc(byte v, ref Registers regs)
        {
            bool c = (v & 0x80) != 0;
            v = (byte)((v << 1) | (c ? 1 : 0));
            SetFlagsZnHc(ref regs, v == 0, false, false, c);
            return v;
        }

        private static byte Rrc(byte v, ref Registers regs)
        {
            bool c = (v & 0x01) != 0;
            v = (byte)((v >> 1) | (c ? 0x80 : 0));
            SetFlagsZnHc(ref regs, v == 0, false, false, c);
            return v;
        }

        private static byte Rl(byte v, ref Registers regs)
        {
            bool oldC = CarryFlag(regs);
            bool c = (v & 0x80) != 0;
            v = (byte)((v << 1) | (oldC ? 1 : 0));
            SetFlagsZnHc(ref regs, v == 0, false, false, c);
            return v;
        }

        private static byte Rr(byte v, ref Registers regs)
        {
            bool oldC = CarryFlag(regs);
            bool c = (v & 0x01) != 0;
            v = (byte)((v >> 1) | (oldC ? 0x80 : 0));
            SetFlagsZnHc(ref regs, v == 0, false, false, c);
            return v;
        }

        private static byte Sla(byte v, ref Registers regs)
        {
            bool c = (v & 0x80) != 0;
            v = (byte)(v << 1);
            SetFlagsZnHc(ref regs, v == 0, false, false, c);
            return v;
        }

        private static byte Sra(byte v, ref Registers regs)
        {
            bool c = (v & 0x01) != 0;
            byte msb = (byte)(v & 0x80);
            v = (byte)((v >> 1) | msb);
            SetFlagsZnHc(ref regs, v == 0, false, false, c);
            return v;
        }

        private static byte Swap(byte v, ref Registers regs)
        {
            v = (byte)(((v & 0x0F) << 4) | ((v & 0xF0) >> 4));
            SetFlagsZnHc(ref regs, v == 0, false, false, false);
            return v;
        }

        private static byte Srl(byte v, ref Registers regs)
        {
            bool c = (v & 0x01) != 0;
            v = (byte)(v >> 1);
            SetFlagsZnHc(ref regs, v == 0, false, false, c);
            return v;
        }

        private static void Bit(int bit, byte v, ref Registers regs)
        {
            bool z = (v & (1 << bit)) == 0;
            byte f = (byte)(regs.F & 0x10); // preserve C
            if (z) f |= 0x80;
            f |= 0x20; // H=1
            regs.F = (byte)(f & 0xF0); // N=0
        }

        private static byte Res(int bit, byte v) => (byte)(v & ~(1 << bit));
        private static byte Set(int bit, byte v) => (byte)(v | (1 << bit));

        private static void ExecCb(byte cb, ICpuBus bus, ref Registers regs)
        {
            int reg = cb & 0x07;
            byte v = ReadRegOrHl(reg, bus, ref regs);

            if (cb <= 0x07) v = Rlc(v, ref regs);
            else if (cb <= 0x0F) v = Rrc(v, ref regs);
            else if (cb <= 0x17) v = Rl(v, ref regs);
            else if (cb <= 0x1F) v = Rr(v, ref regs);
            else if (cb <= 0x27) v = Sla(v, ref regs);
            else if (cb <= 0x2F) v = Sra(v, ref regs);
            else if (cb <= 0x37) v = Swap(v, ref regs);
            else if (cb <= 0x3F) v = Srl(v, ref regs);
            else if (cb <= 0x7F)
            {
                int bit = (cb - 0x40) / 8;
                Bit(bit, v, ref regs);
                return;
            }
            else if (cb <= 0xBF)
            {
                int bit = (cb - 0x80) / 8;
                v = Res(bit, v);
            }
            else
            {
                int bit = (cb - 0xC0) / 8;
                v = Set(bit, v);
            }

            WriteRegOrHl(reg, v, bus, ref regs);
        }

        private static void Push16(ushort value, ICpuBus bus, ref Registers regs)
        {
            regs.SP--;
            bus.Write(regs.SP, (byte)(value >> 8));
            regs.SP--;
            bus.Write(regs.SP, (byte)(value & 0xFF));
        }

        private static ushort Pop16(ICpuBus bus, ref Registers regs)
        {
            byte lo = bus.Read(regs.SP++);
            byte hi = bus.Read(regs.SP++);
            return (ushort)(lo | (hi << 8));
        }

        private static bool FlagZ(Registers regs) => (regs.F & 0x80) != 0;

        private static bool RetCond(byte opcode, Registers regs)
        {
            switch (opcode)
            {
                case 0xC0: return !FlagZ(regs); // RET NZ
                case 0xC8: return FlagZ(regs);  // RET Z
                case 0xD0: return !CarryFlag(regs); // RET NC
                case 0xD8: return CarryFlag(regs);  // RET C
                default: return true;
            }
        }

        private static bool CallCond(byte opcode, Registers regs)
        {
            switch (opcode)
            {
                case 0xC4: return !FlagZ(regs); // CALL NZ
                case 0xCC: return FlagZ(regs);  // CALL Z
                case 0xD4: return !CarryFlag(regs); // CALL NC
                case 0xDC: return CarryFlag(regs);  // CALL C
                default: return true;
            }
        }

        private static ushort RstVector(byte opcode)
        {
            switch (opcode)
            {
                case 0xC7: return 0x0000;
                case 0xCF: return 0x0008;
                case 0xD7: return 0x0010;
                case 0xDF: return 0x0018;
                case 0xE7: return 0x0020;
                case 0xEF: return 0x0028;
                case 0xF7: return 0x0030;
                case 0xFF: return 0x0038;
                default: return 0x0000;
            }
        }

        public int Execute(Instruction instr, DecodedOperands ops, ICpuBus bus, ref Registers regs, ref CpuState state, IInterruptController interrupts)
        {
            // Implementation guide:
            // - Read operands using ops or helper methods here.
            // - Write results to registers/memory in one place.
            // - Keep flag updates in small helper methods.
            if (instr.Opcode >= 0x40 && instr.Opcode <= 0x7F && instr.Opcode != 0x76)
            {
                byte value = ReadRegOrHl(instr.SrcRegIndex, bus, ref regs);
                WriteRegOrHl(instr.DstRegIndex, value, bus, ref regs);
                return instr.Cycles;
            }
            if (instr.Opcode >= 0x80 && instr.Opcode <= 0xBF)
            {
                byte value = ReadRegOrHl(instr.SrcRegIndex, bus, ref regs);
                ExecAluA(instr.Opcode, value, ref regs);
                return instr.Cycles;
            }
            if (instr.Opcode == 0xCB)
            {
                ExecCb(instr.Imm8, bus, ref regs);
                return instr.Cycles;
            }
            if (instr.Opcode == 0xC6 || instr.Opcode == 0xCE || instr.Opcode == 0xD6 || instr.Opcode == 0xDE ||
                instr.Opcode == 0xE6 || instr.Opcode == 0xEE || instr.Opcode == 0xF6 || instr.Opcode == 0xFE)
            {
                ExecAluA(instr.Opcode, ops.Src.Imm8, ref regs);
                return instr.Cycles;
            }
            if (instr.Opcode == 0x18 || instr.Opcode == 0x20 || instr.Opcode == 0x28 || instr.Opcode == 0x30 || instr.Opcode == 0x38)
            {
                bool take = false;
                switch (instr.Opcode)
                {
                    case 0x18: take = true; break;
                    case 0x20: take = (regs.F & 0x80) == 0; break; // NZ
                    case 0x28: take = (regs.F & 0x80) != 0; break; // Z
                    case 0x30: take = (regs.F & 0x10) == 0; break; // NC
                    case 0x38: take = (regs.F & 0x10) != 0; break; // C
                }

                if (take)
                {
                    regs.PC = (ushort)(regs.PC + (sbyte)ops.Src.Imm8);
                    return 12;
                }
                return 8;
            }
            if (instr.Opcode == 0xC3)
            {
                regs.PC = instr.Imm16;
                return 16;
            }
            if (instr.Opcode == 0xC2 || instr.Opcode == 0xCA || instr.Opcode == 0xD2 || instr.Opcode == 0xDA)
            {
                bool take = false;
                switch (instr.Opcode)
                {
                    case 0xC2: take = (regs.F & 0x80) == 0; break; // NZ
                    case 0xCA: take = (regs.F & 0x80) != 0; break; // Z
                    case 0xD2: take = (regs.F & 0x10) == 0; break; // NC
                    case 0xDA: take = (regs.F & 0x10) != 0; break; // C
                }
                if (take)
                {
                    regs.PC = instr.Imm16;
                    return 16;
                }
                return 12;
            }
            if (instr.Opcode == 0xE9)
            {
                regs.PC = regs.HL;
                return 4;
            }
            if (instr.Opcode == 0xC0 || instr.Opcode == 0xC8 || instr.Opcode == 0xD0 || instr.Opcode == 0xD8)
            {
                if (RetCond(instr.Opcode, regs))
                {
                    regs.PC = Pop16(bus, ref regs);
                    return 20;
                }
                return 8;
            }
            if (instr.Opcode == 0xC9)
            {
                regs.PC = Pop16(bus, ref regs);
                return 16;
            }
            if (instr.Opcode == 0xD9)
            {
                state.IME = true;
                regs.PC = Pop16(bus, ref regs);
                return 16;
            }
            if (instr.Opcode == 0xC4 || instr.Opcode == 0xCC || instr.Opcode == 0xD4 || instr.Opcode == 0xDC)
            {
                if (CallCond(instr.Opcode, regs))
                {
                    Push16(regs.PC, bus, ref regs);
                    regs.PC = instr.Imm16;
                    return 24;
                }
                return 12;
            }
            if (instr.Opcode == 0xCD)
            {
                Push16(regs.PC, bus, ref regs);
                regs.PC = instr.Imm16;
                return 24;
            }
            if (instr.Opcode == 0xC7 || instr.Opcode == 0xCF || instr.Opcode == 0xD7 || instr.Opcode == 0xDF ||
                instr.Opcode == 0xE7 || instr.Opcode == 0xEF || instr.Opcode == 0xF7 || instr.Opcode == 0xFF)
            {
                Push16(regs.PC, bus, ref regs);
                regs.PC = RstVector(instr.Opcode);
                return 16;
            }
            if (instr.Opcode == 0xC1) { regs.BC = Pop16(bus, ref regs); return 12; }
            if (instr.Opcode == 0xD1) { regs.DE = Pop16(bus, ref regs); return 12; }
            if (instr.Opcode == 0xE1) { regs.HL = Pop16(bus, ref regs); return 12; }
            if (instr.Opcode == 0xF1)
            {
                ushort af = Pop16(bus, ref regs);
                regs.A = (byte)(af >> 8);
                regs.F = (byte)(af & 0xF0);
                return 12;
            }
            if (instr.Opcode == 0xC5) { Push16(regs.BC, bus, ref regs); return 16; }
            if (instr.Opcode == 0xD5) { Push16(regs.DE, bus, ref regs); return 16; }
            if (instr.Opcode == 0xE5) { Push16(regs.HL, bus, ref regs); return 16; }
            if (instr.Opcode == 0xF5) { Push16((ushort)((regs.A << 8) | (regs.F & 0xF0)), bus, ref regs); return 16; }
            if (instr.Opcode == 0xE0) { bus.Write((ushort)(0xFF00 + instr.Imm8), regs.A); return 12; }
            if (instr.Opcode == 0xF0) { regs.A = bus.Read((ushort)(0xFF00 + instr.Imm8)); return 12; }
            if (instr.Opcode == 0xE2) { bus.Write((ushort)(0xFF00 + regs.C), regs.A); return 8; }
            if (instr.Opcode == 0xF2) { regs.A = bus.Read((ushort)(0xFF00 + regs.C)); return 8; }
            if (instr.Opcode == 0xEA) { bus.Write(instr.Imm16, regs.A); return 16; }
            if (instr.Opcode == 0xFA) { regs.A = bus.Read(instr.Imm16); return 16; }
            if (instr.Opcode == 0x08)
            {
                bus.Write(instr.Imm16, (byte)(regs.SP & 0xFF));
                bus.Write((ushort)(instr.Imm16 + 1), (byte)((regs.SP >> 8) & 0xFF));
                return 20;
            }
            if (instr.Opcode == 0x02) { bus.Write(regs.BC, regs.A); return 8; }
            if (instr.Opcode == 0x0A) { regs.A = bus.Read(regs.BC); return 8; }
            if (instr.Opcode == 0x12) { bus.Write(regs.DE, regs.A); return 8; }
            if (instr.Opcode == 0x1A) { regs.A = bus.Read(regs.DE); return 8; }
            if (instr.Opcode == 0x22) { bus.Write(regs.HL, regs.A); regs.HL++; return 8; }
            if (instr.Opcode == 0x2A) { regs.A = bus.Read(regs.HL); regs.HL++; return 8; }
            if (instr.Opcode == 0x32) { bus.Write(regs.HL, regs.A); regs.HL--; return 8; }
            if (instr.Opcode == 0x3A) { regs.A = bus.Read(regs.HL); regs.HL--; return 8; }
            if (instr.Opcode == 0xE8)
            {
                sbyte e = (sbyte)instr.Imm8;
                byte low = (byte)(regs.SP & 0xFF);
                int result = low + (byte)e;
                bool halfCarry = ((low & 0xF) + ((byte)e & 0xF)) > 0xF;
                bool carry = result > 0xFF;

                regs.SP = (ushort)(regs.SP + e);
                SetFlagsZnHc(ref regs, false, false, halfCarry, carry);
                return 16;
            }
            if (instr.Opcode == 0xF8)
            {
                sbyte e = (sbyte)instr.Imm8;
                byte low = (byte)(regs.SP & 0xFF);
                int result = low + (byte)e;
                bool halfCarry = ((low & 0xF) + ((byte)e & 0xF)) > 0xF;
                bool carry = result > 0xFF;

                ushort r = (ushort)(regs.SP + e);
                regs.H = (byte)(r >> 8);
                regs.L = (byte)(r & 0xFF);
                SetFlagsZnHc(ref regs, false, false, halfCarry, carry);
                return 12;
            }
            if (instr.Opcode == 0xF9) { regs.SP = regs.HL; return 8; }
            if (instr.Opcode == 0x07) // RLCA
            {
                bool c;
                regs.A = (byte)RotLCarry(regs.A, out c);
                regs.F = (byte)(c ? 0x10 : 0x00);
                return 4;
            }
            if (instr.Opcode == 0x0F) // RRCA
            {
                bool c;
                regs.A = (byte)RotRCarry(regs.A, out c);
                regs.F = (byte)(c ? 0x10 : 0x00);
                return 4;
            }
            if (instr.Opcode == 0x17) // RLA
            {
                bool c;
                regs.A = (byte)RotLThroughCarry(regs.A, CarryFlag(regs), out c);
                regs.F = (byte)(c ? 0x10 : 0x00);
                return 4;
            }
            if (instr.Opcode == 0x1F) // RRA
            {
                bool c;
                regs.A = (byte)RotRThroughCarry(regs.A, CarryFlag(regs), out c);
                regs.F = (byte)(c ? 0x10 : 0x00);
                return 4;
            }
            if (instr.Opcode == 0x09) { AddHl16(ref regs, regs.BC); return 8; }
            if (instr.Opcode == 0x19) { AddHl16(ref regs, regs.DE); return 8; }
            if (instr.Opcode == 0x29) { AddHl16(ref regs, regs.HL); return 8; }
            if (instr.Opcode == 0x39) { AddHl16(ref regs, regs.SP); return 8; }
            if (instr.Opcode == 0x10) { regs.PC++; state.Stopped = true; return 4; } // STOP
            if (instr.Opcode == 0x34)
            {
                byte v = bus.Read(regs.HL);
                v = Inc8(v, ref regs);
                bus.Write(regs.HL, v);
                return 12;
            }
            if (instr.Opcode == 0x35)
            {
                byte v = bus.Read(regs.HL);
                v = Dec8(v, ref regs);
                bus.Write(regs.HL, v);
                return 12;
            }
            if (instr.Opcode == 0x36) { bus.Write(regs.HL, instr.Imm8); return 12; }
            if (instr.Opcode == 0x27) { ExecDaa(ref regs); return 4; } // DAA
            if (instr.Opcode == 0x2F) { regs.A = (byte)~regs.A; regs.F = (byte)((regs.F | 0x60) & 0xF0); return 4; } // CPL
            if (instr.Opcode == 0x37) { regs.F = (byte)(((regs.F & 0x80) | 0x10) & 0xF0); return 4; } // SCF
            if (instr.Opcode == 0x3F) // CCF
            {
                bool c = (regs.F & 0x10) != 0;
                regs.F = (byte)(((regs.F & 0x80) | (c ? 0x00 : 0x10)) & 0xF0);
                return 4;
            }
            if (instr.Opcode == 0x76) { state.Halted = true; return 4; } // HALT
            if (instr.Opcode == 0xF3) { state.IME = false; return 4; } // DI
            if (instr.Opcode == 0xFB) { state.EiPending = true; return 4; } // EI

            switch (instr.Opcode)
            {
                case 0x00: // NOP
                    return instr.Cycles;
                case 0x01: // LD BC,d16
                    regs.BC = ops.Src.Imm16;
                    return instr.Cycles;
                case 0x03: // INC BC
                    regs.BC++;
                    return instr.Cycles;
                case 0x04: // INC B
                    regs.B = Inc8(regs.B, ref regs);
                    return instr.Cycles;
                case 0x06: // LD B,d8
                    regs.B = ops.Src.Imm8;
                    return instr.Cycles;
                case 0x05: // DEC B
                    regs.B = Dec8(regs.B, ref regs);
                    return instr.Cycles;
                case 0x0B: // DEC BC
                    regs.BC--;
                    return instr.Cycles;
                case 0x0C: // INC C
                    regs.C = Inc8(regs.C, ref regs);
                    return instr.Cycles;
                case 0x0E: // LD C,d8
                    regs.C = ops.Src.Imm8;
                    return instr.Cycles;
                case 0x0D: // DEC C
                    regs.C = Dec8(regs.C, ref regs);
                    return instr.Cycles;
                case 0x11: // LD DE,d16
                    regs.DE = ops.Src.Imm16;
                    return instr.Cycles;
                case 0x13: // INC DE
                    regs.DE++;
                    return instr.Cycles;
                case 0x14: // INC D
                    regs.D = Inc8(regs.D, ref regs);
                    return instr.Cycles;
                case 0x16: // LD D,d8
                    regs.D = ops.Src.Imm8;
                    return instr.Cycles;
                case 0x15: // DEC D
                    regs.D = Dec8(regs.D, ref regs);
                    return instr.Cycles;
                case 0x1B: // DEC DE
                    regs.DE--;
                    return instr.Cycles;
                case 0x1C: // INC E
                    regs.E = Inc8(regs.E, ref regs);
                    return instr.Cycles;
                case 0x1E: // LD E,d8
                    regs.E = ops.Src.Imm8;
                    return instr.Cycles;
                case 0x1D: // DEC E
                    regs.E = Dec8(regs.E, ref regs);
                    return instr.Cycles;
                case 0x21: // LD HL,d16
                    regs.HL = ops.Src.Imm16;
                    return instr.Cycles;
                case 0x23: // INC HL
                    regs.HL++;
                    return instr.Cycles;
                case 0x24: // INC H
                    regs.H = Inc8(regs.H, ref regs);
                    return instr.Cycles;
                case 0x26: // LD H,d8
                    regs.H = ops.Src.Imm8;
                    return instr.Cycles;
                case 0x25: // DEC H
                    regs.H = Dec8(regs.H, ref regs);
                    return instr.Cycles;
                case 0x2B: // DEC HL
                    regs.HL--;
                    return instr.Cycles;
                case 0x2C: // INC L
                    regs.L = Inc8(regs.L, ref regs);
                    return instr.Cycles;
                case 0x2E: // LD L,d8
                    regs.L = ops.Src.Imm8;
                    return instr.Cycles;
                case 0x2D: // DEC L
                    regs.L = Dec8(regs.L, ref regs);
                    return instr.Cycles;
                case 0x31: // LD SP,d16
                    regs.SP = ops.Src.Imm16;
                    return instr.Cycles;
                case 0x33: // INC SP
                    regs.SP++;
                    return instr.Cycles;
                case 0x3C: // INC A
                    regs.A = Inc8(regs.A, ref regs);
                    return instr.Cycles;
                case 0x3D: // DEC A
                    regs.A = Dec8(regs.A, ref regs);
                    return instr.Cycles;
                case 0x3B: // DEC SP
                    regs.SP--;
                    return instr.Cycles;
                case 0x3E: // LD A,d8
                    regs.A = ops.Src.Imm8;
                    return instr.Cycles;
                case 0xC3: // JP d16
                    regs.PC = ops.Src.Imm16;
                    return instr.Cycles;
                default:
                    return instr.Cycles;
            }
        }
    }

    public struct Cpu2StructuredSnapshot
    {
        public byte A;
        public byte F;
        public byte B;
        public byte C;
        public byte D;
        public byte E;
        public byte H;
        public byte L;
        public ushort SP;
        public ushort PC;
        public bool IME;
        public bool EiPending;
        public bool IsHalted;
        public bool HaltBug;
        public bool IsStopped;
    }
}
