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
    Cartridge cartridge;
    byte[] memory;
    public Bus(ref Cartridge cartridge) {
      memory = new byte[0xFFFF + 1];
      this.cartridge = cartridge;
    }
    public byte Read(int addr) {
      if (addr < 0x8000 || (addr >= 0xA000 && addr <= 0xBFFF)) {
        return cartridge.Read(addr);
      } else {
        if (addr == 0xFF44) return 0x90; // random number for testing
        else
        return memory[addr];
      }
    }

    public void Write(int addr, byte val) {
      memory[addr] = val;
    }
  }
}
