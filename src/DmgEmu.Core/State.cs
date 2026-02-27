using System;
using System.IO;

namespace DmgEmu.Core
{
    public class EmulatorState
    {
        public int Version = 1;
        public CpuBackend CpuBackend;
        public CartridgeState Cartridge;
        public BusState Bus;
        public PpuSnapshot Ppu;
        public CpuSnapshot Cpu;
        public Cpu2Snapshot Cpu2;
        public Cpu2StructuredSnapshot Cpu2Structured;
    }

    // Kept for save-state wire-format compatibility after removing Cpu/Cpu2 runtime classes.
    public struct CpuSnapshot
    {
        public byte A, F, B, C, D, E, H, L;
        public ushort SP, PC;
        public bool IME, EiPending, IsHalted, HaltBug, IsStopped;
    }

    // Kept for save-state wire-format compatibility after removing Cpu/Cpu2 runtime classes.
    public struct Cpu2Snapshot
    {
        public byte A, F, B, C, D, E, H, L;
        public ushort SP, PC;
        public bool IME, EiPending, IsHalted, HaltBug, IsStopped;
    }

    public static class EmulatorStateFile
    {
        private const int CurrentVersion = 1;

        public static void Save(string path, EmulatorState s)
        {
            using (var fs = File.Create(path))
            using (var bw = new BinaryWriter(fs))
            {
                bw.Write(new byte[] { (byte)'D', (byte)'M', (byte)'G', (byte)'S' });
                bw.Write(CurrentVersion);
                bw.Write((int)s.CpuBackend);

                WriteCartridgeState(bw, s.Cartridge);
                WriteBusState(bw, s.Bus);
                WritePpuState(bw, s.Ppu);
                WriteCpuState(bw, s.Cpu);
                WriteCpu2State(bw, s.Cpu2);
                WriteCpu2StructuredState(bw, s.Cpu2Structured);
            }
        }

        public static EmulatorState Load(string path)
        {
            using (var fs = File.OpenRead(path))
            using (var br = new BinaryReader(fs))
            {
                var magic = br.ReadBytes(4);
                if (magic.Length != 4 || magic[0] != 'D' || magic[1] != 'M' || magic[2] != 'G' || magic[3] != 'S')
                    throw new InvalidDataException("Invalid save-state file");

                int version = br.ReadInt32();
                if (version != CurrentVersion)
                    throw new InvalidDataException("Unsupported save-state version: " + version);

                var s = new EmulatorState();
                s.Version = version;
                s.CpuBackend = (CpuBackend)br.ReadInt32();
                s.Cartridge = ReadCartridgeState(br);
                s.Bus = ReadBusState(br);
                s.Ppu = ReadPpuState(br);
                s.Cpu = ReadCpuState(br);
                s.Cpu2 = ReadCpu2State(br);
                s.Cpu2Structured = ReadCpu2StructuredState(br);
                return s;
            }
        }

        private static void WriteByteArray(BinaryWriter bw, byte[] arr)
        {
            if (arr == null)
            {
                bw.Write(-1);
                return;
            }
            bw.Write(arr.Length);
            bw.Write(arr);
        }

        private static byte[] ReadByteArray(BinaryReader br)
        {
            int n = br.ReadInt32();
            if (n < 0) return null;
            return br.ReadBytes(n);
        }

        private static void WriteBoolArray(BinaryWriter bw, bool[] arr)
        {
            if (arr == null)
            {
                bw.Write(-1);
                return;
            }
            bw.Write(arr.Length);
            for (int i = 0; i < arr.Length; i++) bw.Write(arr[i]);
        }

        private static bool[] ReadBoolArray(BinaryReader br)
        {
            int n = br.ReadInt32();
            if (n < 0) return null;
            var arr = new bool[n];
            for (int i = 0; i < n; i++) arr[i] = br.ReadBoolean();
            return arr;
        }

        private static void WriteMapperState(BinaryWriter bw, MapperState s)
        {
            bw.Write(s != null);
            if (s == null) return;

            bw.Write((byte)s.Kind);
            WriteByteArray(bw, s.Ram);
            bw.Write(s.RamEnabled);
            bw.Write(s.RomBankLow5);
            bw.Write(s.BankHigh2);
            bw.Write(s.Mode);
            bw.Write(s.RomBank);
            bw.Write(s.RamRtcSelect);
            bw.Write(s.RtcSec);
            bw.Write(s.RtcMin);
            bw.Write(s.RtcHour);
            bw.Write(s.RtcDay);
            bw.Write(s.RtcHalt);
            bw.Write(s.RtcCarry);
            bw.Write(s.LatchArmed);
            bw.Write(s.RtcLatched);
            bw.Write(s.LatSec);
            bw.Write(s.LatMin);
            bw.Write(s.LatHour);
            bw.Write(s.LatDay);
            bw.Write(s.LatCarry);
            bw.Write(s.LatHalt);
            bw.Write(s.LastUpdateTicks);
        }

