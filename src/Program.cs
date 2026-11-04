using System;
using System.Windows.Forms;

namespace ClipCam
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            // 모니터 배율(125%, 150% 등)이 달라도 좌표가 어긋나지 않게
            NativeMethods.EnableDpiAwareness();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
