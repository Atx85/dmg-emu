# GBASM (Simple Game Boy Assembler)

This repo now includes a small two-pass assembler for DMG ROMs:

- Source: `tools/gbasm.cs`
- Wrapper script: `./gbasm`

## Quick Start

1. Assemble hello world:

```bash
./gbasm asm/hello_serial.asm roms/hello_serial.gb
```

2. Run headless (prints serial output):

```bash
mono program.exe --headless roms/hello_serial.gb
```

3. Run with display:

```bash
mono program.exe roms/hello_serial.gb
```

## Source Syntax

- Comments: `; comment`
- Labels: `name:`
- Constants: `NAME EQU expression`
- Numbers:
  - Hex: `$12` or `0x12`
  - Binary: `%1010`
  - Decimal: `42`
  - Char: `'A'`
- Strings in `DB`: `"HELLO"`, supports escapes (`\n`, `\r`, `\t`, `\\`, `\"`, `\'`, `\0`)

## Directives

- `ORG expr` set current address
- `DB a, b, "text"` emit bytes
- `DW expr, expr` emit 16-bit little-endian words
- `DS count` reserve/fill bytes with 0

## Supported Instructions

### No operand

`NOP RLCA RRCA RLA RRA DAA CPL SCF CCF HALT STOP DI EI RET RETI`

### Control flow

- `JP nn`
- `JP (HL)`
- `JP NZ,nn` / `JP Z,nn` / `JP NC,nn` / `JP C,nn`
- `JR e`
- `JR NZ,e` / `JR Z,e` / `JR NC,e` / `JR C,e`
- `CALL nn`
- `CALL NZ,nn` / `CALL Z,nn` / `CALL NC,nn` / `CALL C,nn`
- `RET` and conditional `RET NZ/Z/NC/C`
- `RST n` where `n` is `0x00..0x38` step 8

### Loads

- `LD r8,r8`
- `LD r8,n8`
- `LD (HL),n8`
- `LD rr,n16` where `rr` is `BC/DE/HL/SP`
- `LD SP,HL`
- `LD HL,SP+e8`
- `LD (BC),A` / `LD (DE),A` / `LD A,(BC)` / `LD A,(DE)`
- `LD (HL+),A` / `LD (HL-),A` / `LD A,(HL+)` / `LD A,(HL-)`
- `LD (a16),A` / `LD A,(a16)`
- `LD (a16),SP`
- `LDH (a8),A` / `LDH A,(a8)`
- `LD (C),A` / `LD A,(C)`

### Arithmetic/logic

- `INC r8` / `DEC r8`
- `INC rr` / `DEC rr` (`BC/DE/HL/SP`)
- `ADD A,x` / `ADC A,x` / `SUB x` / `SBC A,x` / `AND x` / `XOR x` / `OR x` / `CP x`
  - where `x` is `r8`, `(HL)`, or immediate
- `ADD HL,rr`
- `ADD SP,e8`

### Stack

- `PUSH BC/DE/HL/AF`
- `POP BC/DE/HL/AF`

### CB-prefixed ops

- `RLC/RRC/RL/RR/SLA/SRA/SWAP/SRL r8`
- `BIT n,r8`
- `RES n,r8`
- `SET n,r8`

`r8` can be `A/B/C/D/E/H/L/(HL)`.

## Output ROM Size

Default output size is 32768 bytes.

You can override:

```bash
./gbasm in.asm out.gb --size=65536
```

## Example

See: `asm/hello_serial.asm`
