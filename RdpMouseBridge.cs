using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace RdpMouseBridge
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            try
            {
                NativeMethods.SetProcessDPIAware();
            }
            catch
            {
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly uint _ownPid;
        private readonly bool _isAdmin;
        private readonly System.Windows.Forms.Timer _uiTimer;

        private ClientEngine _client;
        private ServerEngine _server;
        private TabControl _tabs;

        private TextBox _txtHost;
        private NumericUpDown _numClientPort;
        private NumericUpDown _numSensitivity;
        private NumericUpDown _numHz;
        private CheckBox _chkOnlyMstsc;
        private Button _btnClient;
        private Label _lblClientState;
        private Label _lblClientRaw;
        private Label _lblClientSent;
        private Label _lblClientForward;
        private Label _lblClientError;
        private Label _lblClientHotkey;

        private NumericUpDown _numServerPort;
        private CheckBox _chkDryRun;
        private CheckBox _chkAlwaysForward;
        private RadioButton _rdoAuto;
        private RadioButton _rdoProcess;
        private TextBox _txtProcess;
        private Label _lblForeground;
        private CheckBox _chkEnableLimits;
        private ComboBox _cmbClipMode;
        private NumericUpDown _numClipSize;
        private CheckBox _chkCenterLock;
        private Button _btnServer;
        private Label _lblServerState;
        private Label _lblServerStats;
        private Label _lblClipState;
        private Label _lblServerError;

        private bool _clipActive;
        private NativeMethods.Rect _clipRect;
        private int _lastClipApplyTick;

        private IntPtr _gameHwnd;
        private uint _gamePid;
        private string _gameProcessName = "";
        private int _lastGameScanTick;

        private IntPtr _fgHwnd;
        private string _fgProcName = "";
        private string _fgTitle = "";

        public MainForm()
        {
            _ownPid = (uint)Process.GetCurrentProcess().Id;
            _isAdmin = IsAdministrator();

            BuildUi();
            LoadConfig();

            if (!_isAdmin)
            {
                ShowServerError(null);
            }

            _uiTimer = new System.Windows.Forms.Timer();
            _uiTimer.Interval = 100;
            _uiTimer.Tick += OnTimer;
            _uiTimer.Start();

            FormClosing += OnFormClosing;
        }

        private void BuildUi()
        {
            Text = "RdpMouseBridge - RDP 相对鼠标桥";
            Font = new Font("Microsoft YaHei UI", 9F);
            ClientSize = new Size(680, 610);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;

            _tabs = new TabControl();
            _tabs.Location = new Point(12, 12);
            _tabs.Size = new Size(656, 585);

            TabPage clientTab = new TabPage("客户端");
            clientTab.Padding = new Padding(3);
            TabPage serverTab = new TabPage("服务端");
            serverTab.Padding = new Padding(3);
            _tabs.TabPages.Add(clientTab);
            _tabs.TabPages.Add(serverTab);
            Controls.Add(_tabs);

            BuildClientTab(clientTab);
            BuildServerTab(serverTab);
        }

        private void BuildClientTab(TabPage page)
        {
            page.Controls.Add(MakeLabel("游戏 PC 的 IP", 15, 23));
            _txtHost = new TextBox();
            _txtHost.Location = new Point(125, 20);
            _txtHost.Size = new Size(210, 23);
            _txtHost.AccessibleName = "Host";
            page.Controls.Add(_txtHost);

            page.Controls.Add(MakeLabel("UDP 端口", 365, 23));
            _numClientPort = MakeNumeric(445, 20, 85, 1, 65535, 50505, 1, 0);
            page.Controls.Add(_numClientPort);
            page.Controls.Add(MakeLabel("例如 192.168.1.50", 125, 48, Color.Gray));

            GroupBox parameters = new GroupBox();
            parameters.Text = "转发参数";
            parameters.Location = new Point(15, 78);
            parameters.Size = new Size(618, 105);
            page.Controls.Add(parameters);

            parameters.Controls.Add(MakeLabel("灵敏度", 15, 29));
            _numSensitivity = MakeNumeric(75, 26, 80, 0.10m, 10.00m, 1.00m, 0.10m, 2);
            parameters.Controls.Add(_numSensitivity);

            parameters.Controls.Add(MakeLabel("转发频率 (Hz)", 180, 29));
            _numHz = MakeNumeric(290, 26, 80, 30, 1000, 250, 10, 0);
            parameters.Controls.Add(_numHz);

            _chkOnlyMstsc = new CheckBox();
            _chkOnlyMstsc.Text = "仅在 mstsc 前台时转发";
            _chkOnlyMstsc.Location = new Point(15, 62);
            _chkOnlyMstsc.AutoSize = true;
            _chkOnlyMstsc.Checked = true;
            parameters.Controls.Add(_chkOnlyMstsc);

            _btnClient = new Button();
            _btnClient.Text = "启动客户端";
            _btnClient.Location = new Point(15, 195);
            _btnClient.Size = new Size(140, 34);
            _btnClient.Click += OnClientToggle;
            page.Controls.Add(_btnClient);

            _lblClientState = MakeLabel("未运行", 170, 203);
            _lblClientState.Font = new Font(Font, FontStyle.Bold);
            page.Controls.Add(_lblClientState);

            GroupBox status = new GroupBox();
            status.Text = "状态";
            status.Location = new Point(15, 240);
            status.Size = new Size(618, 230);
            page.Controls.Add(status);

            _lblClientRaw = MakeLabel("鼠标事件: -", 15, 25);
            status.Controls.Add(_lblClientRaw);
            _lblClientSent = MakeLabel("已发送: -", 15, 50);
            status.Controls.Add(_lblClientSent);
            _lblClientForward = MakeLabel("转发: -", 15, 75);
            status.Controls.Add(_lblClientForward);

            _lblClientError = new Label();
            _lblClientError.Location = new Point(15, 105);
            _lblClientError.Size = new Size(585, 60);
            _lblClientError.ForeColor = Color.Firebrick;
            status.Controls.Add(_lblClientError);

            _lblClientHotkey = MakeLabel("热键 Ctrl+Alt+M：暂停/恢复转发", 15, 175, Color.DimGray);
            status.Controls.Add(_lblClientHotkey);

            page.Controls.Add(MakeLabel("提示：客户端和服务端使用同一个 exe；请先正常连上 mstsc，再回到这里启动客户端。",
                15, 485, Color.DimGray));
        }

        private void BuildServerTab(TabPage page)
        {
            GroupBox listen = new GroupBox();
            listen.Text = "监听";
            listen.Location = new Point(15, 15);
            listen.Size = new Size(618, 65);
            page.Controls.Add(listen);

            listen.Controls.Add(MakeLabel("UDP 端口", 15, 27));
            _numServerPort = MakeNumeric(95, 24, 80, 1, 65535, 50505, 1, 0);
            listen.Controls.Add(_numServerPort);

            _chkDryRun = new CheckBox();
            _chkDryRun.Text = "只统计，不注入 (dry-run)";
            _chkDryRun.Location = new Point(220, 26);
            _chkDryRun.AutoSize = true;
            listen.Controls.Add(_chkDryRun);

            GroupBox detect = new GroupBox();
            detect.Text = "游戏检测";
            detect.Location = new Point(15, 88);
            detect.Size = new Size(618, 140);
            page.Controls.Add(detect);

            _chkAlwaysForward = new CheckBox();
            _chkAlwaysForward.Text = "始终转发（跳过游戏检测，始终生效）";
            _chkAlwaysForward.Location = new Point(15, 22);
            _chkAlwaysForward.AutoSize = true;
            _chkAlwaysForward.CheckedChanged += delegate { UpdateDetectControlState(); };
            detect.Controls.Add(_chkAlwaysForward);

            _rdoAuto = new RadioButton();
            _rdoAuto.Text = "自动检测全屏游戏";
            _rdoAuto.Location = new Point(15, 50);
            _rdoAuto.AutoSize = true;
            _rdoAuto.Checked = true;
            detect.Controls.Add(_rdoAuto);

            _rdoProcess = new RadioButton();
            _rdoProcess.Text = "指定进程名";
            _rdoProcess.Location = new Point(15, 78);
            _rdoProcess.AutoSize = true;
            _rdoProcess.CheckedChanged += delegate { UpdateDetectControlState(); };
            detect.Controls.Add(_rdoProcess);

            _txtProcess = new TextBox();
            _txtProcess.Location = new Point(125, 75);
            _txtProcess.Size = new Size(180, 23);
            _txtProcess.Enabled = false;
            _txtProcess.AccessibleName = "GameProcess";
            detect.Controls.Add(_txtProcess);
            detect.Controls.Add(MakeLabel("例：GenshinImpact.exe / javaw.exe", 315, 78, Color.Gray));

            _lblForeground = new Label();
            _lblForeground.Location = new Point(15, 108);
            _lblForeground.Size = new Size(585, 20);
            _lblForeground.AutoEllipsis = true;
            _lblForeground.Text = "当前前台: -";
            detect.Controls.Add(_lblForeground);

            GroupBox limits = new GroupBox();
            limits.Text = "鼠标限制策略";
            limits.Location = new Point(15, 236);
            limits.Size = new Size(618, 142);
            page.Controls.Add(limits);

            _chkEnableLimits = new CheckBox();
            _chkEnableLimits.Text = "启用鼠标限制与防卡死策略（原神等免限制游戏可直接关闭此总开关）";
            _chkEnableLimits.Location = new Point(15, 22);
            _chkEnableLimits.AutoSize = true;
            _chkEnableLimits.Checked = false;
            _chkEnableLimits.CheckedChanged += delegate { UpdateLimitsControlState(); };
            limits.Controls.Add(_chkEnableLimits);

            limits.Controls.Add(MakeLabel("范围策略", 15, 52));
            _cmbClipMode = new ComboBox();
            _cmbClipMode.Location = new Point(85, 48);
            _cmbClipMode.Size = new Size(315, 23);
            _cmbClipMode.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbClipMode.Items.Add("强制解除限制（破除游戏锁定，推荐）");
            _cmbClipMode.Items.Add("限制在屏幕中心区域");
            _cmbClipMode.Items.Add("限制在游戏窗口客户区");
            _cmbClipMode.Items.Add("不干涉（保持系统默认）");
            _cmbClipMode.SelectedIndex = 0;
            _cmbClipMode.SelectedIndexChanged += delegate { UpdateLimitsControlState(); };
            limits.Controls.Add(_cmbClipMode);

            limits.Controls.Add(MakeLabel("大小", 418, 52));
            _numClipSize = MakeNumeric(460, 48, 75, 100, 5000, 400, 50, 0);
            limits.Controls.Add(_numClipSize);
            limits.Controls.Add(MakeLabel("px", 542, 52));

            _chkCenterLock = new CheckBox();
            _chkCenterLock.Text = "注入后光标实时锁在中心（强烈推荐：解决视角转动撞屏卡死）";
            _chkCenterLock.Location = new Point(15, 78);
            _chkCenterLock.AutoSize = true;
            _chkCenterLock.Checked = true;
            limits.Controls.Add(_chkCenterLock);

            limits.Controls.Add(MakeLabel("提示：若 3D 游戏失焦流畅、聚焦卡死，请开启总开关并选择“强制解除”+“实时锁中”。",
                15, 108, Color.DimGray));

            UpdateDetectControlState();
            UpdateLimitsControlState();

            _btnServer = new Button();
            _btnServer.Text = "启动服务端";
            _btnServer.Location = new Point(15, 386);
            _btnServer.Size = new Size(140, 34);
            _btnServer.Click += OnServerToggle;
            page.Controls.Add(_btnServer);

            _lblServerState = MakeLabel("未运行", 170, 394);
            _lblServerState.Font = new Font(Font, FontStyle.Bold);
            page.Controls.Add(_lblServerState);

            GroupBox status = new GroupBox();
            status.Text = "状态";
            status.Location = new Point(15, 428);
            status.Size = new Size(618, 105);
            page.Controls.Add(status);

            _lblServerStats = MakeLabel("收包: -    已注入: -    失败: -", 15, 22);
            status.Controls.Add(_lblServerStats);

            _lblClipState = new Label();
            _lblClipState.Location = new Point(15, 46);
            _lblClipState.Size = new Size(585, 32);
            _lblClipState.Text = "未启用";
            status.Controls.Add(_lblClipState);

            _lblServerError = new Label();
            _lblServerError.Location = new Point(15, 78);
            _lblServerError.Size = new Size(585, 20);
            _lblServerError.AutoEllipsis = true;
            _lblServerError.ForeColor = Color.Firebrick;
            status.Controls.Add(_lblServerError);
        }

        private static Label MakeLabel(string text, int x, int y)
        {
            Label label = new Label();
            label.Text = text;
            label.Location = new Point(x, y);
            label.AutoSize = true;
            return label;
        }

        private static Label MakeLabel(string text, int x, int y, Color color)
        {
            Label label = MakeLabel(text, x, y);
            label.ForeColor = color;
            return label;
        }

        private static NumericUpDown MakeNumeric(int x, int y, int width, decimal min, decimal max,
            decimal value, decimal increment, int decimals)
        {
            NumericUpDown numeric = new NumericUpDown();
            numeric.Location = new Point(x, y);
            numeric.Size = new Size(width, 23);
            numeric.Minimum = min;
            numeric.Maximum = max;
            numeric.Value = value;
            numeric.Increment = increment;
            numeric.DecimalPlaces = decimals;
            numeric.TextAlign = HorizontalAlignment.Right;
            return numeric;
        }

        private void UpdateDetectControlState()
        {
            bool always = _chkAlwaysForward.Checked;
            _rdoAuto.Enabled = !always;
            _rdoProcess.Enabled = !always;
            _txtProcess.Enabled = !always && _rdoProcess.Checked;
        }

        private void UpdateLimitsControlState()
        {
            bool enabled = _chkEnableLimits.Checked;
            _cmbClipMode.Enabled = enabled;
            _numClipSize.Enabled = enabled && (_cmbClipMode.SelectedIndex == 1);
            _chkCenterLock.Enabled = enabled;
        }

        private void OnTimer(object sender, EventArgs e)
        {
            UpdateForegroundInfo();

            if (_client != null)
            {
                UpdateClientUi();
            }

            if (_server != null)
            {
                UpdateServerUi();
                UpdateClip();
            }
            else
            {
                ReleaseClip();
            }
        }

        private void UpdateClientUi()
        {
            _lblClientState.Text = _client.Paused ? "运行中（已暂停）" : "运行中";
            _lblClientRaw.Text = "鼠标事件: " + _client.RawEventCount;
            _lblClientSent.Text = "已发送: " + _client.SentPacketCount;
            _lblClientForward.Text = "转发: " + (_client.IsForwardingNow() ? "是" : "否");
            if (_lblClientError.Text != (_client.LastError == null ? "" : _client.LastError))
            {
                _lblClientError.Text = _client.LastError == null ? "" : _client.LastError;
            }
            string hotkey = _client.HotkeyRegistered
                ? "热键 Ctrl+Alt+M：暂停/恢复转发"
                : "热键 Ctrl+Alt+M 注册失败（可能被其他程序占用）";
            if (_lblClientHotkey.Text != hotkey)
            {
                _lblClientHotkey.Text = hotkey;
            }
        }

        private void UpdateServerUi()
        {
            _lblServerState.Text = "运行中（UDP " + _numServerPort.Value + "）";
            _lblServerStats.Text = "收包: " + _server.ReceivedPacketCount
                + "    已注入: " + _server.AppliedMoveCount
                + "    失败: " + _server.FailedMoveCount;
            ShowServerError(_server.LastError);
        }

        private void UpdateForegroundInfo()
        {
            IntPtr foreground = NativeMethods.GetForegroundWindow();
            if (foreground != _fgHwnd)
            {
                _fgHwnd = foreground;
                _fgProcName = "";
                _fgTitle = "";
                if (foreground != IntPtr.Zero)
                {
                    uint pid;
                    NativeMethods.GetWindowThreadProcessId(foreground, out pid);
                    _fgProcName = GetProcessName(pid);
                    _fgTitle = WindowTitle(foreground);
                }
            }

            string text = _fgHwnd == IntPtr.Zero
                ? "当前前台: -"
                : "当前前台: " + _fgProcName + " - " + _fgTitle;
            text = Truncate(text, 75);
            if (_lblForeground.Text != text)
            {
                _lblForeground.Text = text;
            }
        }

        private void OnClientToggle(object sender, EventArgs e)
        {
            if (_client != null)
            {
                StopClient();
                return;
            }

            string host = _txtHost.Text.Trim();
            if (host.Length == 0)
            {
                MessageBox.Show(this, "请填写游戏 PC 的 IP 地址。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                _txtHost.Focus();
                return;
            }

            try
            {
                SaveConfig();
                ClientEngine engine = new ClientEngine(host, (int)_numClientPort.Value,
                    (double)_numSensitivity.Value, (int)_numHz.Value, _chkOnlyMstsc.Checked);
                engine.Start();
                _client = engine;

                _btnClient.Text = "停止客户端";
                _txtHost.Enabled = false;
                _numClientPort.Enabled = false;
                _numSensitivity.Enabled = false;
                _numHz.Enabled = false;
                _chkOnlyMstsc.Enabled = false;
                _lblClientState.Text = "运行中";
                _lblClientError.Text = "";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "客户端启动失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void StopClient()
        {
            if (_client == null)
            {
                return;
            }

            ClientEngine engine = _client;
            _client = null;
            try
            {
                engine.Dispose();
            }
            catch
            {
            }

            _btnClient.Text = "启动客户端";
            _txtHost.Enabled = true;
            _numClientPort.Enabled = true;
            _numSensitivity.Enabled = true;
            _numHz.Enabled = true;
            _chkOnlyMstsc.Enabled = true;
            _lblClientState.Text = "未运行";
            _lblClientRaw.Text = "鼠标事件: -";
            _lblClientSent.Text = "已发送: -";
            _lblClientForward.Text = "转发: -";
            _lblClientError.Text = "";
            _lblClientHotkey.Text = "热键 Ctrl+Alt+M：暂停/恢复转发";
        }

        private void OnServerToggle(object sender, EventArgs e)
        {
            if (_server != null)
            {
                StopServer();
                return;
            }

            try
            {
                SaveConfig();
                ServerEngine engine = new ServerEngine((int)_numServerPort.Value, _chkDryRun.Checked);
                bool limitsOn = _chkEnableLimits.Checked;
                engine.RealtimeCenterLock = limitsOn && _chkCenterLock.Checked;
                engine.ForceUnclip = limitsOn && (_cmbClipMode.SelectedIndex == 0);
                engine.CenterX = Screen.PrimaryScreen.Bounds.Width / 2;
                engine.CenterY = Screen.PrimaryScreen.Bounds.Height / 2;
                engine.Start();
                _server = engine;

                _btnServer.Text = "停止服务端";
                _numServerPort.Enabled = false;
                _chkDryRun.Enabled = false;
                _lblServerState.Text = "运行中（UDP " + _numServerPort.Value + "）";
                _lblServerStats.Text = "收包: 0    已注入: 0    失败: 0";
                ShowServerError(null);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "服务端启动失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void StopServer()
        {
            if (_server == null)
            {
                return;
            }

            ServerEngine engine = _server;
            _server = null;
            try
            {
                engine.Dispose();
            }
            catch
            {
            }

            ReleaseClip();
            NativeMethods.ClipCursor(IntPtr.Zero);
            _btnServer.Text = "启动服务端";
            _numServerPort.Enabled = true;
            _chkDryRun.Enabled = true;
            _lblServerState.Text = "未运行";
            _lblServerStats.Text = "收包: -    已注入: -    失败: -";
            _lblClipState.Text = "未启用";
            ShowServerError(null);
        }

        private void UpdateClip()
        {
            bool alwaysForward = _chkAlwaysForward.Checked;
            string title;
            IntPtr game = FindGameWindow(out title);

            bool gameActive = alwaysForward || (game != IntPtr.Zero && !NativeMethods.IsIconic(game) && IsGameActive(game));

            int centerX = Screen.PrimaryScreen.Bounds.Width / 2;
            int centerY = Screen.PrimaryScreen.Bounds.Height / 2;
            NativeMethods.Rect rect = new NativeMethods.Rect();
            bool hasRect = false;

            if (game != IntPtr.Zero)
            {
                hasRect = TryGetClipRect(game, out rect, out centerX, out centerY);
            }
            else
            {
                IntPtr fg = NativeMethods.GetForegroundWindow();
                if (fg != IntPtr.Zero && fg != Handle)
                {
                    hasRect = TryGetClipRect(fg, out rect, out centerX, out centerY);
                    if (string.IsNullOrEmpty(title))
                    {
                        title = WindowTitle(fg);
                    }
                }
            }

            if (alwaysForward && string.IsNullOrEmpty(title))
            {
                title = "全局前台";
            }

            // Master switch: if mouse limits are disabled
            if (!_chkEnableLimits.Checked)
            {
                ReleaseClip();
                if (_server != null)
                {
                    _server.RealtimeCenterLock = false;
                    _server.ForceUnclip = false;
                    _server.CenterX = centerX;
                    _server.CenterY = centerY;
                }

                if (!gameActive)
                {
                    _lblClipState.Text = alwaysForward
                        ? "始终转发运行中（鼠标限制未开启）"
                        : "游戏未激活（鼠标限制未开启）";
                }
                else
                {
                    _lblClipState.Text = (alwaysForward ? "始终转发模式" : "已检测到: " + Truncate(title, 35))
                        + "（纯相对注入，未开启限制）";
                }
                return;
            }

            // Master switch is enabled:
            if (_server != null)
            {
                _server.RealtimeCenterLock = _chkCenterLock.Checked && gameActive;
                _server.ForceUnclip = (_cmbClipMode.SelectedIndex == 0) && gameActive;
                _server.CenterX = centerX;
                _server.CenterY = centerY;
            }

            if (!gameActive)
            {
                ReleaseClip();
                if (game == IntPtr.Zero)
                {
                    _lblClipState.Text = _rdoProcess.Checked
                        ? "未找到指定进程的游戏窗口"
                        : "未检测到全屏游戏（前台窗口不是全屏）";
                }
                else
                {
                    _lblClipState.Text = "游戏未激活，已自动解除鼠标限制";
                }
                return;
            }

            if (_cmbClipMode.SelectedIndex == 0) // 强制解除限制（破除游戏自身限制）
            {
                ReleaseClip();
                NativeMethods.ClipCursor(IntPtr.Zero);
                _lblClipState.Text = "强制解除限制 (ClipCursor NULL) @ " + Truncate(title, 35)
                    + (_chkCenterLock.Checked ? " [实时锁中]" : "");
            }
            else if (_cmbClipMode.SelectedIndex == 1 || _cmbClipMode.SelectedIndex == 2)
            {
                if (!hasRect)
                {
                    ReleaseClip();
                    _lblClipState.Text = "无法获取游戏窗口区域";
                    return;
                }

                int now = Environment.TickCount;
                if (!_clipActive || !RectEquals(rect, _clipRect) || unchecked(now - _lastClipApplyTick) >= 1000)
                {
                    if (!NativeMethods.ClipCursorRect(ref rect))
                    {
                        ReleaseClip();
                        _lblClipState.Text = "限制鼠标失败（错误 " + Marshal.GetLastWin32Error() + "）";
                        return;
                    }
                    _clipActive = true;
                    _clipRect = rect;
                    _lastClipApplyTick = now;
                }

                string mode = _cmbClipMode.SelectedIndex == 1
                    ? "中心 " + _numClipSize.Value + "px"
                    : "游戏窗口";
                _lblClipState.Text = "已限制: " + mode + " @ " + Truncate(title, 35)
                    + (_chkCenterLock.Checked ? " [实时锁中]" : "");
            }
            else // 不干涉
            {
                ReleaseClip();
                _lblClipState.Text = "保持系统默认 @ " + Truncate(title, 35)
                    + (_chkCenterLock.Checked ? " [实时锁中]" : "");
            }

            if (_chkCenterLock.Checked && gameActive)
            {
                NativeMethods.SetCursorPos(centerX, centerY);
            }
        }

        private IntPtr FindGameWindow(out string title)
        {
            title = "";

            if (_rdoProcess.Checked)
            {
                string desired = NormalizeProcessName(_txtProcess.Text);
                if (desired.Length == 0)
                {
                    return IntPtr.Zero;
                }

                int now = Environment.TickCount;
                if (_gameHwnd == IntPtr.Zero
                    || !NativeMethods.IsWindow(_gameHwnd)
                    || !string.Equals(desired, _gameProcessName, StringComparison.OrdinalIgnoreCase)
                    || unchecked(now - _lastGameScanTick) > 3000)
                {
                    _gameProcessName = desired;
                    ResolveGameProcess(desired);
                    _lastGameScanTick = now;
                }

                if (_gameHwnd == IntPtr.Zero || !NativeMethods.IsWindow(_gameHwnd))
                {
                    return IntPtr.Zero;
                }

                title = WindowTitle(_gameHwnd);
                return _gameHwnd;
            }

            IntPtr foreground = NativeMethods.GetForegroundWindow();
            if (foreground == IntPtr.Zero || foreground == Handle)
            {
                return IntPtr.Zero;
            }
            if (!NativeMethods.IsWindowVisible(foreground) || NativeMethods.IsIconic(foreground))
            {
                return IntPtr.Zero;
            }

            uint pid;
            NativeMethods.GetWindowThreadProcessId(foreground, out pid);
            if (pid == 0 || pid == _ownPid)
            {
                return IntPtr.Zero;
            }

            string processName = GetProcessName(pid);
            if (processName == "?" || IsShellProcess(processName))
            {
                return IntPtr.Zero;
            }

            if (!IsFullscreen(foreground))
            {
                return IntPtr.Zero;
            }

            title = WindowTitle(foreground);
            return foreground;
        }

        private bool IsGameActive(IntPtr game)
        {
            if (!_rdoProcess.Checked)
            {
                return NativeMethods.GetForegroundWindow() == game;
            }

            IntPtr foreground = NativeMethods.GetForegroundWindow();
            if (foreground == IntPtr.Zero)
            {
                return false;
            }

            uint pid;
            NativeMethods.GetWindowThreadProcessId(foreground, out pid);
            return pid != 0 && pid == _gamePid;
        }

        private bool TryGetClipRect(IntPtr game, out NativeMethods.Rect clip, out int centerX, out int centerY)
        {
            clip = new NativeMethods.Rect();
            centerX = 0;
            centerY = 0;

            NativeMethods.Rect client;
            if (!NativeMethods.GetClientRect(game, out client))
            {
                return false;
            }

            NativeMethods.NativePoint origin = new NativeMethods.NativePoint();
            if (!NativeMethods.ClientToScreen(game, ref origin))
            {
                return false;
            }

            int width = client.Right - client.Left;
            int height = client.Bottom - client.Top;
            if (width <= 0 || height <= 0)
            {
                return false;
            }

            int left = origin.X + client.Left;
            int top = origin.Y + client.Top;
            centerX = left + width / 2;
            centerY = top + height / 2;

            int clipWidth = width;
            int clipHeight = height;
            if (_cmbClipMode.SelectedIndex == 1)
            {
                int size = (int)_numClipSize.Value;
                clipWidth = Math.Min(size, width);
                clipHeight = Math.Min(size, height);
            }

            clip.Left = centerX - clipWidth / 2;
            clip.Top = centerY - clipHeight / 2;
            clip.Right = clip.Left + clipWidth;
            clip.Bottom = clip.Top + clipHeight;
            return clip.Right > clip.Left && clip.Bottom > clip.Top;
        }

        private void ResolveGameProcess(string processName)
        {
            _gameHwnd = IntPtr.Zero;
            _gamePid = 0;

            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(processName);
            }
            catch
            {
                return;
            }

            foreach (Process process in processes)
            {
                uint pid = (uint)process.Id;
                IntPtr hwnd = FindLargestWindowForPid(pid);
                process.Dispose();
                if (hwnd != IntPtr.Zero)
                {
                    _gameHwnd = hwnd;
                    _gamePid = pid;
                    return;
                }
            }
        }

        private static IntPtr FindLargestWindowForPid(uint pid)
        {
            IntPtr best = IntPtr.Zero;
            long bestArea = 0;
            NativeMethods.EnumWindowsProc callback = delegate(IntPtr hwnd, IntPtr lParam)
            {
                uint windowPid;
                NativeMethods.GetWindowThreadProcessId(hwnd, out windowPid);
                if (windowPid != pid)
                {
                    return true;
                }
                if (!NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsIconic(hwnd))
                {
                    return true;
                }

                NativeMethods.Rect rect;
                if (!NativeMethods.GetWindowRect(hwnd, out rect))
                {
                    return true;
                }

                long area = (long)(rect.Right - rect.Left) * (rect.Bottom - rect.Top);
                if (area > bestArea)
                {
                    bestArea = area;
                    best = hwnd;
                }
                return true;
            };
            NativeMethods.EnumWindows(callback, IntPtr.Zero);
            return best;
        }

        private static bool IsFullscreen(IntPtr hwnd)
        {
            NativeMethods.Rect windowRect;
            if (!NativeMethods.GetWindowRect(hwnd, out windowRect))
            {
                return false;
            }

            IntPtr monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero)
            {
                return false;
            }

            NativeMethods.MonitorInfo info = new NativeMethods.MonitorInfo();
            info.Size = Marshal.SizeOf(typeof(NativeMethods.MonitorInfo));
            if (!NativeMethods.GetMonitorInfo(monitor, ref info))
            {
                return false;
            }

            const int tolerance = 2;
            return windowRect.Left <= info.Monitor.Left + tolerance
                && windowRect.Top <= info.Monitor.Top + tolerance
                && windowRect.Right >= info.Monitor.Right - tolerance
                && windowRect.Bottom >= info.Monitor.Bottom - tolerance;
        }

        private static bool IsShellProcess(string processName)
        {
            return string.Equals(processName, "explorer", StringComparison.OrdinalIgnoreCase)
                || string.Equals(processName, "ShellExperienceHost", StringComparison.OrdinalIgnoreCase)
                || string.Equals(processName, "StartMenuExperienceHost", StringComparison.OrdinalIgnoreCase)
                || string.Equals(processName, "SearchHost", StringComparison.OrdinalIgnoreCase)
                || string.Equals(processName, "LockApp", StringComparison.OrdinalIgnoreCase)
                || string.Equals(processName, "TextInputHost", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeProcessName(string text)
        {
            string value = (text ?? "").Trim();
            value = value.Trim('"');
            if (value.Length == 0)
            {
                return "";
            }

            try
            {
                value = Path.GetFileName(value);
            }
            catch
            {
            }

            if (value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(0, value.Length - 4);
            }
            return value.Trim();
        }

        private static string GetProcessName(uint pid)
        {
            try
            {
                using (Process process = Process.GetProcessById((int)pid))
                {
                    return process.ProcessName;
                }
            }
            catch
            {
                return "?";
            }
        }

        private static string WindowTitle(IntPtr hwnd)
        {
            int length = NativeMethods.GetWindowTextLength(hwnd);
            if (length <= 0)
            {
                return "";
            }

            StringBuilder title = new StringBuilder(length + 1);
            NativeMethods.GetWindowText(hwnd, title, title.Capacity);
            return title.ToString();
        }

        private static string Truncate(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            {
                return text ?? "";
            }
            return text.Substring(0, maxLength - 3) + "...";
        }

        private static bool RectEquals(NativeMethods.Rect a, NativeMethods.Rect b)
        {
            return a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom;
        }

        private void ReleaseClip()
        {
            if (_clipActive)
            {
                NativeMethods.ClipCursor(IntPtr.Zero);
                _clipActive = false;
            }
        }

        private void ShowServerError(string error)
        {
            if (!string.IsNullOrEmpty(error))
            {
                _lblServerError.Text = error;
                _lblServerError.ForeColor = Color.Firebrick;
            }
            else if (!_isAdmin)
            {
                _lblServerError.Text = "提示：当前未以管理员身份运行；如果游戏以管理员运行，注入会被 UIPI 拦截。";
                _lblServerError.ForeColor = Color.DarkOrange;
            }
            else
            {
                _lblServerError.Text = "";
            }
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            _uiTimer.Stop();
            StopClient();
            StopServer();
            ReleaseClip();
            SaveConfig();
        }

        private void SaveConfig()
        {
            try
            {
                AppConfig config = new AppConfig();
                config.SelectedTab = _tabs != null ? _tabs.SelectedIndex : 0;

                config.Client.Host = _txtHost.Text ?? "";
                config.Client.Port = (int)_numClientPort.Value;
                config.Client.Sensitivity = _numSensitivity.Value;
                config.Client.Hz = (int)_numHz.Value;
                config.Client.OnlyMstsc = _chkOnlyMstsc.Checked;

                config.Server.Port = (int)_numServerPort.Value;
                config.Server.DryRun = _chkDryRun.Checked;
                config.Server.AlwaysForward = _chkAlwaysForward.Checked;
                config.Server.AutoDetect = _rdoAuto.Checked;
                config.Server.ProcessName = _txtProcess.Text ?? "";
                config.Server.EnableLimits = _chkEnableLimits.Checked;
                config.Server.ClipMode = _cmbClipMode.SelectedIndex;
                config.Server.ClipSize = (int)_numClipSize.Value;
                config.Server.CenterLock = _chkCenterLock.Checked;

                ConfigManager.Save(config);
            }
            catch
            {
            }
        }

        private void LoadConfig()
        {
            try
            {
                AppConfig config = ConfigManager.Load();
                if (config == null) return;

                if (_tabs != null && config.SelectedTab >= 0 && config.SelectedTab < _tabs.TabPages.Count)
                {
                    _tabs.SelectedIndex = config.SelectedTab;
                }

                if (config.Client != null)
                {
                    if (config.Client.Host != null) _txtHost.Text = config.Client.Host;
                    if (config.Client.Port >= _numClientPort.Minimum && config.Client.Port <= _numClientPort.Maximum)
                    {
                        _numClientPort.Value = config.Client.Port;
                    }
                    if (config.Client.Sensitivity >= _numSensitivity.Minimum && config.Client.Sensitivity <= _numSensitivity.Maximum)
                    {
                        _numSensitivity.Value = config.Client.Sensitivity;
                    }
                    if (config.Client.Hz >= _numHz.Minimum && config.Client.Hz <= _numHz.Maximum)
                    {
                        _numHz.Value = config.Client.Hz;
                    }
                    _chkOnlyMstsc.Checked = config.Client.OnlyMstsc;
                }

                if (config.Server != null)
                {
                    if (config.Server.Port >= _numServerPort.Minimum && config.Server.Port <= _numServerPort.Maximum)
                    {
                        _numServerPort.Value = config.Server.Port;
                    }
                    _chkDryRun.Checked = config.Server.DryRun;
                    _chkAlwaysForward.Checked = config.Server.AlwaysForward;
                    if (config.Server.AutoDetect)
                    {
                        _rdoAuto.Checked = true;
                    }
                    else
                    {
                        _rdoProcess.Checked = true;
                    }
                    if (config.Server.ProcessName != null) _txtProcess.Text = config.Server.ProcessName;
                    _chkEnableLimits.Checked = config.Server.EnableLimits;
                    if (config.Server.ClipMode >= 0 && config.Server.ClipMode < _cmbClipMode.Items.Count)
                    {
                        _cmbClipMode.SelectedIndex = config.Server.ClipMode;
                    }
                    if (config.Server.ClipSize >= _numClipSize.Minimum && config.Server.ClipSize <= _numClipSize.Maximum)
                    {
                        _numClipSize.Value = config.Server.ClipSize;
                    }
                    _chkCenterLock.Checked = config.Server.CenterLock;
                }

                UpdateDetectControlState();
                UpdateLimitsControlState();
            }
            catch
            {
            }
        }

        private static bool IsAdministrator()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>Hidden window used to receive WM_INPUT and WM_HOTKEY.</summary>
    internal sealed class HiddenWindow : NativeWindow
    {
        private readonly Action<int, IntPtr> _callback;

        public HiddenWindow(Action<int, IntPtr> callback)
        {
            _callback = callback;
            CreateParams parameters = new CreateParams();
            parameters.ClassName = "STATIC";
            parameters.Caption = "RdpMouseBridge.Hidden";
            CreateHandle(parameters);
        }

        protected override void WndProc(ref Message message)
        {
            if (_callback != null)
            {
                _callback(message.Msg, message.LParam);
            }
            base.WndProc(ref message);
        }
    }

    internal sealed class ClientEngine : IDisposable
    {
        private const uint RID_INPUT = 0x10000003;
        private const uint RIDEV_INPUTSINK = 0x00000100;
        private const uint RIM_TYPEMOUSE = 0;
        private const ushort HID_USAGE_PAGE_GENERIC = 0x01;
        private const ushort HID_USAGE_GENERIC_MOUSE = 0x02;
        private const ushort MOUSE_MOVE_ABSOLUTE = 0x01;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const int WM_INPUT = 0x00FF;
        private const int WM_HOTKEY = 0x0312;
        private const int HotkeyId = 0x5244;

        private readonly string _host;
        private readonly int _port;
        private readonly double _sensitivity;
        private readonly int _hz;
        private readonly bool _onlyMstsc;
        private readonly ManualResetEvent _stop = new ManualResetEvent(false);
        private readonly object _accLock = new object();

        private UdpClient _udp;
        private HiddenWindow _window;
        private Thread _sender;
        private IntPtr _rawBuffer;
        private int _rawBufferSize;
        private bool _timerActive;
        private bool _disposed;

        private long _accX;
        private long _accY;
        private volatile bool _paused;
        private volatile string _lastError;
        private bool _hotkeyRegistered;

        private long _rawEvents;
        private long _sentPackets;

        private IntPtr _lastForeground;
        private bool _lastForegroundIsMstsc;

        public ClientEngine(string host, int port, double sensitivity, int hz, bool onlyMstsc)
        {
            _host = host;
            _port = port;
            _sensitivity = sensitivity;
            _hz = hz;
            _onlyMstsc = onlyMstsc;
        }

        public bool Paused
        {
            get { return _paused; }
        }

        public bool HotkeyRegistered
        {
            get { return _hotkeyRegistered; }
        }

        public long RawEventCount
        {
            get { return Interlocked.Read(ref _rawEvents); }
        }

        public long SentPacketCount
        {
            get { return Interlocked.Read(ref _sentPackets); }
        }

        public string LastError
        {
            get { return _lastError; }
        }

        public void Start()
        {
            try
            {
                StartCore();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private void StartCore()
        {
            IPAddress address = ResolveIPv4(_host);
            _udp = new UdpClient();
            _udp.Connect(new IPEndPoint(address, _port));

            _window = new HiddenWindow(OnWindowMessage);

            NativeMethods.RawInputDevice[] devices = new NativeMethods.RawInputDevice[1];
            devices[0].UsagePage = HID_USAGE_PAGE_GENERIC;
            devices[0].Usage = HID_USAGE_GENERIC_MOUSE;
            devices[0].Flags = RIDEV_INPUTSINK;
            devices[0].Target = _window.Handle;

            if (!NativeMethods.RegisterRawInputDevices(devices, 1,
                    (uint)Marshal.SizeOf(typeof(NativeMethods.RawInputDevice))))
            {
                throw new Exception("注册 Raw Input 失败，错误代码 " + Marshal.GetLastWin32Error());
            }

            _hotkeyRegistered = NativeMethods.RegisterHotKey(_window.Handle, HotkeyId,
                MOD_CONTROL | MOD_ALT, 0x4D);

            NativeMethods.timeBeginPeriod(1);
            _timerActive = true;

            _sender = new Thread(SenderLoop);
            _sender.IsBackground = true;
            _sender.Name = "mouse-sender";
            _sender.Start();
        }

        public bool IsForwardingNow()
        {
            if (_paused)
            {
                return false;
            }
            if (!_onlyMstsc)
            {
                return true;
            }
            return IsMstscForeground();
        }

        private void OnWindowMessage(int message, IntPtr lParam)
        {
            if (message == WM_INPUT)
            {
                HandleRawInput(lParam);
            }
            else if (message == WM_HOTKEY)
            {
                _paused = !_paused;
                if (_paused)
                {
                    lock (_accLock)
                    {
                        _accX = 0;
                        _accY = 0;
                    }
                }
            }
        }

        private void HandleRawInput(IntPtr rawInput)
        {
            uint headerSize = (uint)Marshal.SizeOf(typeof(NativeMethods.RawInputHeader));
            uint size = 0;
            NativeMethods.GetRawInputData(rawInput, RID_INPUT, IntPtr.Zero, ref size, headerSize);
            if (size == 0)
            {
                return;
            }

            EnsureRawBuffer((int)size);
            uint read = NativeMethods.GetRawInputData(rawInput, RID_INPUT, _rawBuffer, ref size, headerSize);
            if (read != size)
            {
                return;
            }

            NativeMethods.RawInputHeader header =
                (NativeMethods.RawInputHeader)Marshal.PtrToStructure(_rawBuffer, typeof(NativeMethods.RawInputHeader));
            if (header.Type != RIM_TYPEMOUSE)
            {
                return;
            }

            NativeMethods.RawMouse mouse = (NativeMethods.RawMouse)Marshal.PtrToStructure(
                IntPtr.Add(_rawBuffer, (int)headerSize), typeof(NativeMethods.RawMouse));

            if ((mouse.Flags & MOUSE_MOVE_ABSOLUTE) != 0)
            {
                return;
            }
            if (IsInjected(mouse.ExtraInformation))
            {
                return;
            }

            int dx = mouse.LastX;
            int dy = mouse.LastY;
            if (dx == 0 && dy == 0)
            {
                return;
            }

            Interlocked.Increment(ref _rawEvents);

            if (!IsForwardingNow())
            {
                return;
            }

            long scaledX = (long)Math.Round(dx * _sensitivity);
            long scaledY = (long)Math.Round(dy * _sensitivity);
            lock (_accLock)
            {
                _accX += scaledX;
                _accY += scaledY;
            }
        }

        private void EnsureRawBuffer(int size)
        {
            if (_rawBuffer != IntPtr.Zero && _rawBufferSize >= size)
            {
                return;
            }
            if (_rawBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_rawBuffer);
            }
            _rawBufferSize = Math.Max(size, 256);
            _rawBuffer = Marshal.AllocHGlobal(_rawBufferSize);
        }

        private static bool IsInjected(uint extraInfo)
        {
            return extraInfo == NativeMethods.InjectionSignature
                || (extraInfo & NativeMethods.SendInputSignatureMask) == NativeMethods.SendInputSignature;
        }

        private bool IsMstscForeground()
        {
            IntPtr foreground = NativeMethods.GetForegroundWindow();
            if (foreground == IntPtr.Zero)
            {
                _lastForeground = IntPtr.Zero;
                return false;
            }

            if (foreground != _lastForeground)
            {
                _lastForeground = foreground;
                _lastForegroundIsMstsc = false;

                uint pid;
                NativeMethods.GetWindowThreadProcessId(foreground, out pid);
                if (pid != 0)
                {
                    try
                    {
                        using (Process process = Process.GetProcessById((int)pid))
                        {
                            _lastForegroundIsMstsc = string.Equals(process.ProcessName, "mstsc",
                                StringComparison.OrdinalIgnoreCase);
                        }
                    }
                    catch
                    {
                    }
                }
            }

            return _lastForegroundIsMstsc;
        }

        private void SenderLoop()
        {
            int interval = Math.Max(1, 1000 / _hz);
            while (!_stop.WaitOne(interval))
            {
                Flush();
            }
        }

        private void Flush()
        {
            long dx;
            long dy;
            lock (_accLock)
            {
                dx = _accX;
                dy = _accY;
                _accX = 0;
                _accY = 0;
            }

            if (dx == 0 && dy == 0)
            {
                return;
            }

            if (dx > int.MaxValue) dx = int.MaxValue;
            else if (dx < int.MinValue) dx = int.MinValue;
            if (dy > int.MaxValue) dy = int.MaxValue;
            else if (dy < int.MinValue) dy = int.MinValue;

            byte[] packet = new byte[12];
            Buffer.BlockCopy(BitConverter.GetBytes(NativeMethods.Magic), 0, packet, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes((int)dx), 0, packet, 4, 4);
            Buffer.BlockCopy(BitConverter.GetBytes((int)dy), 0, packet, 8, 4);

            try
            {
                _udp.Send(packet, packet.Length);
                Interlocked.Increment(ref _sentPackets);
                if (_lastError != null)
                {
                    _lastError = null;
                }
            }
            catch (Exception ex)
            {
                _lastError = "发送失败: " + ex.Message;
            }
        }

        private static IPAddress ResolveIPv4(string host)
        {
            IPAddress address;
            if (IPAddress.TryParse(host, out address))
            {
                return address;
            }

            IPAddress[] addresses = Dns.GetHostAddresses(host);
            foreach (IPAddress item in addresses)
            {
                if (item.AddressFamily == AddressFamily.InterNetwork)
                {
                    return item;
                }
            }
            if (addresses.Length > 0)
            {
                return addresses[0];
            }
            throw new Exception("无法解析主机地址: " + host);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;

            _stop.Set();
            Thread sender = _sender;
            _sender = null;
            if (sender != null)
            {
                try
                {
                    sender.Join(500);
                }
                catch
                {
                }
            }

            if (_window != null)
            {
                try
                {
                    NativeMethods.UnregisterHotKey(_window.Handle, HotkeyId);
                }
                catch
                {
                }
                try
                {
                    _window.DestroyHandle();
                }
                catch
                {
                }
                _window = null;
            }

            if (_rawBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_rawBuffer);
                _rawBuffer = IntPtr.Zero;
                _rawBufferSize = 0;
            }

            UdpClient udp = _udp;
            _udp = null;
            if (udp != null)
            {
                try
                {
                    udp.Close();
                }
                catch
                {
                }
            }

            if (_timerActive)
            {
                NativeMethods.timeEndPeriod(1);
                _timerActive = false;
            }

            _stop.Dispose();
        }
    }

    internal sealed class ServerEngine : IDisposable
    {
        private readonly int _port;
        private readonly bool _dryRun;
        private readonly ManualResetEvent _stop = new ManualResetEvent(false);
        private readonly NativeMethods.Input[] _inputBuffer = new NativeMethods.Input[1];
        private readonly byte[] _buffer = new byte[64];

        private UdpClient _udp;
        private Thread _receiver;
        private EndPoint _remote = new IPEndPoint(IPAddress.Any, 0);
        private bool _disposed;

        private long _receivedPackets;
        private long _appliedMoves;
        private long _failedMoves;
        private volatile string _lastError;

        public volatile bool RealtimeCenterLock;
        public volatile bool ForceUnclip;
        public volatile int CenterX;
        public volatile int CenterY;

        public ServerEngine(int port, bool dryRun)
        {
            _port = port;
            _dryRun = dryRun;
        }

        public long ReceivedPacketCount
        {
            get { return Interlocked.Read(ref _receivedPackets); }
        }

        public long AppliedMoveCount
        {
            get { return Interlocked.Read(ref _appliedMoves); }
        }

        public long FailedMoveCount
        {
            get { return Interlocked.Read(ref _failedMoves); }
        }

        public string LastError
        {
            get { return _lastError; }
        }

        public void Start()
        {
            try
            {
                _udp = new UdpClient(new IPEndPoint(IPAddress.Any, _port));
                _receiver = new Thread(ReceiveLoop);
                _receiver.IsBackground = true;
                _receiver.Name = "mouse-receiver";
                _receiver.Start();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private void ReceiveLoop()
        {
            while (!_stop.WaitOne(0))
            {
                int received;
                try
                {
                    received = _udp.Client.ReceiveFrom(_buffer, ref _remote);
                }
                catch (SocketException ex)
                {
                    if (_stop.WaitOne(0))
                    {
                        break;
                    }
                    _lastError = "接收失败: " + ex.SocketErrorCode;
                    continue;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                if (received != 12 || BitConverter.ToInt32(_buffer, 0) != NativeMethods.Magic)
                {
                    continue;
                }

                int dx = BitConverter.ToInt32(_buffer, 4);
                int dy = BitConverter.ToInt32(_buffer, 8);
                Interlocked.Increment(ref _receivedPackets);

                if (dx == 0 && dy == 0)
                {
                    continue;
                }

                if (_dryRun)
                {
                    Interlocked.Increment(ref _appliedMoves);
                    continue;
                }

                if (SendRelative(dx, dy))
                {
                    Interlocked.Increment(ref _appliedMoves);
                    if (RealtimeCenterLock)
                    {
                        NativeMethods.SetCursorPos(CenterX, CenterY);
                    }
                    if (ForceUnclip)
                    {
                        NativeMethods.ClipCursor(IntPtr.Zero);
                    }
                    if (_lastError != null)
                    {
                        _lastError = null;
                    }
                }
                else
                {
                    Interlocked.Increment(ref _failedMoves);
                    _lastError = "SendInput 失败（错误 " + Marshal.GetLastWin32Error()
                        + "）；如果游戏以管理员运行，请以管理员身份重启本程序。";
                }
            }
        }

        private bool SendRelative(int dx, int dy)
        {
            NativeMethods.Input input = new NativeMethods.Input();
            input.Type = NativeMethods.INPUT_MOUSE;
            input.Data.Mouse.Dx = dx;
            input.Data.Mouse.Dy = dy;
            input.Data.Mouse.Flags = NativeMethods.MOUSEEVENTF_MOVE;
            input.Data.Mouse.ExtraInfo = new IntPtr(unchecked((int)NativeMethods.InjectionSignature));

            _inputBuffer[0] = input;
            return NativeMethods.SendInput(1, _inputBuffer,
                Marshal.SizeOf(typeof(NativeMethods.Input))) == 1;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;

            _stop.Set();

            try
            {
                NativeMethods.ClipCursor(IntPtr.Zero);
            }
            catch
            {
            }

            UdpClient udp = _udp;
            _udp = null;
            if (udp != null)
            {
                try
                {
                    udp.Close();
                }
                catch
                {
                }
            }

            Thread receiver = _receiver;
            _receiver = null;
            if (receiver != null)
            {
                try
                {
                    receiver.Join(1000);
                }
                catch
                {
                }
            }

            _stop.Dispose();
        }
    }

    internal static class NativeMethods
    {
        public const int Magic = 0x524D4231; // "RMB1"
        public const uint InjectionSignature = 0x52444D42; // "RDMB"
        public const uint SendInputSignature = 0xFF515700;
        public const uint SendInputSignatureMask = 0xFFFFFF00;

        public const uint INPUT_MOUSE = 0;
        public const uint MOUSEEVENTF_MOVE = 0x0001;

        public const uint MONITOR_DEFAULTTONEAREST = 2;

        [StructLayout(LayoutKind.Sequential)]
        public struct RawInputDevice
        {
            public ushort UsagePage;
            public ushort Usage;
            public uint Flags;
            public IntPtr Target;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RawInputHeader
        {
            public uint Type;
            public uint Size;
            public IntPtr Device;
            public IntPtr WParam;
        }

        // Offsets match winuser.h: usFlags, union at 4, ulRawButtons, lLastX, lLastY, ulExtraInformation.
        [StructLayout(LayoutKind.Explicit, Size = 24)]
        public struct RawMouse
        {
            [FieldOffset(0)] public ushort Flags;
            [FieldOffset(4)] public uint Buttons;
            [FieldOffset(8)] public uint RawButtons;
            [FieldOffset(12)] public int LastX;
            [FieldOffset(16)] public int LastY;
            [FieldOffset(20)] public uint ExtraInformation;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MouseInput
        {
            public int Dx;
            public int Dy;
            public uint MouseData;
            public uint Flags;
            public uint Time;
            public IntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)] public MouseInput Mouse;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Input
        {
            public uint Type;
            public InputUnion Data;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct NativePoint
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MonitorInfo
        {
            public int Size;
            public Rect Monitor;
            public Rect Work;
            public uint Flags;
        }

        public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterRawInputDevices(
            RawInputDevice[] devices, uint numDevices, uint size);

        [DllImport("user32.dll")]
        public static extern uint GetRawInputData(
            IntPtr rawInput, uint command, IntPtr data, ref uint size, uint headerSize);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint vk);

        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hwnd, int id);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint numInputs, Input[] inputs, int size);

        [DllImport("winmm.dll")]
        public static extern uint timeBeginPeriod(uint period);

        [DllImport("winmm.dll")]
        public static extern uint timeEndPeriod(uint period);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool ClipCursor(IntPtr rect);

        [DllImport("user32.dll", EntryPoint = "ClipCursor", SetLastError = true)]
        public static extern bool ClipCursorRect(ref Rect rect);

        [DllImport("user32.dll")]
        public static extern bool GetClientRect(IntPtr hwnd, out Rect rect);

        [DllImport("user32.dll")]
        public static extern bool ClientToScreen(IntPtr hwnd, ref NativePoint point);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

        [DllImport("user32.dll")]
        public static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowTextLength(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    }

    [Serializable]
    public class AppConfig
    {
        public int SelectedTab { get; set; }
        public ClientConfig Client { get; set; }
        public ServerConfig Server { get; set; }

        public AppConfig()
        {
            SelectedTab = 0;
            Client = new ClientConfig();
            Server = new ServerConfig();
        }
    }

    [Serializable]
    public class ClientConfig
    {
        public string Host { get; set; }
        public int Port { get; set; }
        public decimal Sensitivity { get; set; }
        public int Hz { get; set; }
        public bool OnlyMstsc { get; set; }

        public ClientConfig()
        {
            Host = "";
            Port = 50505;
            Sensitivity = 1.00m;
            Hz = 250;
            OnlyMstsc = true;
        }
    }

    [Serializable]
    public class ServerConfig
    {
        public int Port { get; set; }
        public bool DryRun { get; set; }
        public bool AlwaysForward { get; set; }
        public bool AutoDetect { get; set; }
        public string ProcessName { get; set; }
        public bool EnableLimits { get; set; }
        public int ClipMode { get; set; }
        public int ClipSize { get; set; }
        public bool CenterLock { get; set; }

        public ServerConfig()
        {
            Port = 50505;
            DryRun = false;
            AlwaysForward = false;
            AutoDetect = true;
            ProcessName = "";
            EnableLimits = false;
            ClipMode = 0;
            ClipSize = 400;
            CenterLock = true;
        }
    }

    internal static class ConfigManager
    {
        private static string GetPrimaryPath()
        {
            try
            {
                string exePath = typeof(Program).Assembly.Location;
                if (!string.IsNullOrEmpty(exePath))
                {
                    string dir = Path.GetDirectoryName(exePath);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        return Path.Combine(dir, "config.json");
                    }
                }
            }
            catch
            {
            }
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
        }

        private static string GetFallbackPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "RdpMouseBridge", "config.json");
        }

        public static string ConfigFilePath
        {
            get
            {
                string primary = GetPrimaryPath();
                if (File.Exists(primary))
                {
                    return primary;
                }
                string fallback = GetFallbackPath();
                if (File.Exists(fallback))
                {
                    return fallback;
                }
                return primary;
            }
        }

        public static AppConfig Load()
        {
            try
            {
                string path = ConfigFilePath;
                if (!File.Exists(path))
                {
                    return new AppConfig();
                }

                string json = File.ReadAllText(path, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return new AppConfig();
                }

                JavaScriptSerializer serializer = new JavaScriptSerializer();
                AppConfig config = serializer.Deserialize<AppConfig>(json);
                if (config == null)
                {
                    config = new AppConfig();
                }
                if (config.Client == null)
                {
                    config.Client = new ClientConfig();
                }
                if (config.Server == null)
                {
                    config.Server = new ServerConfig();
                }
                return config;
            }
            catch
            {
                return new AppConfig();
            }
        }

        public static void Save(AppConfig config)
        {
            if (config == null) return;
            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                string rawJson = serializer.Serialize(config);
                string prettyJson = FormatJson(rawJson);

                string primary = GetPrimaryPath();
                try
                {
                    File.WriteAllText(primary, prettyJson, Encoding.UTF8);
                }
                catch (Exception)
                {
                    string fallback = GetFallbackPath();
                    string dir = Path.GetDirectoryName(fallback);
                    if (!Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    File.WriteAllText(fallback, prettyJson, Encoding.UTF8);
                }
            }
            catch
            {
            }
        }

        private static string FormatJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return "";
            StringBuilder sb = new StringBuilder();
            int indent = 0;
            bool inString = false;
            for (int i = 0; i < json.Length; i++)
            {
                char ch = json[i];
                if (ch == '"')
                {
                    int backslashes = 0;
                    for (int j = i - 1; j >= 0 && json[j] == '\\'; j--)
                    {
                        backslashes++;
                    }
                    if (backslashes % 2 == 0)
                    {
                        inString = !inString;
                    }
                    sb.Append(ch);
                }
                else if (inString)
                {
                    sb.Append(ch);
                }
                else
                {
                    switch (ch)
                    {
                        case '{':
                        case '[':
                            sb.Append(ch);
                            sb.AppendLine();
                            indent++;
                            sb.Append(new string(' ', indent * 2));
                            break;
                        case '}':
                        case ']':
                            sb.AppendLine();
                            indent--;
                            sb.Append(new string(' ', indent * 2));
                            sb.Append(ch);
                            break;
                        case ',':
                            sb.Append(ch);
                            sb.AppendLine();
                            sb.Append(new string(' ', indent * 2));
                            break;
                        case ':':
                            sb.Append(": ");
                            break;
                        default:
                            if (!char.IsWhiteSpace(ch)) sb.Append(ch);
                            break;
                    }
                }
            }
            return sb.ToString();
        }
    }
}
