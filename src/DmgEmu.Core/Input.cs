namespace DmgEmu.Core
{
    public enum JoypadButton
    {
        Right,
        Left,
        Up,
        Down,
        A,
        B,
        Select,
        Start
    }

    public interface IInputHandler
    {
        void SetButton(JoypadButton button, bool pressed);
    }

    public interface IKeyMapper
    {
        bool TryMapSdlKey(int keycode, out JoypadButton button);
        bool TryMapGtkKey(uint keyval, out JoypadButton button);
    }

    public sealed class DefaultKeyMapper : IKeyMapper
    {
        public bool TryMapSdlKey(int keycode, out JoypadButton button)
        {
            switch (keycode)
            {
                case 1073741903: button = JoypadButton.Right; return true; // SDLK_RIGHT
                case 1073741904: button = JoypadButton.Left; return true;  // SDLK_LEFT
                case 1073741905: button = JoypadButton.Down; return true;  // SDLK_DOWN
                case 1073741906: button = JoypadButton.Up; return true;    // SDLK_UP
                case 122: button = JoypadButton.A; return true;            // 'z'
                case 120: button = JoypadButton.B; return true;            // 'x'
                case 13: button = JoypadButton.Start; return true;         // Enter
                case 1073742053: button = JoypadButton.Select; return true; // Right Shift
                case 1073742049: button = JoypadButton.Select; return true; // Left Shift
                default:
                    button = JoypadButton.A;
                    return false;
            }
        }

        public bool TryMapGtkKey(uint keyval, out JoypadButton button)
        {
            switch (keyval)
            {
                case 65363: button = JoypadButton.Right; return true; // Gdk.Key.Right
                case 65361: button = JoypadButton.Left; return true;  // Gdk.Key.Left
                case 65364: button = JoypadButton.Down; return true;  // Gdk.Key.Down
                case 65362: button = JoypadButton.Up; return true;    // Gdk.Key.Up
                case 122: button = JoypadButton.A; return true;       // z
                case 120: button = JoypadButton.B; return true;       // x
                case 65293: button = JoypadButton.Start; return true; // Return
                case 65505: button = JoypadButton.Select; return true; // Shift_L
                case 65506: button = JoypadButton.Select; return true; // Shift_R
                default:
                    button = JoypadButton.A;
                    return false;
            }
        }
    }
}
