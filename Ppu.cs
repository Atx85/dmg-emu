using System;
namespace GB
{
    // --------------------------
    // ENUMS
    // --------------------------
    public enum PpuMode : byte
    {
        HBlank = 0,
        VBlank = 1,
        OamScan = 2,
        Drawing = 3
    }

    // --------------------------
    // INTERFACES
    // --------------------------
    public interface IPpuMemory
    {
        byte ReadVram(ushort addr);
        byte ReadOam(ushort addr);
    }

    public interface IFrameBuffer
    {
        void SetPixel(int x, int y, int color);
        int GetPixel(int x, int y);
        void Clear(int color = 0);
    }

    public interface IPpuRegisters
    {
        byte SCX { get; }
        byte SCY { get; }
        byte LY { get; set; }
        byte LYC { get; }
        byte BGP { get; }
        byte OBP0 { get; }
        byte OBP1 { get; }
        byte LCDC { get; }
        PpuMode Mode { get; set; }
        bool LcdEnabled { get; }

        byte WX { get; }
        byte WY { get; }

        byte STAT { get; set; }
    }
    public interface IPpuInterrupts
    {
        void RequestVBlank();
        void RequestStat();
    }

    public interface ILineSpriteCounter
    {
        int CountSpritesOnLine(int ly);
    }

    public interface IPpuRenderer
    {
        void RenderScanline(int ly);
    }

    // --------------------------
    // FRAMEBUFFER
    // --------------------------
    public class FrameBuffer : IFrameBuffer
    {
        private readonly int[,] pixels = new int[160, 144];

        public void SetPixel(int x, int y, int color)
        {
            if ((uint)x >= 160 || (uint)y >= 144) return;
            pixels[x, y] = color;
        }

        public int GetPixel(int x, int y)
        {
            if ((uint)x >= 160 || (uint)y >= 144) return 0;
            return pixels[x, y];
        }

        public void Clear(int color = 0)
        {
            for (int y = 0; y < 144; y++)
                for (int x = 0; x < 160; x++)
                    pixels[x, y] = color;
        }
    }

    // --------------------------
    // TIMING
    // --------------------------
    public class PpuTiming
    {
        public int GetModeCycles(PpuMode mode)
        {
            switch (mode)
            {
                case PpuMode.OamScan:
                    return 80;
                case PpuMode.Drawing:
                    return 172;
                case PpuMode.HBlank:
                    return 204;
                case PpuMode.VBlank:
                    return 456;
                default:
                    return 0;
            }
        }
    }

    // --------------------------
    // BUS ADAPTERS
    // --------------------------
    public class BusMemoryAdapter : IPpuMemory
    {
        private readonly Bus bus;
        public BusMemoryAdapter(Bus bus) { this.bus = bus; }

        public byte ReadVram(ushort addr) => bus.Read((ushort)(0x8000 + (addr & 0x1FFF)));
        public byte ReadOam(ushort addr) => bus.OAMRam[addr & 0xFF];
    }

    public class SpriteCounter : ILineSpriteCounter
    {
        private readonly IPpuMemory mem;
        private readonly IPpuRegisters regs;

        public SpriteCounter(IPpuMemory mem, IPpuRegisters regs)
        {
            this.mem = mem;
            this.regs = regs;
        }

        public int CountSpritesOnLine(int ly)
        {
            int height = (regs.LCDC & 0x04) != 0 ? 16 : 8;
            int count = 0;
            for (int i = 0; i < 40 && count < 10; i++)
            {
                int oamIndex = i * 4;
                int y = mem.ReadOam((ushort)oamIndex) - 16;
                if (ly < y || ly >= y + height) continue;
                count++;
            }
            return count;
        }
    }

public class BusRegistersAdapter : IPpuRegisters
{
    private readonly Bus bus;

    public BusRegistersAdapter(Bus bus)
    {
        this.bus = bus;
    }

    public byte SCX => bus.SCX;
    public byte SCY => bus.SCY;

