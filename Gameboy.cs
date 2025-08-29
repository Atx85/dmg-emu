using System;
using System.IO;
using System.Collections.Generic;
namespace GB {
public class Gameboy
{
   public Gameboy (string path) {
    // gb.bppTest();
    var cart = new Cartridge();
    cart.load(path);
    var bus = new Bus(ref cart);
    Cpu cpu = new Cpu(bus);
    while(cpu.Step() != 0) cpu.Step();

   }


   public void bppTest() 
  {
// https://www.huderlem.com/demos/gameboy2bpp.html
    //pan doc!!
    // int[] data = {0xFF, 0x00 , 0x7E , 0xFF , 0x85 , 0x81 , 0x89 , 0x83 , 0x93 , 0x85 , 0xA5 , 0x8B , 0xC9 , 0x97 , 0x7E , 0xFF};
    int[] data = {0x7C, 0x7C, 0x00, 0xC6, 0xC6, 0x00, 0x00, 0xFE, 0xC6, 0xC6, 0x00, 0xC6, 0xC6, 0x00, 0x00, 0x00};
    CachedMath CM = new CachedMath();
    for (var i = 0; i < data.Length; i++) {
      string bin1 = CM.bin[data[i]];
      string bin2 = CM.bin[data[i + 1]];

      List<int> row = new List<int>(); 
      for (var j = 0; j < bin1.Length; j++) {
          var binStr = $"{bin2[j]}{bin1[j]}";
          switch (binStr) {
            case "00" : row.Add(0); break;
            case "01" : row.Add(1); break;
            case "10" : row.Add(2); break;
            case "11" : row.Add(3); break;
          }
      }
      foreach (var num in row) 
        Console.Write($"{num}");
      Console.WriteLine($"\n");
      i++;
    }
  }

   public static  string DecimalToBinary(int data)
    {
      string result = string.Empty;
      int rem = 0;
      int num = data;
      if (num == 0) return "00000000";
      while (num > 0)
      {
        rem = num % 2;
        num = num / 2;
        result = rem.ToString() + $"{result}";
      }
      while (result.Length < 8) {
        result = $"0{result}";
      }
      return result;
    }
}

class CachedMath {
  public Dictionary<int, string> bin = new Dictionary<int, string>();
  
