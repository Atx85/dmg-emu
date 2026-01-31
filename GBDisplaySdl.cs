using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using GB;

public class GBDisplaySdl : IDisplay
{
    private const int Width = 160;
    private const int Height = 144;

    private IntPtr window = IntPtr.Zero;
    private IntPtr renderer = IntPtr.Zero;
    private IntPtr texture = IntPtr.Zero;

    private IFrameBuffer framebuffer;

    private readonly uint[] pixelBuffer = new uint[Width * Height];
    private bool dirty;
    private DateTime lastDebugFrame = DateTime.MinValue;
    private readonly TimeSpan debugFrameTime = TimeSpan.FromSeconds(1.0 / 59.73);

    private static readonly uint[] Palette = new uint[]
    {
        0xFFFFFFFF, // White
        0xFFB3B3B3, // Light gray
        0xFF666666, // Dark gray
        0xFF000000  // Black
    };

    private readonly IInputHandler input;
    private readonly IKeyMapper keyMapper;

    public GBDisplaySdl(int pixelSize = 3, IInputHandler input = null, IKeyMapper keyMapper = null)
    {
        this.input = input;
        this.keyMapper = keyMapper ?? new DefaultKeyMapper();

        if (SDL.SDL_Init(SDL.SDL_INIT_VIDEO) != 0)
            throw new Exception("SDL_Init failed: " + SDL.GetError());

        window = SDL.SDL_CreateWindow(
            "Game Boy Display",
            100, 100,
            Width * pixelSize, Height * pixelSize,
            SDL.SDL_WINDOW_SHOWN);

        if (window == IntPtr.Zero)
            throw new Exception("SDL_CreateWindow failed: " + SDL.GetError());

        renderer = SDL.SDL_CreateRenderer(window, -1, SDL.SDL_RENDERER_ACCELERATED);
        if (renderer == IntPtr.Zero)
            renderer = SDL.SDL_CreateRenderer(window, -1, SDL.SDL_RENDERER_SOFTWARE);

        if (renderer == IntPtr.Zero)
            throw new Exception("SDL_CreateRenderer failed: " + SDL.GetError());

        SDL.SDL_RenderSetLogicalSize(renderer, Width, Height);
        SDL.SDL_RenderSetIntegerScale(renderer, 1);

        texture = SDL.SDL_CreateTexture(
            renderer,
            SDL.SDL_PIXELFORMAT_ARGB8888,
            SDL.SDL_TEXTUREACCESS_STREAMING,
            Width, Height);

        if (texture == IntPtr.Zero)
            throw new Exception("SDL_CreateTexture failed: " + SDL.GetError());
    }

    public void SetFrameBuffer(IFrameBuffer fb)
    {
        framebuffer = fb;
    }

    public void Update(IFrameBuffer fb)
    {
        framebuffer = fb;
        dirty = true;
    }

    public void RunLoop(Action<double> onTick)
    {
        var sw = Stopwatch.StartNew();
        long lastTicks = sw.ElapsedTicks;
        bool running = true;

        while (running)
        {
            while (SDL.SDL_PollEvent(out SDL.SDL_Event e) != 0)
            {
                if (e.type == SDL.SDL_QUIT)
                {
                    running = false;
                    break;
                }
                if (e.type == SDL.SDL_KEYDOWN)
                {
                    if (input != null && keyMapper.TryMapSdlKey(e.key.keysym.sym, out JoypadButton btn))
                        input.SetButton(btn, true);
                }
                else if (e.type == SDL.SDL_KEYUP)
                {
                    if (input != null && keyMapper.TryMapSdlKey(e.key.keysym.sym, out JoypadButton btn))
                        input.SetButton(btn, false);
                }
            }

            long nowTicks = sw.ElapsedTicks;
            double deltaSeconds = (nowTicks - lastTicks) / (double)Stopwatch.Frequency;
            lastTicks = nowTicks;

            onTick(deltaSeconds);

            if (dirty && DateTime.UtcNow - lastDebugFrame >= debugFrameTime)
            {
                Render();
                dirty = false;
                lastDebugFrame = DateTime.UtcNow;
            }

            SDL.SDL_Delay(1);
        }

        Shutdown();
    }

    private void Render()
    {
        if (framebuffer == null)
            return;

        for (int y = 0; y < Height; y++)
        {
            int rowOffset = y * Width;
            for (int x = 0; x < Width; x++)
            {
                int idx = framebuffer.GetPixel(x, y);
                if ((uint)idx > 3) idx = 0;
                pixelBuffer[rowOffset + x] = Palette[idx];
            }
        }

        GCHandle handle = GCHandle.Alloc(pixelBuffer, GCHandleType.Pinned);
        try
        {
            IntPtr pixelsPtr = handle.AddrOfPinnedObject();
            SDL.SDL_UpdateTexture(texture, IntPtr.Zero, pixelsPtr, Width * 4);
        }
        finally
        {
            handle.Free();
        }

        SDL.SDL_RenderClear(renderer);
        SDL.SDL_RenderCopy(renderer, texture, IntPtr.Zero, IntPtr.Zero);
        SDL.SDL_RenderPresent(renderer);
    }

    private void Shutdown()
    {
        if (texture != IntPtr.Zero) SDL.SDL_DestroyTexture(texture);
        if (renderer != IntPtr.Zero) SDL.SDL_DestroyRenderer(renderer);
        if (window != IntPtr.Zero) SDL.SDL_DestroyWindow(window);
        SDL.SDL_Quit();
    }
}
