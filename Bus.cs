using System;
using System.IO;

namespace GB
{
    public class Bus
    {
        public Cartridge cartridge;
        private byte[] memory;    // C000–FFFF general purpose
        private byte[] vram;      // 8000–9FFF VRAM
        public byte[] OAMRam;     // FE00–FE9F
        public Timer timer;
        public Joypad Joypad { get; }

        public int PpuMode;       // Current PPU mode (0–3)
        private int dmaCyclesRemaining = 0;

        // PPU-side raw access (bypasses CPU access restrictions)
        public byte ReadVramRaw(ushort addr) => vram[addr & 0x1FFF];
        public byte ReadOamRaw(ushort addr) => OAMRam[addr & 0xFF];
        public void WriteVramRaw(ushort addr, byte value) => vram[addr & 0x1FFF] = value;

        // LCD registers
        private byte scx = 0;
        private byte scy = 0;
        private byte lyc = 0;
        private byte wx = 7;
        private byte wy = 0;

//        public byte SCX { get => scx; set => scx = value; }
//        public byte SCY { get => scy; set => scy = value; }
//        public byte LYC { get => lyc; set => lyc = value; }
//        public byte WX  { get => wx; set => wx = value; }
//        public byte WY  { get => wy; set => wy = value; }

        // LY register
        private byte ly = 0;
//        public byte LY { get => ly; set => ly = value; }

// LCD registers
public byte LCDC
{
    get { return memory[0xFF40]; }
    set
    {
        bool wasEnabled = (memory[0xFF40] & 0x80) != 0;
        memory[0xFF40] = value;
        bool nowEnabled = (value & 0x80) != 0;
        if (wasEnabled && !nowEnabled)
            OnLcdDisabled();
    }
}

public byte STAT
{
    get
    {
        UpdateStatCoincidence();
        byte stat = memory[0xFF41];
        stat = (byte)((stat & 0xFC) | (PpuMode & 0x03)); // keep bit2, update mode bits
        return (byte)(stat | 0x80); // bit 7 always reads as 1
    }
    set { memory[0xFF41] = value; }
}

public byte SCY
{
    get { return memory[0xFF42]; }
    set { memory[0xFF42] = value; }
}

public byte SCX
{
    get { return memory[0xFF43]; }
    set { memory[0xFF43] = value; }
}

public byte LY
{
    get { return memory[0xFF44]; }
    set
    {
        memory[0xFF44] = value;
        UpdateStatCoincidence();
    }
}

public byte LYC
{
    get { return memory[0xFF45]; }
    set
    {
        memory[0xFF45] = value;
        UpdateStatCoincidence();
        if ((memory[0xFF41] & (1 << 6)) != 0 && (memory[0xFF41] & (1 << 2)) != 0)
            RequestInterrupt(1);
    }
}

public byte BGP
{
    get { return memory[0xFF47]; }
    set { memory[0xFF47] = value; }
}

public byte OBP0
{
    get { return memory[0xFF48]; }
    set { memory[0xFF48] = value; }
}

public byte OBP1
{
    get { return memory[0xFF49]; }
    set { memory[0xFF49] = value; }
}

public byte WY
{
    get { return memory[0xFF4A]; }
    set { memory[0xFF4A] = value; }
}

public byte WX
{
    get { return memory[0xFF4B]; }
    set { memory[0xFF4B] = value; }
}


        // Constructor
        public Bus(ref Cartridge cartridge)
        {
            memory = new byte[0x10000];   // Full 64KB addressable memory
            vram = new byte[0x2000];      // 8 KB VRAM
            OAMRam = new byte[160];       // 40 sprites × 4 bytes
            this.cartridge = cartridge;
            timer = new Timer(this);
            Joypad = new Joypad(() => RequestInterrupt(4));
            InitializeIo();
        }

        public void ResetLY() => LY = 0;
        public void TickLY() => LY++; // wrap handled in PPU logic

        // Request an interrupt (0–4)
        public void RequestInterrupt(int id)
        {
            if (id < 0 || id > 4)
                throw new ArgumentOutOfRangeException(nameof(id), "Interrupt ID must be 0–4");

            memory[0xFF0F] |= (byte)(1 << id); // IF register
        }

