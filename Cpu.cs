// https://gist.github.com/mat128/ed951a4d71aa1049cb134e256d3d0b3b

using System;
using System.IO;
using System.Collections.Generic;
namespace GB {
  public class Cpu
  {
    Bus bus;
    Dbg dbg;
    byte A; 
    byte F; 
    byte B; 
    byte C; 
    byte D; 
    byte E; 
    byte H; 
    byte L; 

    ushort SP; 
    ushort PC; 
    bool IME = true; // Interrupt Master Enable
    bool isHalted = false;

    enum FLAG {
      C = 4,
      H,
      N,
      Z,
    };

    /*
       AF	A	-	Accumulator & Flags
       BC	B	C	BC
       DE	D	E	DE
       HL	H	L	HL
       SP	-	-	Stack Pointer
       PC	-	-	Program Counter/Pointer

       The Flags Register (lower 8 bits of AF register)
       Bit	Name	Explanation
       7	z	Zero flag
       6	n	Subtraction flag (BCD)
       5	h	Half Carry flag (BCD)
       4	c	Carry flag
       */
     bool isBitSet (ushort val, int pos) { return (val & (1 << pos)) != 0; }
     void setBitTrue (ref byte val, FLAG pos) { val = (byte)(val | (1 << (int)pos)); }
     void setBitFalse (ref byte val, FLAG pos) { val = (byte)(val & ~(1 << (int)pos)); }
     void setBit(ref byte val, FLAG pos, bool flag) { 
       if (flag == true) { 
         setBitTrue(ref val, pos);  
       } else {
         setBitFalse(ref val, pos);
       }
     }
     void setFlag(FLAG pos, bool val) {
       setBit(ref F, pos, val);
     }

     bool isFlagSet(FLAG pos) {
       return isBitSet((ushort)F, (int)pos);
     }

     void ushortToBytes(ushort num, ref byte a, ref byte b) {
        b = (byte) (num & 0x00FF);
        a = (byte) (num >> 8);
     }

     public Cpu (Bus bus) {
       this.bus = bus;
       dbg = new Dbg(ref bus);
       PC = 0x100;
       A = 0x01;
       F = 0xB0;
       B = 0;
       C = 0x13;
       D = 0;
       E = 0xD8;
       H = 0x01;
       L = 0x4D;
       SP = 0xFFFE;
     }
     byte busReadHL() {
        ushort addr = r8sToUshort(H, L);
        return bus.Read(addr);
     }

     ushort fetchImm16() {
       PC++;
       var lo = bus.Read(PC);
       PC++;
       var hi = bus.Read(PC);
       var res = (ushort)(lo | (hi << 8));
       return res;
     }

     ushort r8sToUshort (byte r1, byte r2) {  
       return (ushort)(r2 | (r1 << 8));
     }

     bool halfCarryOnAdd (int a, int b) {
       return (((a & 0xF) + 1) & 0x10) != 0;
     }

     bool isByteZero (byte b) {
       // 0000 0000 1111 1111 0xFF
       return ((b & 0xFF) == 0); // TODO: I'm not sure about this
     }

     void incR8sAsUshort(ref byte r1, ref byte r2) {
       ushort val = r8sToUshort(r1, r2);
       val++; // TODO: what if it reaches max??
       ushortToBytes(val, ref r1, ref r2);
     }
      void decR8sAsUshort(ref byte r1, ref byte r2) {
       ushort val = r8sToUshort(r1, r2);
       val--; // TODO: what if it reaches 0??
       ushortToBytes(val, ref r1, ref r2);
     }
    void proc_INC_r8(ref byte register) { 
      setFlag(FLAG.H,halfCarryOnAdd(register, 1)); 
      setFlag(FLAG.N, false); 
      register += 1; 
      setFlag(FLAG.Z, isByteZero(register));
     }

    /*
       n = A,B,C,D,E,H,L,(HL),#
       Flags affected:
       Z - Set if result is zero.
       N - Reset.
       H - Set if carry from bit 3.
       C - Set if carry from bit 7.
       */
    void proc_ADD_A_r8(byte r8) {
        int a = A;
        a += r8;
        A = (byte)a;
        setFlag(FLAG.Z, A == 0);
        setFlag(FLAG.N, false);
        // a : 0x12
        // 0001 0010 & 0000 1111 = 0000 1111
        // b: 0xFF -> 0XFF & 0xFF == 0000 1111 
        // 0000 1111 + 0000 1111 == 0001 1110 (1E) -> 1E > 0F
        setFlag(FLAG.H, ((a & 0x0F) + (r8 & 0x0F) > 0x0F));
        setFlag(FLAG.C, A > 0xFF);
    }

    void proc_ADC_A_r8(byte r8) {
        int a = A;
        int r8c = r8 + (int)(isFlagSet(FLAG.C) ? 1 : 0);
        a += r8c;
        A = (byte)a;
        setFlag(FLAG.Z, A == 0);
        setFlag(FLAG.N, false);
        setFlag(FLAG.H, ((a & 0x0F) + (r8c & 0x0F) > 0x0F));
        setFlag(FLAG.C, A > 0xFF);
    }
    /*
       Flags affected:
       Z - Not affected.
       N - Reset.
       H - Set if carry from bit 11.
       C - Set if carry from bit 15.
       */
    void proc_ADD_HL_r16(ushort r16) {
      int hl = r8sToUshort(H, L);
      ushort a = (ushort)hl;
      ushort b = r16;
      hl += r16;
      ushortToBytes((ushort)hl, ref H, ref L);
      setFlag(FLAG.N, false);
      // HC = (((a & 0xFFF) + (b & 0xFFF)) & 0x1000) == 0x1000
      setFlag(FLAG.H, (((a & 0xFFF) + (b & 0xFFF)) & 0x1000) == 0x1000);
      setFlag(FLAG.C, hl > 0xFFFF);
    }