    public byte LY
    {
        get { return bus.LY; }
        set { bus.LY = value; }
    }

    public byte LYC => bus.LYC;
    public byte BGP => bus.BGP;

    public byte OBP0 => bus.OBP0;
    public byte OBP1 => bus.OBP1;

    public byte LCDC => bus.LCDC;

    public PpuMode Mode
    {
        get { return (PpuMode)(bus.PpuMode & 3); }
        set
        {
            bus.PpuMode = (int)value;
            bus.STAT = (byte)((bus.STAT & 0xFC) | (byte)value);
        }
    }

    public bool LcdEnabled
    {
        get { return (bus.LCDC & 0x80) != 0; }
    }

    public byte WX => bus.WX;
    public byte WY => bus.WY;

    public byte STAT
    {
        get { return bus.STAT; }
        set { bus.STAT = value; }
    }
}


    public class BusInterruptsAdapter : IPpuInterrupts
    {
        private readonly Bus bus;
        public BusInterruptsAdapter(Bus bus) { this.bus = bus; }

        public void RequestVBlank() => bus.RequestInterrupt(0);
        public void RequestStat() => bus.RequestInterrupt(1);
    }

    // --------------------------
    // PPU STATE MACHINE
    // --------------------------
    public class PpuStateMachine
    {
        private readonly IPpuRegisters regs;
        private readonly IPpuInterrupts interrupts;
        private readonly PpuTiming timing;
        private readonly ILineSpriteCounter spriteCounter;
        private int cyclesRemaining;
        private bool lcdWasEnabled;
        private int currentDrawingCycles;

        public PpuStateMachine(IPpuRegisters regs, IPpuInterrupts interrupts, PpuTiming timing, ILineSpriteCounter spriteCounter = null)
        {
            this.regs = regs;
            this.interrupts = interrupts;
            this.timing = timing;
            this.spriteCounter = spriteCounter;
            lcdWasEnabled = regs.LcdEnabled;
            if (lcdWasEnabled)
                EnterMode(PpuMode.OamScan);
        }

        public bool Step(int cycles)
        {
            if (HandleLcdEnableTransition())
                return false;

            cyclesRemaining -= cycles;
            if (cyclesRemaining > 0) return false;

            Advance();
            return true;
        }

        private void Advance()
        {
            switch (regs.Mode)
            {
                case PpuMode.OamScan:
                    EnterMode(PpuMode.Drawing);
                    break;
                case PpuMode.Drawing:
                    EnterMode(PpuMode.HBlank);
                    break;
                case PpuMode.HBlank:
                    regs.LY++;
                    UpdateCoincidenceAndMaybeInterrupt();
                    if (regs.LY == 144)
                    {
                        EnterMode(PpuMode.VBlank);
                        interrupts.RequestVBlank();
                        if ((regs.STAT & (1 << 4)) != 0)
                            interrupts.RequestStat();
                    }
                    else
                        EnterMode(PpuMode.OamScan);
                    break;
                case PpuMode.VBlank:
                    regs.LY++;
                    UpdateCoincidenceAndMaybeInterrupt();
                    if (regs.LY > 153)
                    {
                        regs.LY = 0;
                        UpdateCoincidenceAndMaybeInterrupt();
                        EnterMode(PpuMode.OamScan);
                    }
                    break;
            }
        }

        private void EnterMode(PpuMode mode)
        {
            regs.Mode = mode;
            cyclesRemaining = timing.GetModeCycles(mode);

            if (mode == PpuMode.OamScan && spriteCounter != null)
            {
                int count = spriteCounter.CountSpritesOnLine(regs.LY);
                currentDrawingCycles = 172 + count * 6;
            }
            else if (mode == PpuMode.Drawing && currentDrawingCycles > 0)
            {
                cyclesRemaining = currentDrawingCycles;
            }
            else if (mode == PpuMode.HBlank && currentDrawingCycles > 0)
            {
                int hblank = 456 - 80 - currentDrawingCycles;
                cyclesRemaining = hblank > 0 ? hblank : 0;
                currentDrawingCycles = 0;
            }
            regs.STAT = (byte)((regs.STAT & 0xF8) | (byte)mode);

            if (mode == PpuMode.HBlank && (regs.STAT & (1 << 3)) != 0)
                interrupts.RequestStat();
            if (mode == PpuMode.OamScan && (regs.STAT & (1 << 5)) != 0)
                interrupts.RequestStat();
        }

