using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ClipCam
{
    /// <summary>화면의 지정 영역을 1장 찍는 기능</summary>
    internal static class ScreenCapture
    {
        public static Bitmap Capture(Rectangle region, bool includeCursor)
        {
            var bmp = new Bitmap(region.Width, region.Height, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(bmp))
            {
                // 화면 → 비트맵으로 복사
                g.CopyFromScreen(region.Location, Point.Empty, region.Size, CopyPixelOperation.SourceCopy);

                // CopyFromScreen은 마우스 커서를 안 찍어주므로 직접 그려 넣음
                if (includeCursor) DrawCursor(g, region.Location);
            }
            return bmp;
        }

        private static void DrawCursor(Graphics g, Point origin)
        {
            var ci = new NativeMethods.CURSORINFO();
            ci.cbSize = Marshal.SizeOf(typeof(NativeMethods.CURSORINFO));
            if (!NativeMethods.GetCursorInfo(ref ci) || (ci.flags & NativeMethods.CURSOR_SHOWING) == 0) return;

            // 커서 이미지의 "끝 점(핫스팟)" 만큼 위치 보정
            int hotX = 0, hotY = 0;
            NativeMethods.ICONINFO ii;
            if (NativeMethods.GetIconInfo(ci.hCursor, out ii))
            {
                hotX = ii.xHotspot;
                hotY = ii.yHotspot;
                // GetIconInfo가 만든 비트맵은 직접 해제해야 함 (안 하면 메모리 누수)
                if (ii.hbmMask != IntPtr.Zero) NativeMethods.DeleteObject(ii.hbmMask);
                if (ii.hbmColor != IntPtr.Zero) NativeMethods.DeleteObject(ii.hbmColor);
            }

            int x = ci.ptScreenPos.X - origin.X - hotX;
            int y = ci.ptScreenPos.Y - origin.Y - hotY;

            IntPtr hdc = g.GetHdc();
            try { NativeMethods.DrawIconEx(hdc, x, y, ci.hCursor, 0, 0, 0, IntPtr.Zero, NativeMethods.DI_NORMAL); }
            finally { g.ReleaseHdc(hdc); }
        }
    }
}