    void proc_SUB_A_r8(byte r8) {
        int a = A;
        int tempRes = a -= r8;
        setFlag(FLAG.Z, tempRes == 0);
        setFlag(FLAG.N, true);
        setFlag(FLAG.H, ((a & 0x0F) < (r8 & 0x0F)));
        setFlag(FLAG.C, tempRes < 0);
        A = (byte) tempRes;
    }

    void proc_CP_A_r8(byte r8) {
        int a = A;
        int tempRes = a -= r8;
        setFlag(FLAG.Z, tempRes == 0);
        setFlag(FLAG.N, true);
        setFlag(FLAG.H, ((a & 0x0F) < (r8 & 0x0F)));
        setFlag(FLAG.C, tempRes < 0);
    }


    void proc_SBC_A_r8(byte r8) {
      int a = A;
      int intFlagC = isFlagSet(FLAG.C) ? 1 : 0;
      int tempRes = a -= (int)(r8 + intFlagC);
      setFlag(FLAG.Z, tempRes == 0);
      setFlag(FLAG.N, true);
      setFlag(FLAG.H, ((a & 0x0F) < (r8 & 0x0F)));
      setFlag(FLAG.C, (r8 + intFlagC) > a);
      A = (byte) tempRes;
    }

    void proc_AND_A_r8(byte r8) {
      A &= r8;
      setFlag(FLAG.Z, A == 0);
      setFlag(FLAG.N, false);
      setFlag(FLAG.H, true);
      setFlag(FLAG.C, false);
    }

    void proc_XOR_A_r8(byte r8) {
      A ^= r8;
      setFlag(FLAG.Z, A == 0);
      setFlag(FLAG.N, false);
      setFlag(FLAG.H, false);
      setFlag(FLAG.C, false);
    }
    void proc_OR_A_r8(byte r8) {
      A |= r8;
      setFlag(FLAG.Z, A == 0);
      setFlag(FLAG.N, false);
      setFlag(FLAG.H, false);
      setFlag(FLAG.C, false);
    }


    bool halfCarryOnRemove(byte register, byte val) {
      // 0000 0000 0
      // 0000 1111 0x0F
      // if bottom 4 is zero than true maybe
      return ((register - val) & 0x0F) == 0;
    }

    void proc_DEC_r8(ref byte register) {
       setFlag(FLAG.H, halfCarryOnRemove(register, 1)); 
       register -= 1;
       setFlag(FLAG.N, true);
       setFlag(FLAG.Z, isByteZero(register));
    }

    void proc_PUSH_r16(byte h, byte l) {
      SP--;
      bus.Write(SP, h);
      SP--;
      bus.Write(SP, l);
    }

    void proc_POP_r16(ref byte h,ref byte l) {
      l = bus.Read(SP);
      SP++;
      h = bus.Read(SP);
      SP++;
    }

    void proc_JP_COND_ADDR(bool cond, ushort addr) {
      if (cond) PC = addr;
    }

    void proc_JR_COND(bool cond) {
      sbyte n = (sbyte)bus.Read(PC);
      if (cond) {
        PC = (ushort)(PC + n);
      }
    }

    void proc_RET_COND(bool cond) {
      if (cond) {
        byte h = 1; 
        byte l = 1;
        proc_POP_r16(ref h, ref l);  
        PC = r8sToUshort(h, l);
      }
    }
    void proc_CALL_COND_n16 (bool cond, ushort n16) {
      if (cond) {
        byte h = 1;
        byte l = 1;
        ushortToBytes((ushort)(PC + 3), ref h, ref l);
        proc_PUSH_r16(h, l);
        PC = n16;
      }
    }

    // TODO test rotates!!
    void proc_RR_r8(ref byte r8) { // rotate through c flag (old cflag becomes first)
      byte cFlagBefore = (byte)(isFlagSet(FLAG.C) ? 1 : 0);
      byte r8Copy = r8;
      // 0011 0100 & 0000 0001
      byte lastBitBefore = (byte)(r8Copy & 0x01);
      r8 = (byte)((cFlagBefore << 3) | (r8 >> 1));
      setFlag(FLAG.Z, r8 == 0);
      setFlag(FLAG.N, false);
      setFlag(FLAG.H, false);
      setFlag(FLAG.C, (lastBitBefore == 1 ? true : false));
    }
    void proc_RL_r8(ref byte r8) { // rotate through c flag (old cflag becomes first)
      byte cFlagBefore = (byte)(isFlagSet(FLAG.C) ? 1 : 0);
      byte r8Copy = r8;
      // 0011 0100 & 0000 0001
      byte firstBitBefore = (byte)(r8Copy & 0xF0);
      r8 = (byte)((cFlagBefore >> 7) | (r8 << 1));
      setFlag(FLAG.Z, r8 == 0);
      setFlag(FLAG.N, false);
      setFlag(FLAG.H, false);
      setFlag(FLAG.C, (firstBitBefore == 1 ? true : false));
    }
    void proc_RLC_r8(ref byte r8) { // rotate right circular (last bit becomes first) 
      bool cFlagBefore = isFlagSet(FLAG.C);
      byte r8Copy = r8;
      // 0011 0100 & 0000 0001
      byte lastBitBefore = (byte)(r8Copy & 0x01);
      r8 = (byte)((lastBitBefore << 3) | (r8 << 1));
      setFlag(FLAG.Z, r8 == 0);
      setFlag(FLAG.N, false);
      setFlag(FLAG.H, false);
      setFlag(FLAG.C, (lastBitBefore == 1 ? true : false));
    }
 

