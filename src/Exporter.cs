using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ClipCam
{
    public enum ExportFormat { Gif, Mp4 }

    public class ExportResult
    {
        public bool Success;
        public string Log;
    }

    /// <summary>
    /// 프레임(PNG 묶음) → GIF / MP4 변환. 실제 변환은 ffmpeg에 맡김.
    /// </summary>
    internal static class Exporter
    {
        public static async Task<ExportResult> ExportAsync(
            IList<FrameInfo> frames, int fps, double speed, int targetWidth,
            Size originalSize, ExportFormat format, string outputPath, string ffmpegPath)
        {
            // 1) ffmpeg용 "프레임 목록 파일" 작성 (어떤 이미지를 몇 초 보여줄지)
            string listPath = Path.Combine(Path.GetDirectoryName(frames[0].Path), "concat.txt");
            var sb = new StringBuilder();
            sb.Append("ffconcat version 1.0\n");
            foreach (var f in frames)
            {
                sb.Append("file '" + Escape(f.Path) + "'\n");
                sb.Append("duration " + (f.DelayMs / 1000.0).ToString("0.000", CultureInfo.InvariantCulture) + "\n");
            }
            // ffmpeg 규칙: 마지막 프레임을 한 번 더 적어야 마지막 duration이 반영됨
            sb.Append("file '" + Escape(frames[frames.Count - 1].Path) + "'\n");
            // BOM이 붙으면 ffmpeg가 못 읽으므로 BOM 없는 UTF-8로 저장
            File.WriteAllText(listPath, sb.ToString(), new UTF8Encoding(false));

            // 2) 필터 조립
            string sp = speed.ToString(CultureInfo.InvariantCulture);
            int width = targetWidth > 0 ? Math.Min(targetWidth, originalSize.Width) : originalSize.Width;
            string vf = "setpts=PTS/" + sp + ",fps=" + fps;   // 속도 조절 → 일정한 FPS로 맞춤

            var args = new List<string>
            {
                "-y", "-hide_banner", "-nostdin",
                "-f", "concat", "-safe", "0", "-i", listPath,
            };

            if (format == ExportFormat.Gif)
            {
                if (width != originalSize.Width) vf += ",scale=" + width + ":-1:flags=lanczos";
                // GIF는 256색 제한이 있어서, 영상에 맞는 팔레트를 먼저 만들고(palettegen) 그걸로 변환(paletteuse)
                vf += ",split[a][b];[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=5:diff_mode=rectangle";
                args.AddRange(new[] { "-vf", vf, "-loop", "0", outputPath });
            }
            else
            {
                // MP4(H.264)는 가로/세로가 짝수여야 해서 짝수로 맞춤
                width -= width % 2;
                vf += ",scale=" + width + ":-2:flags=lanczos";
                args.AddRange(new[]
                {
                    "-vf", vf,
                    "-c:v", "libx264", "-preset", "medium", "-crf", "20",
                    "-pix_fmt", "yuv420p",          // 대부분의 플레이어/SNS에서 재생되는 형식
                    "-movflags", "+faststart",
                    outputPath,
                });
            }

            // 3) ffmpeg 실행
            var psi = new ProcessStartInfo(ffmpegPath)
            {
                Arguments = string.Join(" ", args.Select(Quote)),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                StandardErrorEncoding = Encoding.UTF8,
            };

            using (var p = Process.Start(psi))
            {
                var logTask = p.StandardError.ReadToEndAsync();  // 출력을 읽어줘야 ffmpeg가 멈추지 않음
                await Task.Run(() => p.WaitForExit());
                string log = await logTask;
                return new ExportResult
                {
                    Success = p.ExitCode == 0 && File.Exists(outputPath),
                    Log = log,
                };
            }
        }

        // ffmpeg 목록 파일용: \ → /, 작은따옴표 이스케이프
        private static string Escape(string path)
        {
            return path.Replace('\\', '/').Replace("'", "'\\''");
        }

        // 명령줄 인수를 큰따옴표로 감싸기 (공백이 있는 경로 대응)
        private static string Quote(string arg)
        {
            return "\"" + arg.Replace("\"", "\\\"") + "\"";
        }
    }

    /// <summary>ffmpeg.exe 위치 찾기</summary>
    internal static class FfmpegLocator
    {
        public static string Find()
        {
            // 1) 이 프로그램과 같은 폴더 (배포 zip에 같이 들어 있음)
            string local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
            if (File.Exists(local)) return local;

            // 2) PATH 환경변수
            var paths = new[]
            {
                Environment.GetEnvironmentVariable("PATH"),
                Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User),
                Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine),
            };
            foreach (var dir in paths.Where(p => p != null).SelectMany(p => p.Split(';')))
            {
                try
                {
                    if (dir.Trim().Length == 0) continue;
                    string candidate = Path.Combine(Environment.ExpandEnvironmentVariables(dir.Trim()), "ffmpeg.exe");
                    if (File.Exists(candidate)) return candidate;
                }
                catch { /* 잘못된 경로는 무시 */ }
            }
            return null;
        }
    }
}
