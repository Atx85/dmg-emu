using System;
using System.IO;
using System.Collections.Generic;
using Gtk;
using Cairo;
using System.Reflection;
using System.Runtime.InteropServices;

using GB;
public class Program
{
  public static void Main(string[] args) {
//    Console.WriteLine(args[0]);
   // new Gameboy("./roms/01-special.gb");
   // new Gameboy("./roms/05-op rp.gb");
  // new Gameboy("./roms/rom.gb");
new Gameboy("./roms/02-interrupts.gb");
  }
}

