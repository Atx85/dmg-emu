// https://gist.github.com/mat128/ed951a4d71aa1049cb134e256d3d0b3b

using System;
using System.IO;
using System.Collections.Generic;
namespace GB {
  public class Cpu
  {
    Bus bus;
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
    public bool IME = true; // Interrupt Master Enable
    public bool eiPending = false;
    public bool isHalted = false;
    public bool haltBug = false;
    public bool isStopped = false;

    bool first = true;

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
     // new set flag
     void SetFlag(FLAG pos, bool val) {
       // setBit(ref F, pos, val);
       if (val) F |= (byte)(1 << (int)pos);
       else F &= (byte)~(1 << (int)pos);
     }
     bool GetFlag(FLAG f) => (F & (1 << (int)f)) != 0;
//------------>

     void ushortToBytes(ushort num, ref byte a, ref byte b) {
        b = (byte) (num & 0x00FF);
        a = (byte) (num >> 8);
     }
     public int handleInterrupts() {
       byte ie = bus.Read(0xFFFF);
       byte flags = bus.Read(0xFF0F);
       int pending = ie & flags;

       if (pending != 0) {
         if (IME) {
         IME = false;

         int bit = 0;
         while (((pending >> bit) & 1) == 0) bit++;

         // clear the pending bit
         bus.Write(0xFF0F, (byte)(flags & ~(1 << bit)));

         proc_PUSH_r16((byte)(PC >> 8), (byte)(PC & 0xFF));

         // jump to vector
         PC = (ushort)(0x0040 + 8 * bit);

         return 20; // 20 cycles on real hardware (~5 m-cycles consumed
       } else if (isHalted) { 
         isHalted = false;
       }
       }
       
       return 0; // no interrupt handled
     }


     public Cpu (Bus bus) {
       this.bus = bus;
       PC = 0x0100;
       A = 0x01;
       F = 0xB0;
       B = 0;
       C = 0x13;
       D = 0;
       E = 0xD8;
       H = 0x01;
       L = 0x4D;
       SP = 0xFFFE;
       IME = true;
       eiPending = false;
       isHalted = false;
       haltBug = false;
       isStopped = false;
     }
     byte busReadHL() {
        ushort addr = r8sToUshort(H, L);
        return bus.Read(addr);
     }

     ushort fetchImm16() {
       var lo = bus.Read(PC++);
       var hi = bus.Read(PC++);
       var res = (ushort)(lo | (hi << 8));
       return res;
     }

     byte fetchImm8() {
       return bus.Read(PC++);
     }

     ushort r8sToUshort (byte r1, byte r2) {  
       return (ushort)(r2 | (r1 << 8));
     }

