

(async function () {
  getData = async () => {
    let res = await fetch("https://gbdev.io/gb-opcodes/Opcodes.json");
    return res.json();
  };

  const data = await getData();
  Object.keys(data.unprefixed).forEach(key => {
    console.log(('/*' + data.unprefixed[key].mnemonic +'*/').padEnd(10) + 'case ' + key + ': Console.WriteLine($"0x{opCode:X2} not implemented!"); Environment.Exit(1);  return ' + data.unprefixed[key].cycles[0] + ';');
  })
})();