    /*
     Z - Set if result is zero.
     N - Reset.
     H - Reset.
     C - Contains old bit 0 data.
     * */
    void proc_RRC_r8(ref byte r8) { // rotate right circular (last bit becomes first) 
      bool cFlagBefore = isFlagSet(FLAG.C);
      byte r8Copy = r8;
      // 0011 0100 & 0000 0001
      byte lastBitBefore = (byte)(r8Copy & 0x01);
      r8 = (byte)((lastBitBefore << 3) | (r8 >> 1));
      setFlag(FLAG.Z, r8 == 0);
      setFlag(FLAG.N, false);
      setFlag(FLAG.H, false);
      setFlag(FLAG.C, (lastBitBefore == 1 ? true : false));
    }
    void proc_DAA() {
      byte a = A;
      bool n = (F & 0x40) != 0; // substract?
      bool h = (F & 0x20) != 0; // half-carry from prior op
      bool c = (F & 0x10) != 0; // carry from prior op

      if (!n) {
        if (c || a > 0x99) { a = (byte)(a + 0x60); c = true; }
        if (h || (a & 0x0F) > 0x09) { a = (byte)(a + 0x06); }
      } else {
        if (c) a = (byte)(a - 0x60);
        if (h) a = (byte)(a - 0x06);
      }

      A = a;

      // Flags after DAA:
      // Z = set if A ==0; N = unchanged; H = 0; C = as computed above
      byte f = 0;
      if (A == 0) f |= 0x80;
      if (n) f |= 0x40;
      if (c) f |= 0x10;
      F = f;
    }

    void LD_HL_SP_e8 () {
      // MSB (most significant bit) -> the leftmost bit
      // MSB = 0 -> non-negative
      // MSB = 1 -> negative
      // sbyte converts byte into signed
      sbyte e = (sbyte)bus.Read(PC++); // consume imm8
      ushort sp = SP;
      
      // 16-bit result with wrap
      ushort r = (ushort)(sp + e);
      H = (byte)(r >> 8);
      L = (byte)r;

      bool h = ((sp & 0x0F) + ((byte)e & 0X0F)) > 0X0F;
      bool c = ((sp & 0xFF) + (byte)e) > 0xFF; 

      F = 0; // Z=0; N=0;
      if (h) F |= 0x20;
      if (c) F |= 0x10;
    }