        // -------------------------
        // READ
        // -------------------------
        public byte Read(int addr)
        {
            addr &= 0xFFFF;

            if (addr < 0x8000) return cartridge.Read(addr); // ROM
            if (addr >= 0xA000 && addr <= 0xBFFF) return cartridge.ReadRam(addr); // External RAM
            if (addr >= 0x8000 && addr <= 0x9FFF) // VRAM
            {
                return vram[addr & 0x1FFF];
            }
            if (addr >= 0xE000 && addr <= 0xFDFF) return memory[addr - 0x2000]; // Echo RAM
            if (addr >= 0xFE00 && addr <= 0xFE9F) // OAM
            {
                if (dmaCyclesRemaining > 0) return 0xFF;
                if (PpuMode >= 2) return 0xFF; // inaccessible in mode 2+
                return OAMRam[addr - 0xFE00];
            }
            if (addr >= 0xFEA0 && addr <= 0xFEFF) return 0xFF; // unusable

            // I/O registers
            switch (addr)
            {
                case 0xFF00: return Joypad.Read();
                case 0xFF01:
                case 0xFF02: return memory[addr]; // serial
                case 0xFF04: return timer.ReadDIV();
                case 0xFF05: return timer.ReadTIMA();
                case 0xFF06: return timer.ReadTMA();
                case 0xFF07: return timer.ReadTAC();
                case 0xFF40: return LCDC;
                case 0xFF41: return STAT;
                case 0xFF42: return SCY;
                case 0xFF43: return SCX;
                case 0xFF44: return LY;
                case 0xFF45: return LYC;
                case 0xFF47: return BGP;
                case 0xFF48: return OBP0;
                case 0xFF49: return OBP1;
                case 0xFF4A: return WY;
                case 0xFF4B: return WX;
                case 0xFF0F: return (byte)(memory[0xFF0F] | 0xE0); // IF (upper bits read as 1)
                case 0xFFFF: return memory[0xFFFF]; // IE
            }

            // Everything else
            return memory[addr];
        }

        // -------------------------
        // WRITE
        // -------------------------
        public void Write(int addr, byte val)
        {
            addr &= 0xFFFF;

            if (addr < 0x8000) { cartridge.Write(addr, val); return; } // MBC control
            if (addr >= 0xA000 && addr <= 0xBFFF) { cartridge.WriteRam(addr, val); return; }
            if (addr >= 0x8000 && addr <= 0x9FFF) // VRAM
            {
                vram[addr & 0x1FFF] = val;
                return;
            }
            if (addr >= 0xE000 && addr <= 0xFDFF) // Echo RAM
            {
                memory[addr - 0x2000] = val;
                return;
            }
            if (addr >= 0xFE00 && addr <= 0xFE9F) // OAM
            {
                if (dmaCyclesRemaining > 0) return;
                if (PpuMode >= 2) return; // inaccessible
                OAMRam[addr - 0xFE00] = val;
                return;
            }
            if (addr >= 0xFEA0 && addr <= 0xFEFF) return; // unusable

            // I/O registers
            switch (addr)
            {
                case 0xFF00: Joypad.Write(val); return;
                case 0xFF01:
                case 0xFF02: memory[addr] = val; return;
                case 0xFF04: timer.WriteDIV(val); return;
                case 0xFF05: timer.WriteTIMA(val); return;
                case 0xFF06: timer.WriteTMA(val); return;
                case 0xFF07: timer.WriteTAC(val); return;
                case 0xFF46:
                    memory[addr] = val;
                    DoDmaTransfer(val);
                    dmaCyclesRemaining = 160;
                    return;
                case 0xFF40: LCDC = val; return;
                case 0xFF41:
                    memory[addr] = (byte)((memory[addr] & 0x07) | (val & 0xF8));
                    return;
                case 0xFF42: SCY = val; return;
                case 0xFF43: SCX = val; return;
                case 0xFF44: LY = 0; return; // writing resets LY
                case 0xFF45: LYC = val; return;
                case 0xFF47: BGP = val; return;
                case 0xFF48: OBP0 = val; return;
                case 0xFF49: OBP1 = val; return;
                case 0xFF4A: WY = val; return;
                case 0xFF4B: WX = val; return;
                case 0xFF0F: memory[0xFF0F] = (byte)(val & 0x1F); return; // IF (lower 5 bits)
                case 0xFFFF: memory[0xFFFF] = val; return; // IE
            }

            memory[addr] = val;
        }

        private void DoDmaTransfer(byte page)
        {
            ushort baseAddr = (ushort)(page << 8);
            for (int i = 0; i < 160; i++)
            {
                OAMRam[i] = ReadDmaSource((ushort)(baseAddr + i));
            }
        }

        public void TickDma(int cycles)
        {
            if (dmaCyclesRemaining <= 0) return;
            dmaCyclesRemaining -= cycles;
            if (dmaCyclesRemaining < 0) dmaCyclesRemaining = 0;
        }

