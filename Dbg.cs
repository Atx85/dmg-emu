using System;

namespace GB {
class Dbg {
  Bus bus;
  string msg;
  public Dbg (ref Bus b) {
    this.bus = b;
    msg = "";
  }
  public void Update() {
    Console.Clear();
    Console.WriteLine($"0xFF02: {bus.Read(0xFF02)}");
    if (bus.Read(0xFF02) == 0x81) {
      char c = (char)bus.Read(0xFF01);
      Console.Write(c);
      msg = $"{msg}{c}";
      bus.Write(0xFF02, 0);
    }
  }

  public void Print() {
    if (msg.Length > 0) {
      Console.WriteLine($"DBG: {msg}"); 
    }
  }
}
}
