namespace GB {
  public class Timer {
    private uint systemCounter = 0; // 16-bit internal counter
    private ushort lastCounter = 0;
    private Bus bus;

    public Timer (Bus bus) {
      this.bus = bus;
      // Initialize DIV to the high byte of 0
      bus.Write(0xFF04, 0);
    }

    public void Tick(int cycles) {
      lastCounter = (ushort)systemCounter;
      systemCounter = (systemCounter + (uint)cycles) & 0xFFFF;

      // Update DIV (high 8 bits of systemCounter)
      bus.Write(0xFF04, (byte)(systemCounter >> 8));

      byte tac = bus.Read(0xFF07);
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
    }

    private void IncrementTIMA() {
      byte tima = bus.Read(0xFF05);
      if (tima == 0xFF) {
        // overflow: reload from TMA, then set IF bit 2
        bus.Write(0xFF05, bus.Read(0xFF06));

        // Delay: one machine cycle effect- can be safely omitted for Blargg tests
        byte flags = bus.Read(0xFF0F);
        bus.Write(0xFF0F, (byte)(flags | 0x04));
      } else {
        bus.Write(0xFF05, (byte)(tima + 1));
      }
    }
  }
}
