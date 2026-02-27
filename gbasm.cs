using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;

public static class Gbasm
{
    private static readonly Dictionary<string, byte> NoArgOpcodes = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase)
    {
        ["NOP"] = 0x00,
        ["RLCA"] = 0x07,
        ["RRCA"] = 0x0F,
        ["RLA"] = 0x17,
        ["RRA"] = 0x1F,
        ["DAA"] = 0x27,
        ["CPL"] = 0x2F,
        ["SCF"] = 0x37,
        ["CCF"] = 0x3F,
        ["HALT"] = 0x76,
        ["RET"] = 0xC9,
        ["RETI"] = 0xD9,
        ["DI"] = 0xF3,
        ["EI"] = 0xFB,
    };

    private static readonly Dictionary<string, int> Reg8 = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["B"] = 0, ["C"] = 1, ["D"] = 2, ["E"] = 3, ["H"] = 4, ["L"] = 5, ["(HL)"] = 6, ["A"] = 7
    };

    private static readonly Dictionary<string, int> Reg16 = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["BC"] = 0, ["DE"] = 1, ["HL"] = 2, ["SP"] = 3
    };

    private static readonly Dictionary<string, int> Reg16Stack = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["BC"] = 0, ["DE"] = 1, ["HL"] = 2, ["AF"] = 3
    };

    private static readonly Dictionary<string, int> CondCodes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["NZ"] = 0, ["Z"] = 1, ["NC"] = 2, ["C"] = 3
    };

    private sealed class AsmException : Exception
    {
        public int LineNo { get; }
        public AsmException(int lineNo, string message) : base($"Line {lineNo}: {message}") { LineNo = lineNo; }
    }

    private sealed class Context
    {
        public readonly Dictionary<string, int> Labels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, int> Constants = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<int, byte> Output = new Dictionary<int, byte>();
        public int Pc;
        public int MaxAddress;
        public bool Emit;
    }

    public static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: gbasm <input.asm> <output.gb> [--size=32768]");
            return 2;
        }

        string inputPath = args[0];
        string outputPath = args[1];
        int outputSize = 32768;

        foreach (var arg in args.Skip(2))
        {
            if (arg.StartsWith("--size=", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(arg.Substring("--size=".Length), out outputSize) || outputSize <= 0)
                {
                    Console.Error.WriteLine("Invalid --size value.");
                    return 2;
                }
            }
        }

        try
        {
            var lines = File.ReadAllLines(inputPath);
            var ctx = new Context();

            // Pass 1: labels/sizes.
            ctx.Emit = false;
            Assemble(lines, ctx);

            // Pass 2: emit bytes.
            ctx.Pc = 0;
            ctx.MaxAddress = 0;
            ctx.Emit = true;
            Assemble(lines, ctx);

            int finalSize = Math.Max(outputSize, ctx.MaxAddress + 1);
            byte[] rom = new byte[finalSize];
            foreach (var kv in ctx.Output)
                rom[kv.Key] = kv.Value;

            File.WriteAllBytes(outputPath, rom);
            Console.WriteLine($"Assembled {inputPath} -> {outputPath} ({finalSize} bytes)");
            return 0;
        }
        catch (AsmException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void Assemble(string[] lines, Context ctx)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            int lineNo = i + 1;
            string line = StripComment(lines[i]).Trim();
            if (line.Length == 0) continue;

            // Label support: label: <optional instruction/directive>
            while (true)
            {
                int colon = line.IndexOf(':');
                if (colon <= 0) break;

                string maybeLabel = line.Substring(0, colon).Trim();
                if (!IsIdentifier(maybeLabel)) break;
                if (!ctx.Emit)
                    ctx.Labels[maybeLabel] = ctx.Pc;
                line = line.Substring(colon + 1).Trim();
                if (line.Length == 0) break;
            }
            if (line.Length == 0) continue;

            // Constant assignment: NAME EQU expr
            var equ = SplitFirstToken(line);
            if (equ.Item2.Length > 0 && equ.Item1.IndexOf(' ') < 0)
            {
                var rest = equ.Item2.Trim();
                if (rest.StartsWith("EQU ", StringComparison.OrdinalIgnoreCase))
                {
                    if (!IsIdentifier(equ.Item1))
                        throw new AsmException(lineNo, "Invalid EQU name.");
                    int val = EvalExpr(rest.Substring(4).Trim(), ctx, lineNo);
                    ctx.Constants[equ.Item1] = val;
                    continue;
                }
            }

            var split = SplitFirstToken(line);
            string mnemonic = split.Item1.ToUpperInvariant();
            string operandText = split.Item2;

            switch (mnemonic)
            {
                case "ORG":
                    ctx.Pc = EvalExpr(operandText, ctx, lineNo);
                    continue;
                case "DB":
                    EmitDb(ParseArgList(operandText), ctx, lineNo);
                    continue;
                case "DW":
                    EmitDw(ParseArgList(operandText), ctx, lineNo);
                    continue;
                case "DS":
                    {
                        int count = EvalExpr(operandText, ctx, lineNo);
                        if (count < 0) throw new AsmException(lineNo, "DS count cannot be negative.");
                        for (int n = 0; n < count; n++) EmitByte(ctx, lineNo, 0x00);
                        continue;
                    }
            }

            EmitInstruction(mnemonic, ParseArgList(operandText), ctx, lineNo);
        }
    }

    private static void EmitInstruction(string mnemonic, List<string> ops, Context ctx, int lineNo)
    {
        if (NoArgOpcodes.TryGetValue(mnemonic, out byte single) && ops.Count == 0)
        {
            EmitByte(ctx, lineNo, single);
            return;
        }

        if (mnemonic.Equals("STOP", StringComparison.OrdinalIgnoreCase))
        {
            if (ops.Count != 0) throw new AsmException(lineNo, "STOP takes no operands.");
            EmitByte(ctx, lineNo, 0x10);
            EmitByte(ctx, lineNo, 0x00);
            return;
        }

        if (mnemonic.Equals("JP", StringComparison.OrdinalIgnoreCase))
        {
            if (ops.Count == 1 && ops[0].Equals("(HL)", StringComparison.OrdinalIgnoreCase))
            {
                EmitByte(ctx, lineNo, 0xE9);
                return;
            }
            if (ops.Count == 1)
            {
                EmitByte(ctx, lineNo, 0xC3);
                EmitWord(ctx, lineNo, EvalExpr(ops[0], ctx, lineNo));
                return;
            }
            if (ops.Count == 2 && CondCodes.TryGetValue(ops[0], out int cc))
            {
                EmitByte(ctx, lineNo, (byte)(0xC2 + (cc * 0x08)));
                EmitWord(ctx, lineNo, EvalExpr(ops[1], ctx, lineNo));
                return;
            }
            throw new AsmException(lineNo, "Invalid JP operands.");
        }

        if (mnemonic.Equals("JR", StringComparison.OrdinalIgnoreCase))
        {
            if (ops.Count == 1)
            {
                EmitByte(ctx, lineNo, 0x18);
                EmitRel8(ctx, lineNo, EvalExpr(ops[0], ctx, lineNo));
                return;
            }
            if (ops.Count == 2 && CondCodes.TryGetValue(ops[0], out int cc))
            {
                EmitByte(ctx, lineNo, (byte)(0x20 + (cc * 0x08)));
                EmitRel8(ctx, lineNo, EvalExpr(ops[1], ctx, lineNo));
                return;
            }
            throw new AsmException(lineNo, "Invalid JR operands.");
        }

        if (mnemonic.Equals("CALL", StringComparison.OrdinalIgnoreCase))
        {
            if (ops.Count == 1)
            {
                EmitByte(ctx, lineNo, 0xCD);
                EmitWord(ctx, lineNo, EvalExpr(ops[0], ctx, lineNo));
                return;
            }
            if (ops.Count == 2 && CondCodes.TryGetValue(ops[0], out int cc))
            {
                EmitByte(ctx, lineNo, (byte)(0xC4 + (cc * 0x08)));
                EmitWord(ctx, lineNo, EvalExpr(ops[1], ctx, lineNo));
                return;
            }
            throw new AsmException(lineNo, "Invalid CALL operands.");
        }

        if (mnemonic.Equals("RET", StringComparison.OrdinalIgnoreCase))
        {
            if (ops.Count == 0) { EmitByte(ctx, lineNo, 0xC9); return; }
            if (ops.Count == 1 && CondCodes.TryGetValue(ops[0], out int cc))
            {
                EmitByte(ctx, lineNo, (byte)(0xC0 + (cc * 0x08)));
                return;
            }
            throw new AsmException(lineNo, "Invalid RET operands.");
        }

        if (mnemonic.Equals("RST", StringComparison.OrdinalIgnoreCase))
        {
            if (ops.Count != 1) throw new AsmException(lineNo, "RST takes one operand.");
            int vec = EvalExpr(ops[0], ctx, lineNo);
            if ((vec % 8) != 0 || vec < 0 || vec > 0x38) throw new AsmException(lineNo, "RST vector must be one of 0x00..0x38 in steps of 8.");
            EmitByte(ctx, lineNo, (byte)(0xC7 + vec));
            return;
        }

        if (mnemonic.Equals("PUSH", StringComparison.OrdinalIgnoreCase) || mnemonic.Equals("POP", StringComparison.OrdinalIgnoreCase))
        {
            if (ops.Count != 1) throw new AsmException(lineNo, $"{mnemonic} takes one operand.");
            if (!Reg16Stack.TryGetValue(ops[0], out int rr)) throw new AsmException(lineNo, $"Invalid register for {mnemonic}.");
            EmitByte(ctx, lineNo, (byte)((mnemonic.Equals("PUSH", StringComparison.OrdinalIgnoreCase) ? 0xC5 : 0xC1) + rr * 0x10));
            return;
        }

        if (mnemonic.Equals("INC", StringComparison.OrdinalIgnoreCase) || mnemonic.Equals("DEC", StringComparison.OrdinalIgnoreCase))
        {
            if (ops.Count != 1) throw new AsmException(lineNo, $"{mnemonic} takes one operand.");
            bool inc = mnemonic.Equals("INC", StringComparison.OrdinalIgnoreCase);
            if (Reg8.TryGetValue(ops[0], out int r8))
            {
                EmitByte(ctx, lineNo, (byte)((inc ? 0x04 : 0x05) + r8 * 0x08));
                return;
            }
            if (Reg16.TryGetValue(ops[0], out int r16))
            {
                EmitByte(ctx, lineNo, (byte)((inc ? 0x03 : 0x0B) + r16 * 0x10));
                return;
            }
            throw new AsmException(lineNo, $"Invalid operand for {mnemonic}.");
        }

        if (mnemonic.Equals("LD", StringComparison.OrdinalIgnoreCase))
        {
            EmitLd(ops, ctx, lineNo);
            return;
        }

        if (mnemonic.Equals("LDH", StringComparison.OrdinalIgnoreCase))
        {
            if (ops.Count != 2) throw new AsmException(lineNo, "LDH requires two operands.");
            if (TryParseMem(ops[0], out string dstInner) && ops[1].Equals("A", StringComparison.OrdinalIgnoreCase))
            {
                EmitByte(ctx, lineNo, 0xE0);
                EmitByte(ctx, lineNo, EvalExpr(dstInner, ctx, lineNo));
                return;
            }
            if (ops[0].Equals("A", StringComparison.OrdinalIgnoreCase) && TryParseMem(ops[1], out string srcInner))
            {
                EmitByte(ctx, lineNo, 0xF0);
                EmitByte(ctx, lineNo, EvalExpr(srcInner, ctx, lineNo));
                return;
            }
            throw new AsmException(lineNo, "Invalid LDH operands.");
        }

        if (mnemonic.Equals("ADD", StringComparison.OrdinalIgnoreCase))
        {
            EmitAddLike("ADD", ops, ctx, lineNo);
            return;
        }
        if (mnemonic.Equals("ADC", StringComparison.OrdinalIgnoreCase) ||
            mnemonic.Equals("SUB", StringComparison.OrdinalIgnoreCase) ||
            mnemonic.Equals("SBC", StringComparison.OrdinalIgnoreCase) ||
            mnemonic.Equals("AND", StringComparison.OrdinalIgnoreCase) ||
            mnemonic.Equals("XOR", StringComparison.OrdinalIgnoreCase) ||
            mnemonic.Equals("OR", StringComparison.OrdinalIgnoreCase) ||
            mnemonic.Equals("CP", StringComparison.OrdinalIgnoreCase))
        {
            EmitAddLike(mnemonic, ops, ctx, lineNo);
            return;
        }

        if (mnemonic.Equals("BIT", StringComparison.OrdinalIgnoreCase) ||
            mnemonic.Equals("RES", StringComparison.OrdinalIgnoreCase) ||
            mnemonic.Equals("SET", StringComparison.OrdinalIgnoreCase) ||
            mnemonic.Equals("RLC", StringComparison.OrdinalIgnoreCase) ||
            mnemonic.Equals("RRC", StringComparison.OrdinalIgnoreCase) ||
            mnemonic.Equals("RL", StringComparison.OrdinalIgnoreCase) ||
            mnemonic.Equals("RR", StringComparison.OrdinalIgnoreCase) ||
            mnemonic.Equals("SLA", StringComparison.OrdinalIgnoreCase) ||
            mnemonic.Equals("SRA", StringComparison.OrdinalIgnoreCase) ||
            mnemonic.Equals("SWAP", StringComparison.OrdinalIgnoreCase) ||
            mnemonic.Equals("SRL", StringComparison.OrdinalIgnoreCase))
        {
            EmitCb(mnemonic, ops, ctx, lineNo);
            return;
        }

        throw new AsmException(lineNo, $"Unsupported instruction: {mnemonic}");
    }

    private static void EmitLd(List<string> ops, Context ctx, int lineNo)
    {
        if (ops.Count != 2) throw new AsmException(lineNo, "LD requires two operands.");
        string dst = ops[0];
        string src = ops[1];

        if (Reg8.TryGetValue(dst, out int dst8) && Reg8.TryGetValue(src, out int src8))
        {
            EmitByte(ctx, lineNo, (byte)(0x40 + dst8 * 8 + src8));
            return;
        }

        string srcMemCheck;
        if (Reg8.TryGetValue(dst, out dst8) && !Reg8.ContainsKey(src) && !TryParseMem(src, out srcMemCheck))
        {
            EmitByte(ctx, lineNo, (byte)(0x06 + dst8 * 8));
            EmitByte(ctx, lineNo, EvalExpr(src, ctx, lineNo));
            return;
        }

        if (dst.Equals("(HL)", StringComparison.OrdinalIgnoreCase) && !Reg8.ContainsKey(src))
        {
            EmitByte(ctx, lineNo, 0x36);
            EmitByte(ctx, lineNo, EvalExpr(src, ctx, lineNo));
            return;
        }

        if (Reg16.TryGetValue(dst, out int dst16))
        {
            if (dst.Equals("SP", StringComparison.OrdinalIgnoreCase) && src.Equals("HL", StringComparison.OrdinalIgnoreCase))
            {
                EmitByte(ctx, lineNo, 0xF9);
                return;
            }
            EmitByte(ctx, lineNo, (byte)(0x01 + dst16 * 0x10));
            EmitWord(ctx, lineNo, EvalExpr(src, ctx, lineNo));
            return;
        }

        if (dst.Equals("HL", StringComparison.OrdinalIgnoreCase) && src.StartsWith("SP+", StringComparison.OrdinalIgnoreCase))
        {
            EmitByte(ctx, lineNo, 0xF8);
            EmitByte(ctx, lineNo, EvalExpr(src.Substring(3), ctx, lineNo));
            return;
        }

        if (dst.Equals("(BC)", StringComparison.OrdinalIgnoreCase) && src.Equals("A", StringComparison.OrdinalIgnoreCase)) { EmitByte(ctx, lineNo, 0x02); return; }
        if (dst.Equals("(DE)", StringComparison.OrdinalIgnoreCase) && src.Equals("A", StringComparison.OrdinalIgnoreCase)) { EmitByte(ctx, lineNo, 0x12); return; }
        if (dst.Equals("(HL+)", StringComparison.OrdinalIgnoreCase) && src.Equals("A", StringComparison.OrdinalIgnoreCase)) { EmitByte(ctx, lineNo, 0x22); return; }
        if (dst.Equals("(HLI)", StringComparison.OrdinalIgnoreCase) && src.Equals("A", StringComparison.OrdinalIgnoreCase)) { EmitByte(ctx, lineNo, 0x22); return; }
        if (dst.Equals("(HL-)", StringComparison.OrdinalIgnoreCase) && src.Equals("A", StringComparison.OrdinalIgnoreCase)) { EmitByte(ctx, lineNo, 0x32); return; }
        if (dst.Equals("(HLD)", StringComparison.OrdinalIgnoreCase) && src.Equals("A", StringComparison.OrdinalIgnoreCase)) { EmitByte(ctx, lineNo, 0x32); return; }
        if (dst.Equals("A", StringComparison.OrdinalIgnoreCase) && src.Equals("(BC)", StringComparison.OrdinalIgnoreCase)) { EmitByte(ctx, lineNo, 0x0A); return; }
        if (dst.Equals("A", StringComparison.OrdinalIgnoreCase) && src.Equals("(DE)", StringComparison.OrdinalIgnoreCase)) { EmitByte(ctx, lineNo, 0x1A); return; }
        if (dst.Equals("A", StringComparison.OrdinalIgnoreCase) && src.Equals("(HL+)", StringComparison.OrdinalIgnoreCase)) { EmitByte(ctx, lineNo, 0x2A); return; }
        if (dst.Equals("A", StringComparison.OrdinalIgnoreCase) && src.Equals("(HLI)", StringComparison.OrdinalIgnoreCase)) { EmitByte(ctx, lineNo, 0x2A); return; }
        if (dst.Equals("A", StringComparison.OrdinalIgnoreCase) && src.Equals("(HL-)", StringComparison.OrdinalIgnoreCase)) { EmitByte(ctx, lineNo, 0x3A); return; }
        if (dst.Equals("A", StringComparison.OrdinalIgnoreCase) && src.Equals("(HLD)", StringComparison.OrdinalIgnoreCase)) { EmitByte(ctx, lineNo, 0x3A); return; }
        if (dst.Equals("(C)", StringComparison.OrdinalIgnoreCase) && src.Equals("A", StringComparison.OrdinalIgnoreCase)) { EmitByte(ctx, lineNo, 0xE2); return; }
        if (dst.Equals("A", StringComparison.OrdinalIgnoreCase) && src.Equals("(C)", StringComparison.OrdinalIgnoreCase)) { EmitByte(ctx, lineNo, 0xF2); return; }

        if (TryParseMem(dst, out string dstMem) && src.Equals("A", StringComparison.OrdinalIgnoreCase))
        {
            if (dstMem.Equals("C", StringComparison.OrdinalIgnoreCase))
            {
                EmitByte(ctx, lineNo, 0xE2);
                return;
            }
            if (dst.Equals("(SP)", StringComparison.OrdinalIgnoreCase))
                throw new AsmException(lineNo, "Use LD (a16),SP instead of LD (SP),A.");
            EmitByte(ctx, lineNo, 0xEA);
            EmitWord(ctx, lineNo, EvalExpr(dstMem, ctx, lineNo));
            return;
        }
        if (dst.Equals("A", StringComparison.OrdinalIgnoreCase) && TryParseMem(src, out string srcMem))
        {
            if (srcMem.Equals("C", StringComparison.OrdinalIgnoreCase))
            {
                EmitByte(ctx, lineNo, 0xF2);
                return;
            }
            EmitByte(ctx, lineNo, 0xFA);
            EmitWord(ctx, lineNo, EvalExpr(srcMem, ctx, lineNo));
            return;
        }

        if (TryParseMem(dst, out dstMem) && src.Equals("SP", StringComparison.OrdinalIgnoreCase))
        {
            EmitByte(ctx, lineNo, 0x08);
            EmitWord(ctx, lineNo, EvalExpr(dstMem, ctx, lineNo));
            return;
        }

        throw new AsmException(lineNo, "Unsupported LD form.");
    }

    private static void EmitAddLike(string mnemonic, List<string> ops, Context ctx, int lineNo)
    {
        if (mnemonic.Equals("ADD", StringComparison.OrdinalIgnoreCase))
        {
            if (ops.Count == 2 && ops[0].Equals("HL", StringComparison.OrdinalIgnoreCase) && Reg16.TryGetValue(ops[1], out int r16))
            {
                EmitByte(ctx, lineNo, (byte)(0x09 + r16 * 0x10));
                return;
            }
            if (ops.Count == 2 && ops[0].Equals("SP", StringComparison.OrdinalIgnoreCase))
            {
                EmitByte(ctx, lineNo, 0xE8);
                EmitByte(ctx, lineNo, EvalExpr(ops[1], ctx, lineNo));
                return;
            }
        }

        string source;
        if (ops.Count == 1) source = ops[0];
        else if (ops.Count == 2 && (ops[0].Equals("A", StringComparison.OrdinalIgnoreCase) || mnemonic.Equals("SUB", StringComparison.OrdinalIgnoreCase) || mnemonic.Equals("AND", StringComparison.OrdinalIgnoreCase) || mnemonic.Equals("XOR", StringComparison.OrdinalIgnoreCase) || mnemonic.Equals("OR", StringComparison.OrdinalIgnoreCase) || mnemonic.Equals("CP", StringComparison.OrdinalIgnoreCase)))
            source = ops[1];
        else
            throw new AsmException(lineNo, $"Invalid {mnemonic} operands.");

        var bases = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["ADD"] = 0x80, ["ADC"] = 0x88, ["SUB"] = 0x90, ["SBC"] = 0x98,
            ["AND"] = 0xA0, ["XOR"] = 0xA8, ["OR"] = 0xB0, ["CP"] = 0xB8
        };
        var imm = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase)
        {
            ["ADD"] = 0xC6, ["ADC"] = 0xCE, ["SUB"] = 0xD6, ["SBC"] = 0xDE,
            ["AND"] = 0xE6, ["XOR"] = 0xEE, ["OR"] = 0xF6, ["CP"] = 0xFE
        };

        if (Reg8.TryGetValue(source, out int r))
        {
            EmitByte(ctx, lineNo, (byte)(bases[mnemonic] + r));
            return;
        }

        EmitByte(ctx, lineNo, imm[mnemonic]);
        EmitByte(ctx, lineNo, EvalExpr(source, ctx, lineNo));
    }

    private static void EmitCb(string mnemonic, List<string> ops, Context ctx, int lineNo)
    {
        int code;
        if (mnemonic.Equals("BIT", StringComparison.OrdinalIgnoreCase) ||
            mnemonic.Equals("RES", StringComparison.OrdinalIgnoreCase) ||
            mnemonic.Equals("SET", StringComparison.OrdinalIgnoreCase))
        {
            if (ops.Count != 2) throw new AsmException(lineNo, $"{mnemonic} takes two operands.");
            int bit = EvalExpr(ops[0], ctx, lineNo);
            if (bit < 0 || bit > 7) throw new AsmException(lineNo, "Bit index must be 0..7.");
            if (!Reg8.TryGetValue(ops[1], out int r)) throw new AsmException(lineNo, "Invalid register for CB instruction.");
            int baseOp = mnemonic.Equals("BIT", StringComparison.OrdinalIgnoreCase) ? 0x40 :
                mnemonic.Equals("RES", StringComparison.OrdinalIgnoreCase) ? 0x80 : 0xC0;
            code = baseOp + bit * 8 + r;
        }
        else
        {
            if (ops.Count != 1) throw new AsmException(lineNo, $"{mnemonic} takes one operand.");
            if (!Reg8.TryGetValue(ops[0], out int r)) throw new AsmException(lineNo, "Invalid register for CB instruction.");
            int baseOp;
            switch (mnemonic.ToUpperInvariant())
            {
                case "RLC": baseOp = 0x00; break;
                case "RRC": baseOp = 0x08; break;
                case "RL": baseOp = 0x10; break;
                case "RR": baseOp = 0x18; break;
                case "SLA": baseOp = 0x20; break;
                case "SRA": baseOp = 0x28; break;
                case "SWAP": baseOp = 0x30; break;
                case "SRL": baseOp = 0x38; break;
                default: throw new AsmException(lineNo, "Unsupported CB instruction: " + mnemonic);
            }
            code = baseOp + r;
        }
        EmitByte(ctx, lineNo, 0xCB);
        EmitByte(ctx, lineNo, (byte)code);
    }

    private static void EmitDb(List<string> args, Context ctx, int lineNo)
    {
        foreach (var raw in args)
        {
            string arg = raw.Trim();
            if (arg.Length == 0) continue;
            if ((arg.StartsWith("\"") && arg.EndsWith("\"")) || (arg.StartsWith("'") && arg.EndsWith("'") && arg.Length > 2))
            {
                var s = ParseStringLiteral(arg, lineNo);
                foreach (var b in s) EmitByte(ctx, lineNo, b);
            }
            else
            {
                EmitByte(ctx, lineNo, EvalExpr(arg, ctx, lineNo));
            }
        }
    }

    private static void EmitDw(List<string> args, Context ctx, int lineNo)
    {
        foreach (var raw in args)
        {
            int val = EvalExpr(raw, ctx, lineNo);
            EmitWord(ctx, lineNo, val);
        }
    }

    private static int EvalExpr(string expr, Context ctx, int lineNo)
    {
        expr = expr.Trim();
        if (expr.Length == 0) throw new AsmException(lineNo, "Expected expression.");

        int i = 0;
        int acc = 0;
        int sign = +1;
        bool expectTerm = true;
        while (i < expr.Length)
        {
            while (i < expr.Length && char.IsWhiteSpace(expr[i])) i++;
            if (i >= expr.Length) break;
            char ch = expr[i];
            if (ch == '+') { sign = +1; i++; expectTerm = true; continue; }
            if (ch == '-') { sign = -1; i++; expectTerm = true; continue; }
            if (!expectTerm) throw new AsmException(lineNo, $"Unexpected token in expression: {expr}");

            int start = i;
            if (ch == '\'' || ch == '"')
            {
                i = FindStringEnd(expr, i, lineNo) + 1;
            }
            else
            {
                while (i < expr.Length && !char.IsWhiteSpace(expr[i]) && expr[i] != '+' && expr[i] != '-') i++;
            }
            string term = expr.Substring(start, i - start);
            acc += sign * EvalTerm(term, ctx, lineNo);
            expectTerm = false;
        }
        return acc;
    }

    private static int EvalTerm(string term, Context ctx, int lineNo)
    {
        term = term.Trim();
        if (term.Length == 0) throw new AsmException(lineNo, "Empty expression term.");

        if (term.StartsWith("$"))
            return Convert.ToInt32(term.Substring(1), 16);
        if (term.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return Convert.ToInt32(term.Substring(2), 16);
        if (term.StartsWith("%"))
            return Convert.ToInt32(term.Substring(1), 2);
        if (char.IsDigit(term[0]))
            return int.Parse(term);
        if (term.Length >= 3 && term[0] == '\'' && term[term.Length - 1] == '\'')
        {
            var bytes = ParseStringLiteral(term, lineNo);
            if (bytes.Length != 1) throw new AsmException(lineNo, "Character literal must contain one character.");
            return bytes[0];
        }

        if (ctx.Constants.TryGetValue(term, out int c)) return c;
        if (ctx.Labels.TryGetValue(term, out int l)) return l;
        if (!ctx.Emit) return 0; // forward label in pass 1
        throw new AsmException(lineNo, $"Unknown symbol: {term}");
    }

    private static void EmitByte(Context ctx, int lineNo, int value)
    {
        if (ctx.Pc < 0 || ctx.Pc > 0xFFFF) throw new AsmException(lineNo, $"Address out of range: 0x{ctx.Pc:X4}");
        byte b = unchecked((byte)value);
        if (ctx.Emit)
        {
            if (ctx.Output.TryGetValue(ctx.Pc, out var prev) && prev != b)
                throw new AsmException(lineNo, $"Address overlap at 0x{ctx.Pc:X4}.");
            ctx.Output[ctx.Pc] = b;
        }
        if (ctx.Pc > ctx.MaxAddress) ctx.MaxAddress = ctx.Pc;
        ctx.Pc++;
    }

    private static void EmitWord(Context ctx, int lineNo, int value)
    {
        EmitByte(ctx, lineNo, value & 0xFF);
        EmitByte(ctx, lineNo, (value >> 8) & 0xFF);
    }

    private static void EmitRel8(Context ctx, int lineNo, int targetAddress)
    {
        int nextPc = ctx.Pc + 1;
        int delta = targetAddress - nextPc;
        if (ctx.Emit && (delta < -128 || delta > 127))
            throw new AsmException(lineNo, $"Relative jump out of range: {delta}.");
        EmitByte(ctx, lineNo, delta);
    }

    private static bool TryParseMem(string operand, out string inner)
    {
        operand = operand.Trim();
        if (operand.Length >= 3 && operand[0] == '(' && operand[operand.Length - 1] == ')')
        {
            inner = operand.Substring(1, operand.Length - 2).Trim();
            return true;
        }
        inner = "";
        return false;
    }

    private static Tuple<string, string> SplitFirstToken(string line)
    {
        line = line.Trim();
        if (line.Length == 0) return Tuple.Create("", "");
        int i = 0;
        while (i < line.Length && !char.IsWhiteSpace(line[i])) i++;
        if (i >= line.Length) return Tuple.Create(line, "");
        return Tuple.Create(line.Substring(0, i), line.Substring(i).Trim());
    }

    private static List<string> ParseArgList(string text)
    {
        var args = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return args;

        var sb = new StringBuilder();
        bool inString = false;
        char quote = '\0';
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (inString)
            {
                sb.Append(ch);
                if (ch == '\\' && i + 1 < text.Length)
                {
                    sb.Append(text[++i]);
                    continue;
                }
                if (ch == quote) inString = false;
                continue;
            }

            if (ch == '"' || ch == '\'')
            {
                inString = true;
                quote = ch;
                sb.Append(ch);
                continue;
            }
            if (ch == ',')
            {
                args.Add(sb.ToString().Trim());
                sb.Clear();
                continue;
            }
            sb.Append(ch);
        }
        if (sb.Length > 0) args.Add(sb.ToString().Trim());
        return args;
    }

    private static string StripComment(string line)
    {
        bool inString = false;
        char quote = '\0';
        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (inString)
            {
                if (ch == '\\') { i++; continue; }
                if (ch == quote) inString = false;
                continue;
            }
            if (ch == '"' || ch == '\'')
            {
                inString = true;
                quote = ch;
                continue;
            }
            if (ch == ';') return line.Substring(0, i);
        }
        return line;
    }

    private static byte[] ParseStringLiteral(string token, int lineNo)
    {
        token = token.Trim();
        if (token.Length < 2) throw new AsmException(lineNo, "Invalid string literal.");
        char quote = token[0];
        if (token[token.Length - 1] != quote) throw new AsmException(lineNo, "Unterminated string literal.");
        var sb = new List<byte>();
        for (int i = 1; i < token.Length - 1; i++)
        {
            char ch = token[i];
            if (ch == '\\')
            {
                if (i + 1 >= token.Length - 1) throw new AsmException(lineNo, "Invalid escape sequence.");
                char esc = token[++i];
                switch (esc)
                {
                    case 'n': ch = '\n'; break;
                    case 'r': ch = '\r'; break;
                    case 't': ch = '\t'; break;
                    case '\\': ch = '\\'; break;
                    case '\'': ch = '\''; break;
                    case '"': ch = '"'; break;
                    case '0': ch = '\0'; break;
                    default: throw new AsmException(lineNo, "Unsupported escape \\" + esc);
                }
            }
            sb.Add((byte)ch);
        }
        return sb.ToArray();
    }

    private static bool IsIdentifier(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        if (!(char.IsLetter(s[0]) || s[0] == '_' || s[0] == '.')) return false;
        for (int i = 1; i < s.Length; i++)
        {
            char ch = s[i];
            if (!(char.IsLetterOrDigit(ch) || ch == '_' || ch == '.')) return false;
        }
        return true;
    }

    private static int FindStringEnd(string s, int start, int lineNo)
    {
        char quote = s[start];
        for (int i = start + 1; i < s.Length; i++)
        {
            if (s[i] == '\\') { i++; continue; }
            if (s[i] == quote) return i;
        }
        throw new AsmException(lineNo, "Unterminated string/char literal.");
    }
}