        private byte ReadDmaSource(ushort addr)
        {
            if (addr < 0x8000) return cartridge.Read(addr);
            if (addr >= 0xA000 && addr <= 0xBFFF) return cartridge.ReadRam(addr);
            if (addr >= 0x8000 && addr <= 0x9FFF) return vram[addr & 0x1FFF];
            if (addr >= 0xE000 && addr <= 0xFDFF) return memory[addr - 0x2000];
            if (addr >= 0xFE00 && addr <= 0xFE9F) return OAMRam[addr - 0xFE00];
            return memory[addr];
        }

        private void OnLcdDisabled()
        {
            LY = 0;
            PpuMode = 0;
            memory[0xFF41] = (byte)((memory[0xFF41] & 0xF8) | 0); // mode = 0
            memory[0xFF41] = (byte)(memory[0xFF41] & 0xFB);       // clear coincidence
        }

        private void UpdateStatCoincidence()
        {
            if (memory[0xFF44] == memory[0xFF45]) memory[0xFF41] |= (1 << 2);
            else memory[0xFF41] = (byte)(memory[0xFF41] & 0xFB);
        }

        private void InitializeIo()
        {
            // Common DMG power-up register values (post-BIOS)
            memory[0xFF00] = 0xCF; // JOYP
            memory[0xFF05] = 0x00; // TIMA
            memory[0xFF06] = 0x00; // TMA
            memory[0xFF07] = 0x00; // TAC
            memory[0xFF0F] = 0xE1; // IF

            memory[0xFF10] = 0x80;
            memory[0xFF11] = 0xBF;
            memory[0xFF12] = 0xF3;
            memory[0xFF14] = 0xBF;
            memory[0xFF16] = 0x3F;
            memory[0xFF17] = 0x00;
            memory[0xFF19] = 0xBF;
            memory[0xFF1A] = 0x7F;
            memory[0xFF1B] = 0xFF;
            memory[0xFF1C] = 0x9F;
            memory[0xFF1E] = 0xBF;
            memory[0xFF20] = 0xFF;
            memory[0xFF21] = 0x00;
            memory[0xFF22] = 0x00;
            memory[0xFF23] = 0xBF;
            memory[0xFF24] = 0x77;
            memory[0xFF25] = 0xF3;
            memory[0xFF26] = 0xF1;

            memory[0xFF40] = 0x91; // LCDC
            memory[0xFF41] = 0x85; // STAT
            memory[0xFF42] = 0x00; // SCY
            memory[0xFF43] = 0x00; // SCX
            memory[0xFF44] = 0x00; // LY
            memory[0xFF45] = 0x00; // LYC
            memory[0xFF46] = 0xFF; // DMA
            memory[0xFF47] = 0xFC; // BGP
            memory[0xFF48] = 0xFF; // OBP0
            memory[0xFF49] = 0xFF; // OBP1
            memory[0xFF4A] = 0x00; // WY
            memory[0xFF4B] = 0x00; // WX

            memory[0xFFFF] = 0x00; // IE
        }

        // -------------------------
        // Debug / Dump
        // -------------------------

        public void DumpPpu(string file = "ppu_dump.bin")
        {
            using (FileStream fs = File.Create(file))
            using (BinaryWriter bw = new BinaryWriter(fs))
            {
                bw.Write(vram);
                bw.Write(OAMRam);
            }
        }

        public BusState GetState()
        {
            var memCopy = new byte[memory.Length];
            var vramCopy = new byte[vram.Length];
            var oamCopy = new byte[OAMRam.Length];
            Array.Copy(memory, memCopy, memory.Length);
            Array.Copy(vram, vramCopy, vram.Length);
            Array.Copy(OAMRam, oamCopy, OAMRam.Length);
            return new BusState
            {
                Memory = memCopy,
                Vram = vramCopy,
                Oam = oamCopy,
                PpuMode = PpuMode,
                DmaCyclesRemaining = dmaCyclesRemaining,
                Timer = timer.GetState(),
                Joypad = Joypad.GetState()
            };
        }

        public void SetState(BusState s)
        {
            if (s.Memory != null) Array.Copy(s.Memory, memory, Math.Min(memory.Length, s.Memory.Length));
            if (s.Vram != null) Array.Copy(s.Vram, vram, Math.Min(vram.Length, s.Vram.Length));
            if (s.Oam != null) Array.Copy(s.Oam, OAMRam, Math.Min(OAMRam.Length, s.Oam.Length));
            PpuMode = s.PpuMode;
            dmaCyclesRemaining = s.DmaCyclesRemaining;
            timer.SetState(s.Timer);
            Joypad.SetState(s.Joypad);
        }

    }

    public class BusState
    {
        public byte[] Memory;
        public byte[] Vram;
        public byte[] Oam;
        public int PpuMode;
        public int DmaCyclesRemaining;
        public TimerState Timer;
        public JoypadState Joypad;
    }
}
