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
    if (bus.Read(0xFF02) == 0x81) {
      char c = (char)bus.Read(0xFF01);
      Console.Write(c);
      msg = $"{msg}{c}";
      bus.Write(0xFF02, 0);

      //if (c== '\n') {
        Console.WriteLine($"DBG: {msg.TrimEnd()}");
     //}
    }
  }

  public void Print() {
   // if (msg.Length > 0) {
   //   Console.WriteLine($"DBG: {msg}"); 
   // }
  }
}
}
