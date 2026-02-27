using System;
using System.IO;
namespace DmgEmu.Core {

  public interface IMapper
  {
    byte ReadRom(int addr);
    void WriteRom(int addr, byte value);
    byte ReadRam(int addr);
    void WriteRam(int addr, byte value);
  }

  public interface IStatefulMapper
  {
    MapperState GetState();
    void SetState(MapperState state);
  }

  public class RomOnlyMapper : IMapper, IStatefulMapper
  {
    private readonly byte[] rom;

    public RomOnlyMapper(byte[] rom)
    {
      this.rom = rom;
    }

    public byte ReadRom(int addr)
    {
      addr &= 0x7FFF;
      if (addr < rom.Length) return rom[addr];
      return 0xFF;
    }

    public void WriteRom(int addr, byte value) { }

    public byte ReadRam(int addr) => 0xFF;
    public void WriteRam(int addr, byte value) { }

    public MapperState GetState()
    {
      return new MapperState { Kind = MapperKind.RomOnly };
    }

    public void SetState(MapperState state) { }
  }

  public class Mbc1Mapper : IMapper, IStatefulMapper
  {
    private readonly byte[] rom;
    private readonly byte[] ram;
    private readonly int romBanks;
    private readonly int ramBanks;
    private bool ramEnabled;
    private int romBankLow5 = 1;
    private int bankHigh2;
    private int mode; // 0 = ROM banking, 1 = RAM banking

    public Mbc1Mapper(byte[] rom, int ramSize)
    {
      this.rom = rom;
      ram = ramSize > 0 ? new byte[ramSize] : null;
      romBanks = rom.Length / 0x4000;
      ramBanks = ramSize / 0x2000;
    }

    public byte ReadRom(int addr)
    {
      addr &= 0x7FFF;
      if (addr < 0x4000)
      {
        int bank0 = 0;
        if (mode == 1)
          bank0 = (bankHigh2 << 5) % Math.Max(1, romBanks);
        int index0 = bank0 * 0x4000 + addr;
        if (index0 < rom.Length) return rom[index0];
        return 0xFF;
      }

      int bank = GetRomBank();
      int index = bank * 0x4000 + (addr - 0x4000);
      if (index < rom.Length) return rom[index];
      return 0xFF;
    }

    public void WriteRom(int addr, byte value)
    {
      addr &= 0x7FFF;
      if (addr < 0x2000)
      {
        ramEnabled = (value & 0x0F) == 0x0A;
        return;
      }
      if (addr < 0x4000)
      {
        romBankLow5 = value & 0x1F;
        if (romBankLow5 == 0) romBankLow5 = 1;
        return;
      }
      if (addr < 0x6000)
      {
        bankHigh2 = value & 0x03;
        return;
      }
      mode = value & 0x01;
    }

    public byte ReadRam(int addr)
    {
      if (!ramEnabled || ram == null) return 0xFF;
      int bank = (mode == 1) ? bankHigh2 : 0;
      bank %= Math.Max(1, ramBanks);
      int index = bank * 0x2000 + (addr & 0x1FFF);
      if ((uint)index >= (uint)ram.Length) return 0xFF;
      return ram[index];
    }

    public void WriteRam(int addr, byte value)
    {
      if (!ramEnabled || ram == null) return;
      int bank = (mode == 1) ? bankHigh2 : 0;
      bank %= Math.Max(1, ramBanks);
      int index = bank * 0x2000 + (addr & 0x1FFF);
      if ((uint)index >= (uint)ram.Length) return;
      ram[index] = value;
    }

    private int GetRomBank()
    {
      int bank = romBankLow5 | (mode == 0 ? (bankHigh2 << 5) : 0);
      bank %= Math.Max(1, romBanks);
      if (bank == 0) bank = 1;
      return bank;
    }

