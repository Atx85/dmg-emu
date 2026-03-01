namespace DmgEmu.Core {
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
      for (int i = 0; i < cycles; i++) {
        StepOneCycle();
      }
    }

    public byte ReadDIV() => (byte)(systemCounter >> 8);
    public void WriteDIV(byte val) {
      bool prevSignal = TimerSignal(systemCounter, tac);
      systemCounter = 0;
      if (prevSignal && !TimerSignal(systemCounter, tac)) {
        IncrementTIMA();
      }
    }

    public byte ReadTIMA() => tima;
    public void WriteTIMA(byte val) { tima = val; }

    public byte ReadTMA() => tma;
    public void WriteTMA(byte val) { tma = val;} 

    public byte ReadTAC() => tac;
    public void WriteTAC(byte val) {
      bool prevSignal = TimerSignal(systemCounter, tac);
      tac = (byte)(val & 0x07);
      if (prevSignal && !TimerSignal(systemCounter, tac)) {
        IncrementTIMA();
      }
    } 
    private void IncrementTIMA() {
      if (tima == 0xFF) {
        tima = 0x00;
        overflowPending = true;
        overflowDelay = 1;
      } else {
        tima++;
      }
    }

    public TimerState GetState()
    {
      return new TimerState
      {
        SystemCounter = systemCounter,
        LastCounter = lastCounter,
        Tima = tima,
        Tma = tma,
        Tac = tac,
        OverflowPending = overflowPending,
        OverflowDelay = overflowDelay
      };
    }

    public void SetState(TimerState s)
    {
      systemCounter = s.SystemCounter & 0xFFFF;
      lastCounter = s.LastCounter;
      tima = s.Tima;
      tma = s.Tma;
      tac = s.Tac;
      overflowPending = s.OverflowPending;
      overflowDelay = s.OverflowDelay;
    }

    private void StepOneCycle() {
      ushort prevCounter = (ushort)systemCounter;
      systemCounter = (systemCounter + 1u) & 0xFFFF;
      lastCounter = prevCounter;

      bool prevSignal = TimerSignal(prevCounter, tac);
      bool currSignal = TimerSignal(systemCounter, tac);
      if (prevSignal && !currSignal) {
        IncrementTIMA();
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

    private static bool TimerSignal(uint counter, byte tacValue) {
      bool timerEnabled = (tacValue & 0x04) != 0;
      if (!timerEnabled) return false;

      int bit;
      switch (tacValue & 0x03) {
        case 0: bit = 9; break;
        case 1: bit = 3; break;
        case 2: bit = 5; break;
        default: bit = 7; break;
      }
      return ((counter >> bit) & 1u) != 0;
    }
  }

  public struct TimerState
  {
    public uint SystemCounter;
    public ushort LastCounter;
    public byte Tima;
    public byte Tma;
    public byte Tac;
    public bool OverflowPending;
    public int OverflowDelay;
  }
}
