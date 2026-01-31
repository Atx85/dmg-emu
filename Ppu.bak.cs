using System;
using System.Collections.Generic;


namespace GB {

  public struct Sprite
  {
    public int x, y;
    public byte tile;
    public byte attr;
    public int oamIndex;

    public override string ToString()
    {
      return $"Sprite[oam={oamIndex}, x={x}, y={y}, tile=0x{tile:X2}, " +
        $"attr=0b{Convert.ToString(attr, 2).PadLeft(8, '0')}]";
    }
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


  public class Ppu
  {
    Bus bus;

    bool windowUsedThisLine;
    int dot;
    int mode;
    int modeStartDot;
    bool frameSignaled;

    int windowLine;

    public Pixel[,] Framebuffer;
    List<Sprite> scanlineSprites = new List<Sprite>(10);

    public delegate void FrameReadyHandler(Pixel[,] framebuffer);
    public event FrameReadyHandler OnFrameReady;

    public Ppu(Bus _bus)
    {
      bus = _bus;
      Framebuffer = new Pixel[144, 160];
      dot = 0;
      EnterMode(2);
    }

    // ---------- Registers ----------
    byte LCDC => bus.Read(0xFF40);
    byte STAT => bus.Read(0xFF41);
    byte SCY  => bus.Read(0xFF42);
    byte SCX  => bus.Read(0xFF43);
    byte LY   => bus.LY;
    byte LYC  => bus.Read(0xFF45);
    byte WX   => bus.Read(0xFF4B);
    byte WY   => bus.Read(0xFF4A);

    bool IsLCDEnabled => (LCDC & 0x80) != 0;
    bool BgEnable     => (LCDC & 0x01) != 0;
    bool ObjEnable    => (LCDC & 0x02) != 0;
    int  ObjSize      => (LCDC & 0x04) != 0 ? 16 : 8;
    bool BgWinSigned  => (LCDC & 0x10) == 0;
    bool WindowEnable => (LCDC & 0x20) != 0;
    ushort BgMap      => (LCDC & 0x08) != 0 ? (ushort)0x9C00 : (ushort)0x9800;
    ushort WinMap     => (LCDC & 0x40) != 0 ? (ushort)0x9C00 : (ushort)0x9800;

    // ---------- Mode handling ----------
    void EnterMode(int newMode)
    {
      mode = newMode;
      bus.PpuMode = newMode;
      modeStartDot = dot;

      byte stat = (byte)((STAT & 0xFC) | (newMode & 3));
      bus.Write(0xFF41, stat);

      CheckSTATInterrupt(newMode);
    }

void UpdateLYC()
    {
        byte stat = STAT;

        if (LY == LYC)
        {
            stat |= 0x04;
            if ((stat & 0x40) != 0)
                bus.RequestInterrupt(1);
        }
        else
        {
            stat &= 0xFB;
        }

        bus.Write(0xFF41, stat);
    }

    bool TryFetchSpritePixel(int pixelX, Pixel bg, out Pixel result)
    {
      foreach (var spr in scanlineSprites)
      {
        int sx = pixelX - spr.x; // relative X position in sprite
        int sy = LY - spr.y;     // relative Y position in sprite

        // Skip if pixel outside sprite bounds (allow partially offscreen sprites)
        if (sx < 0 || sx >= 8 || sy < 0 || sy >= ObjSize)
          continue;

        bool xFlip = (spr.attr & 0x20) != 0;
        bool yFlip = (spr.attr & 0x40) != 0;

        // Apply flipping
        int px = xFlip ? 7 - sx : sx;
        int py = yFlip ? ObjSize - 1 - sy : sy;

        // Select correct tile for 8x16 sprites
        byte tileIndex = spr.tile;
        ushort tileAddr;

        if (ObjSize == 16)
        {
          if (py < 8)
            tileAddr = (ushort)(0x8000 + (tileIndex & 0xFE) * 16 + py * 2);
          else
            tileAddr = (ushort)(0x8000 + (tileIndex | 1) * 16 + (py - 8) * 2);
        }
        else
        {
          tileAddr = (ushort)(0x8000 + tileIndex * 16 + py * 2);
        }

        byte lo = bus.Read(tileAddr);
        byte hi = bus.Read((ushort)(tileAddr + 1));

        int colorId = ((hi >> (7 - px)) & 1) << 1 | ((lo >> (7 - px)) & 1);

        // Transparent pixel
        if (colorId == 0)
          continue;

        // BG priority (optional: ignore for Acid2)
        bool behindBg = (spr.attr & 0x80) != 0;
        // Comment out for testing Acid2 sprites:
         if (behindBg && bg.Color != 0) continue;

        // Select palette
        int paletteId = (spr.attr >> 4) & 1;
        byte obp = bus.Read((ushort)(paletteId == 0 ? 0xFF48 : 0xFF49));
        byte finalColor = (byte)((obp >> (colorId * 2)) & 0b11);

        result = new Pixel(finalColor, (byte)paletteId, 0, behindBg);
        return true;
      }

      result = new Pixel(0, 0, 0, false);
      return false;
    }


