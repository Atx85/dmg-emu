# DMG-ACID2 Notes

This project uses `dmg-acid2` as a headless visual regression for DMG PPU behavior.

## Local Assets

- ROM path used by default: `gb-test-roms/dmg-acid2.gb`
- Reference image used by default: `test-assets/dmg-acid2/reference-dmg.png`

Checksums:

- ROM (`gb-test-roms/dmg-acid2.gb`, SHA-256):
  `464E14B7D42E7FEEA0B7EDE42BE7071DC88913F75B9FFA444299424B63D1DFF1`
- Reference (`test-assets/dmg-acid2/reference-dmg.png`, SHA-256):
  `CA966D50895C7EFEF05838590D148C2CBFD7FBA57DAB986F25B35B4DA71ABB57`

## What It Validates

`dmg-acid2` validates correctness of many DMG PPU rendering rules using a final frame image comparison. It is not a t-cycle torture test.

Coverage includes:

- Background enable/disable behavior
- Window enable, map select, and internal window line progression
- Signed vs unsigned BG/window tile addressing modes
- Sprite enable, palette selection, X/Y flipping, OBJ-to-BG priority
- Sprite priority ordering when X coordinates differ or match
- 10-sprites-per-scanline limit
- 8x16 sprite tile index bit-0 behavior

## Our Headless Test Mode

`tools/cpu-tests.cs` supports:

- `--acid2`
- `--require-acid2`
- `--acid2-rom=<path>`
- `--acid2-ref=<path>`
- `--acid2-frames=<N>`
- `--acid2-tolerance=<gray-delta>`
- `--acid2-max-diff=<pixels>`
- `--acid2-max-cycles=<N>`

The test runs the ROM headless, captures a framebuffer, quantizes to DMG 4-level grayscale, and compares against the reference image.

## Upstream Project

- Repository: `https://github.com/mattcurrie/dmg-acid2`
- License: MIT
- Copyright: (c) 2020 Matt Currie

The upstream repo has detailed scene/failure explanations if deeper debugging is needed.
