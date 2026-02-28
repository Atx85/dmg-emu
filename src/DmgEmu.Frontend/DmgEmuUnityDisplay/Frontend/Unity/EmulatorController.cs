using UnityEngine;
using DmgEmu.Core;

namespace DmgEmu.Frontend.Unity
{
    /// <summary>
    /// Main MonoBehaviour for running the emulator in Unity.
    /// Manages emulator initialization, tick loop, input, and display updates.
    /// </summary>
    public class EmulatorController : MonoBehaviour
    {
        [SerializeField]
        private string romPath = "Assets/Roms/pkred.gb";

        [SerializeField]
        private float speedMultiplier = 1.0f;

        [SerializeField]
        private Renderer displayRenderer;

        // exposed via properties so editor scripts can assign them
        public string RomPath { get => romPath; set => romPath = value; }
        public Renderer DisplayRenderer { get => displayRenderer; set => displayRenderer = value; }

        private Gameboy emulator;
        private UnityDisplay display;
        private double cycleRemainder = 0;
        private const double CPU_HZ = 4194304.0;

        private void Start()
        {
            InitializeEmulator();
        }

        private void InitializeEmulator()
        {
            // Instantiate the emulator with the Cpu2Structured backend
            emulator = new Gameboy(romPath, CpuBackend.Cpu2Structured);

            // Create Unity display
            display = new UnityDisplay();
            display.SetFrameBuffer(emulator.ppu.GetFrameBuffer());

            // Hook frame-ready event to update display
            emulator.ppu.OnFrameReady += fb => display.Update(fb);

            // Set the texture on the renderer
            if (displayRenderer != null)
            {
                displayRenderer.material.mainTexture = display.GetTexture();
            }
        }

        private void Update()
        {
            if (emulator == null)
                return;

            // Calculate cycles for this frame
            double cycles = (Time.deltaTime * CPU_HZ) * speedMultiplier + cycleRemainder;
            int whole = (int)cycles;
            cycleRemainder = cycles - whole;

            // Cap cycles per frame to avoid runaway
            if (whole > 70224)
                whole = 70224;

            // Tick the emulator
            emulator.TickCycles(whole);

            // Handle input
            HandleInput();
        }

        private void HandleInput()
        {
            // Map Unity Input to joypad buttons
            emulator.bus.Joypad.SetButton(JoypadButton.Right, Input.GetKey(KeyCode.RightArrow));
            emulator.bus.Joypad.SetButton(JoypadButton.Left, Input.GetKey(KeyCode.LeftArrow));
            emulator.bus.Joypad.SetButton(JoypadButton.Up, Input.GetKey(KeyCode.UpArrow));
            emulator.bus.Joypad.SetButton(JoypadButton.Down, Input.GetKey(KeyCode.DownArrow));
            emulator.bus.Joypad.SetButton(JoypadButton.A, Input.GetKey(KeyCode.Z));
            emulator.bus.Joypad.SetButton(JoypadButton.B, Input.GetKey(KeyCode.X));
            emulator.bus.Joypad.SetButton(JoypadButton.Select, Input.GetKey(KeyCode.C));
            emulator.bus.Joypad.SetButton(JoypadButton.Start, Input.GetKey(KeyCode.V));

            // Speed toggle (Tab or Shift)
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                speedMultiplier = speedMultiplier > 1.0f ? 1.0f : 3.0f;
                Debug.Log($"Speed: {(speedMultiplier > 1.0f ? "3x" : "1x")}");
            }
        }

        public Gameboy GetEmulator() => emulator;
    }
}
