using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OLEDSaver
{
    public class MainForm : Form
    {
        // Win32 constants
        private const int WM_HOTKEY = 0x0312;
        private const int MOD_CONTROL = 0x0002;
        private const int MOD_ALT = 0x0001;
        private const int HOTKEY_ID = 9001;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        // Core engines
        private AppSettings _settings;
        private InputWatcher _inputWatcher;
        private BlackoutManager _blackoutManager;
        private TeamsWatcher _teamsWatcher;
        private List<DisplayInfo> _displays;

        private Timer _uiUpdateTimer;
        private bool _isPaused = false;
        private bool _isHotKeyRegistered = false;
        private bool _wasVisibleBeforeBlackout = false;
        private bool _initialMinimized = false;
        private long _lastWakeTick = 0;

        // UI Components
        private NotifyIcon _trayIcon;
        private ContextMenuStrip _trayMenu;
        private ToolStripMenuItem _trayActionItem;

        private Label _lblStatusBadge;
        private Label _lblCountdown;
        private Label _lblIdleDetails;
        private ProgressBar _progressBar;

        private Button _btnBlackoutNow;
        private Button _btnPauseResume;
        private Button _btnReset;

        private NumericUpDown _numMinutes;
        private RadioButton _rbKeyboardOnly;
        private RadioButton _rbKeyboardAndMouse;

        private RadioButton _rbActionBlackout;
        private RadioButton _rbActionPowerOff;
        private Label _lblActionHint;

        private Label _lblDisplaysTitle;
        private CheckedListBox _clbDisplays;
        private Button _btnSelectAllDisplays;
        private Button _btnSelectExternalsOnly;
        private Button _btnSelectPrimaryOnly;
        private Button _btnRefreshDisplays;

        private CheckBox _chkTeamsEnable;
        private Label _lblTeamsCurrentStatus;
        private CheckBox _chkTeamsInCall;
        private CheckBox _chkTeamsPresenting;
        private CheckBox _chkTeamsBusy;
        private CheckBox _chkTeamsDnd;
        private CheckBox _chkTeamsAway;

        private CheckBox _chkStartWithWindows;
        private CheckBox _chkMinimizeToTray;
        private CheckBox _chkHotkey;
        private ComboBox _cmbSensitivity;

        // Color Palette
        private readonly Color ColBg = Color.FromArgb(12, 15, 20);
        private readonly Color ColCard = Color.FromArgb(20, 24, 33);
        private readonly Color ColCardBorder = Color.FromArgb(34, 41, 56);
        private readonly Color ColAccent = Color.FromArgb(0, 229, 163);       // Mint Emerald
        private readonly Color ColButtonDark = Color.FromArgb(32, 38, 52);
        private readonly Color ColTextPrimary = Color.FromArgb(243, 244, 246);
        private readonly Color ColTextSecondary = Color.FromArgb(156, 163, 175);
        private readonly Color ColWarning = Color.FromArgb(245, 158, 11);

        public MainForm(bool startMinimized)
        {
            _settings = AppSettings.Load();

            InitializeForm();
            InitializeTray();
            InitializeInputAndBlackout();

            LoadDisplays();
            ApplySettingsToUI();

            _uiUpdateTimer = new Timer();
            _uiUpdateTimer.Interval = 250;
            _uiUpdateTimer.Tick += UIUpdateTimer_Tick;
            _uiUpdateTimer.Start();

            if (startMinimized)
            {
                _initialMinimized = true;
                this.ShowInTaskbar = false;
            }
        }

        private void InitializeForm()
        {
            this.Text = "OLED Blackout Saver";
            this.Size = new Size(720, 880);
            this.MinimumSize = new Size(680, 750);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = ColBg;
            this.ForeColor = ColTextPrimary;
            this.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

            try
            {
                if (System.IO.File.Exists("app.ico"))
                {
                    this.Icon = new Icon("app.ico");
                }
                else
                {
                    this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                }
            }
            catch
            {
            }

            // ========================================================
            // Top Header Panel
            // ========================================================
            Panel headerPanel = new Panel();
            headerPanel.Location = new Point(0, 0);
            headerPanel.Size = new Size(this.ClientSize.Width, 84);
            headerPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            headerPanel.BackColor = ColBg;

            Label titleLabel = new Label();
            titleLabel.Text = "OLED Blackout Saver";
            titleLabel.Font = new Font("Segoe UI", 16f, FontStyle.Bold);
            titleLabel.ForeColor = ColTextPrimary;
            titleLabel.AutoSize = true;
            titleLabel.Location = new Point(24, 12);
            headerPanel.Controls.Add(titleLabel);

            Label subtitleLabel = new Label();
            subtitleLabel.Text = "Keystroke Inactivity Monitor • True OLED Black (#000000) & Screen Power Off";
            subtitleLabel.Font = new Font("Segoe UI", 8.5f, FontStyle.Regular);
            subtitleLabel.ForeColor = ColTextSecondary;
            subtitleLabel.AutoSize = true;
            subtitleLabel.Location = new Point(26, titleLabel.Bottom + 6);
            headerPanel.Controls.Add(subtitleLabel);

            headerPanel.Height = subtitleLabel.Bottom + 16;

            _lblStatusBadge = new Label();
            _lblStatusBadge.Text = "● ACTIVE";
            _lblStatusBadge.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            _lblStatusBadge.ForeColor = ColAccent;
            _lblStatusBadge.BackColor = Color.FromArgb(16, 45, 38);
            _lblStatusBadge.TextAlign = ContentAlignment.MiddleCenter;
            _lblStatusBadge.Size = new Size(125, 28);
            _lblStatusBadge.Location = new Point(headerPanel.Width - 155, 20);
            _lblStatusBadge.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            headerPanel.Controls.Add(_lblStatusBadge);

            this.Controls.Add(headerPanel);

            // ========================================================
            // Scrollable Content Panel
            // ========================================================
            Panel contentPanel = new Panel();
            contentPanel.Location = new Point(0, headerPanel.Height);
            contentPanel.Size = new Size(this.ClientSize.Width, this.ClientSize.Height - headerPanel.Height);
            contentPanel.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            contentPanel.AutoScroll = true;
            contentPanel.Padding = new Padding(24, 8, 24, 24);
            this.Controls.Add(contentPanel);

            int yOffset = 14;

            // ========================================================
            // 1. Hero Countdown Card
            // ========================================================
            Panel cardTimer = CreateCard(contentPanel, ref yOffset, 180);

            Label lblTimerHeading = new Label();
            lblTimerHeading.Text = "COUNTDOWN TO SCREEN PROTECTION";
            lblTimerHeading.UseMnemonic = false;
            lblTimerHeading.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            lblTimerHeading.ForeColor = ColTextSecondary;
            lblTimerHeading.Location = new Point(24, 16);
            lblTimerHeading.AutoSize = true;
            cardTimer.Controls.Add(lblTimerHeading);

            _lblCountdown = new Label();
            _lblCountdown.Text = "05:00";
            _lblCountdown.Font = new Font("Segoe UI", 34f, FontStyle.Bold);
            _lblCountdown.ForeColor = ColAccent;
            _lblCountdown.Location = new Point(20, lblTimerHeading.Bottom + 4);
            _lblCountdown.AutoSize = true;
            cardTimer.Controls.Add(_lblCountdown);

            _lblIdleDetails = new Label();
            _lblIdleDetails.Text = "Idle: 00:00 • Target: 5 minutes";
            _lblIdleDetails.Font = new Font("Segoe UI", 9f);
            _lblIdleDetails.ForeColor = ColTextSecondary;
            _lblIdleDetails.Location = new Point(24, _lblCountdown.Bottom + 8);
            _lblIdleDetails.AutoSize = true;
            cardTimer.Controls.Add(_lblIdleDetails);

            _progressBar = new ProgressBar();
            _progressBar.Location = new Point(24, _lblIdleDetails.Bottom + 10);
            _progressBar.Size = new Size(cardTimer.Width - 220, 12);
            _progressBar.Maximum = 1000;
            _progressBar.Value = 0;
            _progressBar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            cardTimer.Controls.Add(_progressBar);

            cardTimer.Height = _progressBar.Bottom + 18;

            _btnBlackoutNow = CreateStyledButton("⏻ Blackout Now", ColAccent, Color.Black, true);
            _btnBlackoutNow.Location = new Point(cardTimer.Width - 185, 18);
            _btnBlackoutNow.Size = new Size(160, 36);
            _btnBlackoutNow.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnBlackoutNow.Click += (s, e) => TriggerBlackoutNow();
            cardTimer.Controls.Add(_btnBlackoutNow);

            _btnPauseResume = CreateStyledButton("❚❚ Pause Timer", ColButtonDark, ColTextPrimary, false);
            _btnPauseResume.Location = new Point(cardTimer.Width - 185, 60);
            _btnPauseResume.Size = new Size(160, 32);
            _btnPauseResume.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnPauseResume.Click += BtnPauseResume_Click;
            cardTimer.Controls.Add(_btnPauseResume);

            _btnReset = CreateStyledButton("↺ Reset Timer", ColButtonDark, ColTextSecondary, false);
            _btnReset.Location = new Point(cardTimer.Width - 185, 98);
            _btnReset.Size = new Size(160, 28);
            _btnReset.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnReset.Click += (s, e) => { if (_inputWatcher != null) _inputWatcher.ResetTimer(); };
            cardTimer.Controls.Add(_btnReset);

            yOffset = cardTimer.Bottom + 16;

            // ========================================================
            // 2. Timer & Trigger Mode Card
            // ========================================================
            Panel cardConfig = CreateCard(contentPanel, ref yOffset, 175);

            Label lblConfigTitle = new Label();
            lblConfigTitle.Text = "TIMER & INACTIVITY TRIGGER";
            lblConfigTitle.UseMnemonic = false;
            lblConfigTitle.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            lblConfigTitle.ForeColor = ColTextSecondary;
            lblConfigTitle.Location = new Point(24, 14);
            lblConfigTitle.AutoSize = true;
            cardConfig.Controls.Add(lblConfigTitle);

            Label lblTimeout = new Label();
            lblTimeout.Text = "Timeout (minutes):";
            lblTimeout.Location = new Point(24, lblConfigTitle.Bottom + 12);
            lblTimeout.AutoSize = true;
            cardConfig.Controls.Add(lblTimeout);

            _numMinutes = new NumericUpDown();
            _numMinutes.Minimum = 1;
            _numMinutes.Maximum = 180;
            _numMinutes.Value = _settings.TimeoutMinutes;
            _numMinutes.Location = new Point(lblTimeout.Right + 12, lblTimeout.Top - 2);
            _numMinutes.Width = 65;
            _numMinutes.BackColor = ColButtonDark;
            _numMinutes.ForeColor = ColTextPrimary;
            _numMinutes.ValueChanged += NumMinutes_ValueChanged;
            cardConfig.Controls.Add(_numMinutes);

            // Preset Buttons [1m] [2m] [5m] [10m] [15m] [30m]
            int[] presets = new int[] { 1, 2, 5, 10, 15, 30 };
            int px = _numMinutes.Right + 14;
            for (int i = 0; i < presets.Length; i++)
            {
                int val = presets[i];
                Button b = CreateStyledButton(val + "m", ColButtonDark, ColTextPrimary, false);
                b.Size = new Size(42, 28);
                b.Location = new Point(px, lblTimeout.Top - 4);
                b.Click += (s, e) => { _numMinutes.Value = val; };
                cardConfig.Controls.Add(b);
                px += 48;
            }

            _rbKeyboardOnly = new RadioButton();
            _rbKeyboardOnly.Text = "Keyboard Inactivity Only (Ideal for reading/watching without typing)";
            _rbKeyboardOnly.Location = new Point(24, lblTimeout.Bottom + 14);
            _rbKeyboardOnly.AutoSize = true;
            _rbKeyboardOnly.Checked = (_settings.IdleMode == "KeyboardOnly");
            _rbKeyboardOnly.CheckedChanged += TriggerMode_CheckedChanged;
            cardConfig.Controls.Add(_rbKeyboardOnly);

            _rbKeyboardAndMouse = new RadioButton();
            _rbKeyboardAndMouse.Text = "Keyboard + Mouse Inactivity (Blanks when both are idle)";
            _rbKeyboardAndMouse.Location = new Point(24, _rbKeyboardOnly.Bottom + 8);
            _rbKeyboardAndMouse.AutoSize = true;
            _rbKeyboardAndMouse.Checked = (_settings.IdleMode != "KeyboardOnly");
            _rbKeyboardAndMouse.CheckedChanged += TriggerMode_CheckedChanged;
            cardConfig.Controls.Add(_rbKeyboardAndMouse);

            Label lblPrivacy = new Label();
            lblPrivacy.Text = "🔒 Privacy: Passive input timestamping only. Keystrokes are never logged, recorded, or read.";
            lblPrivacy.Font = new Font("Segoe UI", 8f, FontStyle.Italic);
            lblPrivacy.ForeColor = Color.FromArgb(130, 180, 160);
            lblPrivacy.Location = new Point(24, _rbKeyboardAndMouse.Bottom + 12);
            lblPrivacy.AutoSize = true;
            cardConfig.Controls.Add(lblPrivacy);

            cardConfig.Height = lblPrivacy.Bottom + 18;
            yOffset = cardConfig.Bottom + 16;

            // ========================================================
            // 3. Screen Protection Mode Card (Overlay vs Power Off)
            // ========================================================
            Panel cardAction = CreateCard(contentPanel, ref yOffset, 155);

            Label lblActionTitle = new Label();
            lblActionTitle.Text = "IDLE ACTION / SCREEN PROTECTION MODE";
            lblActionTitle.UseMnemonic = false;
            lblActionTitle.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            lblActionTitle.ForeColor = ColTextSecondary;
            lblActionTitle.Location = new Point(24, 14);
            lblActionTitle.AutoSize = true;
            cardAction.Controls.Add(lblActionTitle);

            _rbActionBlackout = new RadioButton();
            _rbActionBlackout.Text = "Pure OLED Blackout Overlay (#000000) [Instant 0ms wake, per-monitor configurable]";
            _rbActionBlackout.Location = new Point(24, lblActionTitle.Bottom + 12);
            _rbActionBlackout.AutoSize = true;
            _rbActionBlackout.Checked = (_settings.ScreenAction != "HardwarePowerOff");
            _rbActionBlackout.CheckedChanged += ScreenActionMode_CheckedChanged;
            cardAction.Controls.Add(_rbActionBlackout);

            _rbActionPowerOff = new RadioButton();
            _rbActionPowerOff.Text = "Hardware Screen Power Off (Standby / No Signal) [Cuts video signal to all monitors]";
            _rbActionPowerOff.Location = new Point(24, _rbActionBlackout.Bottom + 8);
            _rbActionPowerOff.AutoSize = true;
            _rbActionPowerOff.Checked = (_settings.ScreenAction == "HardwarePowerOff");
            _rbActionPowerOff.CheckedChanged += ScreenActionMode_CheckedChanged;
            cardAction.Controls.Add(_rbActionPowerOff);

            _lblActionHint = new Label();
            _lblActionHint.Text = "🌟 Pure OLED Blackout Overlay shuts off OLED pixels with 0ms resume on selected monitors.";
            _lblActionHint.Font = new Font("Segoe UI", 8.5f, FontStyle.Italic);
            _lblActionHint.ForeColor = ColAccent;
            _lblActionHint.Location = new Point(24, _rbActionPowerOff.Bottom + 10);
            _lblActionHint.AutoSize = true;
            cardAction.Controls.Add(_lblActionHint);

            cardAction.Height = _lblActionHint.Bottom + 16;
            yOffset = cardAction.Bottom + 16;

            // ========================================================
            // 4. Multi-Monitor Display Target Card
            // ========================================================
            Panel cardDisplays = CreateCard(contentPanel, ref yOffset, 210);

            _lblDisplaysTitle = new Label();
            _lblDisplaysTitle.Text = "TARGET DISPLAYS TO BLACKOUT (OVERLAY MODE)";
            _lblDisplaysTitle.UseMnemonic = false;
            _lblDisplaysTitle.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            _lblDisplaysTitle.ForeColor = ColTextSecondary;
            _lblDisplaysTitle.Location = new Point(24, 14);
            _lblDisplaysTitle.AutoSize = true;
            cardDisplays.Controls.Add(_lblDisplaysTitle);

            _clbDisplays = new CheckedListBox();
            _clbDisplays.Location = new Point(24, _lblDisplaysTitle.Bottom + 10);
            _clbDisplays.Size = new Size(cardDisplays.Width - 48, 110);
            _clbDisplays.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _clbDisplays.BackColor = ColButtonDark;
            _clbDisplays.ForeColor = ColTextPrimary;
            _clbDisplays.BorderStyle = BorderStyle.None;
            _clbDisplays.CheckOnClick = true;
            _clbDisplays.ItemCheck += ClbDisplays_ItemCheck;
            cardDisplays.Controls.Add(_clbDisplays);

            _btnSelectAllDisplays = CreateStyledButton("All Displays", ColButtonDark, ColTextPrimary, false);
            _btnSelectAllDisplays.Location = new Point(24, _clbDisplays.Bottom + 12);
            _btnSelectAllDisplays.Size = new Size(110, 28);
            _btnSelectAllDisplays.Click += (s, e) => SetAllDisplaysChecked(true);
            cardDisplays.Controls.Add(_btnSelectAllDisplays);

            _btnSelectExternalsOnly = CreateStyledButton("External Only", ColButtonDark, ColTextPrimary, false);
            _btnSelectExternalsOnly.Location = new Point(142, _btnSelectAllDisplays.Top);
            _btnSelectExternalsOnly.Size = new Size(120, 28);
            _btnSelectExternalsOnly.Click += (s, e) => SelectDisplaysByFilter(false);
            cardDisplays.Controls.Add(_btnSelectExternalsOnly);

            _btnSelectPrimaryOnly = CreateStyledButton("Primary Only", ColButtonDark, ColTextPrimary, false);
            _btnSelectPrimaryOnly.Location = new Point(270, _btnSelectAllDisplays.Top);
            _btnSelectPrimaryOnly.Size = new Size(110, 28);
            _btnSelectPrimaryOnly.Click += (s, e) => SelectDisplaysByFilter(true);
            cardDisplays.Controls.Add(_btnSelectPrimaryOnly);

            _btnRefreshDisplays = CreateStyledButton("⟳ Refresh", ColButtonDark, ColTextSecondary, false);
            _btnRefreshDisplays.Location = new Point(cardDisplays.Width - 124, _btnSelectAllDisplays.Top);
            _btnRefreshDisplays.Size = new Size(100, 28);
            _btnRefreshDisplays.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnRefreshDisplays.Click += (s, e) => LoadDisplays();
            cardDisplays.Controls.Add(_btnRefreshDisplays);

            cardDisplays.Height = _btnSelectAllDisplays.Bottom + 18;
            yOffset = cardDisplays.Bottom + 16;

            // ========================================================
            // 5. Microsoft Teams Status Integration Card
            // ========================================================
            Panel cardTeams = CreateCard(contentPanel, ref yOffset, 245);

            Label lblTeamsTitle = new Label();
            lblTeamsTitle.Text = "MICROSOFT TEAMS STATUS INTEGRATION";
            lblTeamsTitle.UseMnemonic = false;
            lblTeamsTitle.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            lblTeamsTitle.ForeColor = ColTextSecondary;
            lblTeamsTitle.Location = new Point(24, 14);
            lblTeamsTitle.AutoSize = true;
            cardTeams.Controls.Add(lblTeamsTitle);

            _chkTeamsEnable = new CheckBox();
            _chkTeamsEnable.Text = "Pause countdown based on Microsoft Teams call / presence status";
            _chkTeamsEnable.Location = new Point(24, lblTeamsTitle.Bottom + 10);
            _chkTeamsEnable.AutoSize = true;
            _chkTeamsEnable.Checked = _settings.TeamsDetectionEnabled;
            _chkTeamsEnable.CheckedChanged += ChkTeamsEnable_CheckedChanged;
            cardTeams.Controls.Add(_chkTeamsEnable);

            _lblTeamsCurrentStatus = new Label();
            _lblTeamsCurrentStatus.Text = "Current Teams Status: Checking...";
            _lblTeamsCurrentStatus.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            _lblTeamsCurrentStatus.ForeColor = Color.FromArgb(120, 180, 255);
            _lblTeamsCurrentStatus.Location = new Point(24, _chkTeamsEnable.Bottom + 8);
            _lblTeamsCurrentStatus.AutoSize = true;
            cardTeams.Controls.Add(_lblTeamsCurrentStatus);

            Label lblTeamsSub = new Label();
            lblTeamsSub.Text = "Prevent screensaver and pause timer when Teams status is:";
            lblTeamsSub.ForeColor = ColTextSecondary;
            lblTeamsSub.Location = new Point(24, _lblTeamsCurrentStatus.Bottom + 8);
            lblTeamsSub.AutoSize = true;
            cardTeams.Controls.Add(lblTeamsSub);

            _chkTeamsInCall = new CheckBox();
            _chkTeamsInCall.Text = "🔴 In a Call / Meeting (Active audio/video stream or meeting window)";
            _chkTeamsInCall.Location = new Point(36, lblTeamsSub.Bottom + 8);
            _chkTeamsInCall.AutoSize = true;
            _chkTeamsInCall.CheckedChanged += TeamsStatusCheckbox_CheckedChanged;
            cardTeams.Controls.Add(_chkTeamsInCall);

            _chkTeamsPresenting = new CheckBox();
            _chkTeamsPresenting.Text = "🟣 Presenting / Sharing Screen";
            _chkTeamsPresenting.Location = new Point(36, _chkTeamsInCall.Bottom + 6);
            _chkTeamsPresenting.AutoSize = true;
            _chkTeamsPresenting.CheckedChanged += TeamsStatusCheckbox_CheckedChanged;
            cardTeams.Controls.Add(_chkTeamsPresenting);

            _chkTeamsBusy = new CheckBox();
            _chkTeamsBusy.Text = "🔴 Busy";
            _chkTeamsBusy.Location = new Point(36, _chkTeamsPresenting.Bottom + 6);
            _chkTeamsBusy.AutoSize = true;
            _chkTeamsBusy.CheckedChanged += TeamsStatusCheckbox_CheckedChanged;
            cardTeams.Controls.Add(_chkTeamsBusy);

            _chkTeamsDnd = new CheckBox();
            _chkTeamsDnd.Text = "⛔ Do Not Disturb";
            _chkTeamsDnd.Location = new Point(36, _chkTeamsBusy.Bottom + 6);
            _chkTeamsDnd.AutoSize = true;
            _chkTeamsDnd.CheckedChanged += TeamsStatusCheckbox_CheckedChanged;
            cardTeams.Controls.Add(_chkTeamsDnd);

            _chkTeamsAway = new CheckBox();
            _chkTeamsAway.Text = "🟡 Away / Be Right Back";
            _chkTeamsAway.Location = new Point(36, _chkTeamsDnd.Bottom + 6);
            _chkTeamsAway.AutoSize = true;
            _chkTeamsAway.CheckedChanged += TeamsStatusCheckbox_CheckedChanged;
            cardTeams.Controls.Add(_chkTeamsAway);

            cardTeams.Height = _chkTeamsAway.Bottom + 16;
            yOffset = cardTeams.Bottom + 16;

            // ========================================================
            // 6. Preferences & Wake-up Behavior
            // ========================================================
            Panel cardOptions = CreateCard(contentPanel, ref yOffset, 175);

            Label lblOptionsTitle = new Label();
            lblOptionsTitle.Text = "PREFERENCES & WAKE-UP BEHAVIOR";
            lblOptionsTitle.UseMnemonic = false;
            lblOptionsTitle.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            lblOptionsTitle.ForeColor = ColTextSecondary;
            lblOptionsTitle.Location = new Point(24, 14);
            lblOptionsTitle.AutoSize = true;
            cardOptions.Controls.Add(lblOptionsTitle);

            _chkStartWithWindows = new CheckBox();
            _chkStartWithWindows.Text = "Start automatically with Windows (minimized to system tray)";
            _chkStartWithWindows.Location = new Point(24, lblOptionsTitle.Bottom + 10);
            _chkStartWithWindows.AutoSize = true;
            _chkStartWithWindows.Checked = _settings.StartWithWindows;
            _chkStartWithWindows.CheckedChanged += (s, e) => { _settings.StartWithWindows = _chkStartWithWindows.Checked; _settings.Save(); };
            cardOptions.Controls.Add(_chkStartWithWindows);

            _chkMinimizeToTray = new CheckBox();
            _chkMinimizeToTray.Text = "Closing this window minimizes to System Tray (keep running)";
            _chkMinimizeToTray.Location = new Point(24, _chkStartWithWindows.Bottom + 8);
            _chkMinimizeToTray.AutoSize = true;
            _chkMinimizeToTray.Checked = _settings.MinimizeToTrayOnClose;
            _chkMinimizeToTray.CheckedChanged += (s, e) => { _settings.MinimizeToTrayOnClose = _chkMinimizeToTray.Checked; _settings.Save(); };
            cardOptions.Controls.Add(_chkMinimizeToTray);

            _chkHotkey = new CheckBox();
            _chkHotkey.Text = "Instant Trigger Hotkey: Ctrl + Alt + B";
            _chkHotkey.Location = new Point(24, _chkMinimizeToTray.Bottom + 8);
            _chkHotkey.AutoSize = true;
            _chkHotkey.Checked = _settings.HotkeyEnabled;
            _chkHotkey.CheckedChanged += ChkHotkey_CheckedChanged;
            cardOptions.Controls.Add(_chkHotkey);

            Label lblSens = new Label();
            lblSens.Text = "Mouse Wake Sensitivity:";
            lblSens.Location = new Point(24, _chkHotkey.Bottom + 12);
            lblSens.AutoSize = true;
            cardOptions.Controls.Add(lblSens);

            _cmbSensitivity = new ComboBox();
            _cmbSensitivity.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbSensitivity.BackColor = ColButtonDark;
            _cmbSensitivity.ForeColor = ColTextPrimary;
            _cmbSensitivity.Items.AddRange(new object[] { "Sensitive (4 px move)", "Normal (10 px move)", "Firm (25 px move - ignores desk bumps)" });
            if (_settings.WakeSensitivityPixels <= 5) _cmbSensitivity.SelectedIndex = 0;
            else if (_settings.WakeSensitivityPixels >= 20) _cmbSensitivity.SelectedIndex = 2;
            else _cmbSensitivity.SelectedIndex = 1;
            _cmbSensitivity.Location = new Point(lblSens.Right + 12, lblSens.Top - 3);
            _cmbSensitivity.Width = 240;
            _cmbSensitivity.SelectedIndexChanged += CmbSensitivity_SelectedIndexChanged;
            cardOptions.Controls.Add(_cmbSensitivity);

            cardOptions.Height = _cmbSensitivity.Bottom + 18;
            yOffset = cardOptions.Bottom + 24;

            // Form Event Handlers
            this.FormClosing += MainForm_FormClosing;
            this.Resize += MainForm_Resize;
        }

        private Panel CreateCard(Panel parent, ref int yOffset, int height)
        {
            Panel card = new Panel();
            card.Location = new Point(24, yOffset);
            card.Size = new Size(parent.ClientSize.Width - 48, height);
            card.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            card.BackColor = ColCard;
            card.Paint += (s, pe) =>
            {
                using (Pen pen = new Pen(ColCardBorder, 1))
                {
                    pe.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
                }
            };
            parent.Controls.Add(card);
            yOffset += height;
            return card;
        }

        private Button CreateStyledButton(string text, Color bg, Color fg, bool isAccent)
        {
            Button btn = new Button();
            btn.Text = text;
            btn.BackColor = bg;
            btn.ForeColor = fg;
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderSize = 0;
            btn.Font = new Font("Segoe UI", 9f, isAccent ? FontStyle.Bold : FontStyle.Regular);
            btn.Cursor = Cursors.Hand;
            return btn;
        }

        private void InitializeTray()
        {
            _trayMenu = new ContextMenuStrip();
            _trayMenu.Items.Add("Open OLED Saver", null, (s, e) => RestoreFromTray());
            _trayMenu.Items.Add(new ToolStripSeparator());

            _trayActionItem = new ToolStripMenuItem("⏻ Blackout Now (Ctrl+Alt+B)");
            _trayActionItem.Click += (s, e) => TriggerBlackoutNow();
            _trayMenu.Items.Add(_trayActionItem);

            ToolStripMenuItem pauseItem = new ToolStripMenuItem("❚❚ Pause Timer");
            pauseItem.Click += (s, e) => TogglePause();
            _trayMenu.Items.Add(pauseItem);

            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add("Exit OLED Saver", null, (s, e) => ExitApplication());

            _trayIcon = new NotifyIcon();
            _trayIcon.Text = "OLED Blackout Saver - Active";
            _trayIcon.Visible = true;
            _trayIcon.ContextMenuStrip = _trayMenu;
            _trayIcon.DoubleClick += (s, e) => RestoreFromTray();

            try
            {
                if (this.Icon != null)
                {
                    _trayIcon.Icon = this.Icon;
                }
                else if (System.IO.File.Exists("app.ico"))
                {
                    _trayIcon.Icon = new Icon("app.ico");
                }
                else
                {
                    _trayIcon.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                }
            }
            catch
            {
                _trayIcon.Icon = SystemIcons.Application;
            }
        }

        private void InitializeInputAndBlackout()
        {
            _inputWatcher = new InputWatcher(this.Handle);
            _blackoutManager = new BlackoutManager(this.Handle);

            // Wake up immediately when physical keyboard input is detected
            _inputWatcher.KeyboardActivityDetected += (s, e) =>
            {
                if (_blackoutManager != null && _blackoutManager.IsBlackoutActive)
                {
                    _blackoutManager.RequestWakeUp();
                }
            };

            // Wake up immediately when physical mouse input is detected
            _inputWatcher.MouseActivityDetected += (s, e) =>
            {
                if (_blackoutManager != null && _blackoutManager.IsBlackoutActive)
                {
                    _blackoutManager.RequestWakeUp();
                }
            };

            _blackoutManager.BlackoutStarted += (s, e) =>
            {
                this.BeginInvoke((Action)(() =>
                {
                    _lblStatusBadge.Text = (_blackoutManager.IsHardwarePowerOff) ? "● SCREENS OFF" : "● BLACKOUT";
                    _lblStatusBadge.ForeColor = ColAccent;
                    _lblStatusBadge.BackColor = Color.FromArgb(16, 45, 38);

                    // Auto-hide MainForm while overlay is active so the primary screen is 100% black
                    if (!_blackoutManager.IsHardwarePowerOff && this.Visible && this.WindowState != FormWindowState.Minimized)
                    {
                        _wasVisibleBeforeBlackout = true;
                        this.Hide();
                    }
                    else
                    {
                        _wasVisibleBeforeBlackout = false;
                    }
                }));
            };

            _blackoutManager.BlackoutEnded += (s, e) =>
            {
                _lastWakeTick = Environment.TickCount;
                if (_inputWatcher != null)
                {
                    _inputWatcher.ResetTimer();
                }
                this.BeginInvoke((Action)(() =>
                {
                    UpdateStatusBadge();

                    // Restore MainForm if it was open before blackout
                    if (_wasVisibleBeforeBlackout)
                    {
                        this.Show();
                        this.WindowState = FormWindowState.Normal;
                        this.BringToFront();
                        this.Activate();
                        _wasVisibleBeforeBlackout = false;
                    }
                }));
            };

            _teamsWatcher = new TeamsWatcher();
            _teamsWatcher.StatusChanged += (s, e) =>
            {
                try
                {
                    this.BeginInvoke((Action)(() => UpdateTeamsUI()));
                }
                catch
                {
                }
            };

            UpdateHotkey();
        }

        private void LoadDisplays()
        {
            _displays = DisplayHelper.GetConnectedDisplays(_settings.SelectedDisplayIds);
            _clbDisplays.Items.Clear();

            for (int i = 0; i < _displays.Count; i++)
            {
                DisplayInfo d = _displays[i];
                _clbDisplays.Items.Add(d.DisplayText, d.IsSelected);
            }
        }

        private void ClbDisplays_ItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (e.Index >= 0 && e.Index < _displays.Count)
            {
                _displays[e.Index].IsSelected = (e.NewValue == CheckState.Checked);
                SaveSelectedDisplays();
            }
        }

        private void SetAllDisplaysChecked(bool checkedState)
        {
            for (int i = 0; i < _clbDisplays.Items.Count; i++)
            {
                _clbDisplays.SetItemChecked(i, checkedState);
                if (i < _displays.Count) _displays[i].IsSelected = checkedState;
            }
            SaveSelectedDisplays();
        }

        private void SelectDisplaysByFilter(bool primaryOnly)
        {
            for (int i = 0; i < _displays.Count; i++)
            {
                bool match = primaryOnly ? _displays[i].IsPrimary : !_displays[i].IsPrimary;
                _clbDisplays.SetItemChecked(i, match);
                _displays[i].IsSelected = match;
            }
            SaveSelectedDisplays();
        }

        private void SaveSelectedDisplays()
        {
            _settings.SelectedDisplayIds.Clear();
            foreach (DisplayInfo d in _displays)
            {
                if (d.IsSelected)
                {
                    _settings.SelectedDisplayIds.Add(d.DeviceId);
                }
            }
            _settings.Save();
        }

        private void ScreenActionMode_CheckedChanged(object sender, EventArgs e)
        {
            if (_rbActionPowerOff.Checked)
            {
                _settings.ScreenAction = "HardwarePowerOff";
            }
            else
            {
                _settings.ScreenAction = "BlackoutOverlay";
            }
            _settings.Save();
            UpdateActionUI();
        }

        private void UpdateActionUI()
        {
            bool isPowerOff = (_settings.ScreenAction == "HardwarePowerOff");

            if (isPowerOff)
            {
                _lblActionHint.Text = "⚡ Hardware Screen Power Off puts all connected monitors into DPMS standby (No Signal). Move mouse or tap any key to wake.";
                _lblActionHint.ForeColor = ColWarning;
                _btnBlackoutNow.Text = "⚡ Turn Off Screens Now";
                if (_trayActionItem != null) _trayActionItem.Text = "⚡ Turn Off Screens Now (Ctrl+Alt+B)";
                _lblDisplaysTitle.Text = "TARGET DISPLAYS (NOT APPLICABLE — HARDWARE STANDBY TURNS OFF ALL DISPLAYS)";
                _lblDisplaysTitle.ForeColor = Color.FromArgb(125, 130, 145);

                // Disable display selection since hardware standby inherently affects all screens
                _clbDisplays.Enabled = false;
                _clbDisplays.BackColor = Color.FromArgb(18, 22, 30);
                _clbDisplays.ForeColor = Color.FromArgb(100, 105, 115);

                _btnSelectAllDisplays.Enabled = false;
                _btnSelectExternalsOnly.Enabled = false;
                _btnSelectPrimaryOnly.Enabled = false;
                _btnRefreshDisplays.Enabled = false;

                _btnSelectAllDisplays.ForeColor = Color.FromArgb(90, 95, 105);
                _btnSelectExternalsOnly.ForeColor = Color.FromArgb(90, 95, 105);
                _btnSelectPrimaryOnly.ForeColor = Color.FromArgb(90, 95, 105);
                _btnRefreshDisplays.ForeColor = Color.FromArgb(90, 95, 105);
            }
            else
            {
                _lblActionHint.Text = "🌟 Pure OLED Blackout Overlay shuts off OLED pixels with 0ms resume on selected monitors.";
                _lblActionHint.ForeColor = ColAccent;
                _btnBlackoutNow.Text = "⏻ Blackout Now";
                if (_trayActionItem != null) _trayActionItem.Text = "⏻ Blackout Now (Ctrl+Alt+B)";
                _lblDisplaysTitle.Text = "TARGET DISPLAYS TO BLACKOUT (OVERLAY MODE)";
                _lblDisplaysTitle.ForeColor = ColTextSecondary;

                // Re-enable display selection for overlay mode
                _clbDisplays.Enabled = true;
                _clbDisplays.BackColor = ColButtonDark;
                _clbDisplays.ForeColor = ColTextPrimary;

                _btnSelectAllDisplays.Enabled = true;
                _btnSelectExternalsOnly.Enabled = true;
                _btnSelectPrimaryOnly.Enabled = true;
                _btnRefreshDisplays.Enabled = true;

                _btnSelectAllDisplays.ForeColor = ColTextPrimary;
                _btnSelectExternalsOnly.ForeColor = ColTextPrimary;
                _btnSelectPrimaryOnly.ForeColor = ColTextPrimary;
                _btnRefreshDisplays.ForeColor = ColTextSecondary;
            }
        }

        private void ApplySettingsToUI()
        {
            _numMinutes.Value = Math.Max(1, Math.Min(180, _settings.TimeoutMinutes));
            _rbKeyboardOnly.Checked = (_settings.IdleMode == "KeyboardOnly");
            _rbKeyboardAndMouse.Checked = (_settings.IdleMode != "KeyboardOnly");
            _rbActionPowerOff.Checked = (_settings.ScreenAction == "HardwarePowerOff");
            _rbActionBlackout.Checked = (_settings.ScreenAction != "HardwarePowerOff");
            _chkStartWithWindows.Checked = _settings.StartWithWindows;
            _chkMinimizeToTray.Checked = _settings.MinimizeToTrayOnClose;
            _chkHotkey.Checked = _settings.HotkeyEnabled;

            _chkTeamsEnable.Checked = _settings.TeamsDetectionEnabled;
            if (_settings.TeamsPreventStatuses == null)
            {
                _settings.TeamsPreventStatuses = new List<string> { "InACall", "Presenting", "Busy" };
            }
            _chkTeamsInCall.Checked = _settings.TeamsPreventStatuses.Contains("InACall");
            _chkTeamsPresenting.Checked = _settings.TeamsPreventStatuses.Contains("Presenting");
            _chkTeamsBusy.Checked = _settings.TeamsPreventStatuses.Contains("Busy");
            _chkTeamsDnd.Checked = _settings.TeamsPreventStatuses.Contains("DoNotDisturb");
            _chkTeamsAway.Checked = _settings.TeamsPreventStatuses.Contains("Away");
            UpdateTeamsUI();

            UpdateActionUI();
            UpdateStatusBadge();
        }

        private void ChkTeamsEnable_CheckedChanged(object sender, EventArgs e)
        {
            _settings.TeamsDetectionEnabled = _chkTeamsEnable.Checked;
            _settings.Save();
            UpdateTeamsUI();
        }

        private void TeamsStatusCheckbox_CheckedChanged(object sender, EventArgs e)
        {
            List<string> list = new List<string>();
            if (_chkTeamsInCall.Checked) list.Add("InACall");
            if (_chkTeamsPresenting.Checked) list.Add("Presenting");
            if (_chkTeamsBusy.Checked) list.Add("Busy");
            if (_chkTeamsDnd.Checked) list.Add("DoNotDisturb");
            if (_chkTeamsAway.Checked) list.Add("Away");

            _settings.TeamsPreventStatuses = list;
            _settings.Save();
            UpdateTeamsUI();
        }

        private void UpdateTeamsUI()
        {
            if (_teamsWatcher == null || _lblTeamsCurrentStatus == null) return;

            string statusName = _teamsWatcher.StatusDisplayName;
            _lblTeamsCurrentStatus.Text = "Current Teams Status: " + statusName;

            bool isBlocking = _settings.TeamsDetectionEnabled && 
                              _settings.TeamsPreventStatuses != null && 
                              _settings.TeamsPreventStatuses.Contains(_teamsWatcher.CurrentStatus);

            if (isBlocking)
            {
                _lblTeamsCurrentStatus.ForeColor = ColAccent;
            }
            else
            {
                _lblTeamsCurrentStatus.ForeColor = Color.FromArgb(120, 180, 255);
            }

            bool enabled = _settings.TeamsDetectionEnabled;
            _chkTeamsInCall.Enabled = enabled;
            _chkTeamsPresenting.Enabled = enabled;
            _chkTeamsBusy.Enabled = enabled;
            _chkTeamsDnd.Enabled = enabled;
            _chkTeamsAway.Enabled = enabled;
        }

        private void NumMinutes_ValueChanged(object sender, EventArgs e)
        {
            _settings.TimeoutMinutes = (int)_numMinutes.Value;
            _settings.Save();
        }

        private void TriggerMode_CheckedChanged(object sender, EventArgs e)
        {
            _settings.IdleMode = _rbKeyboardOnly.Checked ? "KeyboardOnly" : "KeyboardAndMouse";
            _settings.Save();
        }

        private void CmbSensitivity_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_cmbSensitivity.SelectedIndex == 0) _settings.WakeSensitivityPixels = 4;
            else if (_cmbSensitivity.SelectedIndex == 2) _settings.WakeSensitivityPixels = 25;
            else _settings.WakeSensitivityPixels = 10;
            _settings.Save();
        }

        private void ChkHotkey_CheckedChanged(object sender, EventArgs e)
        {
            _settings.HotkeyEnabled = _chkHotkey.Checked;
            _settings.Save();
            UpdateHotkey();
        }

        private void UpdateHotkey()
        {
            if (_settings.HotkeyEnabled && !_isHotKeyRegistered)
            {
                _isHotKeyRegistered = RegisterHotKey(this.Handle, HOTKEY_ID, MOD_CONTROL | MOD_ALT, (int)Keys.B);
            }
            else if (!_settings.HotkeyEnabled && _isHotKeyRegistered)
            {
                UnregisterHotKey(this.Handle, HOTKEY_ID);
                _isHotKeyRegistered = false;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID)
            {
                TriggerBlackoutNow();
            }

            if (_inputWatcher != null)
            {
                _inputWatcher.ProcessWndProc(ref m);
            }

            base.WndProc(ref m);
        }

        private void UIUpdateTimer_Tick(object sender, EventArgs e)
        {
            if (_blackoutManager.IsBlackoutActive)
            {
                _lblCountdown.Text = "00:00";
                _progressBar.Value = _progressBar.Maximum;
                _lblIdleDetails.Text = (_settings.ScreenAction == "HardwarePowerOff")
                    ? "Screens are in hardware standby (No Signal). Move mouse or press any key to resume."
                    : "Selected screens are pitch black. Move mouse or press any key to resume.";
                return;
            }

            // Cooldown guard: Never re-trigger blackout within 5 seconds of waking up!
            long msSinceWake = Environment.TickCount - _lastWakeTick;
            if (msSinceWake >= 0 && msSinceWake < 5000)
            {
                if (_inputWatcher != null) _inputWatcher.ResetTimer();
                return;
            }

            if (_isPaused)
            {
                _lblCountdown.Text = "PAUSED";
                _lblCountdown.ForeColor = ColWarning;
                _progressBar.Value = 0;
                _lblIdleDetails.Text = "Timer is paused. Click Resume to restart OLED saver.";
                return;
            }

            // Check if Microsoft Teams status prevents the screensaver from triggering
            if (_settings.TeamsDetectionEnabled && _teamsWatcher != null)
            {
                string status = _teamsWatcher.CurrentStatus;
                if (_settings.TeamsPreventStatuses != null && _settings.TeamsPreventStatuses.Contains(status))
                {
                    // Reset timer so it never counts down while Teams is in this status
                    if (_inputWatcher != null)
                    {
                        _inputWatcher.ResetTimer();
                    }

                    _lblCountdown.Text = "PAUSED";
                    _lblCountdown.ForeColor = Color.FromArgb(70, 160, 245);
                    _progressBar.Value = 0;
                    _lblIdleDetails.Text = string.Format("⏸ Paused by Microsoft Teams ({0})", _teamsWatcher.StatusDisplayName);

                    string badgeText = "⏸ TEAMS " + status.ToUpper();
                    if (badgeText.Length > 16) badgeText = "⏸ TEAMS ACTIVE";
                    _lblStatusBadge.Text = badgeText;
                    _lblStatusBadge.ForeColor = Color.FromArgb(120, 180, 255);
                    _lblStatusBadge.BackColor = Color.FromArgb(20, 35, 60);

                    string teamsTrayText = string.Format("OLED Saver - Paused ({0})", _teamsWatcher.StatusDisplayName);
                    if (teamsTrayText.Length > 63) teamsTrayText = teamsTrayText.Substring(0, 63);
                    _trayIcon.Text = teamsTrayText;
                    return;
                }
            }

            TimeSpan idle = (_settings.IdleMode == "KeyboardOnly")
                ? _inputWatcher.GetKeyboardIdleTime()
                : _inputWatcher.GetSystemInputIdleTime();

            TimeSpan target = TimeSpan.FromMinutes(_settings.TimeoutMinutes);
            TimeSpan remaining = target - idle;

            if (remaining.TotalMilliseconds <= 0)
            {
                TriggerBlackoutNow();
                return;
            }

            int remMin = (int)remaining.TotalMinutes;
            int remSec = remaining.Seconds;
            _lblCountdown.Text = string.Format("{0:D2}:{1:D2}", remMin, remSec);
            _lblCountdown.ForeColor = ColAccent;

            int idleMin = (int)idle.TotalMinutes;
            int idleSec = idle.Seconds;
            _lblIdleDetails.Text = string.Format("Idle: {0:D2}:{1:D2} • Target: {1} min ({2})",
                idleMin, _settings.TimeoutMinutes, _settings.IdleMode == "KeyboardOnly" ? "No typing" : "No input");

            double pct = idle.TotalMilliseconds / target.TotalMilliseconds;
            if (pct < 0) pct = 0;
            if (pct > 1) pct = 1;
            _progressBar.Value = (int)(pct * _progressBar.Maximum);

            string actionLabel = (_settings.ScreenAction == "HardwarePowerOff") ? "Off" : "Blank";
            string trayText = string.Format("OLED Saver - {0} in {1:D2}:{2:D2}", actionLabel, remMin, remSec);
            if (trayText.Length > 63) trayText = trayText.Substring(0, 63);
            _trayIcon.Text = trayText;
        }

        public void TriggerBlackoutNow()
        {
            if (_blackoutManager.IsBlackoutActive) return;

            // Never re-trigger within 3 seconds of waking up
            long msSinceWake = Environment.TickCount - _lastWakeTick;
            if (msSinceWake >= 0 && msSinceWake < 3000) return;

            // Immediately reset idle timer so it never loops
            if (_inputWatcher != null)
            {
                _inputWatcher.ResetTimer();
            }

            List<Rectangle> targetBounds = new List<Rectangle>();

            // Query live Screen.AllScreens directly to ensure 100% exact hardware coordinates
            foreach (Screen s in Screen.AllScreens)
            {
                bool isSelected = false;
                foreach (DisplayInfo d in _displays)
                {
                    if (d.DeviceName == s.DeviceName && d.IsSelected)
                    {
                        isSelected = true;
                        break;
                    }
                }

                if (isSelected)
                {
                    targetBounds.Add(s.Bounds);
                }
            }

            if (targetBounds.Count == 0)
            {
                foreach (Screen s in Screen.AllScreens)
                {
                    targetBounds.Add(s.Bounds);
                }
            }

            _blackoutManager.StartAction(_settings.ScreenAction, targetBounds, _settings.WakeSensitivityPixels);
        }

        private void TogglePause()
        {
            _isPaused = !_isPaused;
            if (_isPaused)
            {
                _btnPauseResume.Text = "▶ Resume Timer";
                _btnPauseResume.BackColor = Color.FromArgb(70, 50, 15);
                _btnPauseResume.ForeColor = ColWarning;
            }
            else
            {
                _btnPauseResume.Text = "❚❚ Pause Timer";
                _btnPauseResume.BackColor = ColButtonDark;
                _btnPauseResume.ForeColor = ColTextPrimary;
                if (_inputWatcher != null) _inputWatcher.ResetTimer();
            }
            UpdateStatusBadge();
        }

        private void BtnPauseResume_Click(object sender, EventArgs e)
        {
            TogglePause();
        }

        private void UpdateStatusBadge()
        {
            if (_isPaused)
            {
                _lblStatusBadge.Text = "❚❚ PAUSED";
                _lblStatusBadge.ForeColor = ColWarning;
                _lblStatusBadge.BackColor = Color.FromArgb(48, 36, 12);
            }
            else if (_blackoutManager != null && _blackoutManager.IsBlackoutActive)
            {
                _lblStatusBadge.Text = (_blackoutManager.IsHardwarePowerOff) ? "● SCREENS OFF" : "● BLACKOUT";
                _lblStatusBadge.ForeColor = ColAccent;
                _lblStatusBadge.BackColor = Color.FromArgb(16, 45, 38);
            }
            else
            {
                _lblStatusBadge.Text = "● ACTIVE";
                _lblStatusBadge.ForeColor = ColAccent;
                _lblStatusBadge.BackColor = Color.FromArgb(16, 45, 38);
            }
        }

        protected override void SetVisibleCore(bool value)
        {
            if (_initialMinimized)
            {
                value = false;
                if (!this.IsHandleCreated) CreateHandle();
            }
            base.SetVisibleCore(value);
        }

        private void MainForm_Resize(object sender, EventArgs e)
        {
            if (this.WindowState == FormWindowState.Minimized && _settings.MinimizeToTrayOnClose)
            {
                this.Hide();
                this.ShowInTaskbar = false;
            }
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && _settings.MinimizeToTrayOnClose)
            {
                e.Cancel = true;
                this.Hide();
                this.ShowInTaskbar = false;
                _trayIcon.ShowBalloonTip(1500, "OLED Blackout Saver", "Running silently in background. Double-click tray icon to open.", ToolTipIcon.Info);
            }
        }

        private void RestoreFromTray()
        {
            _initialMinimized = false;
            this.Show();
            this.ShowInTaskbar = true;
            this.WindowState = FormWindowState.Normal;
            this.BringToFront();
            this.Activate();
        }

        private void ExitApplication()
        {
            _settings.MinimizeToTrayOnClose = false;
            if (_isHotKeyRegistered)
            {
                UnregisterHotKey(this.Handle, HOTKEY_ID);
                _isHotKeyRegistered = false;
            }
            if (_blackoutManager != null)
            {
                _blackoutManager.Dispose();
            }
            if (_inputWatcher != null)
            {
                _inputWatcher.Dispose();
            }
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }
            Application.Exit();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_isHotKeyRegistered)
                {
                    UnregisterHotKey(this.Handle, HOTKEY_ID);
                }
                if (_uiUpdateTimer != null) _uiUpdateTimer.Dispose();
                if (_inputWatcher != null) _inputWatcher.Dispose();
                if (_blackoutManager != null) _blackoutManager.Dispose();
                if (_teamsWatcher != null) _teamsWatcher.Dispose();
                if (_trayIcon != null) _trayIcon.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