     public int Step () {
      byte opCode = bus.Read(PC);
        dbg.Update();
        dbg.Print();
       switch (opCode) {
                /*NOP*/           case 0x00: PC++; return 4;
                /*LD BC n16 */    case 0x01: ushortToBytes(fetchImm16(), ref B, ref C);
                                             PC++;
                                             return 12;
                /*LD [BC] A*/    case 0x02:  bus.Write(r8sToUshort(B, C), A);
                                             PC++;
                                             return 8;
                /*INC BC*/   case 0x03: incR8sAsUshort(ref B,ref C); PC++; return 8;
                /*INC B*/   case 0x04: proc_INC_r8(ref B);
                                       PC++;
                                       return 4;
                /*DEC*/   case 0x05: proc_INC_r8(ref B); PC++;  return 4;
                /*LD B n8*/    case 0x06: PC++; 
                                          B = bus.Read(PC);  
                                          PC++;
                                          return 8;
                /*RLCA*/  case 0x07: proc_RLC_r8(ref A); PC++; return 4;
                /*LD*/    case 0x08: {
                                       var a16 = fetchImm16();
                                       bus.Write(a16, (byte)(SP & 0xFF)); // i don't know if this is a new sp
                                       bus.Write(a16 + 1, (byte)((SP & 0xFF00) >> 8)); // i don't know if this should be an increased sp 
                                       PC++;
                                     }
                                     return 20;
                /*ADD*/   case 0x09: proc_ADD_HL_r16(r8sToUshort(B, C)); PC++;  return 8;
                /*LD A, [BC] */    case 0x0A: A = bus.Read(r8sToUshort(B, C));
                                              PC++;
                                              return 8;
                /*DEC BC*/   case 0x0B: decR8sAsUshort(ref B, ref C); PC++;  return 8;
                /*INC C*/   case 0x0C: proc_INC_r8(ref C);  
                                       PC++;
                                     return 4;
                /*DEC C*/   case 0x0D: proc_DEC_r8(ref C); PC++;  return 4;
                /*LD C n8*/    case 0x0E: PC++;
                                          C = bus.Read(PC);
                                          PC++;
                                          return 8;
                /*RRCA*/  case 0x0F: proc_RRC_r8(ref A); PC++;  return 4;
                /*STOP*/  case 0x10: Console.WriteLine($"0x{opCode:X2} not implemented!"); Environment.Exit(1);  return 4;
                /*LD DE n16*/    case 0x11: ushortToBytes(fetchImm16(), ref D, ref E);  
                                            PC++;
                                            return 12;
                /*LD [DE] A*/    case 0x12: bus.Write(r8sToUshort(D, E), A);
                                            PC++;  
                                            return 8;
                /*INC D E*/   case 0x13: incR8sAsUshort(ref D,ref E); PC++;  return 8;
                /*INC D*/   case 0x14: proc_INC_r8(ref D);  
                                       PC++;
                                     return 4;
                /*DEC D*/   case 0x15: proc_DEC_r8(ref D); PC++; return 4;
                /*LD D n8*/    case 0x16: PC++; 
                                          D = bus.Read(PC);  
                                          PC++; 
                                          return 8;
                /*RLA*/   case 0x17: proc_RL_r8(ref A); PC++;  return 4;
                /*JR e8 */    case 0x18: proc_JR_COND(true);  return 12;
                /*ADD*/   case 0x19: proc_ADD_HL_r16(r8sToUshort(D, E)); PC++; return 8;
                /*LD A, [DE]*/    case 0x1A:  A = bus.Read(r8sToUshort(D, E));
                                              PC++; return 8;
                /*DEC DE*/   case 0x1B:  decR8sAsUshort(ref D, ref E); PC++;  return 8;
                /*INC E*/   case 0x1C: proc_INC_r8(ref E);  
                                       PC++;
                                     return 4;
                /*DEC E*/   case 0x1D: proc_DEC_r8(ref E); PC++; return 4;
                /*LD E n8 */    case 0x1E: PC++;
                                           E = bus.Read(PC);
                                           PC++;  
                                           return 8;
                /*RRA*/   case 0x1F: proc_RR_r8(ref A); PC++;  return 4;
                /*JR NZ e8*/    case 0x20: proc_JR_COND(!isFlagSet(FLAG.Z)); return 12;
                /*LD HL n16*/    case 0x21: ushortToBytes(fetchImm16(), ref H, ref L);   
                                            PC++;
                                            return 12;
                /*LD [HL+] A*/   case 0x22: bus.Write(r8sToUshort(H, L), A);
                                            incR8sAsUshort(ref H, ref L);
                                            PC++;  
                                            return 8;
                /*INC HL*/   case 0x23: incR8sAsUshort(ref H,ref L); PC++; return 8;
                /*INC H*/   case 0x24: proc_INC_r8(ref H);  
                                       PC++;
                                       return 4;
                /*DEC H*/   case 0x25: proc_DEC_r8(ref H); PC++; return 4;
                /*LD H n8*/    case 0x26: PC++; 
                                          H = bus.Read(PC);  
                                          PC++; 
                                          return 8;
                /*DAA*/   case 0x27: prod_DAA(); PC++;  return 4;
                /*JR Z e8*/    case 0x28: proc_JR_COND(isFlagSet(FLAG.Z)); return 12;
                /*ADD HL HL*/   case 0x29: proc_ADD_HL_r16(r8sToUshort(H, L)); PC++; return 8;
                /*LD A, [HL+] */    case 0x2A:  A = bus.Read(r8sToUshort(H, L)); 
                                                incR8sAsUshort(ref H,ref L);
                                                PC++;
                                                return 8;
                /*DEC HL*/   case 0x2B: decR8sAsUshort(ref H, ref L); PC++;  return 8;
                /*INC L*/   case 0x2C: proc_INC_r8(ref L);  
                                       PC++;
                                     return 4;
                /*DEC L*/   case 0x2D: proc_DEC_r8(ref L); PC++;  return 4;
                /*LD L, n8*/    case 0x2E: PC++;
                                           L = bus.Read(PC);
                                           PC++; 
                                           return 8;
                /*CPL*/   case 0x2F: setFlag(FLAG.N, true); setFlag(FLAG.H, true); PC++;  return 4;
                /*JR NC e8*/    case 0x30: proc_JR_COND(!isFlagSet(FLAG.C));  return 12;
                /*LD SP, n16 */    case 0x31:  ushortToBytes(fetchImm16(), ref H, ref L); 
                                               PC += 1;
                                               return 12;
                /*LD [HL-] A*/    case 0x32: bus.Write(r8sToUshort(H, L), A);
                                             decR8sAsUshort(ref H, ref L);
                                             PC++;  
                                             return 8;
                /*INC SP*/   case 0x33: SP++; PC++;  return 8;
                /*INC HL */ case 0x34: {
                                         var addr = r8sToUshort(H, L);
                                         byte val = bus.Read(addr);
                                         proc_INC_r8(ref val); 
                                         bus.Write(addr, val);
                                         PC++;
                                        return 12;
                                       } 
                /*DEC [HL]*/   case 0x35: {  
                                            ushort addr = r8sToUshort(H, L);
                                            byte val = bus.Read(addr);
                                            proc_DEC_r8(ref val);
                                            bus.Write(addr, val);
                                            PC++;
                                            return 12;
                                          }
                /*LD [HL] n8*/    case 0x36:  PC++; 
                                              {
                                                byte  val = bus.Read(PC);
                                                bus.Write(r8sToUshort(H, L), val);
                                              }
                                              PC++;
                                              return 12;
                /*SCF*/   case 0x37: setFlag(FLAG.N, false); setFlag(FLAG.H, false); setFlag(FLAG.C, true); PC++; return 4;
                /*JR C*/    case 0x38: proc_JR_COND(isFlagSet(FLAG.C));  return 12;
                /*ADD*/   case 0x39: proc_ADD_HL_r16(SP); PC++; return 8;
                /*LD A, [HL-] */    case 0x3A:  A = bus.Read(r8sToUshort(H, L)); 
                                                decR8sAsUshort(ref H,ref L);
                                                PC++; return 8;
                /*DEC*/   case 0x3B: SP--; PC++; return 8;
                /*INC A*/   case 0x3C: proc_INC_r8(ref A); 
                                       PC++;
                                     return 4;
                /*DEC A*/   case 0x3D: proc_DEC_r8(ref A); PC++; return 4;
                /*LD A, n8*/    case 0x3E: PC++;
                                           A = bus.Read(PC);
                                           PC++;
                                           return 8;
                /*CCF*/   case 0x3F: setFlag(FLAG.N, false); setFlag(FLAG.H, false); setFlag(FLAG.C, !isFlagSet(FLAG.C)); PC++; return 4;

                /*LD B, B */    case 0x40: PC++; return 4;
                /*LD*/    case 0x41: B = C; PC++; return 4;
                /*LD*/    case 0x42: B = D; PC++; return 4;
                /*LD*/    case 0x43: B = E; PC++; return 4;
                /*LD*/    case 0x44: B = H; PC++; return 4;
                /*LD*/    case 0x45: B = L; PC++; return 4;
                /*LD*/    case 0x46: B = bus.Read(r8sToUshort(H, L)); 
                                     PC++;
                                     return 8;
                /*LD*/    case 0x47: B = A; PC++; return 4;
                /*LD*/    case 0x48: C = B; PC++; return 4;
                /*LD C, C*/    case 0x49:  return 4;
                /*LD*/    case 0x4A: C = D; PC++; return 4;
                /*LD*/    case 0x4B: C = E; PC++; return 4;
                /*LD*/    case 0x4C: C = H; PC++; return 4;
                /*LD*/    case 0x4D: C = L; PC++; return 4;
                /*LD*/    case 0x4E: C = bus.Read(r8sToUshort(H, L)); PC++; return 8;
                /*LD*/    case 0x4F: C = A; PC++; return 4;

                /*LD*/    case 0x50: D = B; PC++;  return 4;
                /*LD*/    case 0x51: D = C; PC++; return 4;
                /*LD D,D */    case 0x52:  PC++; return 4;
                /*LD*/    case 0x53: D = E; PC++; return 4;
                /*LD*/    case 0x54: D = H; PC++; return 4;
                /*LD*/    case 0x55: D = L; PC++; return 4;
                /*LD*/    case 0x56: D = bus.Read(r8sToUshort(H, L)); 
                                     PC++;
                                     return 8;
                /*LD*/    case 0x57: D = A; PC++; return 4;
                /*LD*/    case 0x58: E = B; PC++; return 4;
                /*LD*/    case 0x59:  return 4;
                /*LD*/    case 0x5A: E = D; PC++; return 4;
                /*LD E,E */    case 0x5B: PC++; return 4;
                /*LD*/    case 0x5C: E = H; PC++; return 4;
                /*LD*/    case 0x5D: E = L; PC++; return 4;
                /*LD*/    case 0x5E: E = bus.Read(r8sToUshort(H, L)); PC++; return 8;
                /*LD*/    case 0x5F: E = A; PC++; return 4;

                /*LD*/    case 0x60: H = B; PC++;  return 4;
                /*LD*/    case 0x61: H = C; PC++; return 4;
                /*LD*/    case 0x62: H = D; PC++; return 4;
                /*LD*/    case 0x63: H = E; PC++; return 4;
                /*LD H,H*/    case 0x64: PC++; return 4;
                /*LD*/    case 0x65: H = L; PC++; return 4;
                /*LD*/    case 0x66: H = bus.Read(r8sToUshort(H, L)); 
                                     PC++;
                                     return 8;
                /*LD*/    case 0x67: H = A; PC++; return 4;
                /*LD*/    case 0x68: L = B; PC++; return 4;
                /*LD*/    case 0x69: L = C; return 4;
                /*LD*/    case 0x6A: L = D; PC++; return 4;
                /*LD*/    case 0x6B: L = E; PC++; return 4;
                /*LD*/    case 0x6C: L = H; PC++; return 4;
                /*LD L,L*/    case 0x6D: PC++; return 4;
                /*LD*/    case 0x6E: L = bus.Read(r8sToUshort(H, L)); PC++; return 8;
                /*LD*/    case 0x6F: L = A; PC++; return 4;

                /*LD*/    case 0x70: bus.Write(bus.Read(r8sToUshort(H, L)), B); PC++;  return 4;
                /*LD*/    case 0x71: bus.Write(bus.Read(r8sToUshort(H, L)), C); PC++; return 4;
                /*LD*/    case 0x72: bus.Write(bus.Read(r8sToUshort(H, L)), D); PC++; return 4;
                /*LD*/    case 0x73: bus.Write(bus.Read(r8sToUshort(H, L)), E); PC++; return 4;
                /*LD*/    case 0x74: bus.Write(bus.Read(r8sToUshort(H, L)), H); PC++; return 4;
                /*LD*/    case 0x75: bus.Write(bus.Read(r8sToUshort(H, L)), L); PC++; return 4;
                /*LD HALT*/    case 0x76: isHalted = true; 
                                     PC++;
                                     return 8;
                /*LD*/    case 0x77: bus.Write(bus.Read(r8sToUshort(H, L)), A); PC++; return 4;
                /*LD*/    case 0x78: A = B; PC++; return 4;
                /*LD*/    case 0x79: A = C; PC++; return 4;
                /*LD*/    case 0x7A: A = D; PC++; return 4;
                /*LD*/    case 0x7B: A = E; PC++; return 4;
                /*LD*/    case 0x7C: A = H; PC++; return 4;
                /*LD*/    case 0x7D: A = L; PC++; return 4;
                /*LD*/    case 0x7E: A = bus.Read(r8sToUshort(H, L)); PC++; return 8;
                /*LD A, A*/    case 0x7F: PC++; return 4;

                /*ADD*/   case 0x80: proc_ADD_A_r8(B); PC++;  return 4;
                /*ADD*/   case 0x81: proc_ADD_A_r8(C); PC++;  return 4;
                /*ADD*/   case 0x82: proc_ADD_A_r8(D); PC++;  return 4;
                /*ADD*/   case 0x83: proc_ADD_A_r8(E); PC++;  return 4;
                /*ADD*/   case 0x84: proc_ADD_A_r8(H); PC++;  return 4;
                /*ADD*/   case 0x85: proc_ADD_A_r8(L); PC++;  return 4;
                /*ADD*/   case 0x86: {
                                       ushort addr = r8sToUshort(H, L);
                                       byte hl = bus.Read(addr);
                                       proc_ADD_A_r8(hl); 
                                       PC++;  
                                       return 8;
                                     }
                /*ADD*/   case 0x87: proc_ADD_A_r8(A); PC++;  return 4;
                /*ADD*/   case 0x88: proc_ADC_A_r8(B); PC++;  return 4;
                /*ADD*/   case 0x89: proc_ADC_A_r8(C); PC++;  return 4;
                /*ADD*/   case 0x8A: proc_ADC_A_r8(D); PC++;  return 4;
                /*ADD*/   case 0x8B: proc_ADC_A_r8(E); PC++;  return 4;
                /*ADD*/   case 0x8C: proc_ADC_A_r8(H); PC++;  return 4;
                /*ADD*/   case 0x8D: proc_ADC_A_r8(L); PC++;  return 4;
                /*ADD*/   case 0x8E: {
                                       ushort addr = r8sToUshort(H, L);
                                       byte hl = bus.Read(addr);
                                       proc_ADD_A_r8(hl); 
                                       PC++;  
                                       return 8;
                                     }
                /*ADD*/   case 0x8F: proc_ADC_A_r8(A); PC++; return 4;
 
                /*SUB*/   case 0x90: proc_SUB_A_r8(B); PC++;  return 4;
                /*SUB*/   case 0x91: proc_SUB_A_r8(C); PC++; return 4;
                /*SUB*/   case 0x92: proc_SUB_A_r8(D); PC++; return 4;
                /*SUB*/   case 0x93: proc_SUB_A_r8(E); PC++; return 4;
                /*SUB*/   case 0x94: proc_SUB_A_r8(H); PC++; return 4;
                /*SUB*/   case 0x95: proc_SUB_A_r8(L); PC++; return 4;
                /*SUB*/   case 0x96: {
                                       byte hl = busReadHL(); 
                                       proc_SUB_A_r8(hl); 
                                       PC++; 
                                       return 8;
                                     }
                /*SUB*/   case 0x97: proc_SUB_A_r8(B); PC++; return 4;


                /*SBC*/   case 0x98: proc_SBC_A_r8(B); PC++; return 4;
                /*SBC*/   case 0x99: proc_SBC_A_r8(C); PC++;return 4;
                /*SBC*/   case 0x9A: proc_SBC_A_r8(D); PC++; return 4;
                /*SBC*/   case 0x9B: proc_SBC_A_r8(E); PC++; return 4;
                /*SBC*/   case 0x9C: proc_SBC_A_r8(H); PC++; return 4;
                /*SBC*/   case 0x9D: proc_SBC_A_r8(L); PC++; return 4;
                /*SBC*/   case 0x9E: {
                                       ushort addr = r8sToUshort(H, L);
                                       byte hl = bus.Read(addr);
                                       proc_SBC_A_r8(hl); 
                                       PC++; 
                                       return 8;
                                     }
                /*SBC*/   case 0x9F: proc_SBC_A_r8(A); PC++; return 4;

                /*AND*/   case 0xA0: proc_AND_A_r8(B); PC++; return 4;
                /*AND*/   case 0xA1: proc_AND_A_r8(C); PC++;return 4;
                /*AND*/   case 0xA2: proc_AND_A_r8(D); PC++;return 4;
                /*AND*/   case 0xA3: proc_AND_A_r8(E); PC++;return 4;
                /*AND*/   case 0xA4: proc_AND_A_r8(H); PC++;return 4;
                /*AND*/   case 0xA5: proc_AND_A_r8(L); PC++;return 4;
                /*AND*/   case 0xA6: {
                                       byte hl = busReadHL();
                                       proc_AND_A_r8(B); 
                                       ushortToBytes(hl, ref H, ref L);
                                       PC++;
                                       return 8;
                                     }
                /*AND*/   case 0xA7: proc_AND_A_r8(A); PC++;return 4;


                /*XOR*/   case 0xA8: proc_XOR_A_r8(B); PC++; return 4;
                /*XOR*/   case 0xA9: proc_XOR_A_r8(C); PC++;return 4;
                /*XOR*/   case 0xAA: proc_XOR_A_r8(D); PC++;return 4;
                /*XOR*/   case 0xAB: proc_XOR_A_r8(E); PC++;return 4;
                /*XOR*/   case 0xAC: proc_XOR_A_r8(H); PC++;return 4;
                /*XOR*/   case 0xAD: proc_XOR_A_r8(L); PC++;return 4;
                /*XOR*/   case 0xAE: {
                                       byte hl = busReadHL();
                                       proc_XOR_A_r8(hl); 
                                       PC++;
                                       return 8;
                                     }
                /*XOR*/   case 0xAF: proc_XOR_A_r8(A); PC++;return 4;


                /*OR*/   case 0xB0: proc_OR_A_r8(B); PC++; return 4;
                /*OR*/   case 0xB1: proc_OR_A_r8(C); PC++;return 4;
                /*OR*/   case 0xB2: proc_OR_A_r8(D); PC++;return 4;
                /*OR*/   case 0xB3: proc_OR_A_r8(E); PC++;return 4;
                /*OR*/   case 0xB4: proc_OR_A_r8(H); PC++;return 4;
                /*OR*/   case 0xB5: proc_OR_A_r8(L); PC++;return 4;
                /*OR*/   case 0xB6: {
                                      byte hl = busReadHL();
                                      proc_OR_A_r8(hl); 
                                      PC++;
                                      return 8;
                                    }
                /*OR*/   case 0xB7: proc_OR_A_r8(A); PC++;return 4;

                /*CP*/    case 0xB8: proc_CP_A_r8(B); PC++; return 4;
                /*CP*/    case 0xB9: proc_CP_A_r8(C); PC++; return 4;
                /*CP*/    case 0xBA: proc_CP_A_r8(D); PC++; return 4;
                /*CP*/    case 0xBB: proc_CP_A_r8(E); PC++; return 4;
                /*CP*/    case 0xBC: proc_CP_A_r8(H); PC++; return 4;
                /*CP*/    case 0xBD: proc_CP_A_r8(L); PC++; return 4;
                /*CP*/    case 0xBE: {
                                       proc_CP_A_r8(busReadHL()); 
                                       PC++; 
                                       return 8;
                                     }
                /*CP*/    case 0xBF: proc_CP_A_r8(A); PC++; return 4;

                /*RET NZ*/   case 0xC0: proc_RET_COND(!isFlagSet(FLAG.Z));  return 20;
                /*POP*/   case 0xC1: proc_POP_r16(ref B, ref C); PC++; return 12;
                /*JP NZ a16 */    case 0xC2: proc_JP_COND_ADDR(!(isFlagSet(FLAG.Z)), fetchImm16()); return 16;
                /*JP A16*/    case 0xC3: PC = fetchImm16(); // no PC++ after JP!! 
                                         return 16;
                /*CALL NZ*/  case 0xC4: proc_CALL_COND_n16(!isFlagSet(FLAG.Z), fetchImm16());  return 24;
                /*PUSH BC*/  case 0xC5: proc_PUSH_r16(B, C); PC++; return 16;
                /*ADD A, n8*/   case 0xC6: proc_ADD_A_r8(bus.Read(PC)); PC++;  return 8;
                /*RST $00*/   case 0xC7: proc_CALL_COND_n16(true, 0x0000);  return 16;
                /*RET Z*/   case 0xC8: proc_RET_COND(isFlagSet(FLAG.Z));  return 20;
                /*RET*/   case 0xC9: proc_RET_COND(true);  return 16;
                /*JP Z*/    case 0xCA: proc_JP_COND_ADDR(isFlagSet(FLAG.Z), fetchImm16());  return 16;
                /*PREFIX*/case 0xCB: Console.WriteLine($"0x{opCode:X2} not implemented!"); Environment.Exit(1);  return 4;
                /*CALL Z*/  case 0xCC:  proc_CALL_COND_n16(isFlagSet(FLAG.Z), fetchImm16()); return 24;
                /*CALL*/  case 0xCD: proc_CALL_COND_n16(true, fetchImm16());  return 24;
                /*ADC a n8*/   case 0xCE: proc_ADC_A_r8(bus.Read(PC)); PC++; return 8;
                /*RST $08*/   case 0xCF: proc_CALL_COND_n16(true, 0x0008);  return 16;
                /*RET NC*/   case 0xD0: proc_RET_COND(!isFlagSet(FLAG.C));  return 20;
                /*POP*/   case 0xD1: proc_POP_r16(ref D, ref E); PC++; return 12;
                /*JP NC*/    case 0xD2: proc_JP_COND_ADDR(!isFlagSet(FLAG.C), fetchImm16()); return 16;
                /*ILLEGAL_D3*/case 0xD3:  return 4;
                /*CALL NC*/  case 0xD4: proc_CALL_COND_n16(!isFlagSet(FLAG.C), fetchImm16());  return 24;
                /*PUSH*/  case 0xD5: proc_PUSH_r16(D, E); PC++; return 16;
                /*SUB A n8 */   case 0xD6: proc_SUB_A_r8(bus.Read(PC)); PC++; return 8;
                /*RST 10*/   case 0xD7: proc_CALL_COND_n16(true, 0x0010);  return 16;
                /*RET C*/   case 0xD8: proc_RET_COND(isFlagSet(FLAG.C));  return 20;
                /*RETI*/  case 0xD9: IME = true; proc_RET_COND(true); return 16;
                /*JP C*/    case 0xDA: proc_JP_COND_ADDR(isFlagSet(FLAG.C), fetchImm16()); return 16;
                /*ILLEGAL_DB*/case 0xDB:  return 4;
                /*CALL C*/  case 0xDC: proc_CALL_COND_n16(isFlagSet(FLAG.C), fetchImm16());  return 24;
                /*ILLEGAL_DD*/case 0xDD:  return 4;
                /*SBC a n8*/   case 0xDE: proc_SBC_A_r8(bus.Read(PC)); PC++; return 8;
                /*RST 18*/   case 0xDF: proc_CALL_COND_n16(true, 0x0018);  return 16;
                /*LDH a8 A*/   case 0xE0: { 
                                            PC++;
                                            byte imm = bus.Read(PC);
                                            bus.Write((ushort)(0xFF00 + imm), A);
                                            PC++;
                                            return 12;
                                          }
                /*POP*/   case 0xE1: proc_POP_r16(ref H, ref L); PC++; return 12;
                /*LDH [FF00 + c] A*/   case 0xE2:  {
                                                    bus.Write((ushort)(0xFF00 + C), A);
                                                    PC++;
                                        return 8;
                                      }
                /*ILLEGAL_E3*/case 0xE3:  return 4;
                /*ILLEGAL_E4*/case 0xE4:  return 4;
                /*PUSH*/  case 0xE5: proc_PUSH_r16(H, L); PC++; return 16;
                /*AND A n8*/   case 0xE6: proc_AND_A_r8(bus.Read(PC)); PC++; return 8;
                /*RST 20*/   case 0xE7: proc_CALL_COND_n16(true, 0x0020);  return 16;
                /*ADD SP e8*/   case 0xE8: Console.WriteLine($"0x{opCode:X2} not implemented!"); Environment.Exit(1);  return 16;
                /*JP HL n16 */    case 0xE9: PC = (ushort) (L | (H << 8)); 
                                             return 4;
                /*LD A16, A*/    case 0xEA: bus.Write(fetchImm16(), A);
                                            PC++;
                                            return 16;
                /*ILLEGAL_EB*/case 0xEB: return 4;
                /*ILLEGAL_EC*/case 0xEC: return 4;
                /*ILLEGAL_ED*/case 0xED: return 4;
                /*XOR A, n8*/   case 0xEE: proc_XOR_A_r8(bus.Read(PC)); PC++;  return 8;
                /*RST 28*/   case 0xEF: proc_CALL_COND_n16(true, 0x0028);  return 16;
                /*LDH A, [FF00 + imm]*/   case 0xF0: { 
                                                       PC++;
                                                       byte imm = bus.Read(PC);
                                                       ushort addr = (ushort)(0xFF00 + imm);
                                                       A = bus.Read(addr);
                                                       PC++;
                                                       return 12;
                                                     } 
                /*POP*/   case 0xF1: proc_POP_r16(ref A, ref F); PC++; return 12;
                /*LDH A, [FF00 + C]*/   case 0xF2: {
                                                     A = bus.Read((ushort)(0xFF00 + C));
                                                     return 8;
                                                   }
                /*DI*/    case 0xF3: IME = false; 
                                     PC++;
                                     return 4;
                /*ILLEGAL_F4*/case 0xF4: return 4;
                /*PUSH AF*/  case 0xF5: proc_PUSH_r16(A, F); PC++; return 16;
                /*OR a, n8*/    case 0xF6: proc_OR_A_r8(bus.Read(PC)); PC++;  return 8;
                /*RST 30*/   case 0xF7: proc_CALL_COND_n16(true, 0x0030);  return 16;
                /*LD HL, SP + e8*/    case 0xF8:  {
                                                    LD_HL_SP_e8();
                                                    PC++;
                                                    return 12;
                                                  }
                /*LD SP HL*/    case 0xF9: SP = r8sToUshort(H, L); PC++;  return 8;
                /*LD A, a16*/    case 0xFA:  A = bus.Read(fetchImm16());
                                             PC++;
                                           return 16;
                /*EI*/    case 0xFB: IME = true; PC++;  return 4;
                /*ILLEGAL_FC*/case 0xFC: return 4;
                /*ILLEGAL_FD*/case 0xFD: return 4;
                /*CP A n8 */    case 0xFE: proc_CP_A_r8(bus.Read(PC)); PC++; return 8;
                /*RST 38*/   case 0xFF: proc_CALL_COND_n16(true, 0x0038); return 16; // this is known to be buggy!
        default: Console.WriteLine($"0x{opCode:X2} not implemented!"); Environment.Exit(1); return 0;
       }
     }
  }
}