        private void UpdateCoincidenceAndMaybeInterrupt()
        {
            if (regs.LY == regs.LYC)
            {
                regs.STAT |= (byte)(1 << 2);
                if ((regs.STAT & (1 << 6)) != 0)
                    interrupts.RequestStat();
            }
            else
            {
                regs.STAT &= (byte)0xFB;
            }
        }

        private bool HandleLcdEnableTransition()
        {
            bool enabled = regs.LcdEnabled;
            if (!enabled)
            {
                if (lcdWasEnabled)
                {
                    regs.LY = 0;
                    regs.Mode = PpuMode.HBlank;
                    regs.STAT = (byte)((regs.STAT & 0xF8) | 0);
                    UpdateCoincidenceAndMaybeInterrupt();
                    cyclesRemaining = 0;
                }
                lcdWasEnabled = false;
                return true;
            }

            if (!lcdWasEnabled && enabled)
            {
                regs.LY = 0;
                UpdateCoincidenceAndMaybeInterrupt();
                EnterMode(PpuMode.OamScan);
            }

            lcdWasEnabled = true;
            return false;
        }
    }

    // --------------------------
    // BACKGROUND RENDERER
    // --------------------------
    public class BackgroundRenderer : IPpuRenderer
    {
        private readonly IPpuMemory mem;
        private readonly IPpuRegisters regs;
        private readonly IFrameBuffer fb;

        public BackgroundRenderer(IPpuMemory mem, IPpuRegisters regs, IFrameBuffer fb)
        {
            this.mem = mem;
            this.regs = regs;
            this.fb = fb;
        }

        public void RenderScanline(int ly)
        {
            if ((regs.LCDC & 0x01) == 0)
            {
                for (int x = 0; x < 160; x++)
                    fb.SetPixel(x, ly, 0);
                return;
            }
            int scy = regs.SCY;
            int scx = regs.SCX;
            int y = (ly + scy) & 0xFF;
            int tileRow = y / 8;
            int pixelRow = y % 8;

            ushort tileMapBase = (regs.LCDC & 0x08) != 0 ? (ushort)0x1C00 : (ushort)0x1800;
            ushort tileDataBase = (regs.LCDC & 0x10) != 0 ? (ushort)0x0000 : (ushort)0x0800;

            for (int x = 0; x < 160; x++)
            {
                int bgX = (x + scx) & 0xFF;
                int tileCol = bgX / 8;
                int pixelCol = bgX % 8;

                byte tileIndex = mem.ReadVram((ushort)(tileMapBase + tileRow * 32 + tileCol));
                if (tileDataBase == 0x0800)
                    tileIndex = (byte)(unchecked((sbyte)tileIndex) + 128);

                ushort tileAddr = (ushort)(tileDataBase + tileIndex * 16 + pixelRow * 2);
                byte low = mem.ReadVram(tileAddr);
                byte high = mem.ReadVram((ushort)(tileAddr + 1));

                int bit = 7 - pixelCol;
                int colorId = ((high >> bit) & 1) << 1 | ((low >> bit) & 1);
                int color = (regs.BGP >> (colorId * 2)) & 0b11;

                fb.SetPixel(x, ly, color);
            }
        }
    }

    // --------------------------
    // SPRITE RENDERER
    // --------------------------
    public class SpriteRenderer : IPpuRenderer
    {
        private readonly IPpuMemory mem;
        private readonly IPpuRegisters regs;
        private readonly IFrameBuffer fb;

        public SpriteRenderer(IPpuMemory mem, IPpuRegisters regs, IFrameBuffer fb)
        {
            this.mem = mem;
            this.regs = regs;
            this.fb = fb;
        }