    public MapperState GetState()
    {
      byte[] ramCopy = null;
      if (ram != null)
      {
        ramCopy = new byte[ram.Length];
        Array.Copy(ram, ramCopy, ram.Length);
      }
      return new MapperState
      {
        Kind = MapperKind.Mbc1,
        RamEnabled = ramEnabled,
        RomBankLow5 = romBankLow5,
        BankHigh2 = bankHigh2,
        Mode = mode,
        Ram = ramCopy
      };
    }

    public void SetState(MapperState state)
    {
      ramEnabled = state.RamEnabled;
      romBankLow5 = state.RomBankLow5;
      bankHigh2 = state.BankHigh2;
      mode = state.Mode;
      if (ram != null && state.Ram != null)
      {
        Array.Copy(state.Ram, ram, Math.Min(ram.Length, state.Ram.Length));
      }
    }
  }

  public class Mbc3Mapper : IMapper, IStatefulMapper
  {
    private readonly byte[] rom;
    private readonly byte[] ram;
    private readonly int romBanks;
    private readonly int ramBanks;
    private bool ramEnabled;
    private int romBank = 1;
    private int ramRtcSelect = 0;

    private int rtcSec;
    private int rtcMin;
    private int rtcHour;
    private int rtcDay;
    private bool rtcHalt;
    private bool rtcCarry;

    private bool latchArmed;
    private bool rtcLatched;
    private int latSec;
    private int latMin;
    private int latHour;
    private int latDay;
    private bool latCarry;
    private bool latHalt;

    private DateTime lastUpdate;

    public Mbc3Mapper(byte[] rom, int ramSize)
    {
      this.rom = rom;
      ram = ramSize > 0 ? new byte[ramSize] : null;
      romBanks = rom.Length / 0x4000;
      ramBanks = ramSize / 0x2000;
      lastUpdate = DateTime.UtcNow;
    }

    public byte ReadRom(int addr)
    {
      addr &= 0x7FFF;
      if (addr < 0x4000)
      {
        int index0 = addr;
        if (index0 < rom.Length) return rom[index0];
        return 0xFF;
      }

      int bank = romBank & 0x7F;
      if (bank == 0) bank = 1;
      bank %= Math.Max(1, romBanks);
      int index = bank * 0x4000 + (addr - 0x4000);
      if (index < rom.Length) return rom[index];
      return 0xFF;
    }

    public void WriteRom(int addr, byte value)
    {
      addr &= 0x7FFF;
      if (addr < 0x2000)
      {
        ramEnabled = (value & 0x0F) == 0x0A;
        return;
      }
      if (addr < 0x4000)
      {
        romBank = value & 0x7F;
        if (romBank == 0) romBank = 1;
        return;
      }
      if (addr < 0x6000)
      {
        ramRtcSelect = value;
        return;
      }
      if (value == 0x00)
      {
        latchArmed = true;
      }
      else if (value == 0x01 && latchArmed)
      {
        LatchRtc();
        latchArmed = false;
      }
    }

    public byte ReadRam(int addr)
    {
      if (!ramEnabled) return 0xFF;
      addr &= 0x1FFF;

      if (ramRtcSelect <= 0x03)
      {
        if (ram == null) return 0xFF;
        int bank = ramRtcSelect % Math.Max(1, ramBanks);
        int index = bank * 0x2000 + addr;
        if ((uint)index >= (uint)ram.Length) return 0xFF;
        return ram[index];
      }

      UpdateRtc();
      return ReadRtcRegister();
    }

    public void WriteRam(int addr, byte value)
    {
      if (!ramEnabled) return;
      addr &= 0x1FFF;

      if (ramRtcSelect <= 0x03)
      {
        if (ram == null) return;
        int bank = ramRtcSelect % Math.Max(1, ramBanks);
        int index = bank * 0x2000 + addr;
        if ((uint)index >= (uint)ram.Length) return;
        ram[index] = value;
        return;
      }

      UpdateRtc();
      WriteRtcRegister(value);
    }

