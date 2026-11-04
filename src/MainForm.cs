using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ClipCam
{
    /// <summary>시작 화면: FPS·커서 설정 후 "영역 선택해서 녹화하기"</summary>
    public class MainForm : Form
    {
        private readonly NumericUpDown _numFps;
        private readonly CheckBox _chkCursor;
        private readonly Label _lblFfmpeg;

        public MainForm()
        {
            Text = "ClipCam - 움짤 녹화기";
            Font = Util.UiFont();
            Icon = AppIcon.Get();
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            var layout = new TableLayoutPanel
            {
                AutoSize = true,
                ColumnCount = 1,
                Padding = new Padding(16),
            };

            var btnRecord = new Button
            {
                Text = "▶  영역 선택해서 녹화하기",
                AutoSize = true,
                Font = Util.UiFont(13f, FontStyle.Bold),
                Padding = new Padding(16, 8, 16, 8),
                Dock = DockStyle.Fill,
            };
            btnRecord.Click += async (s, e) => await RecordFlowAsync();

            var optRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 10, 0, 0) };
            optRow.Controls.Add(new Label { Text = "FPS", AutoSize = true, Padding = new Padding(0, 4, 0, 0) });
            _numFps = new NumericUpDown { Minimum = 5, Maximum = 60, Value = 15, Width = 60 };
            optRow.Controls.Add(_numFps);
            _chkCursor = new CheckBox { Text = "마우스 커서 포함", Checked = true, AutoSize = true, Margin = new Padding(16, 4, 0, 0) };
            optRow.Controls.Add(_chkCursor);

            var hint = new Label
            {
                AutoSize = true,
                ForeColor = Color.DimGray,
                Margin = new Padding(0, 10, 0, 0),
                Text = "· 움짤(GIF)은 10~15fps, 영상(MP4)은 30fps 정도가 적당해요\n· 녹화 중 F10으로 시작/정지할 수 있어요" +
                       "\n· 빨간 테두리: 위쪽 탭을 끌면 이동, 테두리를 끌면 크기 조절 (녹화 중엔 이동만)",
            };

            _lblFfmpeg = new Label { AutoSize = true, Margin = new Padding(0, 6, 0, 0) };

            layout.Controls.Add(btnRecord);
            layout.Controls.Add(optRow);
            layout.Controls.Add(hint);
            layout.Controls.Add(_lblFfmpeg);
            Controls.Add(layout);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            CleanOldTempFolders();
            bool found = FfmpegLocator.Find() != null;
            _lblFfmpeg.Text = found ? "ffmpeg: OK" : "ffmpeg 없음 → winget install Gyan.FFmpeg (또는 같은 폴더에 ffmpeg.exe)";
            _lblFfmpeg.ForeColor = found ? Color.SeaGreen : Color.OrangeRed;
        }

        // 전체 흐름: 영역 선택 → 녹화 → 편집·저장
        private async Task RecordFlowAsync()
        {
            Hide();
            await Task.Delay(250);   // 이 창이 완전히 사라진 뒤에 화면을 찍도록 잠깐 대기
            try
            {
                Rectangle region;
                using (var sel = new RegionSelectForm())
                {
                    if (sel.ShowDialog() != DialogResult.OK) return;
                    region = sel.SelectedRegion;
                }

                RecordingResult result;
                using (var rec = new RecorderForm(region, (int)_numFps.Value, _chkCursor.Checked))
                {
                    if (rec.ShowDialog() != DialogResult.OK || rec.Result == null) return;
                    result = rec.Result;
                }

                using (var editor = new EditorForm(result))
                {
                    editor.ShowDialog();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("오류가 발생했어요: " + ex.Message, "ClipCam",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Show();
                Activate();
            }
        }

        // 프로그램이 비정상 종료돼서 남은 임시 폴더(하루 이상 지난 것)를 정리
        private static void CleanOldTempFolders()
        {
            try
            {
                string root = Path.Combine(Path.GetTempPath(), "ClipCam");
                if (!Directory.Exists(root)) return;
                foreach (var dir in Directory.GetDirectories(root))
                {
                    if (Directory.GetCreationTime(dir) < DateTime.Now.AddDays(-1))
                    {
                        try { Directory.Delete(dir, true); } catch { }
                    }
                }
            }
            catch { }
        }
    }

    /// <summary>exe에 들어 있는 아이콘을 창 아이콘으로 사용</summary>
    internal static class AppIcon
    {
        private static Icon _icon;
        public static Icon Get()
        {
            if (_icon == null)
            {
                try { _icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            }
            return _icon;
        }
    }
}
