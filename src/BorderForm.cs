using System;
using System.Drawing;
using System.Windows.Forms;

namespace ClipCam
{
    /// <summary>
    /// 녹화 영역을 표시하는 빨간 테두리 창 (꿀캠의 녹화 프레임 같은 것).
    ///  - 위쪽 탭을 드래그 → 이동
    ///  - 테두리 변/모서리를 드래그 → 크기 조절 (녹화 중에는 이동만 가능)
    ///  - 테두리 안쪽은 투명 + 클릭 통과라서 아래 창을 그대로 조작할 수 있음
    /// </summary>
    public class BorderForm : Form
    {
        public const int T = 5;          // 테두리 두께
        private const int TabH = 22;     // 위쪽 탭 높이
        private const int Corner = 18;   // 모서리 판정 범위

        // Windows에 "여기를 잡으면 무엇을 할지" 알려주는 값들
        private const int WM_NCHITTEST = 0x0084;
        private const int HTTRANSPARENT = -1, HTCAPTION = 2;
        private const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14;
        private const int HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;

        private bool _recording;
        private readonly Font _tabFont = new Font("Segoe UI", 9f, FontStyle.Bold);

        /// <summary>테두리가 이동/크기 변경되었을 때</summary>
        public event EventHandler CaptureRegionChanged;

        public BorderForm(Rectangle region)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            // 마젠타색 부분은 투명 + 클릭 통과 → 테두리와 탭만 보이고 잡을 수 있음
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;
            MinimumSize = new Size(32 + 2 * T, 32 + 2 * T + TabH);
            Bounds = FromRegion(region);
        }

        /// <summary>실제로 녹화되는 영역 (테두리 안쪽, 화면 좌표)</summary>
        public Rectangle CaptureRegion
        {
            get { return new Rectangle(Left + T, Top + TabH + T, Width - 2 * T, Height - 2 * T - TabH); }
        }

        /// <summary>녹화 중이면 크기 조절 금지(이동만 가능) + 탭에 REC 표시</summary>
        public bool Recording
        {
            get { return _recording; }
            set { _recording = value; Invalidate(); }
        }

        // 녹화 영역 → 이 창의 위치/크기 (테두리 두께와 탭 높이만큼 바깥으로)
        private static Rectangle FromRegion(Rectangle r)
        {
            return new Rectangle(r.X - T, r.Y - T - TabH, r.Width + 2 * T, r.Height + 2 * T + TabH);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { NativeMethods.SetWindowDisplayAffinity(Handle, NativeMethods.WDA_EXCLUDEFROMCAPTURE); } catch { }
        }

        protected override void OnMove(EventArgs e)
        {
            base.OnMove(e);
            UpdateTabPlacement();
            if (CaptureRegionChanged != null) CaptureRegionChanged(this, EventArgs.Empty);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            UpdateTabPlacement();
        }

        // 탭이 화면 위로 벗어나면 테두리 안쪽에 표시 (이 창은 녹화에 안 찍히므로 OK)
        private bool _tabInside;
        private void UpdateTabPlacement()
        {
            bool inside = Top < Screen.FromRectangle(Bounds).Bounds.Top;
            if (inside != _tabInside) { _tabInside = inside; Invalidate(); }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Invalidate();
            if (CaptureRegionChanged != null) CaptureRegionChanged(this, EventArgs.Empty);
        }

        // ===== 그리기 =====
        private Rectangle TabRect()
        {
            var r = CaptureRegion;
            string text = TabText(r);
            int w = TextRenderer.MeasureText(text, _tabFont).Width + 14;
            int y = _tabInside ? TabH + T : 0;
            return new Rectangle(_tabInside ? T : 0, y, Math.Min(w, Width - 2 * T), TabH);
        }

        private string TabText(Rectangle r)
        {
            return (_recording ? "● REC  " : "✥ ") + r.Width + " x " + r.Height;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            int w = ClientSize.Width, h = ClientSize.Height;
            var color = _recording ? Color.Red : Color.FromArgb(255, 60, 60);

            using (var b = new SolidBrush(color))
            {
                // 테두리 4변
                g.FillRectangle(b, 0, TabH, w, T);           // 위
                g.FillRectangle(b, 0, h - T, w, T);          // 아래
                g.FillRectangle(b, 0, TabH, T, h - TabH);    // 왼쪽
                g.FillRectangle(b, w - T, TabH, T, h - TabH); // 오른쪽

                // 위쪽 탭 (잡고 이동하는 손잡이)
                var tab = TabRect();
                g.FillRectangle(b, tab);
                TextRenderer.DrawText(g, TabText(CaptureRegion), _tabFont, tab, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        // ===== 마우스로 잡은 위치에 따라 이동/크기조절을 Windows에 맡김 =====
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST)
            {
                long lp = m.LParam.ToInt64();
                var screen = new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF));
                m.Result = (IntPtr)HitTest(PointToClient(screen));
                return;
            }
            base.WndProc(ref m);
        }

        private int HitTest(Point p)
        {
            int w = ClientSize.Width, h = ClientSize.Height;

            if (TabRect().Contains(p)) return HTCAPTION;   // 탭 = 이동

            bool left = p.X < T, right = p.X >= w - T;
            bool top = p.Y >= TabH && p.Y < TabH + T, bottom = p.Y >= h - T;
            bool onBorder = p.Y >= TabH && (left || right || top || bottom);
            if (!onBorder) return HTTRANSPARENT;           // 안쪽 = 클릭 통과

            if (_recording) return HTCAPTION;              // 녹화 중엔 테두리를 잡아도 이동만

            bool nearLeft = p.X < Corner, nearRight = p.X >= w - Corner;
            bool nearTop = p.Y < TabH + Corner, nearBottom = p.Y >= h - Corner;

            if ((top && nearLeft) || (left && nearTop)) return HTTOPLEFT;
            if ((top && nearRight) || (right && nearTop)) return HTTOPRIGHT;
            if ((bottom && nearLeft) || (left && nearBottom)) return HTBOTTOMLEFT;
            if ((bottom && nearRight) || (right && nearBottom)) return HTBOTTOMRIGHT;
            if (top) return HTTOP;
            if (bottom) return HTBOTTOM;
            if (left) return HTLEFT;
            return HTRIGHT;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _tabFont.Dispose();
            base.Dispose(disposing);
        }
    }
}