    private void UpdateRtc()
    {
      if (rtcHalt) return;

      DateTime now = DateTime.UtcNow;
      long elapsed = (long)(now - lastUpdate).TotalSeconds;
      if (elapsed <= 0) return;
      lastUpdate = now;

      int total = rtcSec + (int)elapsed;
      rtcSec = total % 60;
      int carry = total / 60;

      total = rtcMin + carry;
      rtcMin = total % 60;
      carry = total / 60;

      total = rtcHour + carry;
      rtcHour = total % 24;
      carry = total / 24;

      rtcDay += carry;
      if (rtcDay >= 512)
      {
        rtcDay %= 512;
        rtcCarry = true;
      }
    }

    private void LatchRtc()
    {
      UpdateRtc();
      latSec = rtcSec;
      latMin = rtcMin;
      latHour = rtcHour;
      latDay = rtcDay;
      latCarry = rtcCarry;
      latHalt = rtcHalt;
      rtcLatched = true;
    }

    private byte ReadRtcRegister()
    {
      bool useLatched = rtcLatched;
      switch (ramRtcSelect)
      {
        case 0x08: return (byte)(useLatched ? latSec : rtcSec);
        case 0x09: return (byte)(useLatched ? latMin : rtcMin);
        case 0x0A: return (byte)(useLatched ? latHour : rtcHour);
        case 0x0B: return (byte)((useLatched ? latDay : rtcDay) & 0xFF);
        case 0x0C:
          {
            int day = useLatched ? latDay : rtcDay;
            bool carry = useLatched ? latCarry : rtcCarry;
            bool halt = useLatched ? latHalt : rtcHalt;
            byte v = 0;
            if ((day & 0x100) != 0) v |= 0x01;
            if (halt) v |= 0x40;
            if (carry) v |= 0x80;
            return v;
          }
        default:
          return 0xFF;
      }
    }

    private void WriteRtcRegister(byte value)
    {
      switch (ramRtcSelect)
      {
        case 0x08:
          rtcSec = value % 60;
          break;
        case 0x09:
          rtcMin = value % 60;
          break;
        case 0x0A:
          rtcHour = value % 24;
          break;
        case 0x0B:
          rtcDay = (rtcDay & 0x100) | value;
          rtcDay %= 512;
          break;
        case 0x0C:
          {
            int high = value & 0x01;
            rtcDay = (rtcDay & 0xFF) | (high << 8);
            bool newHalt = (value & 0x40) != 0;
            bool newCarry = (value & 0x80) != 0;
            if (rtcHalt && !newHalt)
              lastUpdate = DateTime.UtcNow;
            rtcHalt = newHalt;
            rtcCarry = newCarry;
            break;
          }
      }
    }

    public MapperState GetState()
    {
      byte[] ramCopy = null;
      if (ram != null)
      {
        ramCopy = new byte[ram.Length];
        Array.Copy(ram, ramCopy, ram.Length);
      }
      return new MapperState
      {
        Kind = MapperKind.Mbc3,
        Ram = ramCopy,
        RamEnabled = ramEnabled,
        RomBank = romBank,
        RamRtcSelect = ramRtcSelect,
        RtcSec = rtcSec,
        RtcMin = rtcMin,
        RtcHour = rtcHour,
        RtcDay = rtcDay,
        RtcHalt = rtcHalt,
        RtcCarry = rtcCarry,
        LatchArmed = latchArmed,
        RtcLatched = rtcLatched,
        LatSec = latSec,
        LatMin = latMin,
        LatHour = latHour,
        LatDay = latDay,
        LatCarry = latCarry,
        LatHalt = latHalt,
        LastUpdateTicks = lastUpdate.Ticks
      };
    }

