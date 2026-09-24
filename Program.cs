// SpotifyHotkey: 全局 Ctrl+Space 控制 Spotify 播放/暂停
// Spotify 未运行时不拦截按键（Ctrl+Space 照常用于切换输入法）
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

static class Program
{
    [STAThread]
    static void Main()
    {
        bool created;
        using (var mutex = new Mutex(true, "SpotifyHotkey_SingleInstance", out created))
        {
            if (!created) return;
            Application.EnableVisualStyles();
            Application.Run(new TrayContext());
        }
    }
}

class TrayContext : ApplicationContext
{
    const string AppName = "SpotifyHotkey";
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    readonly NotifyIcon tray;
    readonly ToolStripMenuItem enabledItem;
    readonly ToolStripMenuItem autoStartItem;
    readonly Native.LowLevelKeyboardProc hookProc; // 保持引用，防止被 GC
    IntPtr hookId = IntPtr.Zero;
    bool swallowingSpace;

    public TrayContext()
    {
        hookProc = HookCallback;
        hookId = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, hookProc,
            Native.GetModuleHandle(null), 0);

        enabledItem = new ToolStripMenuItem("启用 Ctrl+Space") { Checked = true, CheckOnClick = true };
        autoStartItem = new ToolStripMenuItem("开机自启") { Checked = IsAutoStart() };
        autoStartItem.Click += (s, e) =>
        {
            SetAutoStart(!autoStartItem.Checked);
            autoStartItem.Checked = IsAutoStart();
        };
        var exitItem = new ToolStripMenuItem("退出");
        exitItem.Click += (s, e) => ExitThread();

        var menu = new ContextMenuStrip();
        menu.Items.Add(enabledItem);
        menu.Items.Add(autoStartItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        tray = new NotifyIcon
        {
            Icon = MakeIcon(),
            Text = "Spotify 快捷键 (Ctrl+Space)",
            ContextMenuStrip = menu,
            Visible = true
        };
        tray.DoubleClick += (s, e) => TogglePlayPause();
    }

    IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && Marshal.ReadInt32(lParam) == Native.VK_SPACE)
        {
            int msg = wParam.ToInt32();
            bool down = msg == Native.WM_KEYDOWN || msg == Native.WM_SYSKEYDOWN;
            bool up = msg == Native.WM_KEYUP || msg == Native.WM_SYSKEYUP;

            if (down)
            {
                if (swallowingSpace) return (IntPtr)1; // 按住不放时的自动重复
                if (enabledItem.Checked && OnlyCtrlPressed() && TogglePlayPause())
                {
                    swallowingSpace = true;
                    return (IntPtr)1;
                }
            }
            else if (up && swallowingSpace)
            {
                swallowingSpace = false;
                return (IntPtr)1;
            }
        }
        return Native.CallNextHookEx(hookId, nCode, wParam, lParam);
    }

    static bool IsDown(int vk) { return (Native.GetAsyncKeyState(vk) & 0x8000) != 0; }

    static bool OnlyCtrlPressed()
    {
        return IsDown(Native.VK_CONTROL) && !IsDown(Native.VK_SHIFT) && !IsDown(Native.VK_MENU)
            && !IsDown(Native.VK_LWIN) && !IsDown(Native.VK_RWIN);
    }

    // 返回 false 表示 Spotify 没在运行
    static bool TogglePlayPause()
    {
        var pids = new HashSet<uint>();
        foreach (var p in Process.GetProcessesByName("Spotify"))
        {
            pids.Add((uint)p.Id);
            p.Dispose();
        }
        if (pids.Count == 0) return false;

        IntPtr target = FindSpotifyWindow(pids);
        if (target != IntPtr.Zero)
        {
            // WM_APPCOMMAND 直接发给 Spotify 窗口，只影响 Spotify，窗口最小化/在托盘也有效
            Native.PostMessage(target, Native.WM_APPCOMMAND, target,
                (IntPtr)(Native.APPCOMMAND_MEDIA_PLAY_PAUSE << 16));
        }
        else
        {
            // 兜底：模拟键盘多媒体播放/暂停键
            Native.keybd_event(Native.VK_MEDIA_PLAY_PAUSE, 0, 0, UIntPtr.Zero);
            Native.keybd_event(Native.VK_MEDIA_PLAY_PAUSE, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
        return true;
    }

    static IntPtr FindSpotifyWindow(HashSet<uint> pids)
    {
        IntPtr found = IntPtr.Zero;
        var cls = new StringBuilder(256);
        Native.EnumWindows((hWnd, lParam) =>
        {
            uint pid;
            Native.GetWindowThreadProcessId(hWnd, out pid);
            if (!pids.Contains(pid)) return true;
            cls.Length = 0;
            Native.GetClassName(hWnd, cls, cls.Capacity);
            // 主窗口：Chromium 顶层窗口且有标题（播放时为"歌手 - 歌名"，暂停时为"Spotify ..."）
            if (cls.ToString().StartsWith("Chrome_WidgetWin") && Native.GetWindowTextLength(hWnd) > 0)
            {
                found = hWnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    static bool IsAutoStart()
    {
        using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
        {
            return key != null && key.GetValue(AppName) != null;
        }
    }

    static void SetAutoStart(bool on)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            if (on) key.SetValue(AppName, "\"" + Application.ExecutablePath + "\"");
            else key.DeleteValue(AppName, false);
        }
    }

    static Icon MakeIcon()
    {
        using (var bmp = new Bitmap(32, 32))
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using (var green = new SolidBrush(Color.FromArgb(30, 215, 96)))
                g.FillEllipse(green, 1, 1, 30, 30);
            g.FillPolygon(Brushes.Black, new[] { new Point(12, 8), new Point(12, 24), new Point(24, 16) });
            return Icon.FromHandle(bmp.GetHicon());
        }
    }

    protected override void ExitThreadCore()
    {
        if (hookId != IntPtr.Zero) Native.UnhookWindowsHookEx(hookId);
        tray.Visible = false;
        tray.Dispose();
        base.ExitThreadCore();
    }
}

static class Native
{
    public const int WH_KEYBOARD_LL = 13;
    public const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
    public const int WM_APPCOMMAND = 0x0319;
    public const int APPCOMMAND_MEDIA_PLAY_PAUSE = 14;
    public const int VK_SPACE = 0x20, VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LWIN = 0x5B, VK_RWIN = 0x5C;
    public const byte VK_MEDIA_PLAY_PAUSE = 0xB3;
    public const uint KEYEVENTF_KEYUP = 0x0002;

    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string name);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
}
