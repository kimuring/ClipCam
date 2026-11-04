using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ClipCam
{
    /// <summary>
    /// 녹화 컨트롤 바 (녹화 시작 / 정지 / 취소).
    ///
    /// 구조:
    ///  - 캡처 스레드 1개: 정해진 간격(FPS)마다 화면을 찍어서 큐에 넣음
    ///  - 저장 스레드 여러 개: 큐에서 꺼내 PNG 파일로 저장
    ///  → 저장(PNG 압축)이 느려도 캡처 간격이 밀리지 않게 역할을 나눔
    /// </summary>
    public class RecorderForm : Form
    {
        private enum State { Ready, Recording, Stopping }

        private const int HotkeyId = 1;

        // 녹화 영역: 테두리를 드래그하면 바뀌므로, 캡처 스레드와 같이 쓸 때는 lock으로 보호
        private Rectangle _region;
        private readonly object _regionLock = new object();
        private Size _recordSize;   // 녹화 시작 시점의 크기 (녹화 중엔 고정)
        private readonly int _fps;
        private readonly bool _includeCursor;

        private readonly Button _btnStart, _btnStop, _btnCancel;
        private readonly Label _lblTime;
        private readonly System.Windows.Forms.Timer _uiTimer;
        private BorderForm _border;

        private State _state = State.Ready;
        private string _tempDir;
        private CancellationTokenSource _cts;
        private Task _captureTask;
        private Task[] _saveTasks;
        private BlockingCollection<CapturedFrame> _queue;
        private readonly List<long> _timestamps = new List<long>();   // 각 프레임을 찍은 시각(ms)
        private int _capturedCount;
        private Stopwatch _clock;

        public RecordingResult Result { get; private set; }

        public RecorderForm(Rectangle region, int fps, bool includeCursor)
        {
            _region = region;
            _fps = fps;
            _includeCursor = includeCursor;

            Text = "ClipCam";
            Font = Util.UiFont();
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            ControlBox = false;
            TopMost = true;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.Manual;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            var panel = new FlowLayoutPanel
            {
                AutoSize = true,
                WrapContents = false,
                Padding = new Padding(6),
                Dock = DockStyle.Fill,
            };

            _btnStart = new Button { Text = "● 녹화 시작 (F10)", AutoSize = true, ForeColor = Color.Red };
            _btnStop = new Button { Text = "■ 정지 (F10)", AutoSize = true, Enabled = false };
            _btnCancel = new Button { Text = "취소", AutoSize = true };
            _lblTime = new Label
            {
                AutoSize = true,
                Padding = new Padding(8, 7, 4, 0),
                Text = region.Width + "x" + region.Height + " / " + fps + "fps",
            };

            _btnStart.Click += (s, e) => StartRecording();
            _btnStop.Click += async (s, e) => await StopRecordingAsync();
            _btnCancel.Click += async (s, e) => await CancelAsync();

            panel.Controls.AddRange(new Control[] { _btnStart, _btnStop, _btnCancel, _lblTime });
            Controls.Add(panel);

            _uiTimer = new System.Windows.Forms.Timer { Interval = 100 };
            _uiTimer.Tick += (s, e) =>
            {
                var t = _clock.Elapsed;
                _lblTime.Text = string.Format("{0:00}:{1:00}.{2}  |  {3}프레임",
                    (int)t.TotalMinutes, t.Seconds, t.Milliseconds / 100, Volatile.Read(ref _capturedCount));
            };
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            _border = new BorderForm(_region);
            _border.CaptureRegionChanged += (s, ev) => OnBorderChanged();
            _border.Show();
            PlaceToolbar();
        }

        // 테두리를 드래그로 옮기거나 크기를 바꿨을 때
        private void OnBorderChanged()
        {
            var r = _border.CaptureRegion;
            lock (_regionLock) { _region = r; }
            if (_state == State.Ready)
                _lblTime.Text = r.Width + "x" + r.Height + " / " + _fps + "fps";
            PlaceToolbar();
        }

        // 컨트롤 바 위치: 테두리 바로 아래 → 공간 없으면 위 → 그것도 없으면 영역 안쪽
        private void PlaceToolbar()
        {
            if (_border == null) return;
            var frame = _border.Bounds;
            var wa = Screen.FromRectangle(frame).WorkingArea;
            int x = frame.Left;
            int y = frame.Bottom + 6;
            if (y + Height > wa.Bottom) y = frame.Top - Height - 6;
            if (y < wa.Top) y = frame.Top + 30;
            x = Math.Max(wa.Left, Math.Min(x, wa.Right - Width));
            Location = new Point(x, y);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // 컨트롤 바가 녹화 영역에 겹쳐도 찍히지 않게
            try { NativeMethods.SetWindowDisplayAffinity(Handle, NativeMethods.WDA_EXCLUDEFROMCAPTURE); } catch { }
            // F10 = 녹화 시작/정지 단축키 (다른 앱을 보고 있어도 동작)
            NativeMethods.RegisterHotKey(Handle, HotkeyId, 0, (uint)Keys.F10);
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            NativeMethods.UnregisterHotKey(Handle, HotkeyId);
            base.OnHandleDestroyed(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
            {
                if (_state == State.Ready) StartRecording();
                else if (_state == State.Recording) StopRecordingAsync().ContinueWith(t => { });
            }
            base.WndProc(ref m);
        }

        // ===== 녹화 시작 =====
        private void StartRecording()
        {
            if (_state != State.Ready) return;
            _state = State.Recording;
            lock (_regionLock) { _recordSize = _region.Size; }
            _border.Recording = true;   // 이제부터 크기 조절 금지, 이동만 가능

            // 임시 폴더: %TEMP%\ClipCam\날짜시각
            _tempDir = Path.Combine(Path.GetTempPath(), "ClipCam", DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"));
            Directory.CreateDirectory(_tempDir);

            // 큐가 너무 쌓이면(저장이 못 따라오면) 캡처가 잠깐 기다리게 해서 메모리 폭주 방지
            _queue = new BlockingCollection<CapturedFrame>(120);
            int saverCount = Util.Clamp(Environment.ProcessorCount / 2, 1, 4);
            _saveTasks = Enumerable.Range(0, saverCount).Select(i => Task.Run(() => SaveLoop())).ToArray();

            NativeMethods.timeBeginPeriod(1);
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _captureTask = Task.Run(() => CaptureLoop(token));

            _clock = Stopwatch.StartNew();
            _uiTimer.Start();
            _btnStart.Enabled = false;
            _btnStop.Enabled = true;
        }

        // 캡처 스레드: 1/FPS 초마다 화면을 찍음
        private void CaptureLoop(CancellationToken token)
        {
            var sw = Stopwatch.StartNew();
            double interval = 1000.0 / _fps;
            double next = 0;
            int index = 0;

            while (!token.IsCancellationRequested)
            {
                long t = sw.ElapsedMilliseconds;
                Rectangle region;
                lock (_regionLock) { region = _region; }   // 테두리를 옮기면 다음 장부터 새 위치를 찍음

                Bitmap bmp = null;
                try { bmp = ScreenCapture.Capture(region, _includeCursor); }
                catch { /* UAC 화면 전환 등으로 일시적으로 못 찍는 경우는 건너뜀 */ }

                if (bmp != null)
                {
                    _timestamps.Add(t);
                    _queue.Add(new CapturedFrame { Index = index, Bitmap = bmp });
                    index++;
                    Volatile.Write(ref _capturedCount, index);
                }

                // 다음 촬영 시각까지 대기 (늦었으면 기다리지 않고 바로 다음 장)
                next += interval;
                double wait = next - sw.Elapsed.TotalMilliseconds;
                if (wait > 0) Thread.Sleep((int)wait);
                else next = sw.Elapsed.TotalMilliseconds;
            }
        }

        // 저장 스레드: 큐에서 꺼내 PNG로 저장
        private void SaveLoop()
        {
            foreach (var f in _queue.GetConsumingEnumerable())
            {
                using (f.Bitmap)
                {
                    f.Bitmap.Save(FramePath(f.Index), ImageFormat.Png);
                }
            }
        }

        private string FramePath(int index)
        {
            return Path.Combine(_tempDir, "f" + index.ToString("D6") + ".png");
        }

        // 캡처를 멈추고, 남은 저장 작업이 끝날 때까지 기다림
        private async Task FinishCaptureAsync()
        {
            _uiTimer.Stop();
            _cts.Cancel();
            await _captureTask;
            _queue.CompleteAdding();
            await Task.WhenAll(_saveTasks);
            NativeMethods.timeEndPeriod(1);
        }

        // ===== 녹화 정지 → 결과 만들기 =====
        private async Task StopRecordingAsync()
        {
            if (_state != State.Recording) return;
            _state = State.Stopping;
            _btnStop.Enabled = false;
            _btnCancel.Enabled = false;
            _lblTime.Text = "저장 중...";

            await FinishCaptureAsync();

            // 프레임별 표시 시간 = 다음 프레임 촬영 시각 - 이 프레임 촬영 시각
            var frames = new List<FrameInfo>();
            for (int i = 0; i < _timestamps.Count; i++)
            {
                int delay = i < _timestamps.Count - 1
                    ? (int)(_timestamps[i + 1] - _timestamps[i])
                    : (int)(1000.0 / _fps);
                frames.Add(new FrameInfo { Path = FramePath(i), DelayMs = Math.Max(10, delay) });
            }

            Result = new RecordingResult
            {
                Frames = frames,
                TempDir = _tempDir,
                Fps = _fps,
                FrameSize = _recordSize,
            };
            DialogResult = frames.Count > 0 ? DialogResult.OK : DialogResult.Cancel;
        }

        // ===== 취소 =====
        private async Task CancelAsync()
        {
            if (_state == State.Stopping) return;
            if (_state == State.Recording)
            {
                _state = State.Stopping;
                await FinishCaptureAsync();
            }
            TryDeleteDir(_tempDir);
            DialogResult = DialogResult.Cancel;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_border != null) _border.Close();
            _uiTimer.Dispose();
            base.OnFormClosed(e);
        }

        private static void TryDeleteDir(string dir)
        {
            try { if (dir != null && Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }
    }
}
