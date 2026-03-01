# CPU Coverage Strategy

This project does not target literal source line/branch coverage as the primary goal.
The target is behavioral coverage across opcode families, timing branches, and hardware-visible semantics.

## Current Unit Coverage (`tools/cpu-tests.cs`)

- Interrupt semantics:
  - HALT exit when interrupt pending with `IME=0`
  - Interrupt service with `IME=1`
  - Interrupt priority (lowest pending bit wins)
  - `EI` delayed enable and `RETI`
- Control-flow timing:
  - `JR`/`JP` conditional taken vs not taken cycles
  - `CALL`/`RET` and conditional `CALL`/`RET` cycle paths
- ALU/flags:
  - `DAA` and `ADC` edge cases
  - `ADD SP,e8` and `LD HL,SP+e8` signed/flag behavior
- CB-prefixed family:
  - register vs `(HL)` cycle behavior
  - `BIT`/`SET`/`RES` behavior
- Opcode-family matrix checks:
  - full `LD r,r` matrix (except `HALT`) cycle + dataflow checks
  - full `ALU A,r` matrix cycle checks
- Opcode side-effect oracle:
  - representative table-driven opcode behavior assertions
- Stack/load-store paths:
  - `PUSH/POP AF` low-flag nibble masking
  - `LDH`/`FF00+C`/`(a16)` load-store paths
- CPU state behavior:
  - `STOP` state/cycle behavior
  - fixed cycle-trace regression
  - deterministic trace digest anchor

## ROM Regression Gates

- CPU gate: `cpu_instrs/individual/*.gb`
- Blargg suite adapters:
  - `mem_timing/individual`
  - `mem_timing-2/rom_singles`
  - `oam_bug/rom_singles`
  - `interrupt_time`
  - `halt_bug`
- Optional visual PPU gate: `dmg-acid2` image diff

## Next Expansion Candidates

- Add a trace-oracle harness that executes all opcodes in controlled preconditions and validates:
  - post-state invariants
  - cycles
  - memory side effects
- Add targeted tests for:
  - illegal opcode behavior policy
  - DMA/timer interactions that impact observable CPU timing
  - HALT bug edge cases with mixed IF/IE timing transitions
