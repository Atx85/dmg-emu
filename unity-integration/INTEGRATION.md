# Unity Integration Guide

This folder contains example code to integrate the DMG emulator into a Unity project.

## Contents

- **UnityDisplay.cs** – Implements `IDisplay` and renders framebuffer to a `Texture2D`
- **EmulatorController.cs** – MonoBehaviour that manages emulator lifecycle and input

## Setup Instructions

### 1. Copy the Core Emulator

Copy all files from `src/DmgEmu.Core/` into your Unity project:

```
Assets/
  Scripts/
    DmgEmu/
      Core/
        *.cs  (Bus, Ppu, Cpu2Structured, etc.)
```

### 2. Add the Unity Frontend

Copy this folder's C# files into your project:

```
Assets/
  Scripts/
    DmgEmu/
      Frontend/
        Unity/
          UnityDisplay.cs
          EmulatorController.cs
```

Make sure the namespace matches: `namespace DmgEmu.Frontend.Unity`

### 3. Set Up the Scene

1. **Create a Quad** in your scene (or use any GameObject with a Renderer).
2. **Add the EmulatorController** as a component to an empty GameObject.
3. **In the Inspector:**
   - Set **Rom Path** to point to your `.gb` file (e.g., `Assets/Roms/game.gb`)
   - Drag the Quad's Renderer into the **Display Renderer** field
4. **Play** – the emulator will start and render to the quad.

### 4. Input Mapping

The default key mapping in `EmulatorController.HandleInput()`:

| Game Boy Button | Unity Key |
|---|---|
| D-Pad Right | Right Arrow |
| D-Pad Left | Left Arrow |
| D-Pad Up | Up Arrow |
| D-Pad Down | Down Arrow |
| A | Z |
| B | X |
| Select | C |
| Start | V |
| Speed Toggle | Tab |

Edit the mapping in `HandleInput()` to suit your game's input scheme.

### 5. (Optional) Add Save/Load State

The emulator supports state serialization via `SaveState()` / `LoadState()`. You can extend
`EmulatorController` to add hotkeys (e.g., F5 to save, F9 to load).

## Notes

- The emulator runs at ~4.19 MHz and ticks every frame based on `Time.deltaTime`.
- Fast-forward is hardcoded to 3x; adjust `speedMultiplier` in the code as needed.
- The texture is **pixel-perfect** (FilterMode.Point) to maintain the classic look.
- Ensure your ROM file is readable from the path you specify.

## Troubleshooting

**"Gameboy type not found"** – Make sure you've copied all of `DmgEmu.Core` and that namespaces match.

**Texture not showing** – Verify the Renderer is assigned and its material has a shader that uses the texture.

**No input** – Check that the key mappings in `HandleInput()` match your input scheme.

---

Happy emulating! 🎮