        public void RenderScanline(int ly)
        {
            if ((regs.LCDC & 0x02) == 0)
                return;
            int height = (regs.LCDC & 0x04) != 0 ? 16 : 8;

            int[] spriteX = new int[10];
            int[] spriteY = new int[10];
            byte[] spriteTile = new byte[10];
            byte[] spriteFlags = new byte[10];
            int[] spriteIndex = new int[10];
            int count = 0;

            for (int i = 0; i < 40 && count < 10; i++)
            {
                int oamIndex = i * 4;
                int y = mem.ReadOam((ushort)oamIndex) - 16;
                int x = mem.ReadOam((ushort)(oamIndex + 1)) - 8;
                if (ly < y || ly >= y + height) continue;

                spriteY[count] = y;
                spriteX[count] = x;
                spriteTile[count] = mem.ReadOam((ushort)(oamIndex + 2));
                spriteFlags[count] = mem.ReadOam((ushort)(oamIndex + 3));
                spriteIndex[count] = i;
                count++;
            }

            int[] chosenX = new int[160];
            int[] chosenIndex = new int[160];
            byte[] chosenColorId = new byte[160];
            byte[] chosenFlags = new byte[160];
            byte[] chosenPalette = new byte[160];
            bool[] hasSprite = new bool[160];

            for (int s = 0; s < count; s++)
            {
                int y = spriteY[s];
                int x = spriteX[s];
                byte tileIndex = spriteTile[s];
                byte flags = spriteFlags[s];

                int pixelRow = ly - y;
                if ((flags & 0x40) != 0) pixelRow = height - 1 - pixelRow;
                if (height == 16) tileIndex &= 0xFE;

                ushort tileAddr = (ushort)(tileIndex * 16 + pixelRow * 2);
                byte low = mem.ReadVram(tileAddr);
                byte high = mem.ReadVram((ushort)(tileAddr + 1));

                for (int pxl = 0; pxl < 8; pxl++)
                {
                    int px = x + pxl;
                    if (px < 0 || px >= 160) continue;

                    int bit = (flags & 0x20) != 0 ? pxl : 7 - pxl;
                    int colorId = ((high >> bit) & 1) << 1 | ((low >> bit) & 1);
                    if (colorId == 0) continue;

                    if (!hasSprite[px])
                    {
                        hasSprite[px] = true;
                        chosenX[px] = x;
                        chosenIndex[px] = spriteIndex[s];
                        chosenColorId[px] = (byte)colorId;
                        chosenFlags[px] = flags;
                        chosenPalette[px] = (byte)((flags & 0x10) != 0 ? regs.OBP1 : regs.OBP0);
                    }
                    else
                    {
                        int curX = chosenX[px];
                        int curIndex = chosenIndex[px];
                        if (x < curX || (x == curX && spriteIndex[s] < curIndex))
                        {
                            chosenX[px] = x;
                            chosenIndex[px] = spriteIndex[s];
                            chosenColorId[px] = (byte)colorId;
                            chosenFlags[px] = flags;
                            chosenPalette[px] = (byte)((flags & 0x10) != 0 ? regs.OBP1 : regs.OBP0);
                        }
                    }
                }
            }

            bool bgEnabled = (regs.LCDC & 0x01) != 0;
            for (int px = 0; px < 160; px++)
            {
                if (!hasSprite[px]) continue;
                int colorId = chosenColorId[px];
                if (colorId == 0) continue;
                byte palette = chosenPalette[px];
                int color = (palette >> (colorId * 2)) & 0b11;

                int bgColor = fb.GetPixel(px, ly);
                if (bgEnabled && (chosenFlags[px] & 0x80) != 0 && bgColor != 0)
                    continue;

                fb.SetPixel(px, ly, color);
            }
        }
    }