  public CachedMath () {
    bin.Add(0, "00000000 ");
    bin.Add(1, "00000001 ");
    bin.Add(2, "00000010 ");
    bin.Add(3, "00000011 ");
    bin.Add(4, "00000100 ");
    bin.Add(5, "00000101 ");
    bin.Add(6, "00000110 ");
    bin.Add(7, "00000111 ");
    bin.Add(8, "00001000 ");
    bin.Add(9, "00001001 ");
    bin.Add(10, "00001010 ");
    bin.Add(11, "00001011 ");
    bin.Add(12, "00001100 ");
    bin.Add(13, "00001101 ");
    bin.Add(14, "00001110 ");
    bin.Add(15, "00001111 ");
    bin.Add(16, "00010000 ");
    bin.Add(17, "00010001 ");
    bin.Add(18, "00010010 ");
    bin.Add(19, "00010011 ");
    bin.Add(20, "00010100 ");
    bin.Add(21, "00010101 ");
    bin.Add(22, "00010110 ");
    bin.Add(23, "00010111 ");
    bin.Add(24, "00011000 ");
    bin.Add(25, "00011001 ");
    bin.Add(26, "00011010 ");
    bin.Add(27, "00011011 ");
    bin.Add(28, "00011100 ");
    bin.Add(29, "00011101 ");
    bin.Add(30, "00011110 ");
    bin.Add(31, "00011111 ");
    bin.Add(32, "00100000 ");
    bin.Add(33, "00100001 ");
    bin.Add(34, "00100010 ");
    bin.Add(35, "00100011 ");
    bin.Add(36, "00100100 ");
    bin.Add(37, "00100101 ");
    bin.Add(38, "00100110 ");
    bin.Add(39, "00100111 ");
    bin.Add(40, "00101000 ");
    bin.Add(41, "00101001 ");
    bin.Add(42, "00101010 ");
    bin.Add(43, "00101011 ");
    bin.Add(44, "00101100 ");
    bin.Add(45, "00101101 ");
    bin.Add(46, "00101110 ");
    bin.Add(47, "00101111 ");
    bin.Add(48, "00110000 ");
    bin.Add(49, "00110001 ");
    bin.Add(50, "00110010 ");
    bin.Add(51, "00110011 ");
    bin.Add(52, "00110100 ");
    bin.Add(53, "00110101 ");
    bin.Add(54, "00110110 ");
    bin.Add(55, "00110111 ");
    bin.Add(56, "00111000 ");
    bin.Add(57, "00111001 ");
    bin.Add(58, "00111010 ");
    bin.Add(59, "00111011 ");
    bin.Add(60, "00111100 ");
    bin.Add(61, "00111101 ");
    bin.Add(62, "00111110 ");
    bin.Add(63, "00111111 ");
    bin.Add(64, "01000000 ");
    bin.Add(65, "01000001 ");
    bin.Add(66, "01000010 ");
    bin.Add(67, "01000011 ");
    bin.Add(68, "01000100 ");
    bin.Add(69, "01000101 ");
    bin.Add(70, "01000110 ");
    bin.Add(71, "01000111 ");
    bin.Add(72, "01001000 ");
    bin.Add(73, "01001001 ");
    bin.Add(74, "01001010 ");
    bin.Add(75, "01001011 ");
    bin.Add(76, "01001100 ");
    bin.Add(77, "01001101 ");
    bin.Add(78, "01001110 ");
    bin.Add(79, "01001111 ");
    bin.Add(80, "01010000 ");
    bin.Add(81, "01010001 ");
    bin.Add(82, "01010010 ");
    bin.Add(83, "01010011 ");
    bin.Add(84, "01010100 ");
    bin.Add(85, "01010101 ");
    bin.Add(86, "01010110 ");
    bin.Add(87, "01010111 ");
    bin.Add(88, "01011000 ");
    bin.Add(89, "01011001 ");
    bin.Add(90, "01011010 ");
    bin.Add(91, "01011011 ");
    bin.Add(92, "01011100 ");
    bin.Add(93, "01011101 ");
    bin.Add(94, "01011110 ");
    bin.Add(95, "01011111 ");
    bin.Add(96, "01100000 ");
    bin.Add(97, "01100001 ");
    bin.Add(98, "01100010 ");
    bin.Add(99, "01100011 ");
    bin.Add(100, "01100100 ");
    bin.Add(101, "01100101 ");
    bin.Add(102, "01100110 ");
    bin.Add(103, "01100111 ");
    bin.Add(104, "01101000 ");
    bin.Add(105, "01101001 ");
    bin.Add(106, "01101010 ");
    bin.Add(107, "01101011 ");
    bin.Add(108, "01101100 ");
    bin.Add(109, "01101101 ");
    bin.Add(110, "01101110 ");
    bin.Add(111, "01101111 ");
    bin.Add(112, "01110000 ");
    bin.Add(113, "01110001 ");
    bin.Add(114, "01110010 ");
    bin.Add(115, "01110011 ");
    bin.Add(116, "01110100 ");
    bin.Add(117, "01110101 ");
    bin.Add(118, "01110110 ");
    bin.Add(119, "01110111 ");
    bin.Add(120, "01111000 ");
    bin.Add(121, "01111001 ");
    bin.Add(122, "01111010 ");
    bin.Add(123, "01111011 ");
    bin.Add(124, "01111100 ");
    bin.Add(125, "01111101 ");
    bin.Add(126, "01111110 ");
    bin.Add(127, "01111111 ");
    bin.Add(128, "10000000 ");
    bin.Add(129, "10000001 ");
    bin.Add(130, "10000010 ");
    bin.Add(131, "10000011 ");
    bin.Add(132, "10000100 ");
    bin.Add(133, "10000101 ");
    bin.Add(134, "10000110 ");
    bin.Add(135, "10000111 ");
    bin.Add(136, "10001000 ");
    bin.Add(137, "10001001 ");
    bin.Add(138, "10001010 ");
    bin.Add(139, "10001011 ");
    bin.Add(140, "10001100 ");
    bin.Add(141, "10001101 ");
    bin.Add(142, "10001110 ");
    bin.Add(143, "10001111 ");
    bin.Add(144, "10010000 ");
    bin.Add(145, "10010001 ");
    bin.Add(146, "10010010 ");
    bin.Add(147, "10010011 ");
    bin.Add(148, "10010100 ");
    bin.Add(149, "10010101 ");
    bin.Add(150, "10010110 ");
    bin.Add(151, "10010111 ");
    bin.Add(152, "10011000 ");
    bin.Add(153, "10011001 ");
    bin.Add(154, "10011010 ");
    bin.Add(155, "10011011 ");
    bin.Add(156, "10011100 ");
    bin.Add(157, "10011101 ");
    bin.Add(158, "10011110 ");
    bin.Add(159, "10011111 ");
    bin.Add(160, "10100000 ");
    bin.Add(161, "10100001 ");
    bin.Add(162, "10100010 ");
    bin.Add(163, "10100011 ");
    bin.Add(164, "10100100 ");
    bin.Add(165, "10100101 ");
    bin.Add(166, "10100110 ");
    bin.Add(167, "10100111 ");
    bin.Add(168, "10101000 ");
    bin.Add(169, "10101001 ");
    bin.Add(170, "10101010 ");
    bin.Add(171, "10101011 ");
    bin.Add(172, "10101100 ");
    bin.Add(173, "10101101 ");
    bin.Add(174, "10101110 ");
    bin.Add(175, "10101111 ");
    bin.Add(176, "10110000 ");
    bin.Add(177, "10110001 ");
    bin.Add(178, "10110010 ");
    bin.Add(179, "10110011 ");
    bin.Add(180, "10110100 ");
    bin.Add(181, "10110101 ");
    bin.Add(182, "10110110 ");
    bin.Add(183, "10110111 ");
    bin.Add(184, "10111000 ");
    bin.Add(185, "10111001 ");
    bin.Add(186, "10111010 ");
    bin.Add(187, "10111011 ");
    bin.Add(188, "10111100 ");
    bin.Add(189, "10111101 ");
    bin.Add(190, "10111110 ");
    bin.Add(191, "10111111 ");
    bin.Add(192, "11000000 ");
    bin.Add(193, "11000001 ");
    bin.Add(194, "11000010 ");
    bin.Add(195, "11000011 ");
    bin.Add(196, "11000100 ");
    bin.Add(197, "11000101 ");
    bin.Add(198, "11000110 ");
    bin.Add(199, "11000111 ");
    bin.Add(200, "11001000 ");
    bin.Add(201, "11001001 ");
    bin.Add(202, "11001010 ");
    bin.Add(203, "11001011 ");
    bin.Add(204, "11001100 ");
    bin.Add(205, "11001101 ");
    bin.Add(206, "11001110 ");
    bin.Add(207, "11001111 ");
    bin.Add(208, "11010000 ");
    bin.Add(209, "11010001 ");
    bin.Add(210, "11010010 ");
    bin.Add(211, "11010011 ");
    bin.Add(212, "11010100 ");
    bin.Add(213, "11010101 ");
    bin.Add(214, "11010110 ");
    bin.Add(215, "11010111 ");
    bin.Add(216, "11011000 ");
    bin.Add(217, "11011001 ");
    bin.Add(218, "11011010 ");
    bin.Add(219, "11011011 ");
    bin.Add(220, "11011100 ");
    bin.Add(221, "11011101 ");
    bin.Add(222, "11011110 ");
    bin.Add(223, "11011111 ");
    bin.Add(224, "11100000 ");
    bin.Add(225, "11100001 ");
    bin.Add(226, "11100010 ");
    bin.Add(227, "11100011 ");
    bin.Add(228, "11100100 ");
    bin.Add(229, "11100101 ");
    bin.Add(230, "11100110 ");
    bin.Add(231, "11100111 ");
    bin.Add(232, "11101000 ");
    bin.Add(233, "11101001 ");
    bin.Add(234, "11101010 ");
    bin.Add(235, "11101011 ");
    bin.Add(236, "11101100 ");
    bin.Add(237, "11101101 ");
    bin.Add(238, "11101110 ");
    bin.Add(239, "11101111 ");
    bin.Add(240, "11110000 ");
    bin.Add(241, "11110001 ");
    bin.Add(242, "11110010 ");
    bin.Add(243, "11110011 ");
    bin.Add(244, "11110100 ");
    bin.Add(245, "11110101 ");
    bin.Add(246, "11110110 ");
    bin.Add(247, "11110111 ");
    bin.Add(248, "11111000 ");
    bin.Add(249, "11111001 ");
    bin.Add(250, "11111010 ");
    bin.Add(251, "11111011 ");
    bin.Add(252, "11111100 ");
    bin.Add(253, "11111101 ");
    bin.Add(254, "11111110 ");
    bin.Add(255, "11111111 ");
  }
}
}
