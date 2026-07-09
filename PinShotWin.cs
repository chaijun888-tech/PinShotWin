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
using System.Windows.Forms;

namespace PinShotWin
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TrayContext());
        }
    }

    internal sealed class TrayContext : ApplicationContext
    {
        private readonly NotifyIcon tray;
        private readonly HotkeyWindow hotkeyWindow;
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
            menu.Items.Add("设置", null, delegate { ShowSettings(); });
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
            hotkeyWindow.Register(settings.Hotkey);
        }

        private void ShowSettings()
        {
            using (var form = new SettingsForm(settings))
            {
                if (form.ShowDialog() == DialogResult.OK)
                {
                    settings = form.Settings;
                    settings.Save();
                    StartupManager.SetEnabled(settings.StartWithWindows);
                    RegisterCurrentHotkey();
                }
            }
        }

        private void StartCapture()
        {
            if (CaptureOverlay.IsOpen)
            {
                return;
            }

            var overlay = new CaptureOverlay(settings);
            overlay.Show();
        }
    }

    internal sealed class AppSettings
    {
        public Keys Hotkey = Keys.F1;
        public bool StartWithWindows = true;
        public ImageSaveFormat SaveFormat = ImageSaveFormat.Jpg;
        public int JpegQuality = 90;

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
        private const int HotkeyId = 9130;
        private Keys currentKey = Keys.F1;

        public event EventHandler HotkeyPressed;

        public HotkeyWindow()
        {
            CreateHandle(new CreateParams());
        }

        public void Register(Keys key)
        {
            UnregisterHotKey(Handle, HotkeyId);
            currentKey = key;
            if (!RegisterHotKey(Handle, HotkeyId, 0, (int)key))
            {
                MessageBox.Show("快捷键 " + key + " 注册失败，可能被其他程序占用。", "PinShotWin", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmHotkey && m.WParam.ToInt32() == HotkeyId)
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
            UnregisterHotKey(Handle, HotkeyId);
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

    internal sealed class CaptureOverlay : Form
    {
        public static bool IsOpen;

        private readonly AppSettings settings;
        private readonly Bitmap desktopBitmap;
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

        public CaptureOverlay(AppSettings settings)
        {
            this.settings = settings;
            IsOpen = true;
            virtualBounds = SystemInformation.VirtualScreen;
            desktopBitmap = ScreenshotHelper.CaptureVirtualScreen(virtualBounds);

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
            toolbar.CancelClicked += delegate { Close(); };
            Controls.Add(toolbar);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
        }

        protected override void OnClosed(EventArgs e)
        {
            desktopBitmap.Dispose();
            IsOpen = false;
            base.OnClosed(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
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

            if (previewMode && e.KeyCode == Keys.Enter)
            {
                CopySelection();
                e.Handled = true;
                return;
            }

            base.OnKeyDown(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (previewMode)
            {
                if (e.Button == MouseButtons.Left && !toolbar.Bounds.Contains(e.Location))
                {
                    previewDragMode = HitTestSelection(e.Location);
                    if (previewDragMode != SelectionDragMode.None)
                    {
                        previewDragStartPoint = e.Location;
                        previewDragStartBounds = selectedBounds;
                        Capture = true;
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
                if (previewDragMode != SelectionDragMode.None)
                {
                    selectedBounds = BuildAdjustedBounds(previewDragStartBounds, previewDragStartPoint, e.Location, previewDragMode);
                    PositionToolbar();
                    Invalidate();
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
                    previewDragMode = SelectionDragMode.None;
                    Capture = false;
                    Cursor = CursorForMode(HitTestSelection(e.Location));
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
            g.DrawImage(desktopBitmap, ClientRectangle);

            using (var shade = new SolidBrush(Color.FromArgb(110, Color.Black)))
            {
                g.FillRectangle(shade, ClientRectangle);
            }

            var rect = previewMode ? selectedBounds : (dragging ? selectedBounds : TranslateScreenToClient(hoverBounds));
            if (rect.Width > 0 && rect.Height > 0)
            {
                g.SetClip(rect);
                g.DrawImage(desktopBitmap, ClientRectangle);
                g.ResetClip();

                using (var pen = new Pen(Color.FromArgb(18, 132, 255), 2))
                {
                    g.DrawRectangle(pen, rect);
                }

                if (previewMode)
                {
                    DrawSelectionHandles(g, rect);
                    DrawSelectionSize(g, rect);
                }
            }
        }

        private void EnterPreview()
        {
            selectedBounds = ClampToClient(selectedBounds);
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

        private void CopySelection()
        {
            using (var bitmap = CreateSelectionBitmap())
            {
                if (bitmap == null)
                {
                    return;
                }

                Clipboard.SetImage((Bitmap)bitmap.Clone());
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
                    using (var bitmap = CreateSelectionBitmap())
                    {
                        if (bitmap == null)
                        {
                            return;
                        }

                        ImageFile.Save(bitmap, dialog.FileName, format, settings.JpegQuality);
                    }
                    Close();
                }
            }
        }

        private void PinSelection()
        {
            using (var bitmap = CreateSelectionBitmap())
            {
                if (bitmap == null)
                {
                    return;
                }

                var pinLocation = new Point(virtualBounds.X + selectedBounds.X + 16, virtualBounds.Y + selectedBounds.Y + 16);
                var pin = new PinWindow((Bitmap)bitmap.Clone(), pinLocation);
                pin.Show();
            }
            Close();
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

        private static void DrawSelectionSize(Graphics g, Rectangle rect)
        {
            string text = rect.Width + " x " + rect.Height;
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

    internal sealed class CaptureToolbar : UserControl
    {
        public event EventHandler CopyClicked;
        public event EventHandler SaveClicked;
        public event EventHandler PinClicked;
        public event EventHandler CancelClicked;

        private readonly ToolTip tooltip = new ToolTip();

        public CaptureToolbar()
        {
            Width = 178;
            Height = 38;
            BackColor = Color.Transparent;

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

        private void AddButton(ToolbarIcon icon, string tip, int x, EventHandler click)
        {
            var button = new IconButton(icon)
            {
                Location = new Point(x, 4),
                Size = new Size(30, 30)
            };
            button.Click += click;
            tooltip.SetToolTip(button, tip);
            Controls.Add(button);
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
        Close
    }

    internal sealed class IconButton : Control
    {
        private readonly ToolbarIcon icon;
        private bool hovered;
        private bool pressed;

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
            if (hovered || pressed)
            {
                using (var path = RoundedRect(rect, 4))
                using (var brush = new SolidBrush(pressed ? Color.FromArgb(224, 231, 242) : Color.FromArgb(240, 244, 250)))
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
        private bool dragging;
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
            pinMenu.Items.Add("复制", null, delegate { CopyPinnedImage(); });
            pinMenu.Items.Add("保存", null, delegate { SavePinnedImage(); });
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
            if (e.Button == MouseButtons.Left)
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
            if (dragging)
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
            Clipboard.SetImage((Bitmap)image.Clone());
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
