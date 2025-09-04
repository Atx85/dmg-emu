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
   new Gameboy("./roms/01-special.gb");
   // new Gameboy("./roms/debug_flags_halfcarry.gb");
  }
}

