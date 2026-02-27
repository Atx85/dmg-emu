# AGENTS.md

## Project Snapshot
- Emulator: DMG/Game Boy in C# (Mono `mcs` workflow).
- CPU core: `Cpu2Structured` is now the only runtime CPU.
- Removed: `Cpu.cs`, `Cpu2.cs`.
- Main entry: `program.cs`.

## CPU Architecture
- CPU backend enum has only `Cpu2Structured` in `CpuContract.cs`.
- `Gameboy` instantiates only structured CPU and throws for other backends.
- Interrupt/HALT behavior fix applied in `Cpu2Structured.TryHandleInterrupt`:
  - if interrupt pending and `IME=0`, HALT exits without servicing.

## Input/Keybindings
- Unified key handling is owned by `Gameboy`.
- Source-aware binding via `InputKeySource` (`Sdl`, `Gtk`).
- SDL and GTK displays both route key events through `Gameboy` handlers.
- Fast-forward toggle: `Tab` switches `1x <-> 3x` in `program.cs`.
- Save/load hotkeys:
  - `F5` save state
  - `F9` load state

## Save States
- Save format still contains legacy CPU snapshot fields for wire compatibility.
- Runtime only supports `Cpu2Structured` backend; loading legacy-backend states will fail backend mismatch.

## Assembler
- Tool: `./gbasm` wrapper, source at `tools/gbasm.cs`.
- Docs: `ASM.md`.
- Example ROM source: `asm/hello_serial.asm`.
- Scope: useful custom assembler, not RGBDS-compatible/full Pokémon build toolchain.

## Build & Run
- SDL build/run:
  - `./run-sdl [rom.gb] [--headless] [--max-cycles=N] [--save-state=...] [--load-state=...]`
- GTK build/run:
  - `./run [rom.gb] ...`
- Direct compile (SDL):
  - `mcs program.cs IDisplay.cs Input.cs Joypad.cs Timer.cs Bus.cs Ppu.cs Dbg.cs Cartridge.cs State.cs Gameboy.cs CpuContract.cs Cpu2Structured.cs GBDisplaySdl.cs Sdl.cs`

## CPU Test Gate
- Command: `./check-parity`
- Current behavior: structured-only CPU ROM gate over `roms/cpu_instrs/individual/*.gb`.
- Expected: all ROMs report `Passed`.

## Redundant Files
- Moved to `redundant/`:
  - `.DS_Store`
  - `Bus.bak.cs`, `GBDisplay.bak.cs`, `Ppu.bak.cs`, `program.bak.cs`
  - `out.log`, `program.exe`, `program.exe.config`

## Known Caveats
- `02-interrupts` is sensitive to HALT/interrupt semantics; keep regression check.
- `Cpu2Structured.cs` currently has non-blocking warning for unused `cbTable` field.

## Suggested Next Work
1. Add a debugger (breakpoints, step, register/memory dump, trace toggle).
2. Expand assembler incrementally (not full RGBDS unless explicitly desired).
3. Add PPU visual regression/smoke checks in automated script.
