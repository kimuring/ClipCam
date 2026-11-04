using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ClipCam
{
    /// <summary>
    /// 편집 화면: 미리보기 재생, 앞/뒤 자르기, 프레임 삭제, 되돌리기, 속도·크기 설정, GIF/MP4 저장
    /// </summary>
    public class EditorForm : Form
    {
        private readonly RecordingResult _rec;
        private List<FrameInfo> _frames;
        private readonly Stack<List<FrameInfo>> _undo = new Stack<List<FrameInfo>>();
        private int _current;
        private bool _exported;
        private bool _busy;

        private readonly PictureBox _pic;
        private readonly TrackBar _track;
        private readonly Label _lblInfo, _lblStatus;
        private readonly Button _btnPlay, _btnUndo, _btnGif, _btnMp4;
        private readonly ComboBox _cmbSpeed, _cmbWidth;
        private readonly System.Windows.Forms.Timer _playTimer;

        private static readonly double[] Speeds = { 0.5, 1.0, 1.5, 2.0 };
        private static readonly int[] Widths = { 0, 1280, 960, 640, 480, 320 };   // 0 = 원본

        public EditorForm(RecordingResult rec)
        {
            _rec = rec;
            _frames = new List<FrameInfo>(rec.Frames);

            Text = "ClipCam - 편집";
            Font = Util.UiFont();
            Icon = AppIcon.Get();
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            MinimumSize = new Size(860, 580);

            // 창 크기: 녹화 크기 + 아래 컨트롤 영역, 단 화면의 85%까지
            var wa = Screen.PrimaryScreen.WorkingArea;
            Size = new Size(
                Math.Min(Math.Max(rec.FrameSize.Width + 60, 860), (int)(wa.Width * 0.85)),
                Math.Min(Math.Max(rec.FrameSize.Height + 260, 580), (int)(wa.Height * 0.85)));

            // --- 미리보기 ---
            _pic = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.FromArgb(40, 40, 40),
            };

            // --- 아래쪽 컨트롤 영역 ---
            var bottom = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                ColumnCount = 1,
                Padding = new Padding(8),
            };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            _track = new TrackBar { Dock = DockStyle.Fill, TickStyle = TickStyle.None, Minimum = 0 };
            _track.ValueChanged += (s, e) => ShowFrame(_track.Value);

            _lblInfo = new Label { AutoSize = true, Padding = new Padding(0, 0, 0, 4) };

            // 편집 버튼 줄
            var editRow = NewRow();
            _btnPlay = AddButton(editRow, "▶ 재생 (Space)", (s, e) => TogglePlay());
            AddButton(editRow, "◀ 이전", (s, e) => { StopPlay(); SetCurrent(_current - 1); });
            AddButton(editRow, "다음 ▶", (s, e) => { StopPlay(); SetCurrent(_current + 1); });
            AddButton(editRow, "✂ 여기부터 시작", (s, e) => TrimStart());
            AddButton(editRow, "✂ 여기까지 끝", (s, e) => TrimEnd());
            AddButton(editRow, "이 프레임 삭제 (Del)", (s, e) => DeleteCurrent());
            _btnUndo = AddButton(editRow, "↶ 되돌리기 (Ctrl+Z)", (s, e) => Undo());

            // 저장 버튼 줄
            var exportRow = NewRow();
            exportRow.Controls.Add(new Label { Text = "속도", AutoSize = true, Padding = new Padding(0, 7, 0, 0) });
            _cmbSpeed = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70 };
            _cmbSpeed.Items.AddRange(new object[] { "0.5x", "1x", "1.5x", "2x" });
            _cmbSpeed.SelectedIndex = 1;
            exportRow.Controls.Add(_cmbSpeed);

            exportRow.Controls.Add(new Label { Text = "  가로 크기", AutoSize = true, Padding = new Padding(0, 7, 0, 0) });
            _cmbWidth = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
            _cmbWidth.Items.AddRange(new object[] { "원본", "1280", "960", "640", "480", "320" });
            _cmbWidth.SelectedIndex = 0;
            exportRow.Controls.Add(_cmbWidth);

            _btnGif = AddButton(exportRow, "GIF로 저장", async (s, e) => await ExportAsync(ExportFormat.Gif));
            _btnMp4 = AddButton(exportRow, "MP4로 저장", async (s, e) => await ExportAsync(ExportFormat.Mp4));
            _btnGif.Font = _btnMp4.Font = Util.UiFont(9f, FontStyle.Bold);
            _lblStatus = new Label { AutoSize = true, Padding = new Padding(8, 7, 0, 0), ForeColor = Color.DimGray };
            exportRow.Controls.Add(_lblStatus);

            bottom.Controls.Add(_track);
            bottom.Controls.Add(_lblInfo);
            bottom.Controls.Add(editRow);
            bottom.Controls.Add(exportRow);

            // Fill을 먼저 추가해야 Bottom 영역을 빼고 나머지를 채움
            Controls.Add(_pic);
            Controls.Add(bottom);

            _playTimer = new System.Windows.Forms.Timer();
            _playTimer.Tick += (s, e) =>
            {
                SetCurrent((_current + 1) % _frames.Count);
                _playTimer.Interval = CurrentPlayInterval();
            };

            RefreshTrack();
        }

        private static FlowLayoutPanel NewRow()
        {
            return new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Fill, Margin = new Padding(0, 2, 0, 2) };
        }

        private static Button AddButton(Control parent, string text, EventHandler onClick)
        {
            var b = new Button { Text = text, AutoSize = true };
            b.Click += onClick;
            parent.Controls.Add(b);
            return b;
        }

        // ===== 표시 =====
        private void SetCurrent(int index)
        {
            index = Util.Clamp(index, 0, _frames.Count - 1);
            if (_track.Value != index) _track.Value = index;   // ValueChanged → ShowFrame
            else ShowFrame(index);
        }

        private void ShowFrame(int index)
        {
            _current = Util.Clamp(index, 0, _frames.Count - 1);
            var old = _pic.Image;
            _pic.Image = LoadImage(_frames[_current].Path);
            if (old != null) old.Dispose();
            UpdateInfo();
        }

        // Image.FromFile은 파일을 잠가버리므로, 메모리로 읽어서 복사본을 만듦
        private static Image LoadImage(string path)
        {
            using (var ms = new MemoryStream(File.ReadAllBytes(path)))
            using (var tmp = Image.FromStream(ms))
            {
                return new Bitmap(tmp);
            }
        }

        private void UpdateInfo()
        {
            double total = _frames.Sum(f => f.DelayMs) / 1000.0;
            double pos = _frames.Take(_current).Sum(f => f.DelayMs) / 1000.0;
            _lblInfo.Text = string.Format("프레임 {0} / {1}   |   {2:0.00}초 / {3:0.00}초   |   {4} x {5}   |   {6}fps",
                _current + 1, _frames.Count, pos, total, _rec.FrameSize.Width, _rec.FrameSize.Height, _rec.Fps);
            _btnUndo.Enabled = _undo.Count > 0;
        }

        private void RefreshTrack()
        {
            _track.Maximum = Math.Max(0, _frames.Count - 1);
            _current = Util.Clamp(_current, 0, _frames.Count - 1);
            SetCurrent(_current);
        }

        // ===== 재생 =====
        private double CurrentSpeed { get { return Speeds[_cmbSpeed.SelectedIndex]; } }

        private int CurrentPlayInterval()
        {
            return Math.Max(10, (int)(_frames[_current].DelayMs / CurrentSpeed));
        }

        private void TogglePlay()
        {
            if (_playTimer.Enabled)
            {
                _playTimer.Stop();
                _btnPlay.Text = "▶ 재생 (Space)";
            }
            else
            {
                _playTimer.Interval = CurrentPlayInterval();
                _playTimer.Start();
                _btnPlay.Text = "❚❚ 일시정지 (Space)";
            }
        }

        private void StopPlay()
        {
            if (_playTimer.Enabled) TogglePlay();
        }

        // ===== 편집 (모든 편집 전에 현재 상태를 저장해서 되돌리기 가능하게) =====
        private void PushUndo() { _undo.Push(new List<FrameInfo>(_frames)); }

        private void TrimStart()
        {
            StopPlay();
            if (_current == 0) return;
            PushUndo();
            _frames.RemoveRange(0, _current);   // 현재 프레임 앞을 전부 삭제
            _current = 0;
            RefreshTrack();
        }

        private void TrimEnd()
        {
            StopPlay();
            if (_current >= _frames.Count - 1) return;
            PushUndo();
            _frames.RemoveRange(_current + 1, _frames.Count - _current - 1);  // 현재 프레임 뒤를 전부 삭제
            RefreshTrack();
        }

        private void DeleteCurrent()
        {
            StopPlay();
            if (_frames.Count <= 1)
            {
                MessageBox.Show(this, "마지막 1프레임은 삭제할 수 없어요.", "ClipCam");
                return;
            }
            PushUndo();
            _frames.RemoveAt(_current);
            RefreshTrack();
        }

        private void Undo()
        {
            StopPlay();
            if (_undo.Count == 0) return;
            _frames = _undo.Pop();
            RefreshTrack();
        }

        // ===== 단축키 =====
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (_busy) return base.ProcessCmdKey(ref msg, keyData);
            switch (keyData)
            {
                case Keys.Space: TogglePlay(); return true;
                case Keys.Left: StopPlay(); SetCurrent(_current - 1); return true;
                case Keys.Right: StopPlay(); SetCurrent(_current + 1); return true;
                case Keys.Delete: DeleteCurrent(); return true;
                case Keys.Control | Keys.Z: Undo(); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ===== 저장 =====
        private async Task ExportAsync(ExportFormat format)
        {
            StopPlay();

            string ffmpeg = FfmpegLocator.Find();
            if (ffmpeg == null)
            {
                MessageBox.Show(this,
                    "GIF / MP4 저장에 필요한 ffmpeg.exe를 찾을 수 없어요.\n\n" +
                    "아래 둘 중 하나로 준비한 뒤 다시 저장해 주세요.\n\n" +
                    "  1) PowerShell에서:  winget install Gyan.FFmpeg\n" +
                    "     (설치 후 ClipCam을 다시 실행)\n" +
                    "  2) ffmpeg.exe를 ClipCam.exe와 같은 폴더에 넣기",
                    "ClipCam", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            bool gif = format == ExportFormat.Gif;
            string path;
            using (var dlg = new SaveFileDialog())
            {
                dlg.Filter = gif ? "GIF 이미지 (*.gif)|*.gif" : "MP4 동영상 (*.mp4)|*.mp4";
                dlg.FileName = "ClipCam_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + (gif ? ".gif" : ".mp4");
                dlg.InitialDirectory = Environment.GetFolderPath(gif
                    ? Environment.SpecialFolder.MyPictures
                    : Environment.SpecialFolder.MyVideos);
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                path = dlg.FileName;
            }

            SetBusy(true, "변환 중... (길이에 따라 수십 초 걸릴 수 있어요)");
            try
            {
                var result = await Exporter.ExportAsync(
                    _frames, _rec.Fps, CurrentSpeed, Widths[_cmbWidth.SelectedIndex],
                    _rec.FrameSize, format, path, ffmpeg);

                if (result.Success)
                {
                    _exported = true;
                    double mb = new FileInfo(path).Length / 1024.0 / 1024.0;
                    SetBusy(false, string.Format("저장 완료 ({0:0.0}MB)", mb));
                    var r = MessageBox.Show(this, string.Format("저장했어요! ({0:0.0}MB)\n폴더를 열까요?", mb), "ClipCam",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                    if (r == DialogResult.Yes)
                        Process.Start("explorer.exe", "/select,\"" + path + "\"");
                }
                else
                {
                    SetBusy(false, "저장 실패");
                    string log = result.Log ?? "";
                    string tail = log.Length > 1200 ? log.Substring(log.Length - 1200) : log;
                    MessageBox.Show(this, "변환 중 오류가 났어요.\n\n" + tail, "ClipCam",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                SetBusy(false, "저장 실패");
                MessageBox.Show(this, "오류: " + ex.Message, "ClipCam", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SetBusy(bool busy, string status)
        {
            _busy = busy;
            _btnGif.Enabled = _btnMp4.Enabled = !busy;
            _lblStatus.Text = status;
            UseWaitCursor = busy;
        }

        // ===== 닫기 =====
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_busy)
            {
                e.Cancel = true;   // 변환 중에는 닫지 않음
                return;
            }
            if (!_exported)
            {
                var r = MessageBox.Show(this, "아직 저장하지 않았어요. 녹화한 내용을 버리고 닫을까요?", "ClipCam",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r != DialogResult.Yes) { e.Cancel = true; return; }
            }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _playTimer.Dispose();
            if (_pic.Image != null) _pic.Image.Dispose();
            // 임시 프레임 파일 정리
            try { Directory.Delete(_rec.TempDir, true); } catch { }
            base.OnFormClosed(e);
        }
    }
}
