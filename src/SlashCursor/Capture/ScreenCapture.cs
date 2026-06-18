using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace SlashCursor.Capture;

/// <summary>
/// Captures a rectangular region of the screen centered on the cursor and
/// encodes it as PNG bytes. Works in physical pixels (app is Per-Monitor DPI
/// aware via the manifest).
/// </summary>
public static class ScreenCapture
{
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    public static (int X, int Y) GetCursorPosition()
    {
        GetCursorPos(out var p);
        return (p.X, p.Y);
    }

    /// <summary>
    /// Capture a <paramref name="boxSize"/> x <paramref name="boxSize"/> region
    /// centered on the cursor, clamped to the virtual desktop bounds.
    /// </summary>
    public static byte[]? CaptureAroundCursor(int cursorX, int cursorY, int boxSize = 400)
    {
        try
        {
            int vx = GetSystemMetrics(SM_XVIRTUALSCREEN);
            int vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
            int vw = GetSystemMetrics(SM_CXVIRTUALSCREEN);
            int vh = GetSystemMetrics(SM_CYVIRTUALSCREEN);

            int half = boxSize / 2;
            int left = cursorX - half;
            int top = cursorY - half;

            // Clamp to the virtual desktop so we never read off-screen.
            if (left < vx) left = vx;
            if (top < vy) top = vy;
            if (left + boxSize > vx + vw) left = vx + vw - boxSize;
            if (top + boxSize > vy + vh) top = vy + vh - boxSize;

            using var bmp = new Bitmap(boxSize, boxSize, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(left, top, 0, 0, new Size(boxSize, boxSize), CopyPixelOperation.SourceCopy);
            }

            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Capture the entire virtual desktop (all monitors) as PNG bytes.
    /// </summary>
    public static byte[]? CaptureWholeScreen()
    {
        try
        {
            int vx = GetSystemMetrics(SM_XVIRTUALSCREEN);
            int vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
            int vw = GetSystemMetrics(SM_CXVIRTUALSCREEN);
            int vh = GetSystemMetrics(SM_CYVIRTUALSCREEN);

            using var bmp = new Bitmap(vw, vh, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(vx, vy, 0, 0, new Size(vw, vh), CopyPixelOperation.SourceCopy);
            }

            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }
        catch
        {
            return null;
        }
    }
}
