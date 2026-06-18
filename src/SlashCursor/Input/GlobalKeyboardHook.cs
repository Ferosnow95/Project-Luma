using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SlashCursor.Input;

/// <summary>
/// System-wide low-level keyboard hook that watches for the "/" key.
/// When "/" is pressed, raises <see cref="SlashPressed"/>; if the handler
/// returns <c>true</c> the keystroke is swallowed (not delivered to the app
/// underneath), otherwise it passes through normally.
/// </summary>
public sealed class GlobalKeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int VK_OEM_2 = 0xBF;   // '/' or '?' on a US layout
    private const int VK_SHIFT = 0x10;
    private const int VK_ESCAPE = 0x1B;

    // Keep a strong reference so the delegate isn't garbage-collected.
    private readonly LowLevelKeyboardProc _proc;
    private IntPtr _hookId = IntPtr.Zero;

    /// <summary>
    /// Invoked when "/" is pressed. Return <c>true</c> to suppress the key.
    /// </summary>
    public Func<bool>? SlashPressed { get; set; }

    /// <summary>Invoked when Escape is pressed (never suppressed).</summary>
    public Action? EscapePressed { get; set; }

    public GlobalKeyboardHook()
    {
        _proc = HookCallback;
    }

    public void Install()
    {
        if (_hookId != IntPtr.Zero) return;
        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule!;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(module.ModuleName), 0);
        if (_hookId == IntPtr.Zero)
            throw new InvalidOperationException("Failed to install keyboard hook.");
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            if (msg is WM_KEYDOWN or WM_SYSKEYDOWN)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                bool shiftDown = (GetKeyState(VK_SHIFT) & 0x8000) != 0;

                if (vkCode == VK_OEM_2 && !shiftDown)
                {
                    try
                    {
                        if (SlashPressed?.Invoke() == true)
                            return (IntPtr)1; // swallow the keystroke
                    }
                    catch
                    {
                        // Never let a handler exception break the input pipeline.
                    }
                }
                else if (vkCode == VK_ESCAPE)
                {
                    try { EscapePressed?.Invoke(); }
                    catch { /* never break input */ }
                }
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);
}