    void CheckSTATInterrupt(int m)
    {
      byte stat = STAT;
      bool trigger = false;

      if (m == 0 && (stat & 0x08) != 0) trigger = true;
      if (m == 1 && (stat & 0x10) != 0) trigger = true;
      if (m == 2 && (stat & 0x20) != 0) trigger = true;

      if (LY == LYC)
      {
        stat |= 0x04;
        if ((stat & 0x40) != 0) trigger = true;
      }
      else stat &= 0xFB;

      bus.Write(0xFF41, stat);

      if (trigger)
        bus.RequestInterrupt(1);
    }

    // ---------- Sprite scan ----------
    void ScanSpritesForLine()
    {
      scanlineSprites.Clear();

      for (int i = 0; i < 40 && scanlineSprites.Count < 10; i++)
      {
        ushort addr = (ushort)(0xFE00 + i * 4);
        int y = bus.Read(addr) - 16;
        int x = bus.Read((ushort)(addr + 1)) - 8;
        byte tile = bus.Read((ushort)(addr + 2));
        byte attr = bus.Read((ushort)(addr + 3));

        if (LY >= y && LY < y + ObjSize)
        {
          scanlineSprites.Add(new Sprite
              {
              x = x,
              y = y,
              tile = tile,
              attr = attr,
              oamIndex = i
              });
        }
      }

      scanlineSprites.Sort((a, b) =>
          {
          int dx = a.x.CompareTo(b.x);
          return dx != 0 ? dx : a.oamIndex.CompareTo(b.oamIndex);
          });
    }

    // ---------- Rendering ----------

    Pixel FetchBgPixel(int x, bool useWindow)
    {
      if (!BgEnable)
        return new Pixel(0, 0, 0, false);

      int px = useWindow ? x - (WX - 7) : (SCX + x) & 0xFF;
      int py = useWindow ? windowLine : (SCY + LY) & 0xFF;

      ushort map = useWindow ? WinMap : BgMap;
      ushort tileAddr = (ushort)(map + (py >> 3) * 32 + (px >> 3));

      int tileId = bus.Read(tileAddr);

      int tileIndex;
      ushort tileBase;

      if (BgWinSigned)
      {
        tileIndex = (sbyte)tileId;
        tileBase = 0x9000;
      }
      else
      {
        tileIndex = tileId;
        tileBase = 0x8000;
      }

      ushort dataAddr = (ushort)(tileBase + (ushort)(tileIndex * 16) + (py & 7) * 2);
      byte lo = bus.Read(dataAddr);
      byte hi = bus.Read((ushort)(dataAddr + 1));

      int bit = 7 - (px & 7);
      int color = ((hi >> bit) & 1) << 1 | ((lo >> bit) & 1);

      byte bgp = bus.Read(0xFF47);
      byte finalColor = (byte)((bgp >> (color * 2)) & 0b11);

      return new Pixel(finalColor, 0, 0, false);
    }

    public void Tick()
    {
      if (!IsLCDEnabled)
      {
        dot = 0;
        bus.ResetLY();
        byte stat = bus.Read(0xFF41);
        stat &= 0xFC; // mode = 0
        bus.Write(0xFF41, stat);

        mode = 0;
        modeStartDot = 0;
        windowLine = 0;
        return;
      }

      switch (mode)
      {
        case 2: // OAM scan
          if (dot - modeStartDot == 0)
            ScanSpritesForLine();
          if (dot - modeStartDot >= 80)
            EnterMode(3);
          break;

        case 3: // Drawing pixels
          {
            int pixelX = dot - modeStartDot;

            if (pixelX == 0)
              windowUsedThisLine = (WindowEnable && LY >= WY); // reset flag at start of line

            if (pixelX >= 0 && pixelX < 160 && LY < 144)
            {
              bool useWindow = WindowEnable && LY >= WY && pixelX >= WX - 7;

              Pixel bg = FetchBgPixel(pixelX, useWindow);
              // Sprites can be added later:
               if (ObjEnable && TryFetchSpritePixel(pixelX, bg, out Pixel sprite)) Framebuffer[LY, pixelX] = sprite;
               else
              Framebuffer[LY, pixelX] = bg;
            }

            if (dot - modeStartDot >= 172)
              EnterMode(0);
          }
          break;

        case 0: // HBlank
          if (dot - modeStartDot >= 204)
          {
            dot = 0;
            bus.TickLY();
            UpdateLYC();

            // Increment windowLine only once per scanline if window is active
            if (WindowEnable && LY >= WY)
              windowLine++;

            if (LY == 144)
            {
              EnterMode(1); // VBlank
              bus.RequestInterrupt(0);
              // bus.DumpPpu();
              OnFrameReady?.Invoke(Framebuffer);
            }
            else
              EnterMode(2);
          }
          break;

        case 1: // VBlank
          if (dot - modeStartDot >= 456)
          {
            dot = 0;
            bus.TickLY();
            if (LY > 153)
            {
              bus.ResetLY();
              windowLine = 0; // reset window at start of new frame
              EnterMode(2);
            }
          }
          break;
      }

      dot++;
    }
  }
}
