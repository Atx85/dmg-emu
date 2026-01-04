using System;
using System.Collections.Generic;


namespace GB {

  public struct Sprite
  {
    public int x, y;
    public byte tile;
    public byte attr;
  }
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
    List<Sprite> scanlineSprites = new List<Sprite>(10);
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


    void SetSTATMode(int newMode)
    {
      // clear bits 0-1
      byte stat = (byte)(STAT & 0xFC);
      stat |= (byte)(newMode & 0x3);
      bus.Write(0xFF41, stat);
    }
    void ScanSpritesForLine()
    {
      scanlineSprites.Clear();

      int spriteHeight = ObjSize == 2 ? 16 : 8;

      for (int i = 0; i < 40 && scanlineSprites.Count < 10; i++)
      {
        ushort addr = (ushort)(0xFE00 + i * 4);
        int y = bus.Read(addr) - 16;
        int x = bus.Read((ushort)(addr + 1)) - 8;
        byte tile = bus.Read((ushort)(addr + 2));
        byte attr = bus.Read((ushort)(addr + 3));

        if (LY >= y && LY < y + spriteHeight)
        {
          scanlineSprites.Add(new Sprite { x = x, y = y, tile = tile, attr = attr });
        }
      }
    }

    // Find up to 10 sprites for this line

    void Mode2()
    {
      if (dot == 0)
      {
        ScanSpritesForLine();
        mode = 2;
        SetSTATMode(mode);
        CheckSTATInterrupt(mode);
      }

      if (dot == 80)
        mode = 3; // enter drawing
    }
    void CheckSTATInterrupt(int mode)
    {
      byte stat = bus.Read(0xFF41);
      bool trigger = false;

      // Mode interrupt
      switch(mode)
      {
        case 0: if ((stat & 0x08) != 0) trigger = true; break; // HBlank
        case 1: if ((stat & 0x10) != 0) trigger = true; break; // VBlank
        case 2: if ((stat & 0x20) != 0) trigger = true; break; // OAM
      }

      // LYC=LY coincidence
      if (bus.LY == LYC)
      {
        stat |= 0x04; // set coincidence flag
        if ((stat & 0x40) != 0) trigger = true;
      }
      else
      {
        stat &= 0xFB; // clear coincidence flag
      }

      bus.Write(0xFF41, stat);

      if (trigger)
        bus.RequestInterrupt(1); // 1 = LCD STAT
    }

    Pixel FetchBgPixel(int pixelIndex)
    {
      if (!isBit(LCDC, 0))
        return new Pixel(0, 0, 0, false);

      bool usingWindow =
        IsWindowEnabled &&
        LY >= WY &&
        pixelIndex >= WX - 7;

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

      ushort mapBase = (ushort)(usingWindow ? WindowTileMapArea : BgTileMapArea);
      ushort tileMapAddr = (ushort)(mapBase + tileY * 32 + tileX);

      byte tileId = bus.Read(tileMapAddr);
      int tileIndex = BgWinAddrMode ? (int)tileId : (int)(sbyte)tileId;
      ushort tileBase = BgWinAddrMode ? (ushort)0x8000 : (ushort)0x8800;
      ushort tileAddr = (ushort)(tileBase + tileIndex * 16);

      int row = pixelY & 7;
      byte lo = bus.Read((ushort)(tileAddr + row * 2));
      byte hi = bus.Read((ushort)(tileAddr + row * 2 + 1));

      int bit = 7 - (pixelX & 7);
      int colorId = ((hi >> bit) & 1) << 1 | ((lo >> bit) & 1);

      return new Pixel(BGP(colorId), 0, 0, false);
    }

    bool TryFetchSpritePixel(int pixelIndex, Pixel bg, out Pixel result)
    {
      foreach (var spr in scanlineSprites)
      {
        int sx = pixelIndex - spr.x;
        if (sx < 0 || sx >= 8) continue;

        int spriteHeight = ObjSize == 2 ? 16 : 8;
        int sy = LY - spr.y;
        if (sy < 0 || sy >= spriteHeight) continue;

        bool xFlip = (spr.attr & 0x20) != 0;
        bool yFlip = (spr.attr & 0x40) != 0;

        int px = xFlip ? 7 - sx : sx;
        int py = yFlip ? (spriteHeight - 1 - sy) : sy;

        byte tileIndex = spr.tile;
        if (spriteHeight == 16)
          tileIndex &= 0xFE;

        ushort tileAddr = (ushort)(0x8000 + tileIndex * 16 + py * 2);
        byte lo = bus.Read(tileAddr);
        byte hi = bus.Read((ushort)(tileAddr + 1));

        int bit = 7 - px;
        int colorId = ((hi >> bit) & 1) << 1 | ((lo >> bit) & 1);

        if (colorId == 0)
          continue;

        bool behindBg = (spr.attr & 0x80) != 0;
        if (behindBg && bg.Color != 0)
          continue;

        int palette = (spr.attr >> 4) & 1;
        byte finalColor = OBP((byte)palette, colorId);

        result = new Pixel(finalColor, (byte)palette, 0, behindBg);
        return true;
      }

      result = new Pixel();
      return false;
    }
    // Render background, window, sprites


    void Mode3()
    {
      int pixelIndex = dot - 80;
      if (pixelIndex < 0 || pixelIndex >= 160 || LY >= 144)
        return;

      Pixel bg = FetchBgPixel(pixelIndex);

      if (IsObjEnabled && TryFetchSpritePixel(pixelIndex, bg, out Pixel sprite))
        Framebuffer[LY, pixelIndex] = sprite;
      else
        Framebuffer[LY, pixelIndex] = bg;

      if (pixelIndex == 159)
      {
        mode = 0; // HBlank
        SetSTATMode(mode);
        CheckSTATInterrupt(mode);
      }
    }
    // Wait for next line; CPU can access VRAM/OAM


    void Mode0()
    {
      if (dot >= 456)
      {
        dot = 0;
        bus.TickLY();

        if (bus.LY < 144)
        {
          mode = 2; // OAM scan next line
        }
        else
        {
          mode = 1; // VBlank
          frameSignaled = false;
        }

        SetSTATMode(mode);
        CheckSTATInterrupt(mode);
      }
    }
    // No rendering; frame is done

    void Mode1()
    {
      // Fire frame event once, when entering VBlank
      if (!frameSignaled)
      {
        frameSignaled = true;
        OnFrameReady?.Invoke(Framebuffer);
      }

      // Wait for the line to finish
      if (dot >= 456)
      {
        dot = 0;
        bus.TickLY();  // increment LY
        if (bus.LY > 153)
        {
          bus.ResetLY();  // back to line 0
          mode = 2;       // start OAM scan for new frame
          frameSignaled = false;
        }
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
      /*
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
         */
    }
  }
}
