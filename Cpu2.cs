using System;
using System.Collections.Generic;

namespace GB
{
    // Cpu2 is a structured sketch for a full CPU implementation.
    // It is intentionally incomplete and meant to guide a proper design.
    public sealed class Cpu2
    {
        private readonly ICpuBus bus;
        private readonly IClock clock;
        private readonly IInterruptController interrupts;
        private readonly ITraceSink trace;

        private Registers regs;
        private CpuState state;

        private readonly InstructionDecoder decoder;
        private readonly InstructionExecutor executor;
        private readonly OperandFetcher operandFetcher;

        public Cpu2(ICpuBus bus, IClock clock, IInterruptController interrupts, ITraceSink trace = null)
        {
            this.bus = bus;
            this.clock = clock;
            this.interrupts = interrupts;
            this.trace = trace ?? new NullTraceSink();

            regs = Registers.PowerOn();
            state = CpuState.PowerOn();
            decoder = new InstructionDecoder();
            executor = new InstructionExecutor();
            operandFetcher = new OperandFetcher();
        }

        // Step one instruction (or interrupt entry) and return cycles used.
        public int Step()
        {
            if (TryHandleInterrupt(out int intCycles))
            {
                clock.Advance(intCycles);
                return intCycles;
            }

            if (state.Halted)
            {
                clock.Advance(4);
                return 4;
            }

            ushort pc = regs.PC;
            byte opcode = bus.Read(pc);
            regs.PC++;

            Instruction instr = decoder.Decode(opcode, bus, regs);
            DecodedOperands operands = operandFetcher.Fetch(instr, bus, ref regs);
            trace.Trace(regs, instr);

            int cycles = executor.Execute(instr, operands, bus, ref regs, ref state, interrupts);
            clock.Advance(cycles);
            return cycles;
        }

        public Registers Snapshot() => regs;

        private bool TryHandleInterrupt(out int cycles)
        {
            cycles = 0;
            if (!state.IME && !state.HaltBug)
                return false;

            InterruptFlags pending = interrupts.Pending();
            if (pending == InterruptFlags.None)
                return false;

            if (!state.IME)
            {
                // Halt bug: resume without servicing interrupt
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
        private readonly Dictionary<byte, Instruction> table;
        private readonly Dictionary<byte, Instruction> cbTable;

        public InstructionDecoder()
        {
            table = new Dictionary<byte, Instruction>();
            cbTable = new Dictionary<byte, Instruction>();
            SeedMinimal();
        }

        public Instruction Decode(byte opcode, ICpuBus bus, Registers regs)
        {
            if (opcode == 0xCB)
            {
                byte cb = bus.Read(regs.PC);
                regs.PC++;
                return cbTable.TryGetValue(cb, out var cbInstr)
                    ? cbInstr
                    : new Instruction { Opcode = cb, Kind = InstructionKind.Misc, Cycles = 8, Mnemonic = "CB ??" };
            }

            if (!table.TryGetValue(opcode, out var instr))
                instr = new Instruction { Opcode = opcode, Kind = InstructionKind.Misc, Cycles = 4, Mnemonic = "??" };

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
            table[0x00] = new Instruction { Opcode = 0x00, Kind = InstructionKind.Nop, Cycles = 4, Mnemonic = "NOP", Mode = AddressingMode.None };
            table[0x3E] = new Instruction { Opcode = 0x3E, Kind = InstructionKind.Ld, Cycles = 8, Mnemonic = "LD A,d8", Mode = AddressingMode.Imm8 };
            table[0xC3] = new Instruction { Opcode = 0xC3, Kind = InstructionKind.Jp, Cycles = 16, Mnemonic = "JP d16", Mode = AddressingMode.Imm16 };
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
        public int Execute(Instruction instr, DecodedOperands ops, ICpuBus bus, ref Registers regs, ref CpuState state, IInterruptController interrupts)
        {
            // Implementation guide:
            // - Read operands using ops or helper methods here.
            // - Write results to registers/memory in one place.
            // - Keep flag updates in small helper methods.
            switch (instr.Opcode)
            {
                case 0x00: // NOP
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
}
