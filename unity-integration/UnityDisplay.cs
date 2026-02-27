using UnityEngine;
using DmgEmu.Core;

namespace DmgEmu.Frontend.Unity
{
    /// <summary>
    /// Unity implementation of IDisplay.
    /// Renders the emulator framebuffer to a texture that can be displayed on a Unity canvas or quad.
    /// </summary>
    public class UnityDisplay : IDisplay
    {
        private Texture2D displayTexture;
        private Color32[] pixelBuffer;
        private IFrameBuffer framebuffer;

        private static readonly Color32[] Palette = new Color32[]
        {
            new Color32(255, 255, 255, 255), // White
            new Color32(179, 179, 179, 255), // Light gray
            new Color32(102, 102, 102, 255), // Dark gray
            new Color32(0, 0, 0, 255)        // Black
        };

        public UnityDisplay()
        {
            // Create a texture for rendering (160x144, RGB)
            displayTexture = new Texture2D(160, 144, TextureFormat.RGBA32, false);
            displayTexture.filterMode = FilterMode.Point; // Pixel-perfect
            pixelBuffer = new Color32[160 * 144];
        }

        public void SetFrameBuffer(IFrameBuffer fb)
        {
            framebuffer = fb;
        }

        public void Update(IFrameBuffer fb)
        {
            if (fb == null)
                return;

            // Convert framebuffer to texture pixels
            for (int y = 0; y < 144; y++)
            {
                for (int x = 0; x < 160; x++)
                {
                    int colorIndex = fb.GetPixel(x, y);
                    if (colorIndex < 0 || colorIndex > 3)
                        colorIndex = 0;
                    pixelBuffer[y * 160 + x] = Palette[colorIndex];
                }
            }

            // Apply pixels to texture
            displayTexture.SetPixels32(pixelBuffer);
            displayTexture.Apply();
        }

        public Texture2D GetTexture() => displayTexture;

        // IDisplay stub (not used in Unity context)
        public void RunLoop(System.Action<double> onTick)
        {
            // Unity's Update loop handles ticking instead
        }
    }
}