        private static MapperState ReadMapperState(BinaryReader br)
        {
            if (!br.ReadBoolean()) return null;

            return new MapperState
            {
                Kind = (MapperKind)br.ReadByte(),
                Ram = ReadByteArray(br),
                RamEnabled = br.ReadBoolean(),
                RomBankLow5 = br.ReadInt32(),
                BankHigh2 = br.ReadInt32(),
                Mode = br.ReadInt32(),
                RomBank = br.ReadInt32(),
                RamRtcSelect = br.ReadInt32(),
                RtcSec = br.ReadInt32(),
                RtcMin = br.ReadInt32(),
                RtcHour = br.ReadInt32(),
                RtcDay = br.ReadInt32(),
                RtcHalt = br.ReadBoolean(),
                RtcCarry = br.ReadBoolean(),
                LatchArmed = br.ReadBoolean(),
                RtcLatched = br.ReadBoolean(),
                LatSec = br.ReadInt32(),
                LatMin = br.ReadInt32(),
                LatHour = br.ReadInt32(),
                LatDay = br.ReadInt32(),
                LatCarry = br.ReadBoolean(),
                LatHalt = br.ReadBoolean(),
                LastUpdateTicks = br.ReadInt64()
            };
        }

        private static void WriteCartridgeState(BinaryWriter bw, CartridgeState s)
        {
            bw.Write(s != null);
            if (s == null) return;
            bw.Write(s.TypeCode);
            WriteMapperState(bw, s.MapperState);
        }

        private static CartridgeState ReadCartridgeState(BinaryReader br)
        {
            if (!br.ReadBoolean()) return null;
            return new CartridgeState
            {
                TypeCode = br.ReadByte(),
                MapperState = ReadMapperState(br)
            };
        }

        private static void WriteTimerState(BinaryWriter bw, TimerState s)
        {
            bw.Write(s.SystemCounter);
            bw.Write(s.LastCounter);
            bw.Write(s.Tima);
            bw.Write(s.Tma);
            bw.Write(s.Tac);
            bw.Write(s.OverflowPending);
            bw.Write(s.OverflowDelay);
        }

        private static TimerState ReadTimerState(BinaryReader br)
        {
            return new TimerState
            {
                SystemCounter = br.ReadUInt32(),
                LastCounter = br.ReadUInt16(),
                Tima = br.ReadByte(),
                Tma = br.ReadByte(),
                Tac = br.ReadByte(),
                OverflowPending = br.ReadBoolean(),
                OverflowDelay = br.ReadInt32()
            };
        }

        private static void WriteJoypadState(BinaryWriter bw, JoypadState s)
        {
            bw.Write(s.SelectBits);
            WriteBoolArray(bw, s.Buttons);
        }

        private static JoypadState ReadJoypadState(BinaryReader br)
        {
            return new JoypadState
            {
                SelectBits = br.ReadByte(),
                Buttons = ReadBoolArray(br)
            };
        }

        private static void WriteBusState(BinaryWriter bw, BusState s)
        {
            bw.Write(s != null);
            if (s == null) return;
            WriteByteArray(bw, s.Memory);
            WriteByteArray(bw, s.Vram);
            WriteByteArray(bw, s.Oam);
            bw.Write(s.PpuMode);
            bw.Write(s.DmaCyclesRemaining);
            WriteTimerState(bw, s.Timer);
            WriteJoypadState(bw, s.Joypad);
        }

        private static BusState ReadBusState(BinaryReader br)
        {
            if (!br.ReadBoolean()) return null;
            return new BusState
            {
                Memory = ReadByteArray(br),
                Vram = ReadByteArray(br),
                Oam = ReadByteArray(br),
                PpuMode = br.ReadInt32(),
                DmaCyclesRemaining = br.ReadInt32(),
                Timer = ReadTimerState(br),
                Joypad = ReadJoypadState(br)
            };
        }

