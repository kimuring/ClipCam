using System;
using System.Runtime.InteropServices;

namespace ClipCam
{
    /// <summary>
    /// Windows API를 직접 호출하기 위한 선언 모음.
    /// (C#만으로는 할 수 없는 기능: 마우스 커서 그리기, 녹화에서 창 제외, 단축키 등)
    /// </summary>
    internal static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct CURSORINFO
        {
            public int cbSize;
            public int flags;
            public IntPtr hCursor;
            public POINT ptScreenPos;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ICONINFO
        {
            public bool fIcon;
            public int xHotspot;   // 커서의 "클릭 지점" 위치
            public int yHotspot;
            public IntPtr hbmMask;
            public IntPtr hbmColor;
        }

        public const int CURSOR_SHOWING = 0x0001;
        public const int DI_NORMAL = 0x0003;
        public const uint WDA_EXCLUDEFROMCAPTURE = 0x0011; // 이 창은 화면 캡처에 안 찍히게 (Win10 2004 이상)
        public const int WM_HOTKEY = 0x0312;
        public const int WS_EX_TRANSPARENT = 0x00000020;   // 마우스 클릭이 창을 통과
        public const int WS_EX_TOOLWINDOW = 0x00000080;    // 작업표시줄/Alt+Tab에 안 나옴
        public const int WS_EX_LAYERED = 0x00080000;
        public const int WS_EX_NOACTIVATE = 0x08000000;    // 클릭해도 포커스를 뺏지 않음

        [DllImport("user32.dll")]
        public static extern bool GetCursorInfo(ref CURSORINFO pci);

        [DllImport("user32.dll")]
        public static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);

        [DllImport("user32.dll")]
        public static extern bool DrawIconEx(IntPtr hdc, int x, int y, IntPtr hIcon,
            int cx, int cy, int istepIfAniCur, IntPtr hbrFlickerFreeDraw, int diFlags);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32.dll")]
        public static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        // 타이머 정밀도를 1ms로 올림 (기본은 약 15ms라서 30fps 녹화 시 간격이 들쭉날쭉해짐)
        [DllImport("winmm.dll")]
        public static extern uint timeBeginPeriod(uint uPeriod);

        [DllImport("winmm.dll")]
        public static extern uint timeEndPeriod(uint uPeriod);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        /// <summary>고DPI 대응: Per-Monitor V2 (Win10 1703 이상), 안 되면 시스템 DPI 대응</summary>
        public static void EnableDpiAwareness()
        {
            try
            {
                if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return;   // -4 = PER_MONITOR_AWARE_V2
            }
            catch (EntryPointNotFoundException) { }
            try { SetProcessDPIAware(); } catch { }
        }
    }
}
