using System;
using System.IO;
using System.Collections.Generic;
namespace DmgEmu.Core {

public enum InputKeySource
{
    Sdl = 1,
    Gtk = 2
}

public class Gameboy
{
    public Bus bus;
    public Ppu ppu;
    public Cpu2Structured cpu2Structured;
    private Cpu2StructuredCoreAdapter cpu2StructuredAdapter;
    private readonly ICpuCore cpuCore;
    private readonly Dictionary<long, Action> hotkeyBindings = new Dictionary<long, Action>();
    private readonly Dictionary<long, JoypadButton> buttonBindings = new Dictionary<long, JoypadButton>();
    public CpuBackend Backend { get; }

    public Gameboy(CpuBackend cpuBackend = CpuBackend.Cpu2Structured)
    {
        Backend = cpuBackend;
        var cart = new Cartridge();
        bus = new Bus(ref cart);
        if (cpuBackend == CpuBackend.Cpu2Structured)
        {
            var adapter = new Cpu2StructuredCoreAdapter(bus);
            cpu2StructuredAdapter = adapter;
            cpu2Structured = adapter.Inner;
            cpuCore = adapter;
        }
        else
        {
            throw new InvalidOperationException("Only Cpu2Structured backend is supported.");
        }
        
        var fb = new FrameBuffer();
        var regs = new BusRegistersAdapter(bus);
        var ints = new BusInterruptsAdapter(bus);
        var timing = new PpuTiming();
        var mem = new BusMemoryAdapter(bus);
        var counter = new SpriteCounter(mem, regs);
        var sm = new PpuStateMachine(regs, ints, timing, counter);

        var bg = new BackgroundRenderer(mem, regs, fb);
        var window = new WindowRenderer(mem, regs, fb);
        var sprite = new SpriteRenderer(mem, regs, fb);

        ppu = new Ppu(sm, fb, bg, window, sprite);
        // ppu = new Ppu(bus);
        SetDefaultButtonBindings();
    }

    public Gameboy(string path, CpuBackend cpuBackend = CpuBackend.Cpu2Structured)
    {
        Backend = cpuBackend;
        var cart = new Cartridge();
        cart.load(path);
        bus = new Bus(ref cart);
        if (cpuBackend == CpuBackend.Cpu2Structured)
        {
            var adapter = new Cpu2StructuredCoreAdapter(bus);
            cpu2StructuredAdapter = adapter;
            cpu2Structured = adapter.Inner;
            cpuCore = adapter;
        }
        else
        {
            throw new InvalidOperationException("Only Cpu2Structured backend is supported.");
        }
        var fb = new FrameBuffer();
        var regs = new BusRegistersAdapter(bus);
        var ints = new BusInterruptsAdapter(bus);
        var timing = new PpuTiming();
        var mem = new BusMemoryAdapter(bus);
        var counter = new SpriteCounter(mem, regs);
        var sm = new PpuStateMachine(regs, ints, timing, counter);

        var bg = new BackgroundRenderer(mem, regs, fb);
        var window = new WindowRenderer(mem, regs, fb);
        var sprite = new SpriteRenderer(mem, regs, fb);

        ppu = new Ppu(sm, fb, bg, window, sprite);
        SetDefaultButtonBindings();
    }

    /// <summary>
    /// Tick the Game Boy for n CPU cycles
    /// </summary>

    public void TickCycles(int cycles)
    {
        int remaining = cycles;

        while (remaining > 0)
        {
            int used = cpuCore.Step();
            remaining -= used;

            for (int i = 0; i < used; i++)
            {
                ppu.Step(1);
                bus.timer.Tick(1);
                bus.TickDma(1);
            }
        }
    }

    public EmulatorState SaveState()
    {
        var s = new EmulatorState
        {
            CpuBackend = Backend,
            Cartridge = bus.cartridge.GetState(),
            Bus = bus.GetState(),
            Ppu = ppu.GetState()
        };

        if (Backend == CpuBackend.Cpu2Structured && cpu2StructuredAdapter != null) s.Cpu2Structured = cpu2StructuredAdapter.GetState();
        return s;
    }

    public void LoadState(EmulatorState s)
    {
        if (s == null) throw new ArgumentNullException(nameof(s));
        if (s.CpuBackend != Backend) throw new InvalidOperationException("Save-state CPU backend mismatch");

        bus.cartridge.SetState(s.Cartridge);
        bus.SetState(s.Bus);
        ppu.SetState(s.Ppu);

        if (Backend == CpuBackend.Cpu2Structured && cpu2StructuredAdapter != null) cpu2StructuredAdapter.SetState(s.Cpu2Structured);
    }

    private static long MakeBindingKey(InputKeySource source, int keyCode)
    {
        return ((long)(int)source << 32) | (uint)keyCode;
    }

    public void BindHotkey(InputKeySource source, int keyCode, Action action)
    {
        long bindingKey = MakeBindingKey(source, keyCode);
        if (action == null) hotkeyBindings.Remove(bindingKey);
        else hotkeyBindings[bindingKey] = action;
    }

