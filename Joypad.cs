using System;

namespace GB
{
    public sealed class Joypad : IInputHandler
    {
        private readonly Action requestInterrupt;
        private readonly bool[] buttons = new bool[8];
        private byte selectBits = 0x30; // bits 4-5

        public Joypad(Action requestInterrupt)
        {
            this.requestInterrupt = requestInterrupt;
        }

        public void Write(byte value)
        {
            selectBits = (byte)(value & 0x30);
        }

        public byte Read()
        {
            byte result = (byte)(0xC0 | selectBits | 0x0F);

            bool selectDirections = (selectBits & 0x10) == 0;
            bool selectButtons = (selectBits & 0x20) == 0;

            if (selectDirections)
            {
                if (buttons[(int)JoypadButton.Right]) result &= 0xFE;
                if (buttons[(int)JoypadButton.Left]) result &= 0xFD;
                if (buttons[(int)JoypadButton.Up]) result &= 0xFB;
                if (buttons[(int)JoypadButton.Down]) result &= 0xF7;
            }

            if (selectButtons)
            {
                if (buttons[(int)JoypadButton.A]) result &= 0xFE;
                if (buttons[(int)JoypadButton.B]) result &= 0xFD;
                if (buttons[(int)JoypadButton.Select]) result &= 0xFB;
                if (buttons[(int)JoypadButton.Start]) result &= 0xF7;
            }

            return result;
        }

        public void SetButton(JoypadButton button, bool pressed)
        {
            int idx = (int)button;
            bool wasPressed = buttons[idx];
            buttons[idx] = pressed;

            if (!wasPressed && pressed)
            {
                bool selectDirections = (selectBits & 0x10) == 0;
                bool selectButtons = (selectBits & 0x20) == 0;

                bool isDirection = button == JoypadButton.Right || button == JoypadButton.Left ||
                                   button == JoypadButton.Up || button == JoypadButton.Down;
                if ((isDirection && selectDirections) || (!isDirection && selectButtons))
                    requestInterrupt();
            }
        }

        public JoypadState GetState()
        {
            var copy = new bool[buttons.Length];
            Array.Copy(buttons, copy, buttons.Length);
            return new JoypadState
            {
                SelectBits = selectBits,
                Buttons = copy
            };
        }

        public void SetState(JoypadState state)
        {
            selectBits = state.SelectBits;
            if (state.Buttons != null)
            {
                int n = Math.Min(buttons.Length, state.Buttons.Length);
                for (int i = 0; i < n; i++) buttons[i] = state.Buttons[i];
                for (int i = n; i < buttons.Length; i++) buttons[i] = false;
            }
        }
    }

    public struct JoypadState
    {
        public byte SelectBits;
        public bool[] Buttons;
    }
}
