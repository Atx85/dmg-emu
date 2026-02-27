using System;
using System.Runtime.InteropServices;

internal static class SDL
{
    public const uint SDL_INIT_VIDEO = 0x00000020;
    public const uint SDL_WINDOW_SHOWN = 0x00000004;

    public const int SDL_RENDERER_SOFTWARE = 0x00000001;
    public const int SDL_RENDERER_ACCELERATED = 0x00000002;

    public const int SDL_TEXTUREACCESS_STREAMING = 1;
    public const uint SDL_PIXELFORMAT_ARGB8888 = 372645892; // 0x16362004

    public const uint SDL_QUIT = 0x100;
    public const uint SDL_KEYDOWN = 0x300;
    public const uint SDL_KEYUP = 0x301;
    public const int SDLK_F5 = 1073741886;
    public const int SDLK_F9 = 1073741890;
    public const int SDLK_i = (int)'i'; // 105
    public const int SDLK_o = (int)'o'; // 111
    [DllImport("SDL2")]
    public static extern int SDL_Init(uint flags);

    [DllImport("SDL2")]
    public static extern void SDL_Quit();

    [DllImport("SDL2")]
    public static extern IntPtr SDL_CreateWindow(
        [MarshalAs(UnmanagedType.LPStr)] string title,
        int x,
        int y,
        int w,
        int h,
        uint flags);

    [DllImport("SDL2")]
    public static extern IntPtr SDL_CreateRenderer(
        IntPtr window,
        int index,
        int flags);

    [DllImport("SDL2")]
    public static extern void SDL_DestroyRenderer(IntPtr renderer);

    [DllImport("SDL2")]
    public static extern void SDL_DestroyWindow(IntPtr window);

    [DllImport("SDL2")]
    public static extern void SDL_DestroyTexture(IntPtr texture);

    [DllImport("SDL2")]
    public static extern IntPtr SDL_CreateTexture(
        IntPtr renderer,
        uint format,
        int access,
        int w,
        int h);

    [DllImport("SDL2")]
    public static extern int SDL_UpdateTexture(
        IntPtr texture,
        IntPtr rect,
        IntPtr pixels,
        int pitch);

    [DllImport("SDL2")]
    public static extern int SDL_RenderClear(IntPtr renderer);

    [DllImport("SDL2")]
    public static extern int SDL_RenderCopy(
        IntPtr renderer,
        IntPtr texture,
        IntPtr srcrect,
        IntPtr dstrect);

    [DllImport("SDL2")]
    public static extern void SDL_RenderPresent(IntPtr renderer);

    [DllImport("SDL2")]
    public static extern int SDL_RenderSetLogicalSize(IntPtr renderer, int w, int h);

    [DllImport("SDL2")]
    public static extern int SDL_RenderSetIntegerScale(IntPtr renderer, int enable);

    [DllImport("SDL2")]
    public static extern int SDL_PollEvent(out SDL_Event sdlEvent);

    [DllImport("SDL2")]
    public static extern void SDL_Delay(uint ms);

    [DllImport("SDL2")]
    private static extern IntPtr SDL_GetError();

    public static string GetError()
    {
        return Marshal.PtrToStringAnsi(SDL_GetError()) ?? "Unknown SDL error";
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct SDL_Event
    {
        [FieldOffset(0)] public uint type;
        [FieldOffset(0)] public SDL_QuitEvent quit;
        [FieldOffset(0)] public SDL_KeyboardEvent key;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SDL_QuitEvent
    {
        public uint type;
        public uint timestamp;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SDL_KeyboardEvent
    {
        public uint type;
        public uint timestamp;
        public uint windowID;
        public byte state;
        public byte repeat;
        public byte padding2;
        public byte padding3;
        public SDL_Keysym keysym;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SDL_Keysym
    {
        public int scancode;
        public int sym;
        public ushort mod;
        public uint unused;
    }
}
