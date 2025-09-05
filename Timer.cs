namespace GB {
  public class Timer {
    private uint systemCounter = 0; // 16-bit internal counter
    private ushort lastCounter = 0;
    private Bus bus;

    private byte tima = 0;
    private  byte tma = 0;
    private byte tac = 0;

    private bool overflowPending = false;
    private int overflowDelay = 0;

    public Timer (Bus bus) {
      this.bus = bus;
      systemCounter = 0;
    }

    public void Tick(int cycles) {
      lastCounter = (ushort)systemCounter;
      systemCounter = (systemCounter + (uint)cycles) & 0xFFFF;

      bool timerEnabled = (tac & 0x04) != 0;
      if (timerEnabled) {
        int bit; 
        int sel = (tac & 0x03); 
        switch (sel) {
          case  0: bit = 9; break;  // 4096 Hz
          case  1: bit = 3; break;  // 262144 Hz
          case  2: bit = 5; break;  // 65536 Hz
          case  3:  // 16384 Hz
          default: bit = 7; break; 
        };

        bool prev = ((lastCounter >> bit) & 1) != 0;
        bool curr = ((systemCounter >> bit) & 1) != 0;

        // Detect falling edge
        if (prev && !curr) {
          IncrementTIMA();
        }
      }

      if (overflowPending) {
        if (--overflowDelay == 0) {
          tima = tma;
          byte flags = bus.Read(0xFF0F);
          bus.Write(0xFF0F, (byte)(flags | 0x04));
          overflowPending = false;
        }
      }
    }

    public byte ReadDIV() => (byte)(systemCounter >> 8);
    public void WriteDIV(byte val) { systemCounter = 0; }

    public byte ReadTIMA() => tima;
    public void WriteTIMA(byte val) { tima = val; }

    public byte ReadTMA() => tma;
    public void WriteTMA(byte val) { tma = val;} 

    public byte ReadTAC() => tac;
    public void WriteTAC(byte val) {tac = val; } 
    private void IncrementTIMA() {
      if (tima == 0xFF) {
        overflowPending = true;
        overflowDelay = 1;
      } else {
        tima++;
      }
    }
  }
}
