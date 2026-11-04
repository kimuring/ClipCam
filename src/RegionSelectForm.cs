using System;
using System.Drawing;
using System.Windows.Forms;

namespace ClipCam
{
    /// <summary>
    /// 화면 전체를 어둡게 덮고, 마우스 드래그로 녹화 영역을 고르는 창.
    /// 방법: 시작할 때 화면 스크린샷을 1장 찍어서 배경으로 깔고, 선택한 부분만 밝게 보여줌.
    /// </summary>
    public class RegionSelectForm : Form
    {
        private readonly Bitmap _screenShot;
        private readonly Rectangle _virtual;   // 모든 모니터를 합친 영역
        private Point _dragStart;
        private Rectangle _selection = Rectangle.Empty;  // 이 창 기준 좌표
        private bool _dragging;

        /// <summary>선택된 영역 (화면 전체 기준 좌표)</summary>
        public Rectangle SelectedRegion { get; private set; }

        public RegionSelectForm()
        {
            _virtual = SystemInformation.VirtualScreen;
            _screenShot = new Bitmap(_virtual.Width, _virtual.Height);
            using (var g = Graphics.FromImage(_screenShot))
            {
                g.CopyFromScreen(_virtual.Location, Point.Empty, _virtual.Size);
            }

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;   // 배율에 의해 창 크기가 바뀌지 않게
            Bounds = _virtual;
            TopMost = true;
            ShowInTaskbar = false;
            DoubleBuffered = true;                // 깜빡임 방지
            Cursor = Cursors.Cross;
            KeyPreview = true;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Bounds = _virtual;  // 혹시 크기가 바뀌었으면 다시 맞춤
            Activate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.DrawImageUnscaled(_screenShot, 0, 0);

            // 전체를 반투명 검정으로 덮기
            using (var dim = new SolidBrush(Color.FromArgb(130, 0, 0, 0)))
                g.FillRectangle(dim, ClientRectangle);

            // 안내 문구 (메인 모니터 왼쪽 위)
            var primary = Screen.PrimaryScreen.Bounds;
            var hintPos = new Point(primary.X - _virtual.X + 20, primary.Y - _virtual.Y + 20);
            using (var font = Util.UiFont(12f, FontStyle.Bold))
            {
                g.DrawString("드래그해서 녹화할 영역을 선택하세요   (Esc / 오른쪽 클릭: 취소)",
                    font, Brushes.White, hintPos);
            }

            if (_selection.Width > 0 && _selection.Height > 0)
            {
                // 선택 영역만 원래 밝기로
                g.DrawImage(_screenShot, _selection, _selection, GraphicsUnit.Pixel);
                using (var pen = new Pen(Color.DeepSkyBlue, 2))
                    g.DrawRectangle(pen, _selection);

                // 크기 표시
                string size = _selection.Width + " x " + _selection.Height;
                using (var font = new Font("Segoe UI", 10f, FontStyle.Bold))
                {
                    int ty = _selection.Y >= 24 ? _selection.Y - 22 : _selection.Bottom + 4;
                    g.FillRectangle(Brushes.DeepSkyBlue, _selection.X, ty,
                        TextRenderer.MeasureText(size, font).Width, 20);
                    g.DrawString(size, font, Brushes.White, _selection.X, ty);
                }
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right) { DialogResult = DialogResult.Cancel; return; }
            if (e.Button != MouseButtons.Left) return;
            _dragging = true;
            _dragStart = e.Location;
            _selection = Rectangle.Empty;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!_dragging) return;
            // 어느 방향으로 드래그해도 올바른 사각형이 되게
            int x = Math.Min(_dragStart.X, e.X);
            int y = Math.Min(_dragStart.Y, e.Y);
            int w = Math.Abs(e.X - _dragStart.X);
            int h = Math.Abs(e.Y - _dragStart.Y);
            _selection = new Rectangle(x, y, w, h);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (!_dragging || e.Button != MouseButtons.Left) return;
            _dragging = false;

            if (_selection.Width >= 16 && _selection.Height >= 16)
            {
                // 창 기준 좌표 → 화면 전체 기준 좌표로 변환
                SelectedRegion = new Rectangle(
                    _selection.X + _virtual.X, _selection.Y + _virtual.Y,
                    _selection.Width, _selection.Height);
                DialogResult = DialogResult.OK;
            }
            else
            {
                // 너무 작으면(그냥 클릭한 경우 등) 다시 선택
                _selection = Rectangle.Empty;
                Invalidate();
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) DialogResult = DialogResult.Cancel;
            base.OnKeyDown(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _screenShot.Dispose();
            base.Dispose(disposing);
        }
    }
}
