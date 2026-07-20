using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace PinShotWin
{
    internal static class Program
    {
        private const string SingleInstanceMutexName = @"Local\PinShotWin.SingleInstance";

        [STAThread]
        private static void Main(string[] args)
        {
            DpiAwareness.Enable();

            if (args.Length >= 2 && string.Equals(args[0], "--self-test-ui", StringComparison.OrdinalIgnoreCase))
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                SelfTestUi.Run(args[1]);
                return;
            }

            if (args.Length >= 2 && string.Equals(args[0], "--self-test-drag", StringComparison.OrdinalIgnoreCase))
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                CaptureOverlay.RunDragPerformanceTest(args[1]);
                return;
            }

            bool createdNew;
            using (var mutex = new Mutex(true, SingleInstanceMutexName, out createdNew))
            {
                if (!createdNew)
                {
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayContext());
                GC.KeepAlive(mutex);
            }
        }
    }

    internal static class DpiAwareness
    {
        private static readonly IntPtr PerMonitorAwareV2 = new IntPtr(-4);

        public static void Enable()
        {
            try
            {
                if (SetProcessDpiAwarenessContext(PerMonitorAwareV2))
                {
                    return;
                }
            }
            catch (EntryPointNotFoundException)
            {
            }

            try
            {
                if (SetProcessDpiAwareness(2) >= 0)
                {
                    return;
                }
            }
            catch (DllNotFoundException)
            {
            }
            catch (EntryPointNotFoundException)
            {
            }

            try
            {
                SetProcessDPIAware();
            }
            catch (EntryPointNotFoundException)
            {
            }
        }

        public static int GetCurrentAwarenessForTest()
        {
            try
            {
                return GetAwarenessFromDpiAwarenessContext(GetThreadDpiAwarenessContext());
            }
            catch (EntryPointNotFoundException)
            {
                return -1;
            }
        }

        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [DllImport("shcore.dll")]
        private static extern int SetProcessDpiAwareness(int awareness);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        private static extern IntPtr GetThreadDpiAwarenessContext();

        [DllImport("user32.dll")]
        private static extern int GetAwarenessFromDpiAwarenessContext(IntPtr value);
    }

    internal sealed class TrayContext : ApplicationContext
    {
        private readonly NotifyIcon tray;
        private readonly HotkeyWindow hotkeyWindow;
        private readonly ToolStripMenuItem recentMenu;
        private AppSettings settings;

        public TrayContext()
        {
            settings = AppSettings.Load();
            StartupManager.SetEnabled(settings.StartWithWindows);
            AppIcon.Load();

            hotkeyWindow = new HotkeyWindow();
            hotkeyWindow.HotkeyPressed += delegate { StartCapture(); };
            RegisterCurrentHotkey();

            var menu = new ContextMenuStrip();
            menu.Opening += delegate { RefreshRecentMenu(); };
            menu.Items.Add("设置", null, delegate { ShowSettings(); });
            recentMenu = new ToolStripMenuItem("最近截图");
            menu.Items.Add(recentMenu);
            menu.Items.Add("关于", null, delegate { ShowAbout(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { ExitThread(); });

            tray = new NotifyIcon
            {
                Text = "PinShotWin",
                Icon = AppIcon.Current,
                ContextMenuStrip = menu,
                Visible = true
            };
            tray.DoubleClick += delegate { StartCapture(); };
        }

        protected override void ExitThreadCore()
        {
            tray.Visible = false;
            tray.Dispose();
            hotkeyWindow.Dispose();
            base.ExitThreadCore();
        }

        private void RegisterCurrentHotkey()
        {
            if (!hotkeyWindow.TryRegister(settings.Hotkey))
            {
                MessageBox.Show(
                    "快捷键 " + settings.Hotkey + " 注册失败，可能被其他程序占用。",
                    "PinShotWin",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private void ShowSettings()
        {
            using (var form = new SettingsForm(settings))
            {
                if (form.ShowDialog() == DialogResult.OK)
                {
                    AppSettings updated = form.Settings;
                    if (!hotkeyWindow.TryRegister(updated.Hotkey))
                    {
                        MessageBox.Show(
                            "快捷键 " + updated.Hotkey + " 已被占用，原快捷键 " + settings.Hotkey + " 继续有效。",
                            "PinShotWin",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                        updated.Hotkey = settings.Hotkey;
                    }

                    settings = updated;
                    settings.Save();
                    StartupManager.SetEnabled(settings.StartWithWindows);
                }
            }
        }

        private void ShowAbout()
        {
            var version = AppVersion.Current;
            var message =
                "PinShotWin " + version + Environment.NewLine +
                "Hotkey: " + settings.Hotkey + Environment.NewLine +
                "Save: " + settings.SaveFormat + " / JPG " + settings.JpegQuality + "%" + Environment.NewLine +
                "Startup: " + (settings.StartWithWindows ? "On" : "Off") + Environment.NewLine +
                "Exe: " + Application.ExecutablePath + Environment.NewLine +
                "Settings: " + AppSettings.SettingsFilePath;

            MessageBox.Show(message, "About PinShotWin", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void StartCapture()
        {
            if (CaptureOverlay.IsOpen)
            {
                return;
            }

            var overlay = new CaptureOverlay(settings);
            overlay.CaptureCompleted += delegate(object sender, BitmapEventArgs e)
            {
                ScreenshotHistory.Add(e.Bitmap);
            };
            overlay.Show();
        }

        private void RefreshRecentMenu()
        {
            recentMenu.DropDownItems.Clear();
            var items = ScreenshotHistory.Items;
            if (items.Count == 0)
            {
                var empty = recentMenu.DropDownItems.Add("暂无截图");
                empty.Enabled = false;
                return;
            }

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var menuItem = new ToolStripMenuItem((i + 1) + ". " + item.CreatedAt.ToString("HH:mm:ss") + "  " + item.Image.Width + "x" + item.Image.Height);
                Bitmap image = item.Image;
                menuItem.DropDownItems.Add("复制", null, delegate { ClipboardHelper.TrySetImage(image, null); });
                menuItem.DropDownItems.Add("保存", null, delegate { SaveHistoryImage(image); });
                menuItem.DropDownItems.Add("钉住", null, delegate { new PinWindow((Bitmap)image.Clone(), Cursor.Position).Show(); });
                recentMenu.DropDownItems.Add(menuItem);
            }

            recentMenu.DropDownItems.Add(new ToolStripSeparator());
            recentMenu.DropDownItems.Add("清空", null, delegate
            {
                ScreenshotHistory.Clear();
                RefreshRecentMenu();
            });
        }

        private void SaveHistoryImage(Bitmap image)
        {
            using (var dialog = new SaveFileDialog())
            {
                dialog.Title = "保存历史截图";
                dialog.FileName = "screenshot_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                dialog.Filter = "JPG 图片 (*.jpg)|*.jpg|PNG 图片 (*.png)|*.png";
                dialog.DefaultExt = "jpg";

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    ImageSaveFormat format = Path.GetExtension(dialog.FileName).Equals(".png", StringComparison.OrdinalIgnoreCase)
                        ? ImageSaveFormat.Png
                        : ImageSaveFormat.Jpg;
                    ImageFile.Save(image, dialog.FileName, format, settings.JpegQuality);
                }
            }
        }
    }

    internal sealed class BitmapEventArgs : EventArgs
    {
        public Bitmap Bitmap { get; private set; }

        public BitmapEventArgs(Bitmap bitmap)
        {
            Bitmap = bitmap;
        }
    }

    internal sealed class WaitCursor : IDisposable
    {
        private readonly Cursor previous;

        public WaitCursor()
        {
            previous = Cursor.Current;
            Cursor.Current = Cursors.WaitCursor;
        }

        public void Dispose()
        {
            Cursor.Current = previous;
        }
    }

    internal static class ClipboardHelper
    {
        private const int RetryCount = 5;
        private const int RetryDelayMilliseconds = 80;

        public static bool TrySetImage(Bitmap image, IWin32Window owner)
        {
            using (var copy = (Bitmap)image.Clone())
            {
                var data = new DataObject();
                data.SetData(DataFormats.Bitmap, true, copy);
                if (TryWithRetry(delegate { Clipboard.SetDataObject(data, true); }, RetryCount, RetryDelayMilliseconds))
                {
                    return true;
                }
            }

            const string message = "剪贴板正被其他程序占用，请稍后重试。";
            if (owner == null)
            {
                MessageBox.Show(message, "PinShotWin", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                MessageBox.Show(owner, message, "PinShotWin", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            return false;
        }

        internal static bool RunRetryTest()
        {
            int attempts = 0;
            bool result = TryWithRetry(delegate
            {
                attempts++;
                if (attempts < 3)
                {
                    throw new ExternalException("clipboard busy");
                }
            }, RetryCount, 0);
            return result && attempts == 3;
        }

        private static bool TryWithRetry(Action action, int attempts, int delayMilliseconds)
        {
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                try
                {
                    action();
                    return true;
                }
                catch (ExternalException)
                {
                    if (attempt + 1 < attempts && delayMilliseconds > 0)
                    {
                        Thread.Sleep(delayMilliseconds);
                    }
                }
            }
            return false;
        }
    }

    internal static class SelfTestUi
    {
        public static void Run(string outputDir)
        {
            Directory.CreateDirectory(outputDir);
            WriteResult(outputDir, "dpi-awareness.txt", DpiAwareness.GetCurrentAwarenessForTest().ToString());
            WriteResult(outputDir, "hotkey-rollback.txt", HotkeyWindow.RunRegistrationRollbackTest() ? "pass" : "fail");
            WriteResult(outputDir, "clipboard-retry.txt", ClipboardHelper.RunRetryTest() ? "pass" : "fail");
            WriteResult(outputDir, "history-limit.txt", ScreenshotHistory.RunLimitTest() ? "pass" : "fail");

            using (var source = CreateSourceImage())
            {
                var annotations = new List<Annotation>
                {
                    new Annotation { Kind = AnnotationKind.Rectangle, Bounds = new Rectangle(20, 20, 70, 45) },
                    new Annotation { Kind = AnnotationKind.Arrow, Start = new Point(10, 110), End = new Point(140, 30) },
                    new Annotation { Kind = AnnotationKind.Text, Bounds = new Rectangle(18, 130, 120, 28), Text = "TEXT" },
                    new Annotation { Kind = AnnotationKind.Mosaic, Bounds = new Rectangle(95, 95, 50, 40) }
                };

                using (var rendered = AnnotationRenderer.Render(source, annotations))
                {
                    rendered.Save(Path.Combine(outputDir, "annotation.png"), ImageFormat.Png);
                    WriteResult(outputDir, "annotation_size.txt", rendered.Width + "x" + rendered.Height);
                    WriteResult(outputDir, "annotation_probe.txt", PixelSummary(rendered, new Rectangle(95, 95, 50, 40)));
                }
            }

            using (var source = new Bitmap(220, 160, PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(source))
            {
                g.Clear(Color.White);
                var diagonalArrow = new List<Annotation>
                {
                    new Annotation { Kind = AnnotationKind.Arrow, Start = new Point(20, 135), End = new Point(198, 38) }
                };
                using (var rendered = AnnotationRenderer.Render(source, diagonalArrow))
                {
                    rendered.Save(Path.Combine(outputDir, "arrow-diagonal.png"), ImageFormat.Png);
                    WriteResult(outputDir, "arrow-diagonal-solid.txt", CountSolidArrowPixels(rendered).ToString());
                    WriteResult(outputDir, "arrow-diagonal-edge.txt", CountAntialiasedArrowPixels(rendered).ToString());
                }
            }

            using (var canvas = CreateScrollingCanvas(240, 660))
            using (var a = CreateScrollingFrame(canvas, 0))
            using (var b = CreateScrollingFrame(canvas, 180))
            using (var c = CreateScrollingFrame(canvas, 360))
            {
                var frames = new List<Bitmap> { a, b, c };
                using (var stitched = ScrollingCapture.StitchForTest(frames))
                {
                    stitched.Save(Path.Combine(outputDir, "scroll.png"), ImageFormat.Png);
                    WriteResult(outputDir, "scroll_size.txt", stitched.Width + "x" + stitched.Height);
                }
            }

            using (var canvas = CreateEditorScrollingCanvas(270, 660))
            using (var a = CreateEditorScrollingFrame(canvas, 0, 0))
            using (var b = CreateEditorScrollingFrame(canvas, 190, 0))
            using (var c = CreateEditorScrollingFrame(canvas, 380, 0))
            using (var settled = CreateEditorScrollingFrame(canvas, 380, 3))
            {
                var frames = new List<Bitmap> { a, b, c, settled };
                using (var stitched = ScrollingCapture.StitchForTest(frames))
                {
                    stitched.Save(Path.Combine(outputDir, "scroll-editor.png"), ImageFormat.Png);
                    WriteResult(outputDir, "scroll_editor_size.txt", stitched.Width + "x" + stitched.Height);
                    WriteResult(outputDir, "scroll_editor_diagnostics.txt", ScrollingCapture.GetDiagnosticsForTest(frames));
                    WriteResult(outputDir, "scroll_editor_ui_pixels.txt", CountEditorUiPixels(stitched).ToString());
                }

                long streamedBytes;
                using (var streamed = ScrollingCapture.StitchStreamingForTest(frames, out streamedBytes))
                {
                    streamed.Save(Path.Combine(outputDir, "scroll-editor-streamed.png"), ImageFormat.Png);
                    WriteResult(outputDir, "scroll_editor_streamed_ui_pixels.txt", CountEditorUiPixels(streamed).ToString());
                }
            }

            using (var canvas = CreateScrollingCanvas(240, 2100))
            {
                var frames = new List<Bitmap>();
                try
                {
                    for (int offset = 0; offset <= 1800; offset += 180)
                    {
                        frames.Add(CreateScrollingFrame(canvas, offset));
                    }

                    long compressedBytes;
                    using (var streamed = ScrollingCapture.StitchStreamingForTest(frames, out compressedBytes))
                    {
                        streamed.Save(Path.Combine(outputDir, "scroll-streamed.png"), ImageFormat.Png);
                        WriteResult(outputDir, "scroll_streamed_size.txt", streamed.Width + "x" + streamed.Height);
                        WriteResult(outputDir, "scroll_streamed_compressed_bytes.txt", compressedBytes.ToString());
                    }
                }
                finally
                {
                    foreach (var frame in frames)
                    {
                        frame.Dispose();
                    }
                }
            }

            WriteResult(outputDir, "scroll_limits.txt",
                ScrollingCapture.MaxOutputPixelsForTest + "," +
                ScrollingCapture.MaxBitmapDimensionForTest + "," +
                ScreenshotHistory.MaxTotalPixelsForTest + "," +
                ScrollingCapture.CanCreateOutputForTest(1920, 32760) + "," +
                ScrollingCapture.CanCreateOutputForTest(4096, 20000));

            Rectangle previewLayout = CaptureOverlay.CalculateScrollingPreviewBounds(
                new Rectangle(260, 180, 640, 320),
                new Size(640, 1280),
                new Rectangle(0, 0, 1600, 900),
                44);
            WriteResult(outputDir, "scroll_preview_layout.txt",
                previewLayout.X + "," + previewLayout.Y + "," + previewLayout.Width + "," + previewLayout.Height);

            using (var source = new Bitmap(100, 80, PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(source))
            {
                g.Clear(Color.White);
                var ordered = new List<Annotation>
                {
                    new Annotation { Kind = AnnotationKind.Rectangle, Bounds = new Rectangle(10, 10, 80, 60) },
                    new Annotation { Kind = AnnotationKind.Mosaic, Bounds = new Rectangle(5, 5, 90, 70) }
                };
                using (var rendered = AnnotationRenderer.Render(source, ordered))
                {
                    rendered.Save(Path.Combine(outputDir, "annotation-order.png"), ImageFormat.Png);
                    WriteResult(outputDir, "annotation-order-red.txt", CountRedPixels(rendered).ToString());
                }
            }

            using (var desktop = new Bitmap(80, 40, PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(desktop))
            {
                g.Clear(Color.Red);
                g.FillRectangle(Brushes.Lime, 40, 0, 40, 40);
                using (var movedSelection = desktop.Clone(new Rectangle(40, 0, 40, 40), PixelFormat.Format32bppArgb))
                {
                    movedSelection.Save(Path.Combine(outputDir, "moved-selection.png"), ImageFormat.Png);
                }
            }

            WriteResult(outputDir, "text-escape.txt", CaptureOverlay.RunTextEscapeTest() ? "pass" : "fail");
            WriteResult(outputDir, "long-preview-lock.txt", CaptureOverlay.RunLongPreviewLockTest() ? "pass" : "fail");
        }

        private static Bitmap CreateSourceImage()
        {
            var bitmap = new Bitmap(180, 180, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.White);
                for (int y = 0; y < bitmap.Height; y += 10)
                {
                    using (var brush = new SolidBrush(y % 20 == 0 ? Color.FromArgb(220, 234, 255) : Color.FromArgb(236, 246, 232)))
                    {
                        g.FillRectangle(brush, 0, y, bitmap.Width, 10);
                    }
                }
                using (var pen = new Pen(Color.FromArgb(40, 40, 40), 1))
                {
                    g.DrawRectangle(pen, 0, 0, bitmap.Width - 1, bitmap.Height - 1);
                }
                using (var font = new Font("Segoe UI", 11F, FontStyle.Regular, GraphicsUnit.Pixel))
                using (var brush = new SolidBrush(Color.FromArgb(35, 35, 35)))
                {
                    g.DrawString("SECRET TEXT", font, brush, 96, 104);
                }
            }
            return bitmap;
        }

        private static Bitmap CreateScrollingCanvas(int width, int height)
        {
            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.White);
                for (int y = 0; y < height; y += 12)
                {
                    using (var brush = new SolidBrush(Color.FromArgb((y * 7) % 240, (y * 13) % 240, (y * 17) % 240)))
                    {
                        g.FillRectangle(brush, 0, y, width, 12);
                    }
                }
            }
            return bitmap;
        }

        private static Bitmap CreateScrollingFrame(Bitmap canvas, int offset)
        {
            var frame = new Bitmap(300, 300, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(frame))
            {
                g.Clear(Color.FromArgb(235, 239, 247));
                using (var fixedBrush = new SolidBrush(Color.FromArgb(205, 215, 230)))
                {
                    g.FillRectangle(fixedBrush, 0, 0, 60, frame.Height);
                }
                g.DrawImage(canvas, new Rectangle(60, 0, 240, 300), new Rectangle(0, offset, 240, 300), GraphicsUnit.Pixel);
            }
            return frame;
        }

        private static Bitmap CreateEditorScrollingCanvas(int width, int height)
        {
            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.FromArgb(24, 26, 29));
                using (var lineBrush = new SolidBrush(Color.FromArgb(91, 184, 255)))
                using (var accentBrush = new SolidBrush(Color.FromArgb(218, 224, 231)))
                {
                    for (int line = 0; line < 30; line++)
                    {
                        int y = 8 + line * 20;
                        if (y >= 260 && y < 380)
                        {
                            continue;
                        }

                        int indent = 12 + (line % 4) * 11;
                        int length = 70 + (line * 37) % 150;
                        g.FillRectangle(line % 3 == 0 ? accentBrush : lineBrush, indent, y, length, 3);
                        g.FillRectangle(lineBrush, indent + 8, y + 6, Math.Max(24, length / 2), 2);
                    }
                }
            }
            return bitmap;
        }

        private static Bitmap CreateEditorScrollingFrame(Bitmap canvas, int offset, int thumbNudge)
        {
            const int leftPanelWidth = 45;
            const int contentWidth = 270;
            const int miniMapWidth = 30;
            const int scrollBarWidth = 15;
            const int viewportTop = 20;
            const int viewportHeight = 280;
            var frame = new Bitmap(leftPanelWidth + contentWidth + miniMapWidth + scrollBarWidth, 320, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(frame))
            {
                g.Clear(Color.FromArgb(36, 39, 44));
                using (var panelBrush = new SolidBrush(Color.FromArgb(44, 48, 55)))
                using (var mapBrush = new SolidBrush(Color.FromArgb(28, 31, 35)))
                using (var mapLineBrush = new SolidBrush(Color.FromArgb(77, 105, 126)))
                using (var trackBrush = new SolidBrush(Color.FromArgb(30, 32, 35)))
                using (var thumbBrush = new SolidBrush(Color.FromArgb(104, 108, 112)))
                {
                    g.FillRectangle(panelBrush, 0, viewportTop, leftPanelWidth, viewportHeight);
                    g.DrawImage(canvas,
                        new Rectangle(leftPanelWidth, viewportTop, contentWidth, viewportHeight),
                        new Rectangle(0, offset, contentWidth, viewportHeight),
                        GraphicsUnit.Pixel);

                    int mapLeft = leftPanelWidth + contentWidth;
                    g.FillRectangle(mapBrush, mapLeft, viewportTop, miniMapWidth, viewportHeight);
                    for (int y = viewportTop + 4; y < viewportTop + viewportHeight - 3; y += 7)
                    {
                        int sourceBand = (offset / 5 + y * 3) % 97;
                        g.FillRectangle(mapLineBrush, mapLeft + 3, y, 8 + sourceBand % 18, 2);
                    }

                    int barLeft = mapLeft + miniMapWidth;
                    g.FillRectangle(trackBrush, barLeft, viewportTop, scrollBarWidth, viewportHeight);
                    int thumbTravel = viewportHeight - 72;
                    int thumbTop = viewportTop + (int)Math.Round(offset / 380.0 * thumbTravel) + thumbNudge;
                    g.FillRectangle(thumbBrush, barLeft + 2, thumbTop, scrollBarWidth - 3, 72);
                }
            }
            return frame;
        }

        private static int CountRedPixels(Bitmap bitmap)
        {
            int count = 0;
            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    Color color = bitmap.GetPixel(x, y);
                    if (color.R > 180 && color.G < 100 && color.B < 100)
                    {
                        count++;
                    }
                }
            }
            return count;
        }

        private static int CountEditorUiPixels(Bitmap bitmap)
        {
            int count = 0;
            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    Color color = bitmap.GetPixel(x, y);
                    bool fixedPanel = color.R == 44 && color.G == 48 && color.B == 55;
                    bool miniMap = color.R == 77 && color.G == 105 && color.B == 126;
                    bool scrollThumb = color.R == 104 && color.G == 108 && color.B == 112;
                    if (fixedPanel || miniMap || scrollThumb)
                    {
                        count++;
                    }
                }
            }
            return count;
        }

        private static int CountSolidArrowPixels(Bitmap bitmap)
        {
            int count = 0;
            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    Color color = bitmap.GetPixel(x, y);
                    if (color.R >= 225 && color.R <= 242 && color.G <= 55 && color.B <= 55)
                    {
                        count++;
                    }
                }
            }
            return count;
        }

        private static int CountAntialiasedArrowPixels(Bitmap bitmap)
        {
            int count = 0;
            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    Color color = bitmap.GetPixel(x, y);
                    bool white = color.R == 255 && color.G == 255 && color.B == 255;
                    bool solidRed = color.R >= 225 && color.R <= 242 && color.G <= 55 && color.B <= 55;
                    if (!white && !solidRed && color.R > color.G + 30 && color.R > color.B + 30)
                    {
                        count++;
                    }
                }
            }
            return count;
        }

        private static string PixelSummary(Bitmap bitmap, Rectangle rect)
        {
            long total = 0;
            int count = 0;
            for (int y = rect.Top; y < rect.Bottom; y += 5)
            {
                for (int x = rect.Left; x < rect.Right; x += 5)
                {
                    Color c = bitmap.GetPixel(x, y);
                    total += c.R + c.G + c.B;
                    count++;
                }
            }
            return count == 0 ? "0" : (total / count).ToString();
        }

        private static void WriteResult(string outputDir, string fileName, string text)
        {
            File.WriteAllText(Path.Combine(outputDir, fileName), text, Encoding.UTF8);
        }
    }

    internal sealed class HistoryItem
    {
        public Bitmap Image;
        public DateTime CreatedAt;
    }

    internal static class ScreenshotHistory
    {
        private const int MaxItems = 10;
        private const long MaxTotalPixels = 64L * 1024 * 1024;
        private static readonly List<HistoryItem> items = new List<HistoryItem>();
        private static long totalPixels;

        public static long MaxTotalPixelsForTest
        {
            get { return MaxTotalPixels; }
        }

        public static List<HistoryItem> Items
        {
            get { return new List<HistoryItem>(items); }
        }

        public static void Add(Bitmap bitmap)
        {
            items.Insert(0, new HistoryItem
            {
                Image = (Bitmap)bitmap.Clone(),
                CreatedAt = DateTime.Now
            });
            totalPixels += bitmap.Width * (long)bitmap.Height;

            while (items.Count > MaxItems || (totalPixels > MaxTotalPixels && items.Count > 1))
            {
                var last = items[items.Count - 1];
                items.RemoveAt(items.Count - 1);
                totalPixels -= last.Image.Width * (long)last.Image.Height;
                last.Image.Dispose();
            }
        }

        public static void Clear()
        {
            foreach (var item in items)
            {
                item.Image.Dispose();
            }
            items.Clear();
            totalPixels = 0;
        }

        public static bool RunLimitTest()
        {
            Clear();
            try
            {
                using (var image = new Bitmap(3000, 3000, PixelFormat.Format32bppArgb))
                {
                    for (int i = 0; i < 8; i++)
                    {
                        Add(image);
                    }
                }

                long pixels = 0;
                foreach (var item in items)
                {
                    pixels += item.Image.Width * (long)item.Image.Height;
                }
                return items.Count == 7 && pixels <= MaxTotalPixels;
            }
            finally
            {
                Clear();
            }
        }
    }

    internal sealed class AppSettings
    {
        public Keys Hotkey = Keys.F1;
        public bool StartWithWindows = true;
        public ImageSaveFormat SaveFormat = ImageSaveFormat.Jpg;
        public int JpegQuality = 90;

        public static string SettingsFilePath
        {
            get { return SettingsPath; }
        }

        private static string SettingsPath
        {
            get
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PinShotWin");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "settings.ini");
            }
        }

        public static AppSettings Load()
        {
            var settings = new AppSettings();
            if (!File.Exists(SettingsPath))
            {
                settings.Save();
                return settings;
            }

            foreach (var line in File.ReadAllLines(SettingsPath))
            {
                var index = line.IndexOf('=');
                if (index <= 0)
                {
                    continue;
                }

                var key = line.Substring(0, index).Trim();
                var value = line.Substring(index + 1).Trim();

                if (key.Equals("Hotkey", StringComparison.OrdinalIgnoreCase))
                {
                    Keys parsed;
                    if (Enum.TryParse(value, true, out parsed))
                    {
                        settings.Hotkey = parsed;
                    }
                }
                else if (key.Equals("StartWithWindows", StringComparison.OrdinalIgnoreCase))
                {
                    bool parsed;
                    if (bool.TryParse(value, out parsed))
                    {
                        settings.StartWithWindows = parsed;
                    }
                }
                else if (key.Equals("SaveFormat", StringComparison.OrdinalIgnoreCase))
                {
                    ImageSaveFormat parsed;
                    if (Enum.TryParse(value, true, out parsed))
                    {
                        settings.SaveFormat = parsed;
                    }
                }
                else if (key.Equals("JpegQuality", StringComparison.OrdinalIgnoreCase))
                {
                    int parsed;
                    if (int.TryParse(value, out parsed))
                    {
                        settings.JpegQuality = Math.Max(1, Math.Min(100, parsed));
                    }
                }
            }

            return settings;
        }

        public void Save()
        {
            File.WriteAllLines(SettingsPath, new[]
            {
                "Hotkey=" + Hotkey,
                "StartWithWindows=" + StartWithWindows,
                "SaveFormat=" + SaveFormat,
                "JpegQuality=" + JpegQuality
            });
        }
    }

    internal enum ImageSaveFormat
    {
        Jpg,
        Png
    }

    internal static class AppVersion
    {
        public static string Current
        {
            get
            {
                var version = Application.ProductVersion;
                return string.IsNullOrEmpty(version) ? "unknown" : version;
            }
        }
    }

    internal static class AppIcon
    {
        public static Icon Current = SystemIcons.Application;

        public static void Load()
        {
            try
            {
                var embeddedIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (embeddedIcon != null)
                {
                    Current = embeddedIcon;
                    return;
                }
            }
            catch
            {
            }

            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "app.ico");
            if (File.Exists(path))
            {
                Current = new Icon(path);
            }
        }
    }

    internal static class StartupManager
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "PinShotWin";

        public static void SetEnabled(bool enabled)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (key == null)
                    {
                        return;
                    }

                    if (enabled)
                    {
                        key.SetValue(AppName, "\"" + Application.ExecutablePath + "\"");
                    }
                    else
                    {
                        key.DeleteValue(AppName, false);
                    }
                }
            }
            catch
            {
                // Startup is a convenience feature. Failure should not block screenshots.
            }
        }
    }

    internal sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        private const int WmHotkey = 0x0312;
        private const int PrimaryHotkeyId = 9130;
        private const int SecondaryHotkeyId = 9131;
        private int currentHotkeyId;
        private Keys currentKey = Keys.None;
        private bool registered;

        public event EventHandler HotkeyPressed;

        public HotkeyWindow()
        {
            CreateHandle(new CreateParams());
        }

        public Keys CurrentKeyForTest
        {
            get { return currentKey; }
        }

        public bool IsRegisteredForTest
        {
            get { return registered; }
        }

        public bool TryRegister(Keys key)
        {
            if (registered && currentKey == key)
            {
                return true;
            }

            int candidateId = !registered || currentHotkeyId == SecondaryHotkeyId
                ? PrimaryHotkeyId
                : SecondaryHotkeyId;
            if (!RegisterHotKey(Handle, candidateId, 0, (int)key))
            {
                return false;
            }

            if (registered)
            {
                UnregisterHotKey(Handle, currentHotkeyId);
            }

            currentHotkeyId = candidateId;
            currentKey = key;
            registered = true;
            return true;
        }

        public static bool RunRegistrationRollbackTest()
        {
            using (var occupied = new HotkeyWindow())
            using (var subject = new HotkeyWindow())
            {
                if (!occupied.TryRegister(Keys.F24) || !subject.TryRegister(Keys.F23))
                {
                    return false;
                }

                bool switched = subject.TryRegister(Keys.F24);
                return !switched && subject.IsRegisteredForTest && subject.CurrentKeyForTest == Keys.F23;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmHotkey && registered && m.WParam.ToInt32() == currentHotkeyId)
            {
                var handler = HotkeyPressed;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
                return;
            }

            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (registered)
            {
                UnregisterHotKey(Handle, currentHotkeyId);
                registered = false;
            }
            DestroyHandle();
        }

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, int vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }

    internal sealed class SettingsForm : Form
    {
        private readonly ComboBox hotkeyBox = new ComboBox();
        private readonly CheckBox startupBox = new CheckBox();
        private readonly ComboBox formatBox = new ComboBox();
        private readonly NumericUpDown qualityBox = new NumericUpDown();
        private readonly Label qualityValueLabel = new Label();

        public AppSettings Settings { get; private set; }

        public SettingsForm(AppSettings source)
        {
            Settings = new AppSettings
            {
                Hotkey = source.Hotkey,
                StartWithWindows = source.StartWithWindows,
                SaveFormat = source.SaveFormat,
                JpegQuality = source.JpegQuality
            };

            Text = "PinShotWin 设置";
            Icon = AppIcon.Current;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(430, 300);
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.FromArgb(246, 248, 252);

            var title = new Label
            {
                Text = "PinShotWin",
                Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold),
                ForeColor = Color.FromArgb(20, 28, 44),
                AutoSize = true,
                Location = new Point(28, 22)
            };
            Controls.Add(title);

            var subtitle = new Label
            {
                Text = "截图、保存和贴图偏好",
                ForeColor = Color.FromArgb(95, 104, 120),
                AutoSize = true,
                Location = new Point(30, 54)
            };
            Controls.Add(subtitle);

            var panel = new Panel
            {
                BackColor = Color.White,
                Location = new Point(24, 84),
                Size = new Size(382, 154)
            };
            panel.Paint += delegate(object sender, PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var pen = new Pen(Color.FromArgb(224, 228, 236)))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, panel.Width - 1, panel.Height - 1);
                }
            };
            Controls.Add(panel);

            AddSettingsLabel(panel, "截图快捷键", 18, 19);
            AddSettingsLabel(panel, "开机自启", 18, 55);
            AddSettingsLabel(panel, "保存格式", 18, 91);
            AddSettingsLabel(panel, "JPG 质量", 18, 127);

            hotkeyBox.DropDownStyle = ComboBoxStyle.DropDownList;
            hotkeyBox.Items.AddRange(new object[] { Keys.F1, Keys.F2, Keys.F3, Keys.F4, Keys.F6, Keys.F7, Keys.F8, Keys.F9, Keys.F10, Keys.F11, Keys.F12 });
            hotkeyBox.SelectedItem = Settings.Hotkey;
            hotkeyBox.Location = new Point(154, 15);
            hotkeyBox.Width = 190;
            panel.Controls.Add(hotkeyBox);

            startupBox.Checked = Settings.StartWithWindows;
            startupBox.Text = "随 Windows 启动";
            startupBox.ForeColor = Color.FromArgb(42, 50, 66);
            startupBox.Location = new Point(154, 51);
            startupBox.Width = 190;
            panel.Controls.Add(startupBox);

            formatBox.DropDownStyle = ComboBoxStyle.DropDownList;
            formatBox.Items.AddRange(new object[] { ImageSaveFormat.Jpg, ImageSaveFormat.Png });
            formatBox.SelectedItem = Settings.SaveFormat;
            formatBox.Location = new Point(154, 87);
            formatBox.Width = 190;
            panel.Controls.Add(formatBox);

            qualityBox.Minimum = 1;
            qualityBox.Maximum = 100;
            qualityBox.Value = Settings.JpegQuality;
            qualityBox.Location = new Point(154, 123);
            qualityBox.Width = 132;
            panel.Controls.Add(qualityBox);

            qualityValueLabel.Text = Settings.JpegQuality + "%";
            qualityValueLabel.ForeColor = Color.FromArgb(95, 104, 120);
            qualityValueLabel.AutoSize = true;
            qualityValueLabel.Location = new Point(294, 127);
            panel.Controls.Add(qualityValueLabel);
            qualityBox.ValueChanged += delegate { qualityValueLabel.Text = qualityBox.Value + "%"; };

            var okButton = new Button { Text = "保存", DialogResult = DialogResult.OK, Location = new Point(250, 254), Size = new Size(74, 30), FlatStyle = FlatStyle.System };
            var cancelButton = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Location = new Point(332, 254), Size = new Size(74, 30), FlatStyle = FlatStyle.System };
            Controls.Add(okButton);
            Controls.Add(cancelButton);
            AcceptButton = okButton;
            CancelButton = cancelButton;

            okButton.Click += delegate
            {
                Settings.Hotkey = (Keys)hotkeyBox.SelectedItem;
                Settings.StartWithWindows = startupBox.Checked;
                Settings.SaveFormat = (ImageSaveFormat)formatBox.SelectedItem;
                Settings.JpegQuality = (int)qualityBox.Value;
            };
        }

        private static void AddSettingsLabel(Control parent, string text, int x, int y)
        {
            parent.Controls.Add(new Label
            {
                Text = text,
                ForeColor = Color.FromArgb(68, 78, 96),
                AutoSize = true,
                Location = new Point(x, y)
            });
        }
    }

    internal sealed class CaptureOverlay : Form, IMessageFilter
    {
        public static bool IsOpen;
        public event EventHandler<BitmapEventArgs> CaptureCompleted;

        private readonly AppSettings settings;
        private readonly Bitmap desktopBitmap;
        private readonly Bitmap dimmedDesktopBitmap;
        private readonly Rectangle virtualBounds;
        private Rectangle selectedBounds;
        private Rectangle hoverBounds;
        private bool dragging;
        private Point dragStart;
        private bool previewMode;
        private readonly CaptureToolbar toolbar;
        private SelectionDragMode previewDragMode = SelectionDragMode.None;
        private Rectangle previewDragStartBounds;
        private Point previewDragStartPoint;
        private Rectangle hiddenToolbarBounds;
        private readonly List<Annotation> annotations = new List<Annotation>();
        private AnnotationTool activeTool = AnnotationTool.None;
        private bool annotationDragging;
        private Point annotationStart;
        private Point annotationCurrent;
        private TextBox textEditor;
        private Point textEditorImageStart;
        private Bitmap previewBitmap;

        public CaptureOverlay(AppSettings settings)
        {
            this.settings = settings;
            IsOpen = true;
            virtualBounds = SystemInformation.VirtualScreen;
            desktopBitmap = ScreenshotHelper.CaptureVirtualScreen(virtualBounds);
            dimmedDesktopBitmap = ScreenshotHelper.CreateDimmedBitmap(desktopBitmap);

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = virtualBounds;
            TopMost = true;
            ShowInTaskbar = false;
            Cursor = Cursors.Cross;
            DoubleBuffered = true;
            KeyPreview = true;

            toolbar = new CaptureToolbar();
            toolbar.Visible = false;
            toolbar.CopyClicked += delegate { CopySelection(); };
            toolbar.SaveClicked += delegate { SaveSelection(); };
            toolbar.PinClicked += delegate { PinSelection(); };
            toolbar.RectClicked += delegate { SetActiveTool(AnnotationTool.Rectangle); };
            toolbar.ArrowClicked += delegate { SetActiveTool(AnnotationTool.Arrow); };
            toolbar.TextClicked += delegate { SetActiveTool(AnnotationTool.Text); };
            toolbar.MosaicClicked += delegate { SetActiveTool(AnnotationTool.Mosaic); };
            toolbar.UndoClicked += delegate { UndoAnnotation(); };
            toolbar.ScrollClicked += delegate { StartScrollingCapture(); };
            toolbar.CancelClicked += delegate { Close(); };
            Controls.Add(toolbar);
        }

        public static void RunDragPerformanceTest(string outputPath)
        {
            using (var overlay = new CaptureOverlay(new AppSettings()))
            {
                overlay.Show();
                Application.DoEvents();
                overlay.selectedBounds = new Rectangle(300, 220, 750, 500);
                overlay.EnterPreview();
                overlay.BeginSelectionAdjustment(new Point(650, 450), SelectionDragMode.Move);
                bool hiddenDuringDrag = !overlay.toolbar.Visible;

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 1; i <= 90; i++)
                {
                    var location = new Point(650 + 300 * i / 90, 450);
                    overlay.OnMouseMove(new MouseEventArgs(MouseButtons.Left, 0, location.X, location.Y, 0));
                    overlay.Update();
                }
                stopwatch.Stop();
                overlay.EndSelectionAdjustment(new Point(950, 450));

                double fps = 90000.0 / Math.Max(1, stopwatch.ElapsedMilliseconds);
                int finalSelectionX = overlay.GetSelectionXForTest();
                File.WriteAllText(outputPath,
                    "elapsed_ms=" + stopwatch.ElapsedMilliseconds + Environment.NewLine +
                    "fps=" + fps.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + Environment.NewLine +
                    "selection_x=" + finalSelectionX + Environment.NewLine +
                    "toolbar_hidden_during_drag=" + hiddenDuringDrag + Environment.NewLine +
                    "toolbar_visible_after_drag=" + overlay.toolbar.Visible,
                    Encoding.UTF8);
            }
        }

        internal static Rectangle CalculateScrollingPreviewBounds(
            Rectangle previousBounds,
            Size imageSize,
            Rectangle clientBounds,
            int toolbarHeight)
        {
            const int margin = 12;
            const int toolbarGap = 8;
            int availableWidth = Math.Max(1, clientBounds.Width - margin * 2);
            int availableHeight = Math.Max(1, clientBounds.Height - margin * 2 - toolbarHeight - toolbarGap);
            double scale = Math.Min(1.0, Math.Min(
                availableWidth / (double)Math.Max(1, imageSize.Width),
                availableHeight / (double)Math.Max(1, imageSize.Height)));
            int width = Math.Max(1, (int)Math.Round(imageSize.Width * scale));
            int height = Math.Max(1, (int)Math.Round(imageSize.Height * scale));
            int centerX = previousBounds.Left + previousBounds.Width / 2;
            int centerY = previousBounds.Top + previousBounds.Height / 2;
            int left = centerX - width / 2;
            int top = centerY - height / 2;
            left = Math.Max(clientBounds.Left + margin, Math.Min(clientBounds.Right - margin - width, left));
            top = Math.Max(clientBounds.Top + margin, Math.Min(clientBounds.Bottom - margin - toolbarHeight - toolbarGap - height, top));
            return new Rectangle(left, top, width, height);
        }

        private int GetSelectionXForTest()
        {
            return selectedBounds.X;
        }

        public static bool RunTextEscapeTest()
        {
            using (var overlay = new CaptureOverlay(new AppSettings()))
            {
                overlay.Show();
                Application.DoEvents();
                overlay.selectedBounds = new Rectangle(300, 220, 750, 500);
                overlay.EnterPreview();
                overlay.ShowTextEditor(new Point(420, 360));

                var message = Message.Create(overlay.Handle, 0x0100, (IntPtr)(int)Keys.Escape, IntPtr.Zero);
                bool handled = overlay.PreFilterMessage(ref message);
                return handled && overlay.textEditor == null && !overlay.IsDisposed;
            }
        }

        public static bool RunLongPreviewLockTest()
        {
            using (var overlay = new CaptureOverlay(new AppSettings()))
            {
                overlay.Show();
                Application.DoEvents();
                overlay.selectedBounds = new Rectangle(260, 180, 640, 320);
                overlay.EnterPreview();
                overlay.SetPreviewBitmap(new Bitmap(640, 1280, PixelFormat.Format32bppArgb));
                overlay.selectedBounds = CalculateScrollingPreviewBounds(
                    overlay.selectedBounds,
                    overlay.previewBitmap.Size,
                    overlay.ClientRectangle,
                    overlay.toolbar.Height);

                Rectangle before = overlay.selectedBounds;
                overlay.AdjustSelectionWithKeyboard(Keys.Right, true, 10);
                bool resizeLocked = overlay.selectedBounds == before;
                overlay.AdjustSelectionWithKeyboard(Keys.Right, false, 10);
                Rectangle moved = overlay.selectedBounds;
                bool moveAllowed = moved.X == before.X + 10 && moved.Size == before.Size;
                bool originalSizeShown = FormatSelectionSize(overlay.previewBitmap.Size) == "640 x 1280";
                return resizeLocked && moveAllowed && originalSizeShown;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
        }

        protected override void OnClosed(EventArgs e)
        {
            Application.RemoveMessageFilter(this);
            if (previewBitmap != null)
            {
                previewBitmap.Dispose();
            }
            dimmedDesktopBitmap.Dispose();
            desktopBitmap.Dispose();
            IsOpen = false;
            base.OnClosed(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                if (textEditor != null)
                {
                    CancelTextEditor();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    return;
                }

                Close();
                return;
            }

            if (previewMode && e.Control && e.KeyCode == Keys.C)
            {
                CopySelection();
                e.Handled = true;
                return;
            }

            if (previewMode && e.Control && e.KeyCode == Keys.S)
            {
                SaveSelection();
                e.Handled = true;
                return;
            }

            if (previewMode && e.Control && e.KeyCode == Keys.P)
            {
                PinSelection();
                e.Handled = true;
                return;
            }

            if (previewMode && e.Control && e.KeyCode == Keys.Z)
            {
                UndoAnnotation();
                e.Handled = true;
                return;
            }

            if (previewMode && e.KeyCode == Keys.Enter)
            {
                CopySelection();
                e.Handled = true;
                return;
            }

            if (previewMode && IsArrowKey(e.KeyCode))
            {
                AdjustSelectionWithKeyboard(e.KeyCode, e.Control, e.Shift ? 10 : 1);
                e.Handled = true;
                return;
            }

            base.OnKeyDown(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (textEditor != null && keyData == Keys.Escape)
            {
                CancelTextEditor();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (previewMode)
            {
                if (e.Button == MouseButtons.Left && !toolbar.Bounds.Contains(e.Location))
                {
                    if (activeTool != AnnotationTool.None && selectedBounds.Contains(e.Location))
                    {
                        BeginAnnotation(e.Location);
                        return;
                    }
                    else
                    {
                        previewDragMode = HitTestSelection(e.Location);
                        if (previewDragMode != SelectionDragMode.None)
                        {
                            BeginSelectionAdjustment(e.Location, previewDragMode);
                        }
                    }
                }

                base.OnMouseDown(e);
                return;
            }

            if (e.Button != MouseButtons.Left)
            {
                base.OnMouseDown(e);
                return;
            }

            dragging = true;
            dragStart = e.Location;
            selectedBounds = Rectangle.Empty;
            Capture = true;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (previewMode)
            {
                if (annotationDragging)
                {
                    annotationCurrent = ToImagePoint(e.Location);
                    Invalidate();
                }
                else if (previewDragMode != SelectionDragMode.None)
                {
                    var oldBounds = selectedBounds;
                    selectedBounds = BuildAdjustedBounds(previewDragStartBounds, previewDragStartPoint, e.Location, previewDragMode);
                    InvalidateSelectionChange(oldBounds, selectedBounds, Rectangle.Empty, Rectangle.Empty);
                }
                else
                {
                    Cursor = CursorForMode(HitTestSelection(e.Location));
                }

                base.OnMouseMove(e);
                return;
            }

            if (dragging)
            {
                selectedBounds = NormalizeRectangle(dragStart, e.Location);
            }
            else
            {
                hoverBounds = WindowDetector.GetWindowBounds(PointToScreen(e.Location), virtualBounds, Handle);
            }

            Invalidate();
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (previewMode)
            {
                if (e.Button == MouseButtons.Left)
                {
                    if (annotationDragging)
                    {
                        CompleteAnnotation(e.Location);
                    }
                    else
                    {
                        EndSelectionAdjustment(e.Location);
                    }
                }

                base.OnMouseUp(e);
                return;
            }

            if (e.Button != MouseButtons.Left)
            {
                base.OnMouseUp(e);
                return;
            }

            Capture = false;
            dragging = false;

            if (selectedBounds.Width < 6 || selectedBounds.Height < 6)
            {
                selectedBounds = TranslateScreenToClient(hoverBounds);
            }

            if (selectedBounds.Width >= 6 && selectedBounds.Height >= 6)
            {
                EnterPreview();
            }
            else
            {
                selectedBounds = Rectangle.Empty;
                Invalidate();
            }

            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var clip = Rectangle.Intersect(e.ClipRectangle, ClientRectangle);
            if (clip.Width <= 0 || clip.Height <= 0)
            {
                return;
            }

            g.DrawImage(dimmedDesktopBitmap, clip, clip, GraphicsUnit.Pixel);

            var rect = previewMode ? selectedBounds : (dragging ? selectedBounds : TranslateScreenToClient(hoverBounds));
            if (rect.Width > 0 && rect.Height > 0)
            {
                var contentClip = Rectangle.Intersect(rect, clip);
                if (previewMode && previewBitmap != null)
                {
                    if (contentClip.Width > 0 && contentClip.Height > 0)
                    {
                        var source = MapDestinationToSource(contentClip, rect, previewBitmap.Size);
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(previewBitmap, contentClip, source, GraphicsUnit.Pixel);
                    }
                }
                else if (contentClip.Width > 0 && contentClip.Height > 0)
                {
                    g.DrawImage(desktopBitmap, contentClip, contentClip, GraphicsUnit.Pixel);
                }

                using (var pen = new Pen(Color.FromArgb(18, 132, 255), 2))
                {
                    g.DrawRectangle(pen, rect);
                }

                if (previewMode)
                {
                    var state = g.Save();
                    g.SetClip(clip, CombineMode.Intersect);
                    var previewSource = previewBitmap ?? desktopBitmap;
                    var previewSourceOrigin = previewBitmap == null ? selectedBounds.Location : Point.Empty;
                    AnnotationRenderer.DrawPreview(g, previewSource, annotations, rect, PreviewImageSize, previewSourceOrigin);
                    if (annotationDragging)
                    {
                        var preview = BuildAnnotation(activeTool, annotationStart, annotationCurrent, null);
                        if (preview != null)
                        {
                            AnnotationRenderer.DrawPreview(g, previewSource, preview, rect, PreviewImageSize, previewSourceOrigin);
                        }
                    }
                    g.Restore(state);
                    if (previewBitmap == null)
                    {
                        DrawSelectionHandles(g, rect);
                    }
                    DrawSelectionSize(g, rect, PreviewImageSize);
                }
            }
        }

        private void InvalidateSelectionChange(Rectangle oldBounds, Rectangle newBounds, Rectangle oldToolbarBounds, Rectangle newToolbarBounds)
        {
            using (var dirty = new Region(oldBounds))
            {
                if (previewBitmap == null && annotations.Count == 0)
                {
                    dirty.Xor(newBounds);
                }
                else
                {
                    dirty.Union(newBounds);
                }

                AddSelectionFrame(dirty, oldBounds);
                AddSelectionFrame(dirty, newBounds);
                dirty.Union(GetSelectionSizeBounds(oldBounds));
                dirty.Union(GetSelectionSizeBounds(newBounds));
                if (!oldToolbarBounds.IsEmpty) dirty.Union(oldToolbarBounds);
                if (!newToolbarBounds.IsEmpty) dirty.Union(newToolbarBounds);
                Invalidate(dirty);
            }
        }

        private void BeginSelectionAdjustment(Point startPoint, SelectionDragMode mode)
        {
            previewDragMode = mode;
            previewDragStartPoint = startPoint;
            previewDragStartBounds = selectedBounds;
            hiddenToolbarBounds = toolbar.Bounds;
            toolbar.Visible = false;
            Invalidate(hiddenToolbarBounds);
            Capture = true;
        }

        private void EndSelectionAdjustment(Point currentPoint)
        {
            previewDragMode = SelectionDragMode.None;
            Capture = false;
            PositionToolbar();
            toolbar.Visible = true;
            Invalidate(hiddenToolbarBounds);
            hiddenToolbarBounds = Rectangle.Empty;
            Cursor = CursorForMode(HitTestSelection(currentPoint));
        }

        private static void AddSelectionFrame(Region dirty, Rectangle bounds)
        {
            var outer = bounds;
            outer.Inflate(10, 10);
            var inner = bounds;
            inner.Inflate(-10, -10);
            using (var frame = new Region(outer))
            {
                if (inner.Width > 0 && inner.Height > 0)
                {
                    frame.Exclude(inner);
                }
                dirty.Union(frame);
            }
        }

        private static Rectangle GetSelectionSizeBounds(Rectangle bounds)
        {
            return new Rectangle(bounds.Left - 2, Math.Max(0, bounds.Top - 40), 120, 72);
        }

        private static Rectangle MapDestinationToSource(Rectangle destinationClip, Rectangle destinationBounds, Size sourceSize)
        {
            double scaleX = sourceSize.Width / (double)Math.Max(1, destinationBounds.Width);
            double scaleY = sourceSize.Height / (double)Math.Max(1, destinationBounds.Height);
            int left = (int)Math.Floor((destinationClip.Left - destinationBounds.Left) * scaleX);
            int top = (int)Math.Floor((destinationClip.Top - destinationBounds.Top) * scaleY);
            int right = (int)Math.Ceiling((destinationClip.Right - destinationBounds.Left) * scaleX);
            int bottom = (int)Math.Ceiling((destinationClip.Bottom - destinationBounds.Top) * scaleY);
            return Rectangle.FromLTRB(
                Math.Max(0, left),
                Math.Max(0, top),
                Math.Min(sourceSize.Width, right),
                Math.Min(sourceSize.Height, bottom));
        }

        private void EnterPreview()
        {
            selectedBounds = ClampToClient(selectedBounds);
            SetPreviewBitmap(null);
            previewMode = true;
            Cursor = Cursors.Default;
            PositionToolbar();
            toolbar.Visible = true;
            Invalidate();
        }

        private void PositionToolbar()
        {
            var x = selectedBounds.Left + (selectedBounds.Width - toolbar.Width) / 2;
            var y = selectedBounds.Bottom + 8;
            if (y + toolbar.Height > ClientSize.Height)
            {
                y = selectedBounds.Top - toolbar.Height - 8;
            }

            x = Math.Max(8, Math.Min(ClientSize.Width - toolbar.Width - 8, x));
            y = Math.Max(8, Math.Min(ClientSize.Height - toolbar.Height - 8, y));
            toolbar.Location = new Point(x, y);
        }

        private Bitmap CreateSelectionBitmap()
        {
            var bounds = ClampToClient(selectedBounds);
            if (bounds.Width < 1 || bounds.Height < 1)
            {
                return null;
            }

            selectedBounds = bounds;
            return desktopBitmap.Clone(selectedBounds, PixelFormat.Format32bppArgb);
        }

        private void SetPreviewBitmap(Bitmap bitmap)
        {
            if (previewBitmap != null)
            {
                previewBitmap.Dispose();
            }

            previewBitmap = bitmap;
        }

        private Bitmap CreateFinalBitmap()
        {
            using (var bitmap = CreateCurrentBitmap())
            {
                if (bitmap == null)
                {
                    return null;
                }

                return AnnotationRenderer.Render(bitmap, annotations);
            }
        }

        private Bitmap CreateCurrentBitmap()
        {
            if (previewBitmap != null)
            {
                return (Bitmap)previewBitmap.Clone();
            }

            return CreateSelectionBitmap();
        }

        private void SetActiveTool(AnnotationTool tool)
        {
            CommitTextEditor();
            activeTool = activeTool == tool ? AnnotationTool.None : tool;
            toolbar.SetActiveTool(activeTool);
            Cursor = activeTool == AnnotationTool.None ? Cursors.Default : Cursors.Cross;
            Invalidate();
        }

        private void UndoAnnotation()
        {
            CommitTextEditor();
            if (annotations.Count == 0)
            {
                return;
            }

            annotations.RemoveAt(annotations.Count - 1);
            Invalidate();
        }

        private void StartScrollingCapture()
        {
            CommitTextEditor();
            if (!previewMode || previewBitmap != null || selectedBounds.Width < 10 || selectedBounds.Height < 10)
            {
                return;
            }

            using (var wait = new WaitCursor())
            {
                var screenBounds = new Rectangle(
                    virtualBounds.X + selectedBounds.X,
                    virtualBounds.Y + selectedBounds.Y,
                    selectedBounds.Width,
                    selectedBounds.Height);

                toolbar.Visible = false;
                Hide();
                Application.DoEvents();
                Thread.Sleep(180);

                Bitmap stitched = null;
                bool truncated = false;
                string captureError = null;
                try
                {
                    stitched = ScrollingCapture.Capture(screenBounds, 30, out truncated);
                }
                catch (OutOfMemoryException)
                {
                    captureError = "滚动截图过大，内存不足。请缩小截图区域后重试。";
                }
                catch (ExternalException)
                {
                    captureError = "滚动截图生成失败。请缩小截图区域或减少滚动长度后重试。";
                }
                finally
                {
                    Show();
                    Activate();
                    toolbar.Visible = true;
                }

                if (stitched != null)
                {
                    annotations.Clear();
                    SetPreviewBitmap(stitched);
                    selectedBounds = CalculateScrollingPreviewBounds(
                        selectedBounds,
                        stitched.Size,
                        ClientRectangle,
                        toolbar.Height);
                    PositionToolbar();
                    Invalidate();

                    if (truncated)
                    {
                        MessageBox.Show(this,
                            "已达到长图安全上限，当前预览保留了已完成的部分。",
                            "PinShotWin",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                }
                else if (captureError != null)
                {
                    MessageBox.Show(this, captureError, "PinShotWin", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void BeginAnnotation(Point clientPoint)
        {
            annotationStart = ToImagePoint(clientPoint);
            annotationCurrent = annotationStart;

            if (activeTool == AnnotationTool.Text)
            {
                ShowTextEditor(clientPoint);
                return;
            }

            annotationDragging = true;
            Capture = true;
        }

        private void CompleteAnnotation(Point clientPoint)
        {
            annotationCurrent = ToImagePoint(clientPoint);
            annotationDragging = false;
            Capture = false;

            var annotation = BuildAnnotation(activeTool, annotationStart, annotationCurrent, null);
            if (annotation != null)
            {
                annotations.Add(annotation);
            }

            Invalidate();
        }

        private Annotation BuildAnnotation(AnnotationTool tool, Point start, Point end, string text)
        {
            if (tool == AnnotationTool.Rectangle || tool == AnnotationTool.Mosaic)
            {
                var bounds = NormalizeRectangle(start, end);
                if (bounds.Width < 4 || bounds.Height < 4)
                {
                    return null;
                }

                return new Annotation
                {
                    Kind = tool == AnnotationTool.Rectangle ? AnnotationKind.Rectangle : AnnotationKind.Mosaic,
                    Bounds = bounds
                };
            }

            if (tool == AnnotationTool.Arrow)
            {
                if (Distance(start, end) < 6)
                {
                    return null;
                }

                return new Annotation
                {
                    Kind = AnnotationKind.Arrow,
                    Start = start,
                    End = end
                };
            }

            if (tool == AnnotationTool.Text)
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    return null;
                }

                return new Annotation
                {
                    Kind = AnnotationKind.Text,
                    Bounds = new Rectangle(start.X, start.Y, Math.Max(80, end.X - start.X), Math.Max(26, end.Y - start.Y)),
                    Text = text.Trim()
                };
            }

            return null;
        }

        private void ShowTextEditor(Point clientPoint)
        {
            CommitTextEditor();
            var imagePoint = ToImagePoint(clientPoint);
            textEditorImageStart = imagePoint;
            var editorLocation = ImagePointToClient(imagePoint);
            int width = Math.Min(240, Math.Max(120, selectedBounds.Right - editorLocation.X - 8));
            if (width < 80)
            {
                editorLocation.X = Math.Max(selectedBounds.Left + 8, selectedBounds.Right - 248);
                width = Math.Min(240, selectedBounds.Right - editorLocation.X - 8);
                textEditorImageStart = ToImagePoint(editorLocation);
            }

            textEditor = new TextBox
            {
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 11F, FontStyle.Regular),
                Location = editorLocation,
                Width = Math.Max(80, width),
                Height = 26
            };
            textEditor.KeyDown += TextEditorKeyDown;
            textEditor.Leave += delegate { CommitTextEditor(); };
            Controls.Add(textEditor);
            Application.AddMessageFilter(this);
            textEditor.BringToFront();
            textEditor.Focus();
        }

        public bool PreFilterMessage(ref Message m)
        {
            const int WmKeyDown = 0x0100;
            if (textEditor != null && m.Msg == WmKeyDown && (Keys)m.WParam.ToInt32() == Keys.Escape)
            {
                CancelTextEditor();
                return true;
            }

            return false;
        }

        private void TextEditorKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                CommitTextEditor();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                CancelTextEditor();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private void CommitTextEditor()
        {
            if (textEditor == null)
            {
                return;
            }

            var editor = textEditor;
            textEditor = null;
            Application.RemoveMessageFilter(this);
            editor.KeyDown -= TextEditorKeyDown;
            var start = textEditorImageStart;
            var end = ToImagePoint(new Point(editor.Right, editor.Bottom));
            var annotation = BuildAnnotation(AnnotationTool.Text, ClampImagePoint(start), ClampImagePoint(end), editor.Text);
            Controls.Remove(editor);
            editor.Dispose();

            if (annotation != null)
            {
                annotations.Add(annotation);
                Invalidate();
            }
        }

        private void CancelTextEditor()
        {
            if (textEditor == null)
            {
                return;
            }

            var editor = textEditor;
            textEditor = null;
            Application.RemoveMessageFilter(this);
            editor.KeyDown -= TextEditorKeyDown;
            Controls.Remove(editor);
            editor.Dispose();
            Invalidate();
        }

        private Point ToImagePoint(Point clientPoint)
        {
            var size = PreviewImageSize;
            int x = (int)Math.Round((clientPoint.X - selectedBounds.Left) * (size.Width / (double)Math.Max(1, selectedBounds.Width)));
            int y = (int)Math.Round((clientPoint.Y - selectedBounds.Top) * (size.Height / (double)Math.Max(1, selectedBounds.Height)));
            return ClampImagePoint(new Point(x, y));
        }

        private Point ImagePointToClient(Point imagePoint)
        {
            var size = PreviewImageSize;
            int x = selectedBounds.Left + (int)Math.Round(imagePoint.X * (selectedBounds.Width / (double)Math.Max(1, size.Width)));
            int y = selectedBounds.Top + (int)Math.Round(imagePoint.Y * (selectedBounds.Height / (double)Math.Max(1, size.Height)));
            return new Point(x, y);
        }

        private Point ClampImagePoint(Point imagePoint)
        {
            var size = PreviewImageSize;
            int x = Math.Max(0, Math.Min(Math.Max(0, size.Width), imagePoint.X));
            int y = Math.Max(0, Math.Min(Math.Max(0, size.Height), imagePoint.Y));
            return new Point(x, y);
        }

        private Size PreviewImageSize
        {
            get
            {
                if (previewBitmap != null)
                {
                    return previewBitmap.Size;
                }

                return selectedBounds.Size;
            }
        }

        private static double Distance(Point a, Point b)
        {
            int dx = a.X - b.X;
            int dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private void CopySelection()
        {
            CommitTextEditor();
            using (var bitmap = CreateFinalBitmap())
            {
                if (bitmap == null)
                {
                    return;
                }

                if (!ClipboardHelper.TrySetImage(bitmap, this))
                {
                    return;
                }

                RaiseCaptureCompleted(bitmap);
            }
            Close();
        }

        private void SaveSelection()
        {
            using (var dialog = new SaveFileDialog())
            {
                dialog.Title = "保存截图";
                dialog.FileName = "screenshot_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                if (settings.SaveFormat == ImageSaveFormat.Jpg)
                {
                    dialog.Filter = "JPG 图片 (*.jpg)|*.jpg|PNG 图片 (*.png)|*.png";
                    dialog.DefaultExt = "jpg";
                }
                else
                {
                    dialog.Filter = "PNG 图片 (*.png)|*.png|JPG 图片 (*.jpg)|*.jpg";
                    dialog.DefaultExt = "png";
                }

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    ImageSaveFormat format = Path.GetExtension(dialog.FileName).Equals(".png", StringComparison.OrdinalIgnoreCase)
                        ? ImageSaveFormat.Png
                        : ImageSaveFormat.Jpg;
                    CommitTextEditor();
                    using (var bitmap = CreateFinalBitmap())
                    {
                        if (bitmap == null)
                        {
                            return;
                        }

                        RaiseCaptureCompleted(bitmap);
                        ImageFile.Save(bitmap, dialog.FileName, format, settings.JpegQuality);
                    }
                    Close();
                }
            }
        }

        private void PinSelection()
        {
            CommitTextEditor();
            using (var bitmap = CreateFinalBitmap())
            {
                if (bitmap == null)
                {
                    return;
                }

                var pinLocation = new Point(virtualBounds.X + selectedBounds.X + 16, virtualBounds.Y + selectedBounds.Y + 16);
                var pin = new PinWindow((Bitmap)bitmap.Clone(), pinLocation);
                pin.Show();
                RaiseCaptureCompleted(bitmap);
            }
            Close();
        }

        private void RaiseCaptureCompleted(Bitmap bitmap)
        {
            var handler = CaptureCompleted;
            if (handler != null)
            {
                handler(this, new BitmapEventArgs(bitmap));
            }
        }

        private Rectangle TranslateScreenToClient(Rectangle screenRect)
        {
            if (screenRect.IsEmpty)
            {
                return Rectangle.Empty;
            }

            return new Rectangle(screenRect.X - virtualBounds.X, screenRect.Y - virtualBounds.Y, screenRect.Width, screenRect.Height);
        }

        private Rectangle ClampToClient(Rectangle rect)
        {
            var client = new Rectangle(Point.Empty, ClientSize);
            return Rectangle.Intersect(client, rect);
        }

        private SelectionDragMode HitTestSelection(Point point)
        {
            if (!selectedBounds.Contains(point) && !Inflate(selectedBounds, 6).Contains(point))
            {
                return SelectionDragMode.None;
            }

            if (previewBitmap != null)
            {
                return selectedBounds.Contains(point) ? SelectionDragMode.Move : SelectionDragMode.None;
            }

            const int grip = 8;
            bool left = Math.Abs(point.X - selectedBounds.Left) <= grip;
            bool right = Math.Abs(point.X - selectedBounds.Right) <= grip;
            bool top = Math.Abs(point.Y - selectedBounds.Top) <= grip;
            bool bottom = Math.Abs(point.Y - selectedBounds.Bottom) <= grip;

            if (left && top) return SelectionDragMode.TopLeft;
            if (right && top) return SelectionDragMode.TopRight;
            if (left && bottom) return SelectionDragMode.BottomLeft;
            if (right && bottom) return SelectionDragMode.BottomRight;
            if (top) return SelectionDragMode.Top;
            if (bottom) return SelectionDragMode.Bottom;
            if (left) return SelectionDragMode.Left;
            if (right) return SelectionDragMode.Right;
            if (selectedBounds.Contains(point)) return SelectionDragMode.Move;
            return SelectionDragMode.None;
        }

        private Rectangle BuildAdjustedBounds(Rectangle startBounds, Point startPoint, Point currentPoint, SelectionDragMode mode)
        {
            const int minSize = 20;
            int dx = currentPoint.X - startPoint.X;
            int dy = currentPoint.Y - startPoint.Y;
            int left = startBounds.Left;
            int top = startBounds.Top;
            int right = startBounds.Right;
            int bottom = startBounds.Bottom;

            if (mode == SelectionDragMode.Move)
            {
                int x = Math.Max(0, Math.Min(ClientSize.Width - startBounds.Width, startBounds.Left + dx));
                int y = Math.Max(0, Math.Min(ClientSize.Height - startBounds.Height, startBounds.Top + dy));
                return new Rectangle(x, y, startBounds.Width, startBounds.Height);
            }

            if (mode == SelectionDragMode.Left || mode == SelectionDragMode.TopLeft || mode == SelectionDragMode.BottomLeft)
            {
                left = Math.Max(0, Math.Min(startBounds.Right - minSize, startBounds.Left + dx));
            }
            if (mode == SelectionDragMode.Right || mode == SelectionDragMode.TopRight || mode == SelectionDragMode.BottomRight)
            {
                right = Math.Min(ClientSize.Width, Math.Max(startBounds.Left + minSize, startBounds.Right + dx));
            }
            if (mode == SelectionDragMode.Top || mode == SelectionDragMode.TopLeft || mode == SelectionDragMode.TopRight)
            {
                top = Math.Max(0, Math.Min(startBounds.Bottom - minSize, startBounds.Top + dy));
            }
            if (mode == SelectionDragMode.Bottom || mode == SelectionDragMode.BottomLeft || mode == SelectionDragMode.BottomRight)
            {
                bottom = Math.Min(ClientSize.Height, Math.Max(startBounds.Top + minSize, startBounds.Bottom + dy));
            }

            return Rectangle.FromLTRB(left, top, right, bottom);
        }

        private void AdjustSelectionWithKeyboard(Keys keyCode, bool resize, int step)
        {
            if (resize && previewBitmap != null)
            {
                return;
            }

            Rectangle bounds = selectedBounds;
            int dx = 0;
            int dy = 0;

            if (keyCode == Keys.Left) dx = -step;
            else if (keyCode == Keys.Right) dx = step;
            else if (keyCode == Keys.Up) dy = -step;
            else if (keyCode == Keys.Down) dy = step;

            if (resize)
            {
                bounds = ResizeSelectionByKeyboard(bounds, dx, dy);
            }
            else
            {
                bounds = MoveSelectionByKeyboard(bounds, dx, dy);
            }

            if (bounds != selectedBounds)
            {
                selectedBounds = bounds;
                PositionToolbar();
                Invalidate();
            }
        }

        private Rectangle MoveSelectionByKeyboard(Rectangle bounds, int dx, int dy)
        {
            int x = Math.Max(0, Math.Min(ClientSize.Width - bounds.Width, bounds.X + dx));
            int y = Math.Max(0, Math.Min(ClientSize.Height - bounds.Height, bounds.Y + dy));
            return new Rectangle(x, y, bounds.Width, bounds.Height);
        }

        private Rectangle ResizeSelectionByKeyboard(Rectangle bounds, int dx, int dy)
        {
            const int minSize = 20;
            int width = Math.Max(minSize, Math.Min(ClientSize.Width - bounds.Left, bounds.Width + dx));
            int height = Math.Max(minSize, Math.Min(ClientSize.Height - bounds.Top, bounds.Height + dy));
            return new Rectangle(bounds.Left, bounds.Top, width, height);
        }

        private static bool IsArrowKey(Keys keyCode)
        {
            return keyCode == Keys.Left || keyCode == Keys.Right || keyCode == Keys.Up || keyCode == Keys.Down;
        }

        private static Cursor CursorForMode(SelectionDragMode mode)
        {
            if (mode == SelectionDragMode.Move) return Cursors.SizeAll;
            if (mode == SelectionDragMode.Left || mode == SelectionDragMode.Right) return Cursors.SizeWE;
            if (mode == SelectionDragMode.Top || mode == SelectionDragMode.Bottom) return Cursors.SizeNS;
            if (mode == SelectionDragMode.TopLeft || mode == SelectionDragMode.BottomRight) return Cursors.SizeNWSE;
            if (mode == SelectionDragMode.TopRight || mode == SelectionDragMode.BottomLeft) return Cursors.SizeNESW;
            return Cursors.Default;
        }

        private static Rectangle Inflate(Rectangle rect, int amount)
        {
            rect.Inflate(amount, amount);
            return rect;
        }

        private static void DrawSelectionHandles(Graphics g, Rectangle rect)
        {
            var points = new[]
            {
                new Point(rect.Left, rect.Top),
                new Point(rect.Left + rect.Width / 2, rect.Top),
                new Point(rect.Right, rect.Top),
                new Point(rect.Left, rect.Top + rect.Height / 2),
                new Point(rect.Right, rect.Top + rect.Height / 2),
                new Point(rect.Left, rect.Bottom),
                new Point(rect.Left + rect.Width / 2, rect.Bottom),
                new Point(rect.Right, rect.Bottom)
            };

            using (var brush = new SolidBrush(Color.FromArgb(18, 132, 255)))
            using (var pen = new Pen(Color.White, 1))
            {
                foreach (var point in points)
                {
                    var handle = new Rectangle(point.X - 4, point.Y - 4, 8, 8);
                    g.FillEllipse(brush, handle);
                    g.DrawEllipse(pen, handle);
                }
            }
        }

        private static string FormatSelectionSize(Size size)
        {
            return size.Width + " x " + size.Height;
        }

        private static void DrawSelectionSize(Graphics g, Rectangle rect, Size imageSize)
        {
            string text = FormatSelectionSize(imageSize);
            using (var font = new Font("Segoe UI", 8.5F, FontStyle.Regular))
            {
                var size = g.MeasureString(text, font);
                int width = (int)Math.Ceiling(size.Width) + 12;
                int height = (int)Math.Ceiling(size.Height) + 6;
                int x = rect.Left;
                int y = rect.Top - height - 6;
                if (y < 4)
                {
                    y = rect.Top + 6;
                }

                using (var background = new SolidBrush(Color.FromArgb(210, 18, 24, 36)))
                using (var foreground = new SolidBrush(Color.White))
                {
                    g.FillRectangle(background, x, y, width, height);
                    g.DrawString(text, font, foreground, x + 6, y + 3);
                }
            }
        }

        private static Rectangle NormalizeRectangle(Point a, Point b)
        {
            return Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        }
    }

    internal enum SelectionDragMode
    {
        None,
        Move,
        Left,
        Right,
        Top,
        Bottom,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }

    internal enum AnnotationTool
    {
        None,
        Rectangle,
        Arrow,
        Text,
        Mosaic
    }

    internal enum AnnotationKind
    {
        Rectangle,
        Arrow,
        Text,
        Mosaic
    }

    internal sealed class Annotation
    {
        public AnnotationKind Kind;
        public Rectangle Bounds;
        public Point Start;
        public Point End;
        public string Text;
    }

    internal static class AnnotationRenderer
    {
        private static readonly Color Red = Color.FromArgb(235, 38, 38);
        private const int MosaicBlockSize = 10;

        public static Bitmap Render(Bitmap source, IList<Annotation> annotations)
        {
            var output = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(output))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                g.DrawImage(source, 0, 0, source.Width, source.Height);
            }

            foreach (var annotation in annotations)
            {
                if (annotation.Kind == AnnotationKind.Mosaic)
                {
                    ApplyMosaic(output, annotation.Bounds, MosaicBlockSize);
                }
                else
                {
                    using (var g = Graphics.FromImage(output))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                        Draw(g, annotation, new Rectangle(0, 0, output.Width, output.Height), output.Size);
                    }
                }
            }

            return output;
        }

        public static void Draw(Graphics g, IList<Annotation> annotations, Rectangle targetRect, Size imageSize)
        {
            foreach (var annotation in annotations)
            {
                Draw(g, annotation, targetRect, imageSize);
            }
        }

        public static void DrawPreview(Graphics g, Bitmap source, IList<Annotation> annotations, Rectangle targetRect, Size imageSize, Point sourceOrigin)
        {
            foreach (var annotation in annotations)
            {
                DrawPreview(g, source, annotation, targetRect, imageSize, sourceOrigin);
            }
        }

        public static void DrawPreview(Graphics g, Bitmap source, Annotation annotation, Rectangle targetRect, Size imageSize, Point sourceOrigin)
        {
            if (annotation.Kind == AnnotationKind.Mosaic && source != null)
            {
                DrawMosaicPreview(g, source, annotation.Bounds, targetRect, imageSize, sourceOrigin);
                return;
            }

            Draw(g, annotation, targetRect, imageSize);
        }

        public static void Draw(Graphics g, Annotation annotation, Rectangle targetRect, Size imageSize)
        {
            if (imageSize.Width <= 0 || imageSize.Height <= 0)
            {
                return;
            }

            var state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.Half;

            if (annotation.Kind == AnnotationKind.Rectangle)
            {
                using (var pen = new Pen(Red, 2.3F))
                {
                    pen.LineJoin = LineJoin.Round;
                    g.DrawRectangle(pen, MapRect(annotation.Bounds, targetRect, imageSize));
                }
            }
            else if (annotation.Kind == AnnotationKind.Arrow)
            {
                DrawArrow(g, MapPoint(annotation.Start, targetRect, imageSize), MapPoint(annotation.End, targetRect, imageSize), 2.5F);
            }
            else if (annotation.Kind == AnnotationKind.Text)
            {
                var rect = MapRect(annotation.Bounds, targetRect, imageSize);
                using (var font = new Font("Segoe UI", 16F, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var brush = new SolidBrush(Red))
                {
                    g.DrawString(annotation.Text ?? string.Empty, font, brush, rect);
                }
            }

            g.Restore(state);
        }

        private static void DrawArrow(Graphics g, Point start, Point end, float lineWidth)
        {
            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length < 1.0)
            {
                return;
            }

            double ux = dx / length;
            double uy = dy / length;
            float headLength = Math.Min(18F, Math.Max(10F, lineWidth * 5F));
            float headHalfWidth = Math.Min(10F, Math.Max(6F, lineWidth * 3F));
            if (length < headLength * 1.5)
            {
                headLength = (float)(length * 0.45);
                headHalfWidth = Math.Max(3F, headLength * 0.55F);
            }

            var baseCenter = new PointF(
                (float)(end.X - ux * headLength),
                (float)(end.Y - uy * headLength));
            var left = new PointF(
                (float)(baseCenter.X - uy * headHalfWidth),
                (float)(baseCenter.Y + ux * headHalfWidth));
            var right = new PointF(
                (float)(baseCenter.X + uy * headHalfWidth),
                (float)(baseCenter.Y - ux * headHalfWidth));

            using (var pen = new Pen(Red, lineWidth))
            using (var brush = new SolidBrush(Red))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                g.DrawLine(pen, start, baseCenter);
                g.FillPolygon(brush, new[] { new PointF(end.X, end.Y), left, right }, FillMode.Winding);
            }
        }

        private static Rectangle MapRect(Rectangle rect, Rectangle targetRect, Size imageSize)
        {
            float sx = targetRect.Width / (float)Math.Max(1, imageSize.Width);
            float sy = targetRect.Height / (float)Math.Max(1, imageSize.Height);
            int left = targetRect.Left + (int)Math.Round(rect.Left * sx);
            int top = targetRect.Top + (int)Math.Round(rect.Top * sy);
            int right = targetRect.Left + (int)Math.Round(rect.Right * sx);
            int bottom = targetRect.Top + (int)Math.Round(rect.Bottom * sy);
            return Rectangle.FromLTRB(left, top, Math.Max(left + 1, right), Math.Max(top + 1, bottom));
        }

        private static Point MapPoint(Point point, Rectangle targetRect, Size imageSize)
        {
            float sx = targetRect.Width / (float)Math.Max(1, imageSize.Width);
            float sy = targetRect.Height / (float)Math.Max(1, imageSize.Height);
            return new Point(targetRect.Left + (int)Math.Round(point.X * sx), targetRect.Top + (int)Math.Round(point.Y * sy));
        }

        private static void DrawMosaicPreview(Graphics g, Bitmap source, Rectangle sourceBounds, Rectangle targetRect, Size imageSize, Point sourceOrigin)
        {
            var sampleBounds = new Rectangle(
                sourceBounds.X + sourceOrigin.X,
                sourceBounds.Y + sourceOrigin.Y,
                sourceBounds.Width,
                sourceBounds.Height);
            sampleBounds = Rectangle.Intersect(new Rectangle(0, 0, source.Width, source.Height), sampleBounds);
            if (sampleBounds.Width <= 0 || sampleBounds.Height <= 0)
            {
                return;
            }

            using (var mosaic = CreateMosaicRegion(source, sampleBounds, MosaicBlockSize))
            {
                var destination = MapRect(sourceBounds, targetRect, imageSize);
                var oldInterpolation = g.InterpolationMode;
                var oldPixelOffset = g.PixelOffsetMode;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(mosaic, destination, new Rectangle(0, 0, mosaic.Width, mosaic.Height), GraphicsUnit.Pixel);
                g.InterpolationMode = oldInterpolation;
                g.PixelOffsetMode = oldPixelOffset;
            }
        }

        private static void ApplyMosaic(Bitmap bitmap, Rectangle requestedBounds, int blockSize)
        {
            var bounds = Rectangle.Intersect(new Rectangle(0, 0, bitmap.Width, bitmap.Height), requestedBounds);
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            using (var mosaic = CreateMosaicRegion(bitmap, bounds, blockSize))
            using (var g = Graphics.FromImage(bitmap))
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(mosaic, bounds, new Rectangle(0, 0, mosaic.Width, mosaic.Height), GraphicsUnit.Pixel);
            }
        }

        private static Bitmap CreateMosaicRegion(Bitmap source, Rectangle bounds, int blockSize)
        {
            int smallWidth = Math.Max(1, (int)Math.Ceiling(bounds.Width / (double)blockSize));
            int smallHeight = Math.Max(1, (int)Math.Ceiling(bounds.Height / (double)blockSize));
            var reduced = new Bitmap(smallWidth, smallHeight, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(reduced))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(source, new Rectangle(0, 0, smallWidth, smallHeight), bounds, GraphicsUnit.Pixel);
            }
            return reduced;
        }
    }

    internal sealed class CaptureToolbar : UserControl
    {
        public event EventHandler CopyClicked;
        public event EventHandler SaveClicked;
        public event EventHandler PinClicked;
        public event EventHandler RectClicked;
        public event EventHandler ArrowClicked;
        public event EventHandler TextClicked;
        public event EventHandler MosaicClicked;
        public event EventHandler UndoClicked;
        public event EventHandler ScrollClicked;
        public event EventHandler CancelClicked;

        private readonly ToolTip tooltip = new ToolTip();
        private IconButton rectButton;
        private IconButton arrowButton;
        private IconButton textButton;
        private IconButton mosaicButton;

        public CaptureToolbar()
        {
            Width = 472;
            Height = 38;
            BackColor = Color.Transparent;
            rectButton = AddButton(ToolbarIcon.Rect, "Rectangle", 175, delegate { Raise(RectClicked); });
            arrowButton = AddButton(ToolbarIcon.Arrow, "Arrow", 217, delegate { Raise(ArrowClicked); });
            textButton = AddButton(ToolbarIcon.Text, "Text", 259, delegate { Raise(TextClicked); });
            mosaicButton = AddButton(ToolbarIcon.Mosaic, "Mosaic", 301, delegate { Raise(MosaicClicked); });
            AddButton(ToolbarIcon.Undo, "Undo", 343, delegate { Raise(UndoClicked); });
            AddButton(ToolbarIcon.Scroll, "Scroll", 385, delegate { Raise(ScrollClicked); });

            AddButton(ToolbarIcon.Copy, "复制", 7, delegate { Raise(CopyClicked); });
            AddButton(ToolbarIcon.Save, "保存", 49, delegate { Raise(SaveClicked); });
            AddButton(ToolbarIcon.Pin, "钉住", 91, delegate { Raise(PinClicked); });
            AddButton(ToolbarIcon.Close, "取消", 133, delegate { Raise(CancelClicked); });
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var shadow = new SolidBrush(Color.FromArgb(28, 0, 0, 0)))
            using (var brush = new SolidBrush(Color.White))
            {
                e.Graphics.FillRectangle(shadow, 2, 2, Width - 3, Height - 3);
                e.Graphics.FillRectangle(brush, 0, 0, Width - 1, Height - 1);
            }
            base.OnPaint(e);
        }

        public void SetActiveTool(AnnotationTool tool)
        {
            rectButton.Active = tool == AnnotationTool.Rectangle;
            arrowButton.Active = tool == AnnotationTool.Arrow;
            textButton.Active = tool == AnnotationTool.Text;
            mosaicButton.Active = tool == AnnotationTool.Mosaic;
        }

        private IconButton AddButton(ToolbarIcon icon, string tip, int x, EventHandler click)
        {
            var button = new IconButton(icon)
            {
                Location = new Point(x, 4),
                Size = new Size(30, 30)
            };
            button.Click += click;
            tooltip.SetToolTip(button, tip);
            Controls.Add(button);
            return button;
        }

        private void Raise(EventHandler handler)
        {
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        private static GraphicsPath RoundedRect(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            var diameter = radius * 2;
            path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal enum ToolbarIcon
    {
        Copy,
        Save,
        Pin,
        Rect,
        Arrow,
        Text,
        Mosaic,
        Undo,
        Scroll,
        Close
    }

    internal sealed class IconButton : Control
    {
        private readonly ToolbarIcon icon;
        private bool hovered;
        private bool pressed;
        private bool active;

        public bool Active
        {
            get { return active; }
            set
            {
                if (active == value)
                {
                    return;
                }

                active = value;
                Invalidate();
            }
        }

        public IconButton(ToolbarIcon icon)
        {
            this.icon = icon;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hovered = false;
            pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            pressed = true;
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            pressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            var rect = new Rectangle(1, 1, Width - 2, Height - 2);
            if (hovered || pressed || active)
            {
                using (var path = RoundedRect(rect, 4))
                using (var brush = new SolidBrush(active ? Color.FromArgb(220, 234, 255) : (pressed ? Color.FromArgb(224, 231, 242) : Color.FromArgb(240, 244, 250))))
                {
                    e.Graphics.FillPath(brush, path);
                }
            }

            var iconColor = icon == ToolbarIcon.Close ? Color.FromArgb(255, 82, 65) : Color.FromArgb(18, 24, 36);
            using (var pen = new Pen(iconColor, 1.65F))
            using (var thinPen = new Pen(iconColor, 1.45F))
            using (var brush = new SolidBrush(iconColor))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                thinPen.StartCap = LineCap.Round;
                thinPen.EndCap = LineCap.Round;
                thinPen.LineJoin = LineJoin.Round;
                DrawIcon(e.Graphics, pen, thinPen, brush);
            }
        }

        private void DrawIcon(Graphics g, Pen pen, Pen thinPen, Brush brush)
        {
            float cx = Width / 2F;
            float cy = Height / 2F;

            if (icon == ToolbarIcon.Copy)
            {
                g.DrawRectangle(thinPen, cx - 6.5F, cy - 3.5F, 9F, 9F);
                g.DrawRectangle(pen, cx - 2.5F, cy - 7.5F, 9F, 9F);
            }
            else if (icon == ToolbarIcon.Save)
            {
                g.DrawLine(pen, cx, cy - 8, cx, cy + 3);
                g.DrawLine(pen, cx - 5, cy - 2, cx, cy + 3);
                g.DrawLine(pen, cx + 5, cy - 2, cx, cy + 3);
                g.DrawLine(pen, cx - 7, cy + 7, cx + 7, cy + 7);
            }
            else if (icon == ToolbarIcon.Pin)
            {
                g.TranslateTransform(cx, cy);
                var path = new GraphicsPath();
                path.AddLine(-5, -8, 5, -8);
                path.AddLine(2, -3, 2, 4);
                path.AddLine(6, 7, -6, 7);
                path.AddLine(-2, 4, -2, -3);
                path.CloseFigure();
                g.DrawPath(pen, path);
                g.DrawLine(pen, 0, 7, 0, 11);
                path.Dispose();
                g.ResetTransform();
            }
            else if (icon == ToolbarIcon.Rect)
            {
                g.DrawRectangle(pen, cx - 7, cy - 6, 14, 12);
            }
            else if (icon == ToolbarIcon.Arrow)
            {
                pen.EndCap = LineCap.ArrowAnchor;
                g.DrawLine(pen, cx - 7, cy + 7, cx + 7, cy - 7);
            }
            else if (icon == ToolbarIcon.Text)
            {
                using (var font = new Font("Segoe UI", 16F, FontStyle.Bold, GraphicsUnit.Pixel))
                {
                    var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString("T", font, brush, new RectangleF(0, 1, Width, Height), format);
                    format.Dispose();
                }
            }
            else if (icon == ToolbarIcon.Mosaic)
            {
                for (int row = 0; row < 3; row++)
                {
                    for (int col = 0; col < 3; col++)
                    {
                        var fill = (row + col) % 2 == 0;
                        var cell = new RectangleF(cx - 7 + col * 5, cy - 7 + row * 5, 4, 4);
                        if (fill)
                        {
                            g.FillRectangle(brush, cell);
                        }
                        else
                        {
                            g.DrawRectangle(thinPen, cell.X, cell.Y, cell.Width, cell.Height);
                        }
                    }
                }
            }
            else if (icon == ToolbarIcon.Undo)
            {
                g.DrawArc(pen, cx - 7, cy - 6, 14, 14, 210, 250);
                g.DrawLine(pen, cx - 7, cy - 2, cx - 3, cy - 8);
                g.DrawLine(pen, cx - 7, cy - 2, cx, cy - 2);
            }
            else if (icon == ToolbarIcon.Scroll)
            {
                g.DrawRectangle(thinPen, cx - 6, cy - 8, 12, 16);
                g.DrawLine(thinPen, cx - 3, cy - 4, cx + 3, cy - 4);
                g.DrawLine(thinPen, cx - 3, cy, cx + 3, cy);
                g.DrawLine(pen, cx, cy + 3, cx, cy + 9);
                g.DrawLine(pen, cx - 4, cy + 5, cx, cy + 9);
                g.DrawLine(pen, cx + 4, cy + 5, cx, cy + 9);
            }
            else if (icon == ToolbarIcon.Close)
            {
                g.DrawLine(pen, cx - 6, cy - 6, cx + 6, cy + 6);
                g.DrawLine(pen, cx + 6, cy - 6, cx - 6, cy + 6);
            }
        }

        private static GraphicsPath RoundedRect(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            var diameter = radius * 2;
            path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class PinWindow : Form
    {
        private readonly Bitmap image;
        private readonly ContextMenuStrip pinMenu;
        private readonly ToolStripMenuItem lockMenuItem;
        private readonly ToolStripMenuItem opacityMenuItem;
        private bool dragging;
        private bool locked;
        private Point dragOffset;
        private double scale = 1.0;

        public PinWindow(Bitmap image, Point preferredLocation)
        {
            this.image = image;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Bounds = new Rectangle(ClampLocation(preferredLocation, image.Size), image.Size);
            DoubleBuffered = true;
            Cursor = Cursors.SizeAll;
            KeyPreview = true;

            pinMenu = new ContextMenuStrip();
            pinMenu.Opening += delegate { RefreshPinMenu(); };
            pinMenu.Items.Add("复制", null, delegate { CopyPinnedImage(); });
            pinMenu.Items.Add("保存", null, delegate { SavePinnedImage(); });
            lockMenuItem = new ToolStripMenuItem("锁定位置");
            lockMenuItem.Click += delegate
            {
                locked = !locked;
                Cursor = locked ? Cursors.Default : Cursors.SizeAll;
            };
            pinMenu.Items.Add(lockMenuItem);
            opacityMenuItem = new ToolStripMenuItem("透明度");
            AddOpacityItem("100%", 1.0);
            AddOpacityItem("90%", 0.9);
            AddOpacityItem("75%", 0.75);
            AddOpacityItem("60%", 0.6);
            AddOpacityItem("45%", 0.45);
            pinMenu.Items.Add(opacityMenuItem);
            pinMenu.Items.Add(new ToolStripSeparator());
            pinMenu.Items.Add("关闭", null, delegate { Close(); });
            ContextMenuStrip = pinMenu;
        }

        protected override void OnClosed(EventArgs e)
        {
            pinMenu.Dispose();
            image.Dispose();
            base.OnClosed(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.InterpolationMode = InterpolationMode.Bilinear;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
            e.Graphics.DrawImage(image, ClientRectangle);
            using (var pen = new Pen(Color.FromArgb(130, 130, 130)))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Activate();
            if (!locked && e.Button == MouseButtons.Left)
            {
                dragging = true;
                dragOffset = e.Location;
                Capture = true;
            }
            base.OnMouseDown(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.C))
            {
                CopyPinnedImage();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!locked && dragging)
            {
                var screen = PointToScreen(e.Location);
                Location = new Point(screen.X - dragOffset.X, screen.Y - dragOffset.Y);
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            dragging = false;
            Capture = false;
            base.OnMouseUp(e);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            Close();
            base.OnMouseDoubleClick(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (locked)
            {
                return;
            }

            var oldScale = scale;
            scale *= e.Delta > 0 ? 1.1 : 0.9;
            scale = Math.Max(0.15, Math.Min(6.0, scale));
            if (Math.Abs(oldScale - scale) < 0.001)
            {
                return;
            }

            var mouseScreen = PointToScreen(e.Location);
            var newWidth = Math.Max(40, (int)(image.Width * scale));
            var newHeight = Math.Max(40, (int)(image.Height * scale));
            var ratioX = e.Location.X / (double)Math.Max(1, Width);
            var ratioY = e.Location.Y / (double)Math.Max(1, Height);
            Size = new Size(newWidth, newHeight);
            Location = new Point(mouseScreen.X - (int)(newWidth * ratioX), mouseScreen.Y - (int)(newHeight * ratioY));
            Invalidate();
        }

        private void CopyPinnedImage()
        {
            ClipboardHelper.TrySetImage(image, this);
        }

        private void SavePinnedImage()
        {
            using (var dialog = new SaveFileDialog())
            {
                dialog.Title = "保存贴图";
                dialog.FileName = "pinshot_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                dialog.Filter = "JPG 图片 (*.jpg)|*.jpg|PNG 图片 (*.png)|*.png";
                dialog.DefaultExt = "jpg";

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    ImageSaveFormat format = Path.GetExtension(dialog.FileName).Equals(".png", StringComparison.OrdinalIgnoreCase)
                        ? ImageSaveFormat.Png
                        : ImageSaveFormat.Jpg;
                    ImageFile.Save(image, dialog.FileName, format, 90);
                }
            }
        }

        private void AddOpacityItem(string label, double value)
        {
            var item = new ToolStripMenuItem(label);
            item.Tag = value;
            item.Click += delegate
            {
                Opacity = value;
            };
            opacityMenuItem.DropDownItems.Add(item);
        }

        private void RefreshPinMenu()
        {
            lockMenuItem.Checked = locked;
            foreach (ToolStripItem item in opacityMenuItem.DropDownItems)
            {
                var menuItem = item as ToolStripMenuItem;
                if (menuItem != null && menuItem.Tag is double)
                {
                    double value = (double)menuItem.Tag;
                    menuItem.Checked = Math.Abs(Opacity - value) < 0.01;
                }
            }
        }

        private static Point ClampLocation(Point preferredLocation, Size imageSize)
        {
            var area = Screen.FromPoint(preferredLocation).WorkingArea;
            var x = preferredLocation.X;
            var y = preferredLocation.Y;

            if (imageSize.Width <= area.Width)
            {
                x = Math.Max(area.Left, Math.Min(area.Right - imageSize.Width, x));
            }
            else
            {
                x = area.Left;
            }

            if (imageSize.Height <= area.Height)
            {
                y = Math.Max(area.Top, Math.Min(area.Bottom - imageSize.Height, y));
            }
            else
            {
                y = area.Top;
            }

            return new Point(x, y);
        }
    }

    internal static class ScreenshotHelper
    {
        public static Bitmap CaptureVirtualScreen(Rectangle bounds)
        {
            var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
            }
            return bitmap;
        }

        public static Bitmap CreateDimmedBitmap(Bitmap source)
        {
            var bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.DrawImage(source, 0, 0, source.Width, source.Height);
                using (var shade = new SolidBrush(Color.FromArgb(110, Color.Black)))
                {
                    g.FillRectangle(shade, 0, 0, source.Width, source.Height);
                }
            }
            return bitmap;
        }
    }

    internal static class ScrollingCapture
    {
        private const int WheelDelta = -720;
        private const int PixelDifferenceThreshold = 45;
        private const long MaxOutputPixels = 64L * 1024 * 1024;
        private const int MaxBitmapDimension = 32760;

        public static long MaxOutputPixelsForTest
        {
            get { return MaxOutputPixels; }
        }

        public static int MaxBitmapDimensionForTest
        {
            get { return MaxBitmapDimension; }
        }

        public static Bitmap Capture(Rectangle screenBounds, int maxFrames, out bool truncated)
        {
            truncated = false;
            if (screenBounds.Width < 1 || screenBounds.Height < 1)
            {
                return null;
            }

            var strips = new List<CapturedStrip>();
            var bootstrapFrames = new List<Bitmap>();
            Bitmap previous = null;
            Bitmap current = null;
            Rectangle contentBounds = Rectangle.Empty;
            int outputHeight = 0;
            int acceptedFrames = 0;
            Point oldCursor = Cursor.Position;
            try
            {
                Cursor.Position = new Point(screenBounds.Left + screenBounds.Width / 2, screenBounds.Top + screenBounds.Height / 2);

                Thread.Sleep(120);
                bootstrapFrames.Add(ScreenshotHelper.CaptureVirtualScreen(screenBounds));
                if (maxFrames <= 1)
                {
                    return (Bitmap)bootstrapFrames[0].Clone();
                }

                int bootstrapTarget = Math.Min(3, maxFrames);
                for (int i = 1; i < bootstrapTarget; i++)
                {
                    if (IsEscapePressed())
                    {
                        break;
                    }

                    SendWheel(WheelDelta);
                    Application.DoEvents();
                    Thread.Sleep(260);
                    current = ScreenshotHelper.CaptureVirtualScreen(screenBounds);
                    if (LooksSame(bootstrapFrames[bootstrapFrames.Count - 1], current))
                    {
                        break;
                    }
                    bootstrapFrames.Add(current);
                    current = null;
                }

                if (bootstrapFrames.Count < 2)
                {
                    return (Bitmap)bootstrapFrames[0].Clone();
                }

                contentBounds = DetectContentBounds(bootstrapFrames);
                if (contentBounds.IsEmpty)
                {
                    return (Bitmap)bootstrapFrames[0].Clone();
                }

                strips.Add(CapturedStrip.Create(bootstrapFrames[0], contentBounds));
                outputHeight = contentBounds.Height;
                acceptedFrames = 1;
                for (int i = 1; i < bootstrapFrames.Count; i++)
                {
                    VerticalMatch match = FindVerticalMatch(bootstrapFrames[i - 1], bootstrapFrames[i], contentBounds);
                    if (!match.IsReliable || match.Shift <= 0)
                    {
                        return CombineStrips(strips, contentBounds.Width, outputHeight);
                    }

                    int nextHeight = outputHeight + match.Shift;
                    if (!CanCreateOutput(contentBounds.Width, nextHeight))
                    {
                        truncated = true;
                        break;
                    }

                    int overlap = contentBounds.Height - match.Shift;
                    var source = new Rectangle(
                        contentBounds.Left,
                        contentBounds.Top + overlap,
                        contentBounds.Width,
                        match.Shift);
                    strips.Add(CapturedStrip.Create(bootstrapFrames[i], source));
                    outputHeight = nextHeight;
                    acceptedFrames++;
                }

                previous = bootstrapFrames[bootstrapFrames.Count - 1];
                bootstrapFrames.RemoveAt(bootstrapFrames.Count - 1);
                foreach (var frame in bootstrapFrames)
                {
                    frame.Dispose();
                }
                bootstrapFrames.Clear();

                for (int i = acceptedFrames; i < maxFrames; i++)
                {
                    if (IsEscapePressed())
                    {
                        break;
                    }

                    SendWheel(WheelDelta);
                    Application.DoEvents();
                    Thread.Sleep(260);
                    current = ScreenshotHelper.CaptureVirtualScreen(screenBounds);
                    if (LooksSame(previous, current))
                    {
                        break;
                    }

                    VerticalMatch match = FindVerticalMatch(previous, current, contentBounds);
                    if (!match.IsReliable || match.Shift <= 0)
                    {
                        break;
                    }

                    int nextHeight = outputHeight + match.Shift;
                    if (!CanCreateOutput(contentBounds.Width, nextHeight))
                    {
                        truncated = true;
                        break;
                    }

                    int overlap = contentBounds.Height - match.Shift;
                    var source = new Rectangle(
                        contentBounds.Left,
                        contentBounds.Top + overlap,
                        contentBounds.Width,
                        match.Shift);
                    strips.Add(CapturedStrip.Create(current, source));
                    outputHeight = nextHeight;
                    acceptedFrames++;

                    previous.Dispose();
                    previous = current;
                    current = null;
                }

                if (acceptedFrames >= maxFrames)
                {
                    truncated = true;
                }

                return CombineStrips(strips, contentBounds.Width, outputHeight);
            }
            finally
            {
                Cursor.Position = oldCursor;
                if (current != null)
                {
                    current.Dispose();
                }
                if (previous != null)
                {
                    previous.Dispose();
                }
                foreach (var frame in bootstrapFrames)
                {
                    frame.Dispose();
                }
                foreach (var strip in strips)
                {
                    strip.Dispose();
                }
            }
        }

        private static bool CanCreateOutput(int width, int height)
        {
            return width > 0 && height > 0 &&
                width <= MaxBitmapDimension && height <= MaxBitmapDimension &&
                width * (long)height <= MaxOutputPixels;
        }

        public static bool CanCreateOutputForTest(int width, int height)
        {
            return CanCreateOutput(width, height);
        }

        private static Bitmap CombineStrips(IList<CapturedStrip> strips, int width, int height)
        {
            var output = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(output))
            {
                g.Clear(Color.Transparent);
                int destinationY = 0;
                foreach (var strip in strips)
                {
                    strip.Draw(g, destinationY);
                    destinationY += strip.Height;
                }
            }
            return output;
        }

        private static Rectangle DetectContentBounds(IList<Bitmap> frames)
        {
            Rectangle roughBounds = DetectScrollingBounds(frames);
            if (roughBounds.IsEmpty)
            {
                return Rectangle.Empty;
            }

            var matches = new List<VerticalMatch>();
            for (int i = 1; i < frames.Count; i++)
            {
                matches.Add(FindVerticalMatch(frames[i - 1], frames[i], roughBounds));
            }
            return RefineHorizontalBounds(frames, roughBounds, matches);
        }

        public static Bitmap StitchForTest(IList<Bitmap> frames)
        {
            return Stitch(frames);
        }

        public static Bitmap StitchStreamingForTest(IList<Bitmap> frames, out long compressedBytes)
        {
            compressedBytes = 0;
            if (frames.Count == 0)
            {
                return null;
            }
            if (frames.Count == 1)
            {
                return (Bitmap)frames[0].Clone();
            }

            int bootstrapCount = Math.Min(3, frames.Count);
            var bootstrap = new List<Bitmap>();
            for (int i = 0; i < bootstrapCount; i++)
            {
                bootstrap.Add(frames[i]);
            }
            Rectangle contentBounds = DetectContentBounds(bootstrap);
            if (contentBounds.IsEmpty)
            {
                return (Bitmap)frames[0].Clone();
            }
            int outputHeight = 0;
            var strips = new List<CapturedStrip>();
            try
            {
                strips.Add(CapturedStrip.Create(frames[0], contentBounds));
                outputHeight = contentBounds.Height;
                for (int i = 1; i < frames.Count; i++)
                {
                    VerticalMatch match = FindVerticalMatch(frames[i - 1], frames[i], contentBounds);
                    if (!match.IsReliable || match.Shift <= 0)
                    {
                        break;
                    }

                    int nextHeight = outputHeight + match.Shift;
                    if (!CanCreateOutput(contentBounds.Width, nextHeight))
                    {
                        break;
                    }

                    int overlap = contentBounds.Height - match.Shift;
                    strips.Add(CapturedStrip.Create(frames[i], new Rectangle(
                        contentBounds.Left,
                        contentBounds.Top + overlap,
                        contentBounds.Width,
                        match.Shift)));
                    outputHeight = nextHeight;
                }

                if (strips.Count == 0)
                {
                    return (Bitmap)frames[0].Clone();
                }

                foreach (var strip in strips)
                {
                    compressedBytes += strip.CompressedBytes;
                }
                return CombineStrips(strips, contentBounds.Width, outputHeight);
            }
            finally
            {
                foreach (var strip in strips)
                {
                    strip.Dispose();
                }
            }
        }

        public static string GetDiagnosticsForTest(IList<Bitmap> frames)
        {
            Rectangle bounds = DetectScrollingBounds(frames);
            var values = new List<string>();
            for (int i = 1; i < frames.Count; i++)
            {
                VerticalMatch match = FindVerticalMatch(frames[i - 1], frames[i], bounds);
                values.Add(match.Shift + ":" + match.Score.ToString("0.0") + ":" + match.Samples + ":" + match.IsReliable);
            }
            return bounds.ToString() + Environment.NewLine + string.Join(Environment.NewLine, values.ToArray());
        }

        private static Bitmap Stitch(IList<Bitmap> frames)
        {
            if (frames.Count == 0)
            {
                return null;
            }

            Rectangle roughBounds = DetectScrollingBounds(frames);
            if (roughBounds.IsEmpty)
            {
                return frames[0].Clone(new Rectangle(0, 0, frames[0].Width, frames[0].Height), PixelFormat.Format32bppArgb);
            }

            var roughMatches = new List<VerticalMatch>();
            for (int i = 1; i < frames.Count; i++)
            {
                roughMatches.Add(FindVerticalMatch(frames[i - 1], frames[i], roughBounds));
            }

            Rectangle contentBounds = RefineHorizontalBounds(frames, roughBounds, roughMatches);
            var shifts = new List<int>();
            var acceptedFrames = new List<int> { 0 };
            int height = contentBounds.Height;
            for (int i = 1; i < frames.Count; i++)
            {
                int previousIndex = acceptedFrames[acceptedFrames.Count - 1];
                VerticalMatch match = FindVerticalMatch(frames[previousIndex], frames[i], contentBounds);
                if (!match.IsReliable || match.Shift <= 0)
                {
                    continue;
                }

                acceptedFrames.Add(i);
                shifts.Add(match.Shift);
                height += match.Shift;
            }

            var output = new Bitmap(contentBounds.Width, height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(output))
            {
                g.Clear(Color.Transparent);
                int destinationY = 0;
                for (int i = 0; i < acceptedFrames.Count; i++)
                {
                    int shift = i == 0 ? contentBounds.Height : shifts[i - 1];
                    int overlap = contentBounds.Height - shift;
                    int sourceY = i == 0 ? contentBounds.Top : contentBounds.Top + overlap;
                    int sourceHeight = i == 0 ? contentBounds.Height : shift;
                    var source = new Rectangle(contentBounds.Left, sourceY, contentBounds.Width, sourceHeight);
                    var dest = new Rectangle(0, destinationY, contentBounds.Width, sourceHeight);
                    g.DrawImage(frames[acceptedFrames[i]], dest, source, GraphicsUnit.Pixel);
                    destinationY += sourceHeight;
                }
            }

            return output;
        }

        private static Rectangle DetectScrollingBounds(IList<Bitmap> frames)
        {
            int minX = frames[0].Width;
            int minY = frames[0].Height;
            int maxX = -1;
            int maxY = -1;
            int acceptedPairs = 0;
            int comparisons = Math.Min(frames.Count - 1, 4);
            var full = new Rectangle(0, 0, frames[0].Width, frames[0].Height);

            for (int i = 0; i < comparisons; i++)
            {
                int pairMinX = full.Width;
                int pairMinY = full.Height;
                int pairMaxX = -1;
                int pairMaxY = -1;
                int pairChanged = 0;
                using (var a = new PixelBuffer(frames[i]))
                using (var b = new PixelBuffer(frames[i + 1]))
                {
                    for (int y = 0; y < a.Height; y += 3)
                    {
                        for (int x = 0; x < a.Width; x += 3)
                        {
                            if (a.Difference(b, x, y) <= PixelDifferenceThreshold)
                            {
                                continue;
                            }

                            pairMinX = Math.Min(pairMinX, x);
                            pairMinY = Math.Min(pairMinY, y);
                            pairMaxX = Math.Max(pairMaxX, x);
                            pairMaxY = Math.Max(pairMaxY, y);
                            pairChanged++;
                        }
                    }
                }

                int pairWidth = pairMaxX >= pairMinX ? pairMaxX - pairMinX + 1 : 0;
                int pairHeight = pairMaxY >= pairMinY ? pairMaxY - pairMinY + 1 : 0;
                if (pairChanged < 24 || pairWidth < full.Width / 4 || pairHeight < full.Height / 3)
                {
                    continue;
                }

                minX = Math.Min(minX, pairMinX);
                minY = Math.Min(minY, pairMinY);
                maxX = Math.Max(maxX, pairMaxX);
                maxY = Math.Max(maxY, pairMaxY);
                acceptedPairs++;
            }

            if (acceptedPairs == 0 || maxX <= minX || maxY <= minY)
            {
                return Rectangle.Empty;
            }

            var detected = Rectangle.FromLTRB(
                Math.Max(0, minX - 12),
                Math.Max(0, minY - 1),
                Math.Min(full.Right, maxX + 13),
                Math.Min(full.Bottom, maxY + 3));

            if (detected.Width < full.Width / 4 || detected.Height < full.Height / 3)
            {
                return Rectangle.Empty;
            }

            return detected;
        }

        private static Rectangle RefineHorizontalBounds(
            IList<Bitmap> frames,
            Rectangle roughBounds,
            IList<VerticalMatch> matches)
        {
            int blockWidth = Math.Max(6, Math.Min(12, roughBounds.Width / 28));
            int blockCount = (roughBounds.Width + blockWidth - 1) / blockWidth;
            var state = new sbyte[blockCount];
            var differences = new long[blockCount];
            var texturedSamples = new int[blockCount];
            var matchedSamples = new int[blockCount];

            for (int pair = 0; pair < matches.Count; pair++)
            {
                VerticalMatch match = matches[pair];
                if (!match.IsReliable || match.Shift <= 0)
                {
                    continue;
                }

                using (var a = new PixelBuffer(frames[pair]))
                using (var b = new PixelBuffer(frames[pair + 1]))
                {
                    int overlap = roughBounds.Height - match.Shift;
                    for (int y = 2; y < overlap - 2; y += 3)
                    {
                        int previousY = roughBounds.Top + match.Shift + y;
                        int currentY = roughBounds.Top + y;
                        for (int block = 0; block < blockCount; block++)
                        {
                            int left = roughBounds.Left + block * blockWidth;
                            int right = Math.Min(roughBounds.Right, left + blockWidth);
                            for (int x = left + 1; x < right - 1; x += 2)
                            {
                                if (Math.Max(a.Texture(x, previousY), b.Texture(x, currentY)) < 18)
                                {
                                    continue;
                                }

                                int pixelDifference = a.Difference(b, x, previousY, x, currentY);
                                differences[block] += pixelDifference;
                                texturedSamples[block]++;
                                if (pixelDifference <= 42)
                                {
                                    matchedSamples[block]++;
                                }
                            }
                        }
                    }
                }
            }

            for (int block = 0; block < blockCount; block++)
            {
                int minimumEvidence = Math.Max(10, matches.Count * 8);
                if (texturedSamples[block] >= minimumEvidence)
                {
                    double average = differences[block] / (double)texturedSamples[block];
                    double ratio = matchedSamples[block] / (double)texturedSamples[block];
                    state[block] = average <= 42 && ratio >= 0.58 ? (sbyte)1 : (sbyte)-1;
                }
            }

            FillShortUnknownGaps(state, 2);
            int bestStart = -1;
            int bestLength = 0;
            for (int index = 0; index < state.Length;)
            {
                if (state[index] != 1)
                {
                    index++;
                    continue;
                }

                int start = index;
                while (index < state.Length && state[index] == 1)
                {
                    index++;
                }

                int length = index - start;
                if (length > bestLength)
                {
                    bestStart = start;
                    bestLength = length;
                }
            }

            if (bestStart < 0 || bestLength * blockWidth < roughBounds.Width / 3)
            {
                return roughBounds;
            }

            int regionStart = bestStart;
            while (regionStart > 0 && state[regionStart - 1] == 0)
            {
                regionStart--;
            }

            int regionEnd = bestStart + bestLength;
            while (regionEnd < state.Length && state[regionEnd] == 0)
            {
                regionEnd++;
            }

            int refinedLeft = Math.Max(roughBounds.Left, roughBounds.Left + regionStart * blockWidth - blockWidth);
            int refinedRight = Math.Min(
                roughBounds.Right,
                roughBounds.Left + regionEnd * blockWidth);
            int minimumWidth = Math.Min(roughBounds.Width, Math.Max(1, (int)Math.Ceiling(roughBounds.Width * 0.85)));
            if (refinedRight - refinedLeft < minimumWidth)
            {
                int missing = minimumWidth - (refinedRight - refinedLeft);
                int growLeft = Math.Min(refinedLeft - roughBounds.Left, missing / 2);
                refinedLeft -= growLeft;
                missing -= growLeft;
                refinedRight = Math.Min(roughBounds.Right, refinedRight + missing);
                missing = minimumWidth - (refinedRight - refinedLeft);
                refinedLeft = Math.Max(roughBounds.Left, refinedLeft - missing);
            }

            return Rectangle.FromLTRB(refinedLeft, roughBounds.Top, refinedRight, roughBounds.Bottom);
        }

        private static void FillShortUnknownGaps(sbyte[] values, int maximumGap)
        {
            int index = 0;
            while (index < values.Length)
            {
                if (values[index] != 0)
                {
                    index++;
                    continue;
                }

                int start = index;
                while (index < values.Length && values[index] == 0)
                {
                    index++;
                }

                if (start > 0 && index < values.Length &&
                    values[start - 1] == 1 && values[index] == 1 &&
                    index - start <= maximumGap)
                {
                    for (int fill = start; fill < index; fill++)
                    {
                        values[fill] = 1;
                    }
                }
            }
        }

        private static VerticalMatch FindVerticalMatch(Bitmap previous, Bitmap current, Rectangle bounds)
        {
            int maxShift = Math.Max(0, bounds.Height - Math.Max(24, bounds.Height / 10));
            int bestShift = 0;
            double bestScore = double.MaxValue;
            int bestSamples = 0;

            using (var a = new PixelBuffer(previous))
            using (var b = new PixelBuffer(current))
            {
                int xStep = Math.Max(2, bounds.Width / 70);
                for (int shift = 0; shift <= maxShift; shift += 2)
                {
                    VerticalMatch candidate = ScoreVerticalShift(a, b, bounds, shift, xStep);
                    if (candidate.Samples >= 24 && candidate.Score < bestScore)
                    {
                        bestScore = candidate.Score;
                        bestShift = shift;
                        bestSamples = candidate.Samples;
                    }
                }

                int fineStart = Math.Max(0, bestShift - 2);
                int fineEnd = Math.Min(maxShift, bestShift + 2);
                for (int shift = fineStart; shift <= fineEnd; shift++)
                {
                    VerticalMatch candidate = ScoreVerticalShift(a, b, bounds, shift, xStep);
                    if (candidate.Samples >= 24 && candidate.Score < bestScore)
                    {
                        bestScore = candidate.Score;
                        bestShift = shift;
                        bestSamples = candidate.Samples;
                    }
                }
            }

            return new VerticalMatch
            {
                Shift = bestShift,
                Score = bestScore,
                Samples = bestSamples,
                IsReliable = bestSamples >= 24 && bestScore <= 48
            };
        }

        private static VerticalMatch ScoreVerticalShift(
            PixelBuffer previous,
            PixelBuffer current,
            Rectangle bounds,
            int shift,
            int xStep)
        {
            int overlap = bounds.Height - shift;
            long difference = 0;
            int samples = 0;
            for (int y = 2; y < overlap - 2; y += 2)
            {
                int previousY = bounds.Top + shift + y;
                int currentY = bounds.Top + y;
                for (int x = bounds.Left + 2; x < bounds.Right - 2; x += xStep)
                {
                    if (Math.Max(previous.Texture(x, previousY), current.Texture(x, currentY)) < 18)
                    {
                        continue;
                    }

                    difference += previous.Difference(current, x, previousY, x, currentY);
                    samples++;
                }
            }

            return new VerticalMatch
            {
                Shift = shift,
                Score = samples == 0 ? double.MaxValue : difference / (double)samples,
                Samples = samples
            };
        }

        private struct VerticalMatch
        {
            public int Shift;
            public double Score;
            public int Samples;
            public bool IsReliable;
        }

        private sealed class CapturedStrip : IDisposable
        {
            private readonly MemoryStream data;

            public int Height { get; private set; }
            public long CompressedBytes { get { return data.Length; } }

            private CapturedStrip(MemoryStream data, int height)
            {
                this.data = data;
                Height = height;
            }

            public static CapturedStrip Create(Bitmap source, Rectangle bounds)
            {
                var stream = new MemoryStream();
                using (var strip = source.Clone(bounds, PixelFormat.Format32bppArgb))
                {
                    strip.Save(stream, ImageFormat.Png);
                }
                stream.Position = 0;
                return new CapturedStrip(stream, bounds.Height);
            }

            public void Draw(Graphics graphics, int destinationY)
            {
                data.Position = 0;
                using (var image = Image.FromStream(data, false, false))
                {
                    graphics.DrawImageUnscaled(image, 0, destinationY);
                }
            }

            public void Dispose()
            {
                data.Dispose();
            }
        }

        private static bool LooksSame(Bitmap a, Bitmap b)
        {
            if (a.Width != b.Width || a.Height != b.Height)
            {
                return false;
            }

            int changed = 0;
            int samples = 0;
            int stepX = Math.Max(2, a.Width / 28);
            int stepY = Math.Max(2, a.Height / 28);
            using (var first = new PixelBuffer(a))
            using (var second = new PixelBuffer(b))
            {
                for (int y = stepY / 2; y < a.Height; y += stepY)
                {
                    for (int x = stepX / 2; x < a.Width; x += stepX)
                    {
                        if (first.Difference(second, x, y) > PixelDifferenceThreshold)
                        {
                            changed++;
                        }
                        samples++;
                    }
                }
            }

            return samples > 0 && changed / (double)samples < 0.008;
        }

        private sealed class PixelBuffer : IDisposable
        {
            private readonly Bitmap bitmap;
            private readonly BitmapData data;
            private readonly byte[] pixels;

            public int Width { get { return bitmap.Width; } }
            public int Height { get { return bitmap.Height; } }

            public PixelBuffer(Bitmap bitmap)
            {
                this.bitmap = bitmap;
                data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                pixels = new byte[Math.Abs(data.Stride) * bitmap.Height];
                Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            }

            public int Difference(PixelBuffer other, int x, int y)
            {
                return Difference(other, x, y, x, y);
            }

            public int Difference(PixelBuffer other, int x1, int y1, int x2, int y2)
            {
                int firstRow = data.Stride >= 0 ? y1 : Height - 1 - y1;
                int secondRow = other.data.Stride >= 0 ? y2 : other.Height - 1 - y2;
                int first = firstRow * Math.Abs(data.Stride) + x1 * 4;
                int second = secondRow * Math.Abs(other.data.Stride) + x2 * 4;
                return Math.Abs(pixels[first] - other.pixels[second]) +
                    Math.Abs(pixels[first + 1] - other.pixels[second + 1]) +
                    Math.Abs(pixels[first + 2] - other.pixels[second + 2]);
            }

            public int Texture(int x, int y)
            {
                int horizontal = Difference(this, x - 1, y, x + 1, y);
                int vertical = Difference(this, x, y - 1, x, y + 1);
                return Math.Max(horizontal, vertical);
            }

            public void Dispose()
            {
                bitmap.UnlockBits(data);
            }
        }

        private static void SendWheel(int delta)
        {
            mouse_event(0x0800, 0, 0, delta, UIntPtr.Zero);
        }

        private static bool IsEscapePressed()
        {
            return (GetAsyncKeyState((int)Keys.Escape) & 0x8000) != 0;
        }

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint flags, uint dx, uint dy, int data, UIntPtr extraInfo);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int virtualKey);
    }

    internal static class ImageFile
    {
        public static void Save(Bitmap bitmap, string path, ImageSaveFormat format, int jpegQuality)
        {
            if (format == ImageSaveFormat.Png)
            {
                bitmap.Save(path, ImageFormat.Png);
                return;
            }

            using (var flattened = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format24bppRgb))
            using (var g = Graphics.FromImage(flattened))
            {
                g.Clear(Color.White);
                g.DrawImage(bitmap, 0, 0, bitmap.Width, bitmap.Height);

                var encoder = GetEncoder(ImageFormat.Jpeg);
                var parameters = new EncoderParameters(1);
                parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, Math.Max(1, Math.Min(100, jpegQuality)));
                flattened.Save(path, encoder, parameters);
            }
        }

        private static ImageCodecInfo GetEncoder(ImageFormat format)
        {
            foreach (var codec in ImageCodecInfo.GetImageDecoders())
            {
                if (codec.FormatID == format.Guid)
                {
                    return codec;
                }
            }
            return null;
        }
    }

    internal static class WindowDetector
    {
        private const int GaRoot = 2;

        public static Rectangle GetWindowBounds(Point screenPoint, Rectangle virtualBounds, IntPtr excludedWindow)
        {
            IntPtr hwnd = FindTopLevelWindowAtPoint(screenPoint, excludedWindow);
            if (hwnd == IntPtr.Zero)
            {
                return virtualBounds;
            }

            RECT rect;
            if (DwmGetWindowAttribute(hwnd, 9, out rect, Marshal.SizeOf(typeof(RECT))) != 0 || IsEmpty(rect))
            {
                if (!GetWindowRect(hwnd, out rect))
                {
                    return virtualBounds;
                }
            }

            var bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            bounds = Rectangle.Intersect(bounds, virtualBounds);
            if (bounds.Width < 10 || bounds.Height < 10)
            {
                return virtualBounds;
            }

            return bounds;
        }

        private static IntPtr FindTopLevelWindowAtPoint(Point screenPoint, IntPtr excludedWindow)
        {
            IntPtr found = IntPtr.Zero;
            EnumWindows(delegate(IntPtr hwnd, IntPtr lParam)
            {
                if (hwnd == excludedWindow || !IsWindowVisible(hwnd))
                {
                    return true;
                }

                IntPtr root = GetAncestor(hwnd, GaRoot);
                if (root != hwnd)
                {
                    return true;
                }

                RECT rect;
                if (!GetWindowRect(hwnd, out rect) || IsEmpty(rect))
                {
                    return true;
                }

                if (screenPoint.X >= rect.Left && screenPoint.X <= rect.Right && screenPoint.Y >= rect.Top && screenPoint.Y <= rect.Bottom)
                {
                    found = hwnd;
                    return false;
                }

                return true;
            }, IntPtr.Zero);

            return found;
        }

        private static bool IsEmpty(RECT rect)
        {
            return rect.Right <= rect.Left || rect.Bottom <= rect.Top;
        }

        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hwnd, int flags);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hwnd);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT rect, int size);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }
}
