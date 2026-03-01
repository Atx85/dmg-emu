# dmg-emu

This is a Game Boy (DMG) emulator written in C# using Mono/GTK/SDL.
The repository has been reorganised into a more conventional .NET layout:

```
src/
  DmgEmu.Core/        # core emulation logic (class library)
  DmgEmu.Frontend/    # display and input wrappers (GTK + SDL)
  DmgEmu.Cli/         # console application entrypoint
tools/                # helper tools (assembler, etc.)
scripts/              # build/run helper scripts
roms/                 # sample ROMs and tests
asm/                  # example asm sources
```

## Building & Running

A small helper script is provided at `scripts/run`. It uses `mcs` and the
Mono runtime, preserving the behaviour of the original project:

```sh
./scripts/run [options] [rom.gb]
```

CPU benchmark helper:

```sh
./scripts/bench --bench-seconds=5
```

CPU test suite (unit + ROM smoke gate):

```sh
./scripts/test-cpu
```

On PowerShell:

```powershell
./scripts/test-cpu.ps1
```

CPU coverage checklist/strategy is documented in `docs/CPU-COVERAGE.md`.
Ongoing coverage status notes are in `docs/TEST-COVERAGE-NOTES.md`.
Use `--require-mem-timing` to make the `mem_timing` suite a hard gate.

Optional `dmg-acid2` headless visual check:

```sh
./scripts/test-cpu --acid2 --acid2-rom=./gb-test-roms/dmg-acid2.gb --acid2-ref=./test-assets/dmg-acid2/reference-dmg.png
```

Reference notes are stored in `docs/DMG-ACID2.md`.

> The emulator accepts the same command-line arguments as the legacy
> `program.exe` entrypoint (shown here with their defaults):
>
> ```text
> --headless            run without opening a display window
> --max-cycles=<N>      stop after N CPU cycles (default 20_000_000)
> --save-state=<path>   write a save state on exit
> --load-state=<path>   load a state before starting
> --gtk                 prefer the GTK display backend
> --sdl                 prefer the SDL display backend
> <rom.gb>              path to the ROM file (default: roms/pkred.gb)
> ```
>
> Hotkeys available while running:
> * F5 / `i` – save state (if `--save-state` given)
> * F9 / `o` – load state (if `--load-state` given)
> * Tab – toggle fast‑forward speed
> * Additional key mappings are handled by the frontend (`GBDisplay` /
>   `GBDisplaySdl`).


### Compilation options
The script accepts the same flags that were used in the original root-level
`run` helper.  Internally it simply invokes `mcs` on all `src/**/*.cs` files
with these parameters:

* `-define:GTK` — compile with the GTK# symbols enabled (used by the GTK
  frontend).
* `-pkg:gtk-sharp-3.0` — pulls in the GTK# assemblies at compile time.

When building manually you can pass additional `mcs` options as needed, for
example you might use `-debug` or `-optimize+` or supply custom
`-reference:` arguments.  The same flags are accepted by the `check-parity`
script (and by `scripts/run` via the `MCSFLAGS` environment variable).

You can also compile the individual projects with `msbuild` or a .NET SDK if
available; `*.csproj` files are stored under `src/*` for that purpose.

## Project Structure

- **DmgEmu.Core** contains the emulator components (CPU, PPU, bus, etc.)
  namespaced under `DmgEmu.Core`.
- **DmgEmu.Frontend** holds the GTK and SDL display implementations.
- **DmgEmu.Cli** is the command‑line driver that wires everything together.

The code has been refactored to use proper C# namespaces rather than the
previous global `GB` namespace, and source files now live in folders that
mirror their namespaces.

## Notes

- An old backup file `GBDisplayGTK2.cs_` remains in the repository but is no
  longer part of the build.
- The `run_old` script has been kept for reference but is superseded by
  `scripts/run`.

## License

This project is licensed under **GPL-2.0-only** (GNU GPL v2 only), similar to
the Linux kernel. See `LICENSE`.

Enjoy developing!
