using System;
using System.IO;
namespace GB {
public class Cartridge {
  byte[] data;
  public byte Read(int addr) { 
    if (addr < data.Length) {
      return data[addr];
    }
    if (addr < 0x8000) {
      return data[addr % data.Length];
    }
    return 0xFF; 
  }

  public void load(string path) {
    data = File.ReadAllBytes(path); 
    /*
    Console.Write($"Title :"); 
    for(var i = 0x0134; i<0x0143; i++) {
      if ((char)data[i] >= 65 && (char)data[i] < 91) 
      Console.Write((char)data[i]);
    }
    Console.WriteLine($"\nSGB : {(data[0x146] == 0x03 ? "yes" : "no")}");
    string memType = "";
    switch(data[0x147]) {
      case 0x00: memType="ROM ONLY"; break;
      case 0x01: memType="MBC1"; break;
      case 0x02: memType="MBC1+RAM"; break;
      case 0x03: memType="MBC1+RAM+BATTERY"; break;
      case 0x05: memType="MBC2"; break;
      case 0x06: memType="MBC2+BATTERY"; break;
      case 0x08: memType="ROM+RAM 9"; break;
      case 0x09: memType="ROM+RAM+BATTERY 9"; break;
      case 0x0B: memType="MMM01"; break;
      case 0x0C: memType="MMM01+RAM"; break;
      case 0x0D: memType="MMM01+RAM+BATTERY"; break;
      case 0x0F: memType="MBC3+TIMER+BATTERY"; break;
      case 0x10: memType="MBC3+TIMER+RAM+BATTERY 10"; break;
      case 0x11: memType="MBC3"; break;
      case 0x12: memType="MBC3+RAM 10"; break;
      case 0x13: memType="MBC3+RAM+BATTERY 10"; break;
      case 0x19: memType="MBC5"; break;
      case 0x1A: memType="MBC5+RAM"; break;
      case 0x1B: memType="MBC5+RAM+BATTERY"; break;
      case 0x1C: memType="MBC5+RUMBLE"; break;
      case 0x1D: memType="MBC5+RUMBLE+RAM"; break;
      case 0x1E: memType="MBC5+RUMBLE+RAM+BATTERY"; break;
      case 0x20: memType="MBC6"; break;
      case 0x22: memType="MBC7+SENSOR+RUMBLE+RAM+BATTERY"; break;
      case 0xFC: memType="POCKET CAMERA"; break;
      case 0xFD: memType="BANDAI TAMA5"; break;
      case 0xFE: memType="HuC3"; break;
      case 0xFF: memType="HuC1+RAM+BATTERY"; break;
    }
    Console.WriteLine($"Memory type: {memType}"); 
    Console.WriteLine($"ROM: { (32 * ( 1 << data[0x148])) }KB");
    Console.Write("RAM: ");
    switch (data[0x149]) {
      case 0: Console.WriteLine("0	No RAM");break;
      case 1: Console.WriteLine("–	Unused 12");break;
      case 2: Console.WriteLine("8 KiB	1 bank");break;
      case 3: Console.WriteLine("32 KiB 4 banks of 8 KiB each");break;
      case 4: Console.WriteLine("128 KiB	16 banks of 8 KiB each");break;
      case 5: Console.WriteLine("64 KiB	8 banks of 8 KiB each");break;
    }
    byte checksum = 0;
    for (ushort address = 0x0134; address <= 0x014C; address++) {
        checksum = (byte)(checksum - data[address] - 1);
    }
    Console.WriteLine($"Checksum: {((checksum == data[0x14d]) ? "ok" : "")}");
 */ }
}
}
