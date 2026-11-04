using System;
using System.Collections.Generic;
using System.Drawing;

namespace ClipCam
{
    /// <summary>프레임 1장 = 이미지 파일 경로 + 화면에 보여줄 시간(ms)</summary>
    public class FrameInfo
    {
        public string Path { get; set; }
        public int DelayMs { get; set; }
    }

    /// <summary>녹화 결과 한 묶음</summary>
    public class RecordingResult
    {
        public List<FrameInfo> Frames { get; set; } = new List<FrameInfo>();
        public string TempDir { get; set; }   // 프레임 PNG가 저장된 임시 폴더
        public int Fps { get; set; }
        public Size FrameSize { get; set; }
    }

    /// <summary>캡처 직후, 파일로 저장되기 전의 프레임</summary>
    internal class CapturedFrame
    {
        public int Index;
        public Bitmap Bitmap;
    }

    internal static class Util
    {
        public static int Clamp(int v, int min, int max)
        {
            return v < min ? min : (v > max ? max : v);
        }

        /// <summary>모든 화면에서 같은 글꼴을 쓰기 위한 공통 UI 글꼴</summary>
        public static Font UiFont(float size = 9f, FontStyle style = FontStyle.Regular)
        {
            return new Font("Malgun Gothic", size, style);
        }
    }
}
