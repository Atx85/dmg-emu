namespace DmgEmu.Core
{
    public enum CpuBackend
    {
        Cpu2Structured = 2
    }

    public interface ICpuCore
    {
        int Step();
    }

    public sealed class Cpu2StructuredCoreAdapter : ICpuCore
    {
        public Cpu2Structured Inner { get; }

        public Cpu2StructuredCoreAdapter(Bus bus, IClock clock = null)
        {
            Inner = new Cpu2Structured(new BusCpuBus(bus), clock ?? new NullCpuClock(), new BusInterruptController(bus));
        }

        public int Step() => Inner.Step();

        public Cpu2StructuredSnapshot GetState()
        {
            return Inner.GetState();
        }

        public void SetState(Cpu2StructuredSnapshot s)
        {
            Inner.SetState(s);
        }
    }

    internal sealed class BusCpuBus : ICpuBus
    {
        private readonly Bus bus;

        public BusCpuBus(Bus bus)
        {
            this.bus = bus;
        }

        public byte Read(ushort addr) => bus.Read(addr);
        public void Write(ushort addr, byte value) => bus.Write(addr, value);
    }

    internal sealed class NullCpuClock : IClock
    {
        public void Advance(int cycles) { }
    }

    internal sealed class BusInterruptController : IInterruptController
    {
        private readonly Bus bus;

        public BusInterruptController(Bus bus)
        {
            this.bus = bus;
        }

        public InterruptFlags Pending()
        {
            byte ie = bus.Read(0xFFFF);
            byte flags = bus.Read(0xFF0F);
            return (InterruptFlags)(ie & flags & 0x1F);
        }

        public int HighestPendingBit(InterruptFlags flags)
        {
            int value = (int)flags;
            for (int i = 0; i < 5; i++)
            {
                if (((value >> i) & 1) != 0) return i;
            }
            return 0;
        }

        public void Clear(int bit)
        {
            byte flags = bus.Read(0xFF0F);
            flags = (byte)(flags & ~(1 << bit));
            bus.Write(0xFF0F, flags);
        }
    }
}
