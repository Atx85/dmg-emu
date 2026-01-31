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
        private int cyclesRemaining;

        public PpuStateMachine(IPpuRegisters regs, IPpuInterrupts interrupts, PpuTiming timing)
        {
            this.regs = regs;
            this.interrupts = interrupts;
            this.timing = timing;
            EnterMode(PpuMode.OamScan);
        }

        public bool Step(int cycles)
        {
            if (!regs.LcdEnabled) return false;

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
                    if (regs.LY > 153)
                    {
                        regs.LY = 0;
                        EnterMode(PpuMode.OamScan);
                    }
                    break;
            }

            // Update STAT mode bits (0-1: mode flag)
            regs.STAT = (byte)((regs.STAT & 0xF8) | (byte)regs.Mode);

            // Coincidence flag
            if (regs.LY == regs.LYC)
            {
                regs.STAT |= (byte)(1 << 2); // cast to byte
                if ((regs.STAT & (1 << 6)) != 0)
                    interrupts.RequestStat();
            }
            else
            {
                regs.STAT &= (byte)0xFB;
            }

            // Mode 0 interrupt
            if (regs.Mode == PpuMode.HBlank && (regs.STAT & (1 << 3)) != 0)
                interrupts.RequestStat();

            // Mode 2 interrupt
            if (regs.Mode == PpuMode.OamScan && (regs.STAT & (1 << 5)) != 0)
                interrupts.RequestStat();
        }

        private void EnterMode(PpuMode mode)
        {
            regs.Mode = mode;
            cyclesRemaining = timing.GetModeCycles(mode);
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
            int spritesDrawn = 0;

            for (int i = 0; i < 40 && spritesDrawn < 10; i++)
            {
                int oamIndex = i * 4;
                int spriteY = mem.ReadOam((ushort)oamIndex) - 16;
                int spriteX = mem.ReadOam((ushort)(oamIndex + 1)) - 8;
                byte tileIndex = mem.ReadOam((ushort)(oamIndex + 2));
                byte flags = mem.ReadOam((ushort)(oamIndex + 3));

                int height = (regs.LCDC & 0x04) != 0 ? 16 : 8;

                if (ly < spriteY || ly >= spriteY + height) continue;
                spritesDrawn++;

                int pixelRow = ly - spriteY;

                if ((flags & 0x40) != 0) pixelRow = height - 1 - pixelRow;
                if (height == 16) tileIndex &= 0xFE;

                ushort tileAddr = (ushort)(tileIndex * 16 + pixelRow * 2);
                byte low = mem.ReadVram(tileAddr);
                byte high = mem.ReadVram((ushort)(tileAddr + 1));

                for (int x = 0; x < 8; x++)
                {
                    int px = spriteX + x;
                    if (px < 0 || px >= 160) continue;

                    int bit = (flags & 0x20) != 0 ? x : 7 - x;
                    int colorId = ((high >> bit) & 1) << 1 | ((low >> bit) & 1);
                    if (colorId == 0) continue;

                    //byte palette = (flags & 0x10) != 0 ? (byte)0xFF : (byte)0xFC;
                    byte palette = (flags & 0x10) != 0 ? regs.OBP1 : regs.OBP0;
                    int color = (palette >> (colorId * 2)) & 0b11;

                    int bgColor = fb.GetPixel(px, ly);
                    if ((flags & 0x80) != 0 && bgColor != 0) continue;

                    fb.SetPixel(px, ly, color);
                }
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

        public WindowRenderer(IPpuMemory mem, IPpuRegisters regs, IFrameBuffer fb)
        {
            this.mem = mem;
            this.regs = regs;
            this.fb = fb;
        }

        public void RenderScanline(int ly)
        {
            if (ly < regs.WY) return;

            int y = ly - regs.WY;
            ushort tileMapBase = (regs.LCDC & 0x40) != 0 ? (ushort)0x1C00 : (ushort)0x1800;
            ushort tileDataBase = (regs.LCDC & 0x10) != 0 ? (ushort)0x0000 : (ushort)0x0800;

            for (int x = 0; x < 160; x++)
            {
                if (x + 7 < regs.WX) continue;

                int tileCol = (x - (regs.WX - 7)) / 8;
                int pixelCol = (x - (regs.WX - 7)) % 8;
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