    // --------------------------
    // WINDOW RENDERER
    // --------------------------
    public class WindowRenderer : IPpuRenderer
    {
        private readonly IPpuMemory mem;
        private readonly IPpuRegisters regs;
        private readonly IFrameBuffer fb;
        private int windowLine = 0;
        private int lastLyRendered = -1;

        public WindowRenderer(IPpuMemory mem, IPpuRegisters regs, IFrameBuffer fb)
        {
            this.mem = mem;
            this.regs = regs;
            this.fb = fb;
        }

        public void RenderScanline(int ly)
        {
            if ((regs.LCDC & 0x80) == 0)
            {
                windowLine = 0;
                lastLyRendered = -1;
                return;
            }

            if (ly == 0)
            {
                windowLine = 0;
                lastLyRendered = -1;
            }
            if ((regs.LCDC & 0x01) == 0)
                return;
            if ((regs.LCDC & 0x20) == 0)
                return;
            if (ly < regs.WY) return;

            if (lastLyRendered == ly)
                return;

            int y = windowLine;
            int windowXStart = regs.WX - 7;
            if (windowXStart >= 160)
                return;
            if (windowXStart < 0) windowXStart = 0;
            ushort tileMapBase = (regs.LCDC & 0x40) != 0 ? (ushort)0x1C00 : (ushort)0x1800;
            ushort tileDataBase = (regs.LCDC & 0x10) != 0 ? (ushort)0x0000 : (ushort)0x0800;

            for (int x = 0; x < 160; x++)
            {
                if (x < windowXStart) continue;

                int tileCol = (x - windowXStart) / 8;
                int pixelCol = (x - windowXStart) % 8;
                int tileRow = y / 8;
                int pixelRow = y % 8;

                byte tileIndex = mem.ReadVram((ushort)(tileMapBase + tileRow * 32 + tileCol));
                if (tileDataBase == 0x0800)
                    tileIndex = (byte)(unchecked((sbyte)tileIndex) + 128);

                ushort tileAddr = (ushort)(tileDataBase + tileIndex * 16 + pixelRow * 2);
                byte low = mem.ReadVram(tileAddr);
                byte high = mem.ReadVram((ushort)(tileAddr + 1));

                int bit = 7 - pixelCol;
                int colorId = ((high >> bit) & 1) << 1 | ((low >> bit) & 1);
                int color = (regs.BGP >> (colorId * 2)) & 0b11;

                fb.SetPixel(x, ly, color);
            }

            lastLyRendered = ly;
            windowLine++;
        }
    }

    // --------------------------
    // PPU COORDINATOR
    // --------------------------
    public class Ppu
    {
        public event Action<IFrameBuffer> OnFrameReady;
        private readonly PpuStateMachine stateMachine;
        private readonly IPpuRenderer[] renderers;
        private readonly IFrameBuffer fb;

        public Ppu(PpuStateMachine sm, IFrameBuffer framebuffer, params IPpuRenderer[] renderers)
        {
            stateMachine = sm;
            this.renderers = renderers;
            fb = framebuffer;
        }

        public void Step(int cpuCycles)
        {
            if (stateMachine.Step(cpuCycles))
            {
                var regs = GetRegisters();
                if (regs.Mode == PpuMode.HBlank)
                {
                    int ly = regs.LY;
                    foreach (var r in renderers)
                        r.RenderScanline(ly);

                    if (ly == 143)
                        OnFrameReady?.Invoke(fb);
                }
            }
        }

        public IFrameBuffer GetFrameBuffer() => fb;

        public void DumpFrameBuffer()
        {
            for (int y = 0; y < 144; y++)
            {
                for (int x = 0; x < 160; x++)
                {
                    int c = fb.GetPixel(x, y);
                    Console.Write(c == 0 ? "." : c.ToString());
                }
                Console.WriteLine();
            }
        }

        private IPpuRegisters GetRegisters()
        {
            var regsField = typeof(PpuStateMachine)
                .GetField("regs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (regsField != null && regsField.GetValue(stateMachine) is IPpuRegisters regs)
                return regs;

            throw new InvalidOperationException("Cannot access PPU registers");
        }
    }
}
