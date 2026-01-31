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

        public int PpuMode;       // Current PPU mode (0–3)

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
    set { memory[0xFF40] = value; }
}

public byte STAT
{
    get { return memory[0xFF41]; }
    set { memory[0xFF41] = (byte)((memory[0xFF41] & 0x07) | (value & 0xF8)); }
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
    set { memory[0xFF44] = value; }
}

public byte LYC
{
    get { return memory[0xFF45]; }
    set { memory[0xFF45] = value; }
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
            if (addr >= 0xA000 && addr <= 0xBFFF) return 0xFF; // External RAM placeholder
            if (addr >= 0x8000 && addr <= 0x9FFF) // VRAM
            {
                if (PpuMode == 3) return 0xFF; // inaccessible in mode 3
                return vram[addr & 0x1FFF];
            }
            if (addr >= 0xE000 && addr <= 0xFDFF) return memory[addr - 0x2000]; // Echo RAM
            if (addr >= 0xFE00 && addr <= 0xFE9F) // OAM
            {
                if (PpuMode >= 2) return 0xFF; // inaccessible in mode 2+
                return OAMRam[addr - 0xFE00];
            }

            // I/O registers
            switch (addr)
            {
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
                case 0xFF0F: return memory[0xFF0F]; // IF
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

            if (addr < 0x8000) return; // ROM ignored
            if (addr >= 0x8000 && addr <= 0x9FFF) // VRAM
            {
                if (PpuMode == 3) return; // inaccessible
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
                if (PpuMode >= 2) return; // inaccessible
                OAMRam[addr - 0xFE00] = val;
                return;
            }

            // I/O registers
            switch (addr)
            {
                case 0xFF01:
                case 0xFF02: memory[addr] = val; return;
                case 0xFF04: timer.WriteDIV(val); return;
                case 0xFF05: timer.WriteTIMA(val); return;
                case 0xFF06: timer.WriteTMA(val); return;
                case 0xFF07: timer.WriteTAC(val); return;
                case 0xFF40: LCDC = val; return;
                case 0xFF41: STAT = val; return;
                case 0xFF42: SCY = val; return;
                case 0xFF43: SCX = val; return;
                case 0xFF44: LY = 0; return; // writing resets LY
                case 0xFF45: LYC = val; return;
                case 0xFF47: BGP = val; return;
                case 0xFF48: OBP0 = val; return;
                case 0xFF49: OBP1 = val; return;
                case 0xFF4A: WY = val; return;
                case 0xFF4B: WX = val; return;
                case 0xFF0F: memory[0xFF0F] = val; return; // IF
                case 0xFFFF: memory[0xFFFF] = val; return; // IE
            }

            memory[addr] = val;
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

    }
}