        private static void WritePpuState(BinaryWriter bw, PpuSnapshot s)
        {
            bw.Write(s != null);
            if (s == null) return;
            WriteByteArray(bw, s.Pixels);
            bw.Write(s.CyclesRemaining);
            bw.Write(s.LcdWasEnabled);
            bw.Write(s.WindowLine);
            bw.Write(s.LastLyRendered);
        }

        private static PpuSnapshot ReadPpuState(BinaryReader br)
        {
            if (!br.ReadBoolean()) return null;
            return new PpuSnapshot
            {
                Pixels = ReadByteArray(br),
                CyclesRemaining = br.ReadInt32(),
                LcdWasEnabled = br.ReadBoolean(),
                WindowLine = br.ReadInt32(),
                LastLyRendered = br.ReadInt32()
            };
        }

        private static void WriteCpuState(BinaryWriter bw, CpuSnapshot s)
        {
            bw.Write(s.A); bw.Write(s.F); bw.Write(s.B); bw.Write(s.C); bw.Write(s.D); bw.Write(s.E); bw.Write(s.H); bw.Write(s.L);
            bw.Write(s.SP); bw.Write(s.PC);
            bw.Write(s.IME); bw.Write(s.EiPending); bw.Write(s.IsHalted); bw.Write(s.HaltBug); bw.Write(s.IsStopped);
        }

        private static CpuSnapshot ReadCpuState(BinaryReader br)
        {
            return new CpuSnapshot
            {
                A = br.ReadByte(), F = br.ReadByte(), B = br.ReadByte(), C = br.ReadByte(), D = br.ReadByte(), E = br.ReadByte(), H = br.ReadByte(), L = br.ReadByte(),
                SP = br.ReadUInt16(), PC = br.ReadUInt16(),
                IME = br.ReadBoolean(), EiPending = br.ReadBoolean(), IsHalted = br.ReadBoolean(), HaltBug = br.ReadBoolean(), IsStopped = br.ReadBoolean()
            };
        }

        private static void WriteCpu2State(BinaryWriter bw, Cpu2Snapshot s)
        {
            bw.Write(s.A); bw.Write(s.F); bw.Write(s.B); bw.Write(s.C); bw.Write(s.D); bw.Write(s.E); bw.Write(s.H); bw.Write(s.L);
            bw.Write(s.SP); bw.Write(s.PC);
            bw.Write(s.IME); bw.Write(s.EiPending); bw.Write(s.IsHalted); bw.Write(s.HaltBug); bw.Write(s.IsStopped);
        }

        private static Cpu2Snapshot ReadCpu2State(BinaryReader br)
        {
            return new Cpu2Snapshot
            {
                A = br.ReadByte(), F = br.ReadByte(), B = br.ReadByte(), C = br.ReadByte(), D = br.ReadByte(), E = br.ReadByte(), H = br.ReadByte(), L = br.ReadByte(),
                SP = br.ReadUInt16(), PC = br.ReadUInt16(),
                IME = br.ReadBoolean(), EiPending = br.ReadBoolean(), IsHalted = br.ReadBoolean(), HaltBug = br.ReadBoolean(), IsStopped = br.ReadBoolean()
            };
        }

        private static void WriteCpu2StructuredState(BinaryWriter bw, Cpu2StructuredSnapshot s)
        {
            bw.Write(s.A); bw.Write(s.F); bw.Write(s.B); bw.Write(s.C); bw.Write(s.D); bw.Write(s.E); bw.Write(s.H); bw.Write(s.L);
            bw.Write(s.SP); bw.Write(s.PC);
            bw.Write(s.IME); bw.Write(s.EiPending); bw.Write(s.IsHalted); bw.Write(s.HaltBug); bw.Write(s.IsStopped);
        }

        private static Cpu2StructuredSnapshot ReadCpu2StructuredState(BinaryReader br)
        {
            return new Cpu2StructuredSnapshot
            {
                A = br.ReadByte(), F = br.ReadByte(), B = br.ReadByte(), C = br.ReadByte(), D = br.ReadByte(), E = br.ReadByte(), H = br.ReadByte(), L = br.ReadByte(),
                SP = br.ReadUInt16(), PC = br.ReadUInt16(),
                IME = br.ReadBoolean(), EiPending = br.ReadBoolean(), IsHalted = br.ReadBoolean(), HaltBug = br.ReadBoolean(), IsStopped = br.ReadBoolean()
            };
        }
    }
}
