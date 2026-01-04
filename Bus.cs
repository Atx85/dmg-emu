using System;
/*
Start	End	Description	Notes
0000	3FFF	16 KiB ROM bank 00	From cartridge, usually a fixed bank
4000	7FFF	16 KiB ROM Bank 01–NN	From cartridge, switchable bank via mapper (if any)
8000	9FFF	8 KiB Video RAM (VRAM)	In CGB mode, switchable bank 0/1
A000	BFFF	8 KiB External RAM	From cartridge, switchable bank if any
C000	CFFF	4 KiB Work RAM (WRAM)	
D000	DFFF	4 KiB Work RAM (WRAM)	In CGB mode, switchable bank 1–7
E000	FDFF	Echo RAM (mirror of C000–DDFF)	Nintendo says use of this area is prohibited.
FE00	FE9F	Object attribute memory (OAM)	
FEA0	FEFF	Not Usable	Nintendo says use of this area is prohibited.
FF00	FF7F	I/O Registers	
FF80	FFFE	High RAM (HRAM)	
FFFF	FFFF	Interrupt Enable register (IE)	
*/

namespace GB {
    public class Bus {
        public Cartridge cartridge;
        byte[] memory;
        byte[] vram;
        public byte[] OAMRam;
        public Timer timer;

        public Bus(ref Cartridge cartridge) {
            memory = new byte[0x10000];   // 0x0000–0xFFFF
            vram = new byte[0x2000];      // 8 KB VRAM
            OAMRam = new byte[160];       // 160 bytes OAM
            this.cartridge = cartridge;
            timer = new Timer(this);
        }

        // in Bus
        public byte LY { get; private set; } = 0;
        public void ResetLY()
        {
            LY = 0;
        }
        public void TickLY() {
            LY++;
            // if (LY > 153) LY = 0; // 144 visible + 10 VBlank lines
        }

        public byte Read(int addr) {
            addr &= 0xFFFF;

            if (addr < 0x8000 || (addr >= 0xA000 && addr <= 0xBFFF)) {
                return cartridge.Read(addr);
            }

            if (addr >= 0x8000 && addr <= 0x9FFF) {
                int vramAddr = addr & 0x1FFF;
                return vram[vramAddr];
            }

            if (addr >= 0xE000 && addr <= 0xFDFF) {
                return memory[addr - 0x2000];
            }

            if (addr >= 0xFE00 && addr <= 0xFE9F) {
                return OAMRam[addr - 0xFE00];
            }

            // I/O registers simplified
            switch (addr) {
                case 0xFF04: return timer.ReadDIV();
                case 0xFF05: return timer.ReadTIMA();
                case 0xFF06: return timer.ReadTMA();
                case 0xFF07: return timer.ReadTAC();
                case 0xFF01:
                case 0xFF02: return memory[addr];
                case 0xFF44: return LY;//0x90; // LY test value
            }

            return memory[addr];
        }

        public void Write(int addr, byte val) {
            addr &= 0xFFFF;

            if (addr < 0x8000) return; // ROM write ignored

            if (addr >= 0x8000 && addr <= 0x9FFF) {
                vram[addr & 0x1FFF] = val;
                return;
            }

            if (addr >= 0xE000 && addr <= 0xFDFF) {
                memory[addr - 0x2000] = val;
                return;
            }

            if (addr >= 0xFE00 && addr <= 0xFE9F) {
                OAMRam[addr - 0xFE00] = val;
                return;
            }

            // I/O registers simplified
            switch (addr) {
                case 0xFF04: timer.WriteDIV(val); return;
                case 0xFF05: timer.WriteTIMA(val); return;
                case 0xFF06: timer.WriteTMA(val); return;
                case 0xFF07: timer.WriteTAC(val); return;
                case 0xFF01:
                case 0xFF02:
                    memory[addr] = val;
                    return;
            }

            memory[addr] = val;
        }
    }
}

