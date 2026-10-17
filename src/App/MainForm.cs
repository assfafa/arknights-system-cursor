using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ArknightsSystemCursor
{
    public sealed class MainForm : Form
    {
        private readonly CursorService service = new CursorService();
        private readonly TextBox pathBox = new TextBox();
        private readonly TextBox logBox = new TextBox();
        private readonly Label statusTitle = new Label();
        private readonly Label statusDescription = new Label();
        private readonly Label statusDetail = new Label();
        private readonly Panel statusCard = new Panel();
        private readonly Button browseButton;
        private readonly Button refreshButton;
        private readonly Button enableButton;
        private readonly Button restoreButton;
        private readonly Button backupButton;
        private readonly bool preview;
        private readonly string? initialPath;
        private CursorStatus? currentStatus;
        private bool busy;
        private static readonly Color Ink = Color.FromArgb(24, 37, 47);
        private static readonly Color Accent = Color.FromArgb(0, 113, 97);

        public MainForm(bool preview = false, string? initialPath = null)
        {
            this.preview = preview;
            this.initialPath = initialPath;
            SuspendLayout();
            AutoScaleMode = AutoScaleMode.None;
            Text = "明日方舟 · 系统指针  /  v" + typeof(MainForm).Assembly.GetName().Version.ToString(3);
            ClientSize = new Size(780, 620);
            MinimumSize = new Size(796, 659);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.FromArgb(244, 247, 248);
            ForeColor = Ink;
            Icon = SystemIcons.Application;

            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 6
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 94));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 132));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            Controls.Add(layout);

            Panel header = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 12) };
            Label title = MakeLabel("让鼠标，跟得上你的手。", 21, FontStyle.Bold);
            title.Location = new Point(0, 0);
            title.Size = new Size(650, 38);
            header.Controls.Add(title);
            Label subtitle = MakeLabel("明日方舟 PC 客户端  ·  系统指针切换工具", 10);
            subtitle.ForeColor = Color.FromArgb(95, 111, 120);
            subtitle.Location = new Point(2, 43);
            subtitle.Size = new Size(640, 24);
            header.Controls.Add(subtitle);
            layout.Controls.Add(header, 0, 0);

            TableLayoutPanel selector = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3, Margin = Padding.Empty };
            selector.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            selector.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
            selector.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
            selector.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
            selector.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            selector.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            Label gameLabel = MakeLabel("游戏目录", 10, FontStyle.Bold);
            gameLabel.Dock = DockStyle.Fill;
            selector.Controls.Add(gameLabel, 0, 0);
            selector.SetColumnSpan(gameLabel, 3);
            pathBox.Dock = DockStyle.Fill;
            pathBox.Margin = new Padding(0, 4, 10, 0);
            pathBox.AccessibleName = "明日方舟游戏目录";
            selector.Controls.Add(pathBox, 0, 1);
            browseButton = MakeButton("选择游戏…", false);
            browseButton.Margin = new Padding(0, 0, 8, 0);
            browseButton.Click += Browse;
            selector.Controls.Add(browseButton, 1, 1);
            refreshButton = MakeButton("重新检测", false);
            refreshButton.Margin = Padding.Empty;
            refreshButton.Click += async delegate { await InspectAsync(); };
            selector.Controls.Add(refreshButton, 2, 1);
            Label hint = MakeLabel("选择 Arknights.exe 所在文件夹；检测过程不会修改游戏。", 8.5F);
            hint.ForeColor = Color.FromArgb(100, 116, 124);
            hint.Dock = DockStyle.Fill;
            hint.Padding = new Padding(0, 5, 0, 0);
            selector.Controls.Add(hint, 0, 2);
            selector.SetColumnSpan(hint, 3);
            layout.Controls.Add(selector, 0, 1);

            statusCard.Dock = DockStyle.Fill;
            statusCard.Margin = new Padding(0, 6, 0, 6);
            statusCard.BackColor = Color.White;
            statusTitle.Location = new Point(20, 14);
            statusTitle.Size = new Size(660, 34);
            statusTitle.Font = new Font(Font.FontFamily, 18, FontStyle.Bold);
            statusTitle.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            statusDescription.Location = new Point(22, 56);
            statusDescription.Size = new Size(660, 24);
            statusDescription.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            statusDetail.Location = new Point(22, 87);
            statusDetail.Size = new Size(660, 22);
            statusDetail.Font = new Font(Font.FontFamily, 8.5F);
            statusDetail.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            statusCard.Controls.AddRange(new Control[] { statusTitle, statusDescription, statusDetail });
            layout.Controls.Add(statusCard, 0, 2);

            TableLayoutPanel actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0, 7, 0, 9) };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
            enableButton = MakeButton("启用系统指针", true);
            restoreButton = MakeButton("恢复游戏指针", false);
            backupButton = MakeButton("打开备份目录", false);
            enableButton.Click += async delegate { await OperateAsync(true); };
            restoreButton.Click += async delegate { await OperateAsync(false); };
            backupButton.Click += OpenBackups;
            actions.Controls.Add(enableButton, 0, 0);
            actions.Controls.Add(restoreButton, 1, 0);
            actions.Controls.Add(backupButton, 2, 0);
            layout.Controls.Add(actions, 0, 3);

            logBox.Dock = DockStyle.Fill;
            logBox.Multiline = true;
            logBox.ReadOnly = true;
            logBox.ScrollBars = ScrollBars.Vertical;
            logBox.BackColor = Color.White;
            logBox.BorderStyle = BorderStyle.FixedSingle;
            logBox.ForeColor = Color.FromArgb(77, 96, 106);
            logBox.Font = new Font("Microsoft YaHei UI", 8.5F);
            logBox.Margin = new Padding(0, 4, 0, 0);
            logBox.AccessibleName = "操作记录";
            layout.Controls.Add(logBox, 0, 4);

            TableLayoutPanel footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
            Label note = MakeLabel("操作前请退出游戏。更新或修复文件后，可重新启用。", 8.5F);
            note.ForeColor = Color.FromArgb(100, 116, 124);
            note.Dock = DockStyle.Fill;
            note.TextAlign = ContentAlignment.MiddleLeft;
            footer.Controls.Add(note, 0, 0);
            LinkLabel source = new LinkLabel { Text = "GitHub · 开源 / 帮助", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, LinkColor = Accent };
            source.LinkClicked += delegate { Process.Start("https://github.com/assfafa/arknights-system-cursor"); };
            footer.Controls.Add(source, 1, 0);
            layout.Controls.Add(footer, 0, 5);

            SetNeutral("请选择游戏", "支持资源检测、自动备份和一键恢复。");
            UpdateButtons();
            using (Graphics graphics = Graphics.FromHwnd(IntPtr.Zero))
            {
                // Scale after creating every child. This also works for the standalone EXE
                // without requiring a separate .exe.config for Framework DPI opt-in.
                float scale = graphics.DpiY / 96F;
                if (scale > 1F)
                {
                    Scale(new SizeF(scale, scale));
                }
            }
            ResumeLayout(true);
            Shown += async delegate
            {
                if (this.preview)
                {
                    ShowPreview();
                }
                else
                {
                    await LoadInitialPathAsync();
                }
            };
        }

        private async Task LoadInitialPathAsync()
        {
            if (initialPath != null)
            {
                pathBox.Text = initialPath;
                await InspectAsync();
            }
            else
            {
                string settings = SettingsPath();
                if (File.Exists(settings))
                {
                    pathBox.Text = File.ReadAllText(settings).Trim();
                }
                else
                {
                    var candidates = await Task.Run(() => GameLocator.Find());
                    if (candidates.Count > 0)
                    {
                        pathBox.Text = candidates[0];
                    }
                }
                if (pathBox.Text.Length > 0)
                {
                    await InspectAsync();
                }
                else
                {
                    Log("请选择游戏目录，或将 Arknights.exe 路径粘贴到上方后重新检测。");
                }
            }
        }

        private async void Browse(object? sender, EventArgs args)
        {
            using (OpenFileDialog dialog = new OpenFileDialog
            {
                Title = "选择明日方舟 PC 客户端的 Arknights.exe",
                Filter = "明日方舟客户端 (Arknights.exe)|Arknights.exe",
                CheckFileExists = true
            })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    pathBox.Text = Path.GetDirectoryName(dialog.FileName);
                    await InspectAsync();
                }
            }
        }

        private async Task InspectAsync()
        {
            if (!busy && !preview)
            {
                busy = true;
                UpdateButtons();
                SetNeutral("正在检测…", "识别鼠标资源内容，并核对当前状态。");
                string path = pathBox.Text;
                try
                {
                    currentStatus = await Task.Run(() => service.Inspect(path));
                    pathBox.Text = currentStatus.GameDirectory;
                    Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath())!);
                    File.WriteAllText(SettingsPath(), currentStatus.GameDirectory);
                    ShowStatus(currentStatus);
                    Log(currentStatus.Message + "  识别到 " + currentStatus.Resources.Count + " 个资源文件。");
                }
                catch (Exception error)
                {
                    currentStatus = null;
                    SetNeutral("需要检查", error.Message);
                    Log(error.Message);
                }
                finally
                {
                    busy = false;
                    UpdateButtons();
                }
            }
        }

        private async Task OperateAsync(bool enable)
        {
            if (!busy && !preview && currentStatus != null)
            {
                busy = true;
                UpdateButtons();
                string path = pathBox.Text;
                try
                {
                    OperationResult result;
                    if (enable)
                    {
                        Log("正在核对资源并创建备份…");
                        result = await Task.Run(() => service.Enable(path));
                    }
                    else
                    {
                        Log("正在校验并恢复原始资源…");
                        result = await Task.Run(() => service.Restore(path));
                    }
                    currentStatus = result.Status;
                    if (currentStatus != null)
                    {
                        ShowStatus(currentStatus);
                    }
                    Log(result.Message);
                }
                catch (Exception error)
                {
                    Log("操作未完成：" + error.Message);
                    MessageBox.Show(this, error.Message, "操作未完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    try
                    {
                        currentStatus = await Task.Run(() => service.Inspect(path));
                        ShowStatus(currentStatus);
                    }
                    catch (Exception inspectionError)
                    {
                        currentStatus = null;
                        SetNeutral("需要检查", inspectionError.Message);
                    }
                }
                finally
                {
                    busy = false;
                    UpdateButtons();
                }
            }
        }

        private void ShowStatus(CursorStatus status)
        {
            if (status.Mode == "system")
            {
                statusCard.BackColor = Color.FromArgb(226, 244, 237);
                statusTitle.ForeColor = Accent;
                statusTitle.Text = "系统指针已启用";
            }
            else if (status.Mode == "game")
            {
                statusCard.BackColor = Color.FromArgb(255, 245, 222);
                statusTitle.ForeColor = Color.FromArgb(133, 91, 27);
                statusTitle.Text = "当前使用游戏指针";
            }
            else
            {
                SetNeutral("需要检查", status.Message);
            }
            statusDescription.Text = status.Message;
            statusDetail.Text = "已验证资源 " + status.Resources.Count + " 个  ·  备份按游戏目录分别保存";
            if (status.GameRunning)
            {
                statusDetail.Text = "游戏正在运行，请退出后再切换指针。";
            }
        }

        private void SetNeutral(string title, string description)
        {
            statusCard.BackColor = Color.White;
            statusTitle.ForeColor = Ink;
            statusTitle.Text = title;
            statusDescription.Text = description;
            statusDetail.Text = "";
        }

        private void UpdateButtons()
        {
            bool available = !busy && !preview;
            browseButton.Enabled = available;
            refreshButton.Enabled = available;
            pathBox.Enabled = available;
            enableButton.Enabled = available && currentStatus != null && currentStatus.Mode == "game" && !currentStatus.GameRunning;
            if (enableButton.Enabled)
            {
                enableButton.BackColor = Accent;
                enableButton.ForeColor = Color.White;
                enableButton.FlatAppearance.BorderColor = Accent;
            }
            else
            {
                enableButton.BackColor = Color.FromArgb(222, 230, 231);
                enableButton.ForeColor = Color.FromArgb(100, 116, 124);
                enableButton.FlatAppearance.BorderColor = Color.FromArgb(205, 216, 221);
            }
            restoreButton.Enabled = available && currentStatus != null && (currentStatus.Mode == "system" || currentStatus.Mode == "missing") && !currentStatus.GameRunning;
            backupButton.Enabled = available && currentStatus != null && Directory.Exists(currentStatus.StateDirectory);
        }

        private void OpenBackups(object? sender, EventArgs args)
        {
            if (currentStatus != null && Directory.Exists(currentStatus.StateDirectory))
            {
                Process.Start("explorer.exe", "\"" + currentStatus.StateDirectory + "\"");
            }
        }

        private void Log(string message)
        {
            logBox.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine);
        }

        private void ShowPreview()
        {
            pathBox.Text = @"D:\Games\Hypergryph Launcher\games\Arknights Game";
            ShowStatus(new CursorStatus { Mode = "system", Message = "游戏鼠标资源已停用，启动游戏后使用系统指针。", Resources = { new ResourceFile() } });
            logBox.Text = "已识别鼠标管理器资源：pc_mouse_mgr.prefab" + Environment.NewLine
                + "原始资源已备份，SHA-256 校验通过。" + Environment.NewLine
                + "系统指针已启用。需要时可随时恢复游戏指针。";
            restoreButton.Enabled = true;
            backupButton.Enabled = true;
            browseButton.Enabled = true;
            refreshButton.Enabled = true;
            pathBox.Enabled = true;
        }

        private static string SettingsPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArknightsSystemCursor", "last-game.txt");
        }

        private static Label MakeLabel(string text, float size, FontStyle style = FontStyle.Regular)
        {
            return new Label { Text = text, Font = new Font("Microsoft YaHei UI", size, style), Margin = Padding.Empty };
        }

        private static Button MakeButton(string text, bool primary)
        {
            Button button = new Button { Text = text, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 0, 10, 0), Cursor = Cursors.Hand };
            button.FlatAppearance.BorderSize = 1;
            if (primary)
            {
                button.BackColor = Accent;
                button.ForeColor = Color.White;
                button.FlatAppearance.BorderColor = Accent;
            }
            else
            {
                button.BackColor = Color.White;
                button.ForeColor = Ink;
                button.FlatAppearance.BorderColor = Color.FromArgb(205, 216, 221);
            }
            return button;
        }
    }
}
