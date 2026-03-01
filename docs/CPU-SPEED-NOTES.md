# CPU Speed Notes

Last updated: 2026-03-01

## Baseline (before optimization pass)

Branch: `fix/mem-timing-clock-plumbing`

Command:

```powershell
$core = Get-ChildItem -Recurse src/DmgEmu.Core -Filter *.cs | ForEach-Object { $_.FullName }
mcs $core tools/cpu-bench.cs -out:bench.exe
mono ./bench.exe --seconds=5
```

Results:

- NOP loop
  - steps/s: `7,588,004`
  - cycles/s: `48,563,226`
  - emulated MHz: `48.56`
  - realtime vs DMG: `11.58x`
- Mixed ALU/load loop
  - steps/s: `7,553,675`
  - cycles/s: `70,585,425`
  - emulated MHz: `70.59`
  - realtime vs DMG: `16.83x`

## Progress Runs (same command, 5s)

### Pass 1 (fast-path + timed bus wrapper call overhead cut)

- NOP loop
  - steps/s: `17,456,167`
  - cycles/s: `111,719,472`
  - emulated MHz: `111.72`
  - realtime vs DMG: `26.64x`
- Mixed ALU/load loop
  - steps/s: `7,257,159`
  - cycles/s: `72,226,570`
  - emulated MHz: `72.23`
  - realtime vs DMG: `17.22x`

### Pass 2 (extended hot opcode fast-path)

- NOP loop
  - steps/s: `18,638,824`
  - cycles/s: `119,288,474`
  - emulated MHz: `119.29`
  - realtime vs DMG: `28.44x`
- Mixed ALU/load loop
  - steps/s: `7,169,583`
  - cycles/s: `71,354,877`
  - emulated MHz: `71.35`
  - realtime vs DMG: `17.01x`

### Pass 3 candidate (reverted)

- This iteration regressed benchmark results and was reverted.

### Current (decode table array + trace fast skip)

- NOP loop
  - steps/s: `22,313,202`
  - cycles/s: `142,804,496`
  - emulated MHz: `142.80`
  - realtime vs DMG: `34.05x`
- Mixed ALU/load loop
  - steps/s: `8,111,777`
  - cycles/s: `80,732,084`
  - emulated MHz: `80.73`
  - realtime vs DMG: `19.25x`

## Baseline vs Current Delta

- NOP loop:
  - `48.56 -> 142.80 MHz` (`+94.24 MHz`, `~2.94x`)
- Mixed ALU/load loop:
  - `70.59 -> 80.73 MHz` (`+10.14 MHz`, `~1.14x`)

## Notes

- DMG reference clock is `4,194,304 Hz`.
- These numbers are synthetic CPU-core kernels; they are not end-to-end frame time.
- Use the same command and duration when comparing future optimization changes.