     bool halfCarryOnAdd (int a, int b) {
       // 0001 0000
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

      // INC/DEC helpers
      byte INC(byte value) {
        byte res = (byte)(value + 1);
        SetFlag(FLAG.Z, res == 0);
        SetFlag(FLAG.N, false);
        SetFlag(FLAG.H, (value & 0x0F) == 0x0F);
        return res;
      }
      byte DEC(byte value) {
        byte res = (byte)(value - 1);
        SetFlag(FLAG.Z, res == 0);
        SetFlag(FLAG.N, false);
        SetFlag(FLAG.H, (value & 0x0F) == 0x0F);
        return res;
      }


      //

      bool isCarryFromBit3(byte a, byte b) {
        return (((a & 0x0F) + (b & 0x0F)) > 0x0F);
      }

      /*
       * INC n          - Increment register n.

        n = A,B,C,D,E,H,L,(HL)

        Flags affected:
            Z - Set if result is zero.
            N - Reset.
            H - Set if carry from bit 3.
            C - Not affected.
        Cycles: 1
        Bytes: 1
       */
    void proc_INC_r8(ref byte register) { 
      setFlag(FLAG.H,isCarryFromBit3(register, 1)); 
      setFlag(FLAG.N, false); 
      register += 1; 
      setFlag(FLAG.Z, register == 0);
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
        // the original A has to be used for calculating the flags
        int a0 = A, r = a0 + r8;
        setFlag(FLAG.H, ((a0 & 0x0F) + (r8 & 0x0F)) > 0x0F);
        setFlag(FLAG.C, r > 0xFF);
        A = (byte)r;
        setFlag(FLAG.Z, A == 0); setFlag(FLAG.N, false);
    }

    void proc_ADC_A_r8(byte r8) {
       int a0 = A, c = isFlagSet(FLAG.C) ? 1 : 0, r = a0 + r8 + c;
       setFlag(FLAG.H, ((a0 & 0x0F) + (r8 & 0x0F) + c) > 0x0F);
       setFlag(FLAG.C, r > 0xFF);
       A = (byte)r;
       setFlag(FLAG.Z, A == 0); setFlag(FLAG.N, false);
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
       int a0 = A, r = a0 - r8;
       setFlag(FLAG.H, (a0 & 0x0F) < (r8 & 0x0F));
       setFlag(FLAG.C, r < 0);
       A = (byte) r;
       setFlag(FLAG.Z, A == 0);
       setFlag(FLAG.N, true);
    }

    void proc_CP_A_r8(byte r8) {
       int a0 = A, r = a0 - r8;
       setFlag(FLAG.H, (a0 & 0x0F) < (r8 & 0x0F));
       setFlag(FLAG.C, r < 0);
       setFlag(FLAG.Z, r == 0);
       setFlag(FLAG.N, true);
    }


    void proc_SBC_A_r8(byte r8) {
      int a0 = A, c = isFlagSet(FLAG.C) ? 1 : 0, r = a0 - r8 - c;
      setFlag(FLAG.H, (a0 & 0x0F) < ((r8 & 0x0F) + c));
      setFlag(FLAG.C, a0 < r8 + c);
      A = (byte)r;
      setFlag(FLAG.Z, A == 0);
      setFlag(FLAG.N, true);
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

    byte SWAP(byte v) {
      byte res = (byte)((v >> 4) | (v << 4));
      SetFlag(FLAG.Z, res == 0);
      SetFlag(FLAG.N, false );
      SetFlag(FLAG.H, false );
      SetFlag(FLAG.C, false );
      return res;
    }

    byte SLA(byte v) {
      byte carry = (byte)((v >> 7) & 1);
      byte res = (byte)(v << 1);
      SetFlag(FLAG.Z, res == 0);
      SetFlag(FLAG.N, false );
      SetFlag(FLAG.H, false );
      SetFlag(FLAG.C, carry == 1 );
      return res;
    }

    byte SRA(byte v) {
      byte carry = (byte)(v & 1);
      byte msb = (byte)(v & 0x80);
      byte res = (byte)((v >> 1) | msb);
      SetFlag(FLAG.Z, res == 0);
      SetFlag(FLAG.N, false );
      SetFlag(FLAG.H, false );
      SetFlag(FLAG.C, carry == 1 );
      return res;
    }

    byte SRL(byte v) {
      byte carry = (byte)(v & 1);
      byte res = (byte)(v >> 1);
      SetFlag(FLAG.Z, res == 0);
      SetFlag(FLAG.N, false );
      SetFlag(FLAG.H, false );
      SetFlag(FLAG.C, carry == 1 );
      return res;
    }

    void BIT(int bit, byte v) {
      bool z = ((v >> bit) & 1) == 0;
      SetFlag(FLAG.Z, z);
      SetFlag(FLAG.N, false);
      setFlag(FLAG.H, true);
    }

    byte RES(int bit, byte v) {
      return (byte)(v & ~(1 << bit));
    }

    byte SET(int bit, byte v) {
      return (byte)(v | (1 << bit));
    }


    bool halfCarryOnRemove(byte register, byte val) {
      // 0000 0000 0
      // 0000 1111 0x0F
      // if bottom 4 is zero than true maybe
      return ((register - val) & 0x0F) == 0;
    }

    void proc_DEC_r8(ref byte register) {
       setFlag(FLAG.H, (register & 0x0F) == 0); 
       register -= 1;
       setFlag(FLAG.N, true);
       setFlag(FLAG.Z, register == 0);
    }

    void proc_PUSH_r16(byte h, byte l) {
      SP--;
      bus.Write(SP, h);
      SP--;
      bus.Write(SP, l);
      // Console.WriteLine($"PUSH: {h:X2}{l:X2} -> SP={SP:X4}");
    }

    void proc_POP_r16(ref byte h,ref byte l) {
      l = bus.Read(SP++);
      h = bus.Read(SP++);
      // Console.WriteLine($"POP: {h:X2}{l:X2} -> SP={SP:X4}");
    }

    void proc_JP_COND_ADDR(bool cond, ushort addr) {
      if (cond) PC = addr;
    }

    int proc_JR_COND(bool cond) {
      /*sbyte n = (sbyte)bus.Read(PC++);
      PC +=2;
      if (cond) {
        PC = (ushort)(PC + n);
      }
      return cond;*/
      sbyte offset = (sbyte)bus.Read(PC++);
      if (cond) {
        PC = (ushort)(PC + offset);
        return 12;
      }
      return 8;
    }

    void proc_RET_COND(bool cond) {
      if (cond) {
        byte h = 0, l = 0;
        proc_POP_r16(ref h, ref l);
        PC = r8sToUshort(h, l);

      }    
    }
    void proc_CALL_COND_n16 (bool cond, ushort n16) {
      if (cond) {
        ushort retAddr = PC;
        proc_PUSH_r16((byte)(retAddr >> 8), (byte)(retAddr & 0xFF));
        PC = n16;
      }
    }

    void proc_RST(ushort vec) {
      //ushort retAddr = PC;
      //proc_PUSH_r16((byte)(retAddr >> 8), (byte)(retAddr & 0xFF));
      //PC = vec;
      ushort retAddr = PC;
      SP--;
      bus.Write(SP, (byte)(retAddr >> 8));
      SP--;
      bus.Write(SP, (byte)(retAddr & 0xFF));
      PC = vec;
    }

    // TODO test rotates!!
    void proc_RR_r8(ref byte r8, bool isAFlag) { // rotate through c flag (old cflag becomes first)
      bool oldC = isFlagSet(FLAG.C);
      bool newC = (r8 & 1) != 0;
      r8 = (byte)((r8 >> 1) | (oldC ? 0x80 : 0x00));
      setFlag(FLAG.Z, isAFlag ? (r8 == 0) : false);
      setFlag(FLAG.N, false);
      setFlag(FLAG.H, false);
      setFlag(FLAG.C, newC);
    }
    void proc_RL_r8(ref byte r8, bool isAFlag) { // rotate through c flag (old cflag becomes first)
      bool oldC = isFlagSet(FLAG.C);
      bool newC = (r8 & 0x80) != 0;
      r8 = (byte)((r8 << 1) | (oldC ? 1 : 0)); 
      setFlag(FLAG.Z, (isAFlag ? (r8 == 0) : false));
      setFlag(FLAG.N, false);
      setFlag(FLAG.H, false);
      setFlag(FLAG.C, newC);
    }
    void proc_RLC_r8(ref byte r8) { // rotate right circular (last bit becomes first) 
      bool newC = (r8 & 0x80) != 0;
      r8 = (byte)((r8 << 1) | (newC ? 1 : 0));
      setFlag(FLAG.Z, r8 == 0);
      setFlag(FLAG.N, false);
      setFlag(FLAG.H, false);
      setFlag(FLAG.C, newC);
    }
 

    /*
     Z - Set if result is zero.
     N - Reset.
     H - Reset.
     C - Contains old bit 0 data.
     * */
    void proc_RRC_r8(ref byte r8) { // rotate right circular (last bit becomes first) 
    bool newC = (r8 & 1) != 0;
    r8 = (byte)((r8 >> 1) | (newC ? 0x80 : 0x00));
     setFlag(FLAG.Z, r8 == 0);
      setFlag(FLAG.N, false);
      setFlag(FLAG.H, false);
      setFlag(FLAG.C, newC);
    }
    void proc_DAA() {
      byte a = A;
      bool n = (F & 0x40) != 0; // substract?
      bool h = (F & 0x20) != 0; // half-carry from prior op
      bool c = (F & 0x10) != 0; // carry from prior op

      int correction = 0;
      if (!n) {
        if (h || (a & 0x0F) > 9) correction |= 0x06;
        if (c || a > 0x99) { correction |= 0x60; c = true; }
        a = (byte)(a + correction);
      } else {
        if (h) correction |= 0x06;
        if (c) { correction |= 0x60; }
        a = (byte)(a - correction);
      }
      //if (!n) {
      //  if (c || a > 0x99) { a = (byte)(a + 0x60); c = true; }
      //  if (h || (a & 0x0F) > 0x09) { a = (byte)(a + 0x06); }
      //} else {
      //  if (c) a = (byte)(a - 0x60);
      //  if (h) a = (byte)(a - 0x06);
      //}

      A = a;

      // Flags after DAA:
      // Z = set if A ==0; N = unchanged; H = 0; C = as computed above
      byte f = 0;
      if (A == 0) f |= 0x80;
      if (n) f |= 0x40;
      if (c) f |= 0x10;
      F = f;
    }

private int BitOperation(int bit, int regIndex) {

    switch (regIndex) {

        case 0: BIT(bit,B); return 8;

        case 1: BIT(bit,C); return 8;

        case 2: BIT(bit,D); return 8;

        case 3: BIT(bit,E); return 8;

        case 4: BIT(bit,H); return 8;

        case 5: BIT(bit,L); return 8;

        case 6: { ushort addr=r8sToUshort(H,L); byte v=bus.Read(addr); BIT(bit,v); return 12; }

        case 7: BIT(bit,A); return 8;

        default: return 8;

    }

}

 

private int ResOperation(int bit, int regIndex) {

    switch (regIndex) {

        case 0: B=RES(bit,B); return 8;

        case 1: C=RES(bit,C); return 8;

        case 2: D=RES(bit,D); return 8;

        case 3: E=RES(bit,E); return 8;

        case 4: H=RES(bit,H); return 8;

        case 5: L=RES(bit,L); return 8;

        case 6: { ushort addr=r8sToUshort(H,L); byte v=bus.Read(addr); v=RES(bit,v); bus.Write(addr,v); return 16; }

        case 7: A=RES(bit,A); return 8;

        default: return 8;

    }

}

 

private int SetOperation(int bit, int regIndex) {

    switch (regIndex) {

        case 0: B=SET(bit,B); return 8;

        case 1: C=SET(bit,C); return 8;

        case 2: D=SET(bit,D); return 8;

        case 3: E=SET(bit,E); return 8;

        case 4: H=SET(bit,H); return 8;

        case 5: L=SET(bit,L); return 8;

        case 6: { ushort addr=r8sToUshort(H,L); byte v=bus.Read(addr); v=SET(bit,v); bus.Write(addr,v); return 16; }

        case 7: A=SET(bit,A); return 8;

        default: return 8;

    }

}

 

 

private int ExecuteCB(byte cbOp) {

    ushort addr;

 

    switch (cbOp) {

        // --- RLC r ---

        case 0x00: proc_RLC_r8(ref B); return 8;

        case 0x01: proc_RLC_r8(ref C); return 8;

        case 0x02: proc_RLC_r8(ref D); return 8;

        case 0x03: proc_RLC_r8(ref E); return 8;

        case 0x04: proc_RLC_r8(ref H); return 8;

        case 0x05: proc_RLC_r8(ref L); return 8;

        case 0x06: addr=r8sToUshort(H,L); { byte v=bus.Read(addr); proc_RLC_r8(ref v); bus.Write(addr,v);} return 16;

        case 0x07: proc_RLC_r8(ref A); return 8;

 

        // --- RRC r ---

        case 0x08: proc_RRC_r8(ref B); return 8;

        case 0x09: proc_RRC_r8(ref C); return 8;

        case 0x0A: proc_RRC_r8(ref D); return 8;

        case 0x0B: proc_RRC_r8(ref E); return 8;

        case 0x0C: proc_RRC_r8(ref H); return 8;

        case 0x0D: proc_RRC_r8(ref L); return 8;

        case 0x0E: addr=r8sToUshort(H,L); { byte v=bus.Read(addr); proc_RRC_r8(ref v); bus.Write(addr,v);} return 16;

        case 0x0F: proc_RRC_r8(ref A); return 8;

 

        // --- RL r ---

        case 0x10: proc_RL_r8(ref B,false); return 8;

        case 0x11: proc_RL_r8(ref C,false); return 8;

        case 0x12: proc_RL_r8(ref D,false); return 8;

        case 0x13: proc_RL_r8(ref E,false); return 8;

        case 0x14: proc_RL_r8(ref H,false); return 8;

        case 0x15: proc_RL_r8(ref L,false); return 8;

        case 0x16: addr=r8sToUshort(H,L); { byte v=bus.Read(addr); proc_RL_r8(ref v,true); bus.Write(addr,v);} return 16;

        case 0x17: proc_RL_r8(ref A,true); return 8;

 

        // --- RR r ---

        case 0x18: proc_RR_r8(ref B,false); return 8;

        case 0x19: proc_RR_r8(ref C,false); return 8;

        case 0x1A: proc_RR_r8(ref D,false); return 8;

        case 0x1B: proc_RR_r8(ref E,false); return 8;

        case 0x1C: proc_RR_r8(ref H,false); return 8;

        case 0x1D: proc_RR_r8(ref L,false); return 8;

        case 0x1E: addr=r8sToUshort(H,L); { byte v=bus.Read(addr); proc_RR_r8(ref v,true); bus.Write(addr,v);} return 16;

        case 0x1F: proc_RR_r8(ref A,true); return 8;

 

        // --- SLA r ---

        case 0x20: B=SLA(B); return 8;

        case 0x21: C=SLA(C); return 8;

        case 0x22: D=SLA(D); return 8;

        case 0x23: E=SLA(E); return 8;

        case 0x24: H=SLA(H); return 8;

        case 0x25: L=SLA(L); return 8;

        case 0x26: addr=r8sToUshort(H,L); { byte v=bus.Read(addr); v=SLA(v); bus.Write(addr,v);} return 16;

        case 0x27: A=SLA(A); return 8;

 

        // --- SRA r ---

        case 0x28: B=SRA(B); return 8;

        case 0x29: C=SRA(C); return 8;

        case 0x2A: D=SRA(D); return 8;

        case 0x2B: E=SRA(E); return 8;

        case 0x2C: H=SRA(H); return 8;

        case 0x2D: L=SRA(L); return 8;

        case 0x2E: addr=r8sToUshort(H,L); { byte v=bus.Read(addr); v=SRA(v); bus.Write(addr,v);} return 16;

        case 0x2F: A=SRA(A); return 8;

 

        // --- SWAP r ---

        case 0x30: B=SWAP(B); return 8;

        case 0x31: C=SWAP(C); return 8;

        case 0x32: D=SWAP(D); return 8;

        case 0x33: E=SWAP(E); return 8;

        case 0x34: H=SWAP(H); return 8;

        case 0x35: L=SWAP(L); return 8;

        case 0x36: addr=r8sToUshort(H,L); { byte v=bus.Read(addr); v=SWAP(v); bus.Write(addr,v);} return 16;

        case 0x37: A=SWAP(A); return 8;

 

        // --- SRL r ---

        case 0x38: B=SRL(B); return 8;

        case 0x39: C=SRL(C); return 8;

        case 0x3A: D=SRL(D); return 8;

        case 0x3B: E=SRL(E); return 8;

        case 0x3C: H=SRL(H); return 8;

        case 0x3D: L=SRL(L); return 8;

        case 0x3E: addr=r8sToUshort(H,L); { byte v=bus.Read(addr); v=SRL(v); bus.Write(addr,v);} return 16;

        case 0x3F: A=SRL(A); return 8;

    }

 

    // --- BIT 40–7F, RES 80–BF, SET C0–FF ---

    if (cbOp >= 0x40 && cbOp <= 0x7F) {

        int bit = (cbOp - 0x40) / 8;

        int regIndex = (cbOp - 0x40) % 8;

        return BitOperation(bit, regIndex);

    }

    if (cbOp >= 0x80 && cbOp <= 0xBF) {

        int bit = (cbOp - 0x80) / 8;

        int regIndex = (cbOp - 0x80) % 8;

        return ResOperation(bit, regIndex);

    }

    if (cbOp >= 0xC0 && cbOp <= 0xFF) {

        int bit = (cbOp - 0xC0) / 8;

        int regIndex = (cbOp - 0xC0) % 8;

        return SetOperation(bit, regIndex);

    }

 

    Console.WriteLine($"Unhandled CB opcode {cbOp:X2} at PC={PC-1:X4}");

    Environment.Exit(1);

    return 0;

}    void LD_HL_SP_e8 () {
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
    void LogCpuState() {
      string regs = $"A:{A:X2} F:{F:X2} B:{B:X2} C:{C:X2} D:{D:X2} E:{E:X2} H:{H:X2} L:{L:X2} SP:{SP:X4} PC:{PC:X4}";
        byte[] pcBuf = new byte[4];
        for (int i = 0; i < 4; i++) {
          if (PC < 0xFFFF){
          int addr = (PC + i) & 0xFFFF;
          pcBuf[i] = bus.Read(addr);
          }
        }
        string pcMem = $"PCMEM:{pcBuf[0]:X2},{pcBuf[1]:X2},{pcBuf[2]:X2},{pcBuf[3]:X2}";
        Console.WriteLine($"{regs} {pcMem}");
    }
     public int Step () {
       if (first) {
         first = false;
       // LogCpuState();
       }

       if (eiPending) {
           IME = true;
           eiPending = false;
       }
       int ic = handleInterrupts();
       if (ic > 0) { isHalted = false; return ic;}
       if (isHalted) return 4;
       byte opCode;
       if (haltBug) {
        opCode = bus.Read(PC);
        haltBug = false;
       } else {
         opCode = bus.Read(PC++);
       }
       //Console.WriteLine($"Executing opcode {opCode:X2} at PC={PC -1:X4}, SP={SP:X4}");
       int cycles = Execute(opCode);
       // LogCpuState();
       return cycles;
     }

     private int Execute(byte opCode) {
       switch(opCode) {

         /*NOP*/           case 0x00: return 4;
         /*LD BC n16 */    case 0x01: ushortToBytes(fetchImm16(), ref B, ref C);
                                      return 12;
         /*LD [BC] A*/    case 0x02:  bus.Write(r8sToUshort(B, C), A);
                                      return 8;
         /*INC BC*/   case 0x03: incR8sAsUshort(ref B,ref C); return 8;
         /*INC B*/   case 0x04: proc_INC_r8(ref B);
                                return 4;
         /*DEC*/   case 0x05: proc_DEC_r8(ref B); return 4;
         /*LD B n8*/    case 0x06: B = fetchImm8();  
                                   return 8;
         /*RLCA*/  case 0x07: proc_RLC_r8(ref A);  return 4;
         /*LD*/    case 0x08: {
                                var a16 = fetchImm16();
                                bus.Write(a16, (byte)(SP & 0xFF)); // i don't know if this is a new sp
                                bus.Write(a16 + 1, (byte)((SP >> 8) & 0xFF)); // i don't know if this should be an increased sp 
                              }
                              return 20;
         /*ADD*/   case 0x09: proc_ADD_HL_r16(r8sToUshort(B, C));  return 8;
         /*LD A, [BC] */    case 0x0A: A = bus.Read(r8sToUshort(B, C));
                                       return 8;
         /*DEC BC*/   case 0x0B: decR8sAsUshort(ref B, ref C);  return 8;
         /*INC C*/   case 0x0C: proc_INC_r8(ref C);  
                                return 4;
         /*DEC C*/   case 0x0D: proc_DEC_r8(ref C);  return 4;
         /*LD C n8*/    case 0x0E: C = fetchImm8();
                                   return 8;
         /*RRCA*/  case 0x0F: proc_RRC_r8(ref A); return 4;
         /*STOP*/  case 0x10: Console.WriteLine(
                                  $"STOP executed at PC=0x{{(PC -1):X4}}");  
                              PC++;
                              isStopped = true;
                              return 4;
         /*LD DE n16*/    case 0x11: ushortToBytes(fetchImm16(), ref D, ref E);  
                                     return 12;
         /*LD [DE] A*/    case 0x12: bus.Write(r8sToUshort(D, E), A);
                                     return 8;
         /*INC D E*/   case 0x13: incR8sAsUshort(ref D,ref E);  return 8;
         /*INC D*/   case 0x14: proc_INC_r8(ref D);  
                                return 4;
         /*DEC D*/   case 0x15: proc_DEC_r8(ref D); return 4;
         /*LD D n8*/    case 0x16: D = fetchImm8();  
                                   return 8;
         /*RLA*/   case 0x17: proc_RL_r8(ref A, true);  return 4;
         /*JR e8 */    case 0x18: return proc_JR_COND(true); 
         /*ADD*/   case 0x19: proc_ADD_HL_r16(r8sToUshort(D, E)); return 8;
         /*LD A, [DE]*/    case 0x1A:  A = bus.Read(r8sToUshort(D, E));
                                       return 8;
         /*DEC DE*/   case 0x1B:  decR8sAsUshort(ref D, ref E);  return 8;
         /*INC E*/   case 0x1C: proc_INC_r8(ref E);  
                                return 4;
         /*DEC E*/   case 0x1D: proc_DEC_r8(ref E); return 4;
         /*LD E n8 */    case 0x1E: E = fetchImm8();
                                    return 8;
         /*RRA*/   case 0x1F: proc_RR_r8(ref A, true);  return 4;
         /*JR NZ e8*/    case 0x20:  return proc_JR_COND(!isFlagSet(FLAG.Z)); 
         /*LD HL n16*/    case 0x21: ushortToBytes(fetchImm16(), ref H, ref L);   
                                     return 12;
         /*LD [HL+] A*/   case 0x22: bus.Write(r8sToUshort(H, L), A);
                                     incR8sAsUshort(ref H, ref L);
                                     return 8;
         /*INC HL*/   case 0x23: incR8sAsUshort(ref H,ref L); return 8;
         /*INC H*/   case 0x24: proc_INC_r8(ref H);  
                                return 4;
         /*DEC H*/   case 0x25: proc_DEC_r8(ref H); return 4;
         /*LD H n8*/    case 0x26: H = fetchImm8();;  
                                   return 8;
         /*DAA*/   case 0x27: proc_DAA();  return 4;
         /*JR Z e8*/    case 0x28: return proc_JR_COND(isFlagSet(FLAG.Z));  
         /*ADD HL HL*/   case 0x29: proc_ADD_HL_r16(r8sToUshort(H, L));  return 8;
         /*LD A, [HL+] */    case 0x2A:  A = bus.Read(r8sToUshort(H, L)); 
                                         incR8sAsUshort(ref H,ref L);
                                         return 8;
         /*DEC HL*/   case 0x2B: decR8sAsUshort(ref H, ref L);  return 8;
         /*INC L*/   case 0x2C: proc_INC_r8(ref L);  
                                return 4;
         /*DEC L*/   case 0x2D: proc_DEC_r8(ref L);  return 4;
         /*LD L, n8*/    case 0x2E: L = fetchImm8(); return 8;
         /*CPL*/   case 0x2F: 
                            A = (byte)~A;
                            setFlag(FLAG.N, true); 
                            setFlag(FLAG.H, true);   return 4;
         /*JR NC e8*/    case 0x30: return proc_JR_COND(!isFlagSet(FLAG.C));
         /*LD SP, n16 */    case 0x31:  SP = fetchImm16(); 
                                        // Console.WriteLine($"LD SP, {SP:X4}");
                                        return 12;
         /*LD [HL-] A*/    case 0x32: bus.Write(r8sToUshort(H, L), A);
                                      decR8sAsUshort(ref H, ref L);
                                      return 8;
         /*INC SP*/   case 0x33: SP++;  return 8;
         /*INC HL */ case 0x34: {
                                  var addr = r8sToUshort(H, L);
                                  byte val = bus.Read(addr);
                                  proc_INC_r8(ref val); 
                                  bus.Write(addr, val);
                                  return 12;
                                } 
         /*DEC [HL]*/   case 0x35: {  
                                     ushort addr = r8sToUshort(H, L);
                                     byte val = bus.Read(addr);
                                     proc_DEC_r8(ref val);
                                     bus.Write(addr, val);
                                     return 12;
                                   }
         /*LD [HL] n8*/    case 0x36: bus.Write(r8sToUshort(H, L), fetchImm8());
                                      return 12;
         /*SCF*/   case 0x37: setFlag(FLAG.N, false); setFlag(FLAG.H, false); setFlag(FLAG.C, true); return 4;
         /*JR C*/    case 0x38: return proc_JR_COND(isFlagSet(FLAG.C));
         /*ADD*/   case 0x39: proc_ADD_HL_r16(SP); return 8;
         /*LD A, [HL-] */    case 0x3A:  A = bus.Read(r8sToUshort(H, L)); 
                                         decR8sAsUshort(ref H,ref L);
                                         return 8;
         /*DEC*/   case 0x3B: SP--; return 8;
         /*INC A*/   case 0x3C: proc_INC_r8(ref A); 
                                return 4;
         /*DEC A*/   case 0x3D: proc_DEC_r8(ref A); return 4;
         /*LD A, n8*/    case 0x3E: A = fetchImm8();
                                    return 8;
         /*CCF*/   case 0x3F: setFlag(FLAG.N, false); setFlag(FLAG.H, false); setFlag(FLAG.C, !isFlagSet(FLAG.C));  return 4;

         /*LD B, B */    case 0x40:  return 4;
         /*LD*/    case 0x41: B = C; return 4;
         /*LD*/    case 0x42: B = D; return 4;
         /*LD*/    case 0x43: B = E; return 4;
         /*LD*/    case 0x44: B = H; return 4;
         /*LD*/    case 0x45: B = L; return 4;
         /*LD*/    case 0x46: B = bus.Read(r8sToUshort(H, L)); 
                              return 8;
         /*LD*/    case 0x47: B = A;  return 4;
         /*LD*/    case 0x48: C = B;  return 4;
         /*LD C, C*/    case 0x49: /*C = C;*/   return 4;
         /*LD*/    case 0x4A: C = D; return 4;
         /*LD*/    case 0x4B: C = E; return 4;
         /*LD*/    case 0x4C: C = H; return 4;
         /*LD*/    case 0x4D: C = L; return 4;
         /*LD*/    case 0x4E: C = bus.Read(r8sToUshort(H, L)); return 8;
         /*LD*/    case 0x4F: C = A; return 4;

         /*LD*/    case 0x50: D = B;  return 4;
         /*LD*/    case 0x51: D = C; return 4;
         /*LD D,D */    case 0x52:   return 4;
         /*LD*/    case 0x53: D = E; return 4;
         /*LD*/    case 0x54: D = H; return 4;
         /*LD*/    case 0x55: D = L; return 4;
         /*LD*/    case 0x56: D = bus.Read(r8sToUshort(H, L)); 
                              return 8;
         /*LD*/    case 0x57: D = A; return 4;
         /*LD*/    case 0x58: E = B; return 4;
         /*LD*/    case 0x59: E = C;  return 4;
         /*LD*/    case 0x5A: E = D; return 4;
         /*LD E,E */    case 0x5B:   return 4;
         /*LD*/    case 0x5C: E = H; return 4;
         /*LD*/    case 0x5D: E = L; return 4;
         /*LD*/    case 0x5E: E = bus.Read(r8sToUshort(H, L)); return 8;
         /*LD*/    case 0x5F: E = A; return 4;

         /*LD*/    case 0x60: H = B;  return 4;
         /*LD*/    case 0x61: H = C; return 4;
         /*LD*/    case 0x62: H = D; return 4;
         /*LD*/    case 0x63: H = E; return 4;
         /*LD H,H*/    case 0x64: return 4;
         /*LD*/    case 0x65: H = L; return 4;
         /*LD*/    case 0x66: H = bus.Read(r8sToUshort(H, L)); 
                              return 8;
         /*LD*/    case 0x67: H = A; return 4;
         /*LD*/    case 0x68: L = B; return 4;
         /*LD*/    case 0x69: L = C; return 4;
         /*LD*/    case 0x6A: L = D; return 4;
         /*LD*/    case 0x6B: L = E; return 4;
         /*LD*/    case 0x6C: L = H; return 4;
         /*LD L,L*/    case 0x6D: return 4;
         /*LD*/    case 0x6E: L = bus.Read(r8sToUshort(H, L)); return 8;
         /*LD*/    case 0x6F: L = A; return 4;

         /*LD*/    case 0x70: bus.Write(r8sToUshort(H, L), B); return 8;
         /*LD*/    case 0x71: bus.Write(r8sToUshort(H, L), C); return 8;
         /*LD*/    case 0x72: bus.Write(r8sToUshort(H, L), D); return 8;
         /*LD*/    case 0x73: bus.Write(r8sToUshort(H, L), E); return 8;
         /*LD*/    case 0x74: bus.Write(r8sToUshort(H, L), H); return 8;
         /*LD*/    case 0x75: bus.Write(r8sToUshort(H, L), L); return 8;
         /*LD HALT*/    case 0x76: 
                              if (!IME && (bus.Read(0xFFFF) & bus.Read(0xFF0F)) != 0) {
                                // halt bug triggered
                                isHalted = false; // cpu doesn't truely halt
                                haltBug = true; // handled in the next cycle
                              } else {
                                isHalted = true; 
                              }
                              return 4;
         /*LD*/    case 0x77: bus.Write(r8sToUshort(H, L), A); return 8;
         /*LD*/    case 0x78: A = B; return 4;
         /*LD*/    case 0x79: A = C; return 4;
         /*LD*/    case 0x7A: A = D; return 4;
         /*LD*/    case 0x7B: A = E; return 4;
         /*LD*/    case 0x7C: A = H; return 4;
         /*LD*/    case 0x7D: A = L; return 4;
         /*LD*/    case 0x7E: A = bus.Read(r8sToUshort(H, L)); return 8;
         /*LD A, A*/    case 0x7F: return 4;

         /*ADD*/   case 0x80: proc_ADD_A_r8(B); return 4;
         /*ADD*/   case 0x81: proc_ADD_A_r8(C); return 4;
         /*ADD*/   case 0x82: proc_ADD_A_r8(D); return 4;
         /*ADD*/   case 0x83: proc_ADD_A_r8(E); return 4;
         /*ADD*/   case 0x84: proc_ADD_A_r8(H); return 4;
         /*ADD*/   case 0x85: proc_ADD_A_r8(L); return 4;
         /*ADD*/   case 0x86: {
                                ushort addr = r8sToUshort(H, L);
                                byte hl = bus.Read(addr);
                                proc_ADD_A_r8(hl); 
                                return 8;
                              }
         /*ADD*/   case 0x87: proc_ADD_A_r8(A); return 4;
         /*ADD*/   case 0x88: proc_ADC_A_r8(B); return 4;
         /*ADD*/   case 0x89: proc_ADC_A_r8(C); return 4;
         /*ADD*/   case 0x8A: proc_ADC_A_r8(D); return 4;
         /*ADD*/   case 0x8B: proc_ADC_A_r8(E); return 4;
         /*ADD*/   case 0x8C: proc_ADC_A_r8(H); return 4;
         /*ADD*/   case 0x8D: proc_ADC_A_r8(L); return 4;
         /*ADC (HL)*/   case 0x8E: {
                                     ushort addr = r8sToUshort(H, L);
                                     byte hl = bus.Read(addr);
                                     proc_ADC_A_r8(hl); 
                                     return 8;
                                   }
         /*ADD*/   case 0x8F: proc_ADC_A_r8(A); return 4;

         /*SUB*/   case 0x90: proc_SUB_A_r8(B);  return 4;
         /*SUB*/   case 0x91: proc_SUB_A_r8(C); return 4;
         /*SUB*/   case 0x92: proc_SUB_A_r8(D); return 4;
         /*SUB*/   case 0x93: proc_SUB_A_r8(E); return 4;
         /*SUB*/   case 0x94: proc_SUB_A_r8(H); return 4;
         /*SUB*/   case 0x95: proc_SUB_A_r8(L); return 4;
         /*SUB*/   case 0x96: {
                                byte hl = busReadHL(); 
                                proc_SUB_A_r8(hl); 
                                return 8;
                              }
         /*SUB*/   case 0x97: proc_SUB_A_r8(A);  return 4;


         /*SBC*/   case 0x98: proc_SBC_A_r8(B);  return 4;
         /*SBC*/   case 0x99: proc_SBC_A_r8(C); return 4;
         /*SBC*/   case 0x9A: proc_SBC_A_r8(D);  return 4;
         /*SBC*/   case 0x9B: proc_SBC_A_r8(E);  return 4;
         /*SBC*/   case 0x9C: proc_SBC_A_r8(H);  return 4;
         /*SBC*/   case 0x9D: proc_SBC_A_r8(L);  return 4;
         /*SBC*/   case 0x9E: {
                                ushort addr = r8sToUshort(H, L);
                                byte hl = bus.Read(addr);
                                proc_SBC_A_r8(hl); 
                                return 8;
                              }
         /*SBC*/   case 0x9F: proc_SBC_A_r8(A);  return 4;

         /*AND*/   case 0xA0: proc_AND_A_r8(B);  return 4;
         /*AND*/   case 0xA1: proc_AND_A_r8(C); return 4;
         /*AND*/   case 0xA2: proc_AND_A_r8(D); return 4;
         /*AND*/   case 0xA3: proc_AND_A_r8(E); return 4;
         /*AND*/   case 0xA4: proc_AND_A_r8(H); return 4;
         /*AND*/   case 0xA5: proc_AND_A_r8(L); return 4;
         /*AND*/   case 0xA6: {
                                proc_AND_A_r8(busReadHL()); 
                                return 8;
                              }
         /*AND*/   case 0xA7: proc_AND_A_r8(A); return 4;


         /*XOR*/   case 0xA8: proc_XOR_A_r8(B);  return 4;
         /*XOR*/   case 0xA9: proc_XOR_A_r8(C); return 4;
         /*XOR*/   case 0xAA: proc_XOR_A_r8(D); return 4;
         /*XOR*/   case 0xAB: proc_XOR_A_r8(E); return 4;
         /*XOR*/   case 0xAC: proc_XOR_A_r8(H); return 4;
         /*XOR*/   case 0xAD: proc_XOR_A_r8(L); return 4;
         /*XOR*/   case 0xAE: {
                                byte hl = busReadHL();
                                proc_XOR_A_r8(hl); 
                                return 8;
                              }
         /*XOR*/   case 0xAF: proc_XOR_A_r8(A); return 4;


         /*OR*/   case 0xB0: proc_OR_A_r8(B);  return 4;
         /*OR*/   case 0xB1: proc_OR_A_r8(C); return 4;
         /*OR*/   case 0xB2: proc_OR_A_r8(D); return 4;
         /*OR*/   case 0xB3: proc_OR_A_r8(E); return 4;
         /*OR*/   case 0xB4: proc_OR_A_r8(H); return 4;
         /*OR*/   case 0xB5: proc_OR_A_r8(L); return 4;
         /*OR*/   case 0xB6: {
                               byte hl = busReadHL();
                               proc_OR_A_r8(hl); 
                               return 8;
                             }
         /*OR*/   case 0xB7: proc_OR_A_r8(A);return 4;

         /*CP*/    case 0xB8: proc_CP_A_r8(B); return 4;
         /*CP*/    case 0xB9: proc_CP_A_r8(C); return 4;
         /*CP*/    case 0xBA: proc_CP_A_r8(D); return 4;
         /*CP*/    case 0xBB: proc_CP_A_r8(E); return 4;
         /*CP*/    case 0xBC: proc_CP_A_r8(H); return 4;
         /*CP*/    case 0xBD: proc_CP_A_r8(L); return 4;
         /*CP*/    case 0xBE: {
                                proc_CP_A_r8(busReadHL()); 
                                return 8;
                              }
         /*CP*/    case 0xBF: proc_CP_A_r8(A); return 4;

         /*RET NZ*/   case 0xC0: {
                                   bool t = !isFlagSet(FLAG.Z);
                                   if (t) {
                                     byte h = 0, l = 0;
                                     proc_POP_r16(ref h, ref l);
                                     PC = r8sToUshort(h, l);
                                   } 
                                   return t ? 20 : 8;
                                 }
         /*POP*/   case 0xC1: proc_POP_r16(ref B, ref C); return 12;
         /*JP NZ a16 */    case 0xC2: {
                                        ushort a = fetchImm16();
                                        bool t = !isFlagSet(FLAG.Z);
                                        if (t) PC = a; 
                                        return t ? 16 : 12;
                                      }
         /*JP A16*/    case 0xC3: PC = fetchImm16(); // no PC++ after JP!! 
                                  return 16;
         /*CALL NZ*/  case 0xC4: {
                                   ushort a = fetchImm16();
                                   bool take = !isFlagSet(FLAG.Z);
                                   if (take) proc_CALL_COND_n16(true, a);
                                   return take ? 24 : 12;
                                 }
         /*PUSH BC*/  case 0xC5: proc_PUSH_r16(B, C);  return 16;
         /*ADD A, n8*/   case 0xC6: proc_ADD_A_r8(fetchImm8());  return 8;
         /*RST $00*/   case 0xC7: proc_RST(0x0000);  return 16;
         /*RET Z*/   case 0xC8: {
                                  bool take = isFlagSet(FLAG.Z);
                                  if (take) proc_RET_COND(true);
                                  return take ? 20 : 8;
                                }
         /*RET*/   case 0xC9: 
                // Console.WriteLine($"RET: pc={PC-1:X4} -> SP={SP:X4}");
                                  proc_RET_COND(true);  return 16;
         /*JP Z*/    case 0xCA: {
                                  ushort a = fetchImm16();
                                  bool take = isFlagSet(FLAG.Z);
                                  if (take) PC = a;
                                  return take ? 16: 12;
                                }
         /*PREFIX*/case 0xCB: return ExecuteCB(bus.Read(PC++));
         /*CALL Z*/  case 0xCC:  {
                                   ushort a = fetchImm16();
                                   bool take = isFlagSet(FLAG.Z);
                                   if (take) proc_CALL_COND_n16(true, a); 
                                   return take ? 24 : 12;
                                 }
         /*CALL*/  case 0xCD: {
                                //proc_CALL_COND_n16(true, fetchImm16());  
                                ushort target = fetchImm16();
                                ushort returnAddr = PC;
      // Console.WriteLine($"CALL: {target:X4} from {returnAddr:X4} -> SP={SP:X4}");
                                proc_PUSH_r16((byte)(returnAddr >> 8), (byte)(returnAddr & 0xFF));
                                PC = target;
                                return 24;
                              }
         /*ADC a n8*/   case 0xCE: proc_ADC_A_r8(fetchImm8()); return 8;
         /*RST $08*/   case 0xCF: proc_RST(0x0008);  return 16;
         /*RET NC*/   case 0xD0: {
                                   bool take = !isFlagSet(FLAG.C);
                                   if (take) proc_RET_COND(true);  
                                   return take ? 20 : 8;
                                 }
         /*POP*/   case 0xD1: proc_POP_r16(ref D, ref E); return 12;
         /*JP NC*/    case 0xD2: {
                                   ushort a = fetchImm16();
                                   bool take = !isFlagSet(FLAG.C);
                                   if (take) PC = a;
                                   // proc_JP_COND_ADDR(!isFlagSet(FLAG.C), fetchImm16()); 

                                   return take ? 16 : 12;
                                 }
         /*ILLEGAL_D3*/case 0xD3:  return 4;
         /*CALL NC*/  case 0xD4: {
                                   // proc_CALL_COND_n16(!isFlagSet(FLAG.C), fetchImm16());  
                                   ushort a =  fetchImm16();
                                   bool take = !isFlagSet(FLAG.C);
                                   if (take) proc_CALL_COND_n16(true, a);
                                   return take ? 24 : 12;
                                 }
         /*PUSH*/  case 0xD5: proc_PUSH_r16(D, E); return 16;
         /*SUB A n8 */   case 0xD6: proc_SUB_A_r8(fetchImm8()); return 8;
         /*RST 10*/   case 0xD7: proc_RST(0x0010);  return 16;
         /*RET C*/   case 0xD8: {
                                  bool take = isFlagSet(FLAG.C);
                                  if (take) proc_RET_COND(take);  
                                  return take ? 20 : 8;
                                }
         /*RETI*/  case 0xD9: {
                                IME = true; 
                                proc_RET_COND(true); 
                                return 16;
                              }
         /*JP C*/    case 0xDA: {
                                  ushort a = fetchImm16();
                                  bool take = isFlagSet(FLAG.C);
                                  if (take) proc_JP_COND_ADDR(true, a); 
                                  return take ? 16 : 12;
                                }
         /*ILLEGAL_DB*/case 0xDB:  return 4;
         /*CALL C*/  case 0xDC: {
                                  ushort a = fetchImm16();
                                  bool take = isFlagSet(FLAG.C);
                                  if (take) proc_CALL_COND_n16(true, a);  
                                  return take ? 24 : 12;
                                }
         /*ILLEGAL_DD*/case 0xDD:  return 4;
         /*SBC a n8*/   case 0xDE: proc_SBC_A_r8(fetchImm8());  return 8;
         /*RST 18*/   case 0xDF: proc_RST(0x0018);  return 16;
         /*LDH a8 A*/   case 0xE0: { 
                                     byte imm = fetchImm8();
                                     bus.Write((ushort)(0xFF00 + imm), A);
                                     return 12;
                                   }
         /*POP*/   case 0xE1: proc_POP_r16(ref H, ref L); return 12;
         /*LDH [FF00 + c] A*/   case 0xE2:  {
                                              bus.Write((ushort)(0xFF00 + C), A);
                                              return 8;
                                            }
         /*ILLEGAL_E3*/case 0xE3:  return 4;
         /*ILLEGAL_E4*/case 0xE4:  return 4;
         /*PUSH*/  case 0xE5: proc_PUSH_r16(H, L);  return 16;
         /*AND A n8*/   case 0xE6: proc_AND_A_r8(fetchImm8());  return 8;
         /*RST 20*/   case 0xE7: proc_RST(0x0020);  return 16;
         /*ADD SP e8*/   case 0xE8: {
                                      sbyte e = (sbyte)fetchImm8();
                                      byte low = (byte)(SP & 0xFF);
                                      int result = low + (byte)e;
                                      bool halfCarry = ((low & 0xF) + ((byte)e & 0xF)) > 0xF;
                                      bool carry = result > 0xFF;

                                      SP = (ushort)(SP + e);
                                      SetFlag(FLAG.Z, false);
                                      SetFlag(FLAG.N, false);
                                      SetFlag(FLAG.H, halfCarry);
                                      SetFlag(FLAG.C, carry);
                                      return 16;
                                    }
         /*JP HL n16 */    case 0xE9: PC = (ushort) (L | (H << 8)); 
                                      return 4;
         /*LD A16, A*/    case 0xEA: bus.Write(fetchImm16(), A);
                                     return 16;
         /*ILLEGAL_EB*/case 0xEB: return 4;
         /*ILLEGAL_EC*/case 0xEC: return 4;
         /*ILLEGAL_ED*/case 0xED: return 4;
         /*XOR A, n8*/   case 0xEE: proc_XOR_A_r8(fetchImm8());  return 8;
         /*RST 28*/   case 0xEF: proc_RST(0x0028);  return 16;
         /*LDH A, [FF00 + imm]*/   case 0xF0: { 
                                                byte imm = fetchImm8();
                                                ushort addr = (ushort)(0xFF00 + imm);
                                                A = bus.Read(addr);
                                                return 12;
                                              } 
         /*POP*/   case 0xF1: proc_POP_r16(ref A, ref F); F &= 0xF0; /* bits 3-0 are always zero) */  return 12;
         /*LDH A, [FF00 + C]*/   case 0xF2: {
                                              A = bus.Read((ushort)(0xFF00 + C));
                                              return 8;
                                            }
         /*DI*/    case 0xF3: IME = false; 
                              return 4;
         /*ILLEGAL_F4*/case 0xF4: return 4;
         /*PUSH AF*/  case 0xF5: proc_PUSH_r16(A, F);  return 16;
         /*OR a, n8*/    case 0xF6: proc_OR_A_r8(fetchImm8());  return 8;
         /*RST 30*/   case 0xF7: proc_RST(0x0030);  return 16;
         /*LD HL, SP + e8*/    case 0xF8:  {
                                             sbyte e = (sbyte)fetchImm8();
                                             byte low = (byte)(SP & 0xFF);
                                             int result = low + (byte)e;
                                             bool halfCarry = ((low & 0xF) + ((byte)e & 0xF)) > 0xF;
                                             bool carry = result > 0xFF;

                                             ushort r = (ushort)(SP + e);
                                             H = (byte)(r >> 8);
                                             L = (byte)(r & 0xFF);

                                             SetFlag(FLAG.Z, false);
                                             SetFlag(FLAG.N, false);
                                             SetFlag(FLAG.H, halfCarry);
                                             SetFlag(FLAG.C, carry);
                                             return 12;
                                           }
         /*LD SP HL*/    case 0xF9: SP = r8sToUshort(H, L);   return 8;
         /*LD A, a16*/    case 0xFA:  A = bus.Read(fetchImm16());
                                      return 16;
         /*EI*/    case 0xFB: eiPending = true;   return 4;
         /*ILLEGAL_FC*/case 0xFC: return 4;
         /*ILLEGAL_FD*/case 0xFD: return 4;
         /*CP A n8 */    case 0xFE: proc_CP_A_r8(fetchImm8());  return 8;
         /*RST 38*/   case 0xFF: {
                                 proc_RST(0x0038); 
                                 // ushort retAddr = PC;
                                 // SP--;
                                 // bus.Write(SP, (byte)(retAddr >> 8));
                                 // SP--;
                                 // bus.Write(SP, (byte)(retAddr & 0xFF));
                                 // PC = 0x0038;
                                   return 16; // this is known to be buggy!
                                 }
         default: Console.WriteLine($"0x{opCode:X2} not implemented!"); Environment.Exit(1); return 0;
       }
     }
  }
}
