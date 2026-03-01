# Test Coverage Notes

Last updated: 2026-03-01

## Scope

This file tracks practical emulator test coverage (behavior/timing/visual), not only line or branch percentages.

## Current Coverage

### CPU Unit Tests

Runner: `tools/cpu-tests.cs`

- Count: 17 unit tests
- Count: 19 unit tests
- Areas covered:
  - Interrupt behavior (`IME`, `EI`, `RETI`, interrupt priority, HALT exit semantics)
  - Control-flow timing paths (`JR`/`JP`/`CALL`/`RET`, taken + not taken)
  - ALU/flag edge cases (`DAA`, `ADC`, signed SP math flags)
  - CB-prefixed operations (`RLC`, `BIT`/`RES`/`SET`, register vs `(HL)` cycles)
  - Opcode-family matrix checks (`LD r,r`, `ALU A,r`)
  - Stack/load-store behavior (`PUSH/POP AF`, `LDH`, `(FF00+C)`, `(a16)`)
  - STOP behavior + cycle trace regression
  - deterministic trace digest regression anchor

### CPU ROM Conformance

- `gb-test-roms/cpu_instrs/individual/*.gb`: passing (11/11)

### Blargg Suite Adapters

- `mem_timing/individual/*.gb` (first-class suite, can be required with `--require-mem-timing`)
- `mem_timing-2/rom_singles/*.gb`
- `oam_bug/rom_singles/*.gb`
- `interrupt_time/*.gb`
- `halt_bug.gb`

Result detection now uses:
- serial text (`Passed` / `Failed`)
- Blargg memory protocol (`$A000` status + `$A001..$A003 = DE B0 61`)

### Visual PPU Regression

- `dmg-acid2` headless image compare: passing
- Baseline reference: `test-assets/dmg-acid2/reference-dmg.png`

## Known Gaps

- No numeric line/branch coverage report in CI yet.
- `mem_timing` suite is currently failing and not yet promoted to a required gate.
- `interrupt_time` and `halt_bug` still produce `no-result` under current adapters.
- No dedicated automated full-system frame-time/perf regression gate yet.

## Recommended Next Additions

1. Add a lightweight opcode execution matrix report (opcode families vs tested paths).
2. Promote `mem_timing` to required gate after fixes.
3. Add per-suite result adapters for non-serial ROMs (where applicable).
4. Add optional coverage tooling output (line/branch %) for `DmgEmu.Core`.

## Commands

- Full local gate (unit + CPU ROM + blargg suites + optional acid2):
  - PowerShell: `./scripts/test-cpu.ps1 --acid2`
  - Bash: `./scripts/test-cpu --acid2`
- Require `mem_timing` to pass (CI-ready once fixed):
  - PowerShell: `./scripts/test-cpu.ps1 --require-mem-timing --acid2`
  - Bash: `./scripts/test-cpu --require-mem-timing --acid2`