    public bool HandleHotkey(InputKeySource source, int keyCode)
    {
        Action action;
        if (hotkeyBindings.TryGetValue(MakeBindingKey(source, keyCode), out action))
        {
            action();
            return true;
        }
        return false;
    }

    // Backward compatible SDL hotkey API.
    public void BindHotkey(int keySym, Action action)
    {
        BindHotkey(InputKeySource.Sdl, keySym, action);
    }

    // Backward compatible SDL hotkey API.
    public bool HandleHotkey(int keySym)
    {
        return HandleHotkey(InputKeySource.Sdl, keySym);
    }

    public void BindButton(InputKeySource source, int keyCode, JoypadButton button)
    {
        buttonBindings[MakeBindingKey(source, keyCode)] = button;
    }

    public void UnbindButton(InputKeySource source, int keyCode)
    {
        buttonBindings.Remove(MakeBindingKey(source, keyCode));
    }

    public void ClearButtonBindings(InputKeySource source)
    {
        var toRemove = new List<long>();
        foreach (var kvp in buttonBindings)
        {
            if ((int)(kvp.Key >> 32) == (int)source) toRemove.Add(kvp.Key);
        }
        foreach (var key in toRemove) buttonBindings.Remove(key);
    }

    public void ClearAllButtonBindings()
    {
        buttonBindings.Clear();
    }

    public void SetDefaultButtonBindings()
    {
        SetDefaultSdlButtonBindings();
        SetDefaultGtkButtonBindings();
    }

    public void SetDefaultSdlButtonBindings()
    {
        ClearButtonBindings(InputKeySource.Sdl);
        BindButton(InputKeySource.Sdl, 1073741903, JoypadButton.Right); // SDLK_RIGHT
        BindButton(InputKeySource.Sdl, 1073741904, JoypadButton.Left);  // SDLK_LEFT
        BindButton(InputKeySource.Sdl, 1073741905, JoypadButton.Down);  // SDLK_DOWN
        BindButton(InputKeySource.Sdl, 1073741906, JoypadButton.Up);    // SDLK_UP
        BindButton(InputKeySource.Sdl, 122, JoypadButton.A);            // z
        BindButton(InputKeySource.Sdl, 120, JoypadButton.B);            // x
        BindButton(InputKeySource.Sdl, 13, JoypadButton.Start);         // Enter
        BindButton(InputKeySource.Sdl, 1073742053, JoypadButton.Select); // RShift
        BindButton(InputKeySource.Sdl, 1073742049, JoypadButton.Select); // LShift
    }

    public void SetDefaultGtkButtonBindings()
    {
        ClearButtonBindings(InputKeySource.Gtk);
        BindButton(InputKeySource.Gtk, 65363, JoypadButton.Right); // Gdk.Key.Right
        BindButton(InputKeySource.Gtk, 65361, JoypadButton.Left);  // Gdk.Key.Left
        BindButton(InputKeySource.Gtk, 65364, JoypadButton.Down);  // Gdk.Key.Down
        BindButton(InputKeySource.Gtk, 65362, JoypadButton.Up);    // Gdk.Key.Up
        BindButton(InputKeySource.Gtk, 122, JoypadButton.A);       // z
        BindButton(InputKeySource.Gtk, 120, JoypadButton.B);       // x
        BindButton(InputKeySource.Gtk, 65293, JoypadButton.Start); // Return
        BindButton(InputKeySource.Gtk, 65505, JoypadButton.Select); // Shift_L
        BindButton(InputKeySource.Gtk, 65506, JoypadButton.Select); // Shift_R
    }

    public void BindSdlButton(int keySym, JoypadButton button)
    {
        BindButton(InputKeySource.Sdl, keySym, button);
    }

    public void UnbindSdlButton(int keySym)
    {
        UnbindButton(InputKeySource.Sdl, keySym);
    }

    public void ClearSdlButtonBindings()
    {
        ClearButtonBindings(InputKeySource.Sdl);
    }

    public void BindGtkButton(uint keyVal, JoypadButton button)
    {
        BindButton(InputKeySource.Gtk, unchecked((int)keyVal), button);
    }

    public void UnbindGtkButton(uint keyVal)
    {
        UnbindButton(InputKeySource.Gtk, unchecked((int)keyVal));
    }

    public void ClearGtkButtonBindings()
    {
        ClearButtonBindings(InputKeySource.Gtk);
    }

    public bool HandleKey(InputKeySource source, int keyCode, bool pressed)
    {
        if (pressed && HandleHotkey(source, keyCode))
            return true;

        JoypadButton button;
        if (buttonBindings.TryGetValue(MakeBindingKey(source, keyCode), out button))
        {
            bus.Joypad.SetButton(button, pressed);
            return true;
        }
        return false;
    }

    // Backward compatible SDL key path.
    public bool HandleSdlKey(int keySym, bool pressed)
    {
        return HandleKey(InputKeySource.Sdl, keySym, pressed);
    }

    public bool HandleGtkKey(uint keyVal, bool pressed)
    {
        return HandleKey(InputKeySource.Gtk, unchecked((int)keyVal), pressed);
    }

}

}
