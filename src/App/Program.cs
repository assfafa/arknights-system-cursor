using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ArknightsSystemCursor
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length == 2 && args[0] == "--preview")
            {
                string destination = Path.GetFullPath(args[1]);
                using (MainForm preview = new MainForm(true))
                {
                    preview.Shown += delegate
                    {
                        Timer timer = new Timer { Interval = 450 };
                        timer.Tick += delegate
                        {
                            timer.Stop();
                            using (Bitmap bitmap = new Bitmap(preview.Width, preview.Height))
                            {
                                preview.DrawToBitmap(bitmap, new Rectangle(Point.Empty, preview.Size));
                                bitmap.Save(destination, System.Drawing.Imaging.ImageFormat.Png);
                            }
                            timer.Dispose();
                            preview.Close();
                        };
                        timer.Start();
                    };
                    Application.Run(preview);
                }
            }
            else
            {
                string? initialPath = null;
                if (args.Length == 1)
                {
                    initialPath = args[0];
                }
                Application.Run(new MainForm(false, initialPath));
            }
        }
    }
}
