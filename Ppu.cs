using System;
using System.Collections.Generic;


namespace GB {

  public struct Pixel {
    private ushort _data;

    public byte Color {
      get => GetBits(_data, 0, 2); // bits 0–1
      set => _data = SetBits(_data, 0, 2, value);
    }

    public byte Palette {
      get => GetBits(_data, 2, 3); // bits 2–4
      set => _data = SetBits(_data, 2, 3, value);
    }

    public byte SpritePriority {
      get => GetBits(_data, 5, 6); // bits 5–10
      set => _data = SetBits(_data, 5, 6, value);
    }

    public bool BackgroundPriority {
      get => GetBits(_data, 11, 1) != 0; // bit 11
      set => _data = SetBits(_data, 11, 1, (byte)(value ? 1 : 0));
    }

    public bool IsTransparent => Color == 0;

    public Pixel(byte color, byte palette, byte spritePriority, bool bgPriority) {
      _data = 0;
      Color = color;
      Palette = palette;
      SpritePriority = spritePriority;
      BackgroundPriority = bgPriority;
    }

    // Bitfield helper methods
    private static byte GetBits(ushort source, int pos, int length) {
      return (byte)((source >> pos) & ((1 << length) - 1));
    }

    private static ushort SetBits(ushort source, int pos, int length, byte value) {
      ushort mask = (ushort)(((1 << length) - 1) << pos);
      ushort cleared = (ushort)(source & ~mask);
      ushort shifted = (ushort)((value << pos) & mask);
      return (ushort)(cleared | shifted);
    }
  }

  public class Ppu {
    Bus bus;
    int dot;
    bool frameSignaled = false;
    public delegate void FrameReadyHandler(Pixel[,] framebuffer);
    public event FrameReadyHandler OnFrameReady;
    int mode;
    public Pixel[,] Framebuffer;
    bool isBit(byte val, ushort i) => i < 8 && (val & (1 << i)) != 0;
    // LCDC - 0xFF40
    byte LCDC => bus.Read(0xFF40);
    bool IsLCDEnabled => isBit(LCDC, 7); 
    int  WindowTileMapArea => isBit(LCDC, 6) ? 0x9C00 : 0x9800;
    bool IsWindowEnabled => isBit(LCDC, 5); // Changing the value of this register mid-frame triggers a more complex behaviour
    bool BgWinAddrMode => isBit(LCDC, 4);
    int  BgTileMapArea => isBit(LCDC, 3) ? 0x9C00 : 0x9800;
    int  ObjSize => isBit(LCDC, 2) ? 2 : 1;
    bool IsObjEnabled => isBit(LCDC, 1);
    /*
       LCDC.0 — BG and Window enable/priority
       LCDC.0 has different meanings depending on Game Boy type and Mode:
       Non-CGB Mode (DMG, SGB and CGB in compatibility mode): BG and Window display
       When Bit 0 is cleared, both background and window become blank (white), and the Window Display Bit is ignored in that case. Only objects may still be displayed (if enabled in Bit 1). */
    // LCD Status Register - 0xFF41
    byte STAT => bus.Read(0xFF41);
    byte SCY => bus.Read(0xFF42);
    byte SCX => bus.Read(0xFF43);
    byte LY => bus.LY;//bus.Read(0xFF44); // LCD Y coord
    byte LYC => bus.Read(0xFF45); 
    byte BGP(int id) {
      if (id < 0 || id > 3)
        throw new ArgumentOutOfRangeException(nameof(id), "ID must be 0–3");

      byte bgp = bus.Read(0xFF47);
      return (byte)((bgp >> (id * 2)) & 0b11);
    }
    // ff48, ff49
    byte OBP(byte palette, int id) {
      if (id < 0 || id > 3)
        throw new ArgumentOutOfRangeException(nameof(id), "ID must be 0–3");
      if (palette != 0 && palette != 1)
        throw new ArgumentOutOfRangeException(nameof(palette), "Palette must be 0 or 1");

      ushort address = (ushort)(palette == 0 ? 0xFF48 : 0xFF49);
      byte obp = bus.Read(address);
      return (byte)((obp >> (id * 2)) & 0b11);
    }

    byte WX => bus.Read(0xFF4A);
    byte WY => bus.Read(0xFF4B);
    public Ppu(Bus _bus) {
      Framebuffer = new Pixel[144, 160];
      bus = _bus;
      dot = 0;
      mode = 2;
   }

    // Find up to 10 sprites for this line
    void Mode2 () {
      if (dot == 80) mode = 3; 
    }

    // Render background, window, sprites

    void Mode3()
    {

        int pixelIndex = dot - 80;
        if (pixelIndex < 0 || pixelIndex >= 160 || LY >= 144) return;
        // BG disabled → white (DMG behavior)
        if (!isBit(LCDC, 0))
        {
            Framebuffer[LY, pixelIndex] = new Pixel(0, 0, 0, false);
            return;
        }
        bool usingWindow =
            IsWindowEnabled &&
            LY >= WY &&
            dot >= WX - 7;

        int pixelX, pixelY;

        if (usingWindow)
        {
            pixelX = pixelIndex - (WX - 7);
            pixelY = LY - WY;
        }
        else
        {
            pixelX = (SCX + pixelIndex) & 0xFF;
            pixelY = (SCY + LY) & 0xFF;
        }

        int tileX = pixelX >> 3;
        int tileY = pixelY >> 3;

        ushort tileMapAddr = (ushort)(
            (usingWindow ? WindowTileMapArea : BgTileMapArea) +
            tileY * 32 + tileX
        );

        byte tileId = bus.Read(tileMapAddr);

        int tileIndex = BgWinAddrMode
            ? tileId
            : (int)(sbyte)tileId;

        ushort tileDataBase = BgWinAddrMode ? (ushort)0x8000 : (ushort)0x8800;

        ushort tileAddr = (ushort)(
            tileDataBase + tileIndex * 16
        );

        int row = pixelY & 7;
        byte lo = bus.Read((ushort)(tileAddr + row * 2));
        byte hi = bus.Read((ushort)(tileAddr + row * 2 + 1));

        int bit = 7 - (pixelX & 7);
        int colorId =
            ((hi >> bit) & 1) << 1 |
            ((lo >> bit) & 1);

        byte finalColor = BGP(colorId);
        Pixel px = new Pixel(finalColor, 0, 0, false);
        Framebuffer[LY, pixelIndex] = px;
        if (dot == 252) mode = 0;
    }
    // Wait for next line; CPU can access VRAM/OAM

    void Mode0()
    {
  
    }

    // No rendering; frame is done

 
    void Mode1()
    {
        if (!frameSignaled)
        {
            frameSignaled = true;
            OnFrameReady?.Invoke(Framebuffer);
        }
    }



    public void Tick()
    {
        switch (mode)
        {
            case 0: Mode0(); break;
            case 1: Mode1(); break;
            case 2: Mode2(); break;
            case 3: Mode3(); break;
        }

        dot++;

        if (dot >= 456)
        {
            dot = 0;
            bus.TickLY();

            if (bus.LY == 144)
            {
                mode = 1; // enter VBlank
                frameSignaled = false;
            }
            else if (bus.LY > 153)
            {
                bus.ResetLY();
                mode = 2; // new frame
            }
            else
            {
                mode = 2;
            }
        }
    }
  }
}
