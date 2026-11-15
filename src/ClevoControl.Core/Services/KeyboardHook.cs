using System.Runtime.InteropServices;

namespace ClevoControl.Core.Services;

/// <summary>
/// Low-level keyboard hook on its own message-loop thread. Used to block the Windows key
/// (gaming mode) and to feed the reactive typing effect. Key codes are never stored or logged.
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13, WM_KEYDOWN = 0x100, WM_SYSKEYDOWN = 0x104, WM_QUIT = 0x12;
    private const int VK_LWIN = 0x5B, VK_RWIN = 0x5C;

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr mod, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint thread, uint msg, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }

    private readonly HookProc _proc;
    private readonly Thread _thread;
    private uint _threadId;
    private IntPtr _hook;
    private readonly HashSet<int> _held = []; // only touched on the hook thread

    public KeyboardHook()
    {
        _proc = Callback;
        _thread = new Thread(Run) { IsBackground = true, Name = "KeyboardHook" };
        // Global keyboard hooks are Windows-only; elsewhere the hook stays inactive.
        if (OperatingSystem.IsWindows()) _thread.Start();
    }

    public volatile bool BlockWindowsKey;

    /// <summary>Optional filter (vk, isKeyDown) → true swallows the event. Injected input is never filtered.</summary>
    public Func<int, bool, bool>? Filter;

    /// <summary>Raised for every key-down (on the hook thread – keep handlers fast).</summary>
    public event Action? KeyPressed;

    private void Run()
    {
        _threadId = GetCurrentThreadId();
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        while (GetMessage(out _, IntPtr.Zero, 0, 0) > 0) { }
        if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
    }

    private IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var msg = wParam.ToInt32();
            var vk = Marshal.ReadInt32(lParam);
            var injected = (Marshal.ReadInt32(lParam, 8) & 0x10) != 0; // LLKHF_INJECTED
            var down = msg is WM_KEYDOWN or WM_SYSKEYDOWN;
            if (BlockWindowsKey && vk is VK_LWIN or VK_RWIN) return 1;
            if (!injected && Filter is { } f)
            {
                try { if (f(vk, down)) return 1; } catch { }
            }
            // Auto-repeat while a key is held sends more key-downs: only the first press counts.
            if (down)
            {
                if (_held.Add(vk)) KeyPressed?.Invoke();
            }
            else
            {
                _held.Remove(vk);
            }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    public void Dispose()
    {
        if (_threadId != 0) PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
    }
}