    public void SetState(MapperState state)
    {
      ramEnabled = state.RamEnabled;
      romBank = state.RomBank == 0 ? 1 : state.RomBank;
      ramRtcSelect = state.RamRtcSelect;
      rtcSec = state.RtcSec;
      rtcMin = state.RtcMin;
      rtcHour = state.RtcHour;
      rtcDay = state.RtcDay;
      rtcHalt = state.RtcHalt;
      rtcCarry = state.RtcCarry;
      latchArmed = state.LatchArmed;
      rtcLatched = state.RtcLatched;
      latSec = state.LatSec;
      latMin = state.LatMin;
      latHour = state.LatHour;
      latDay = state.LatDay;
      latCarry = state.LatCarry;
      latHalt = state.LatHalt;
      lastUpdate = new DateTime(state.LastUpdateTicks == 0 ? DateTime.UtcNow.Ticks : state.LastUpdateTicks, DateTimeKind.Utc);
      if (ram != null && state.Ram != null)
      {
        Array.Copy(state.Ram, ram, Math.Min(ram.Length, state.Ram.Length));
      }
    }
  }

  public class Cartridge {
    byte[] rom;
    IMapper mapper;
    byte typeCode;

    public byte Read(int addr)
    {
      addr &= 0x7FFF;
      return mapper.ReadRom(addr);
    }

    public void Write(int addr, byte value)
    {
      addr &= 0x7FFF;
      mapper.WriteRom(addr, value);
    }

    public byte ReadRam(int addr) => mapper.ReadRam(addr);
    public void WriteRam(int addr, byte value) => mapper.WriteRam(addr, value);

    public void load(string path) {
      rom = File.ReadAllBytes(path);
      typeCode = rom[0x147];
      mapper = CreateMapper(rom);
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

    private static IMapper CreateMapper(byte[] data)
    {
      byte type = data[0x147];
      int ramSize = GetRamSize(data[0x149]);

      switch (type)
      {
        case 0x00:
          return new RomOnlyMapper(data);
        case 0x01:
        case 0x02:
        case 0x03:
          return new Mbc1Mapper(data, ramSize);
        case 0x0F:
        case 0x10:
        case 0x11:
        case 0x12:
        case 0x13:
          return new Mbc3Mapper(data, ramSize);
        default:
          return new RomOnlyMapper(data);
      }
    }

    private static int GetRamSize(byte code)
    {
      switch (code)
      {
        case 0x00: return 0;
        case 0x02: return 8 * 1024;
        case 0x03: return 32 * 1024;
        case 0x04: return 128 * 1024;
        case 0x05: return 64 * 1024;
        default: return 0;
      }
    }

    public CartridgeState GetState()
    {
      MapperState mapperState = null;
      if (mapper is IStatefulMapper stateful)
      {
        mapperState = stateful.GetState();
      }
      return new CartridgeState
      {
        TypeCode = typeCode,
        MapperState = mapperState
      };
    }

    public void SetState(CartridgeState state)
    {
      if (mapper is IStatefulMapper stateful && state.MapperState != null)
      {
        stateful.SetState(state.MapperState);
      }
    }
  }

  public enum MapperKind : byte
  {
    RomOnly = 0,
    Mbc1 = 1,
    Mbc3 = 3
  }

  public class MapperState
  {
    public MapperKind Kind;
    public byte[] Ram;

    public bool RamEnabled;
    public int RomBankLow5;
    public int BankHigh2;
    public int Mode;

    public int RomBank;
    public int RamRtcSelect;
    public int RtcSec;
    public int RtcMin;
    public int RtcHour;
    public int RtcDay;
    public bool RtcHalt;
    public bool RtcCarry;
    public bool LatchArmed;
    public bool RtcLatched;
    public int LatSec;
    public int LatMin;
    public int LatHour;
    public int LatDay;
    public bool LatCarry;
    public bool LatHalt;
    public long LastUpdateTicks;
  }

  public class CartridgeState
  {
    public byte TypeCode;
    public MapperState MapperState;
  }
}
