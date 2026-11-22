using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace SesKalkani
{
    static class C
    {
        public static readonly Color Bg = Color.FromArgb(32, 33, 37);
        public static readonly Color Card = Color.FromArgb(43, 45, 49);
        public static readonly Color Field = Color.FromArgb(30, 31, 34);
        public static readonly Color Text = Color.FromArgb(230, 232, 235);
        public static readonly Color Dim = Color.FromArgb(148, 155, 164);
        public static readonly Color Accent = Color.FromArgb(88, 101, 242);
        public static readonly Color Green = Color.FromArgb(35, 165, 90);
        public static readonly Color Orange = Color.FromArgb(240, 150, 50);
        public static readonly Color Red = Color.FromArgb(218, 55, 60);
        public static readonly Color Gray = Color.FromArgb(78, 80, 88);
        public static readonly Color Link = Color.FromArgb(0, 168, 252);

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath(); float d = rad * 2;
            if (d > r.Height) d = r.Height;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    // ---------------------------------------------------------------- Yuvarlak köşeli kart
    class Card : Panel
    {
        public float Radius = 10;
        public Card() { BackColor = C.Card; }
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            using (var p = C.Round(new RectangleF(0, 0, Width, Height), Radius)) Region = new Region(p);
        }
    }

    // ---------------------------------------------------------------- Aç/kapa anahtarı
    class Toggle : Control
    {
        bool on;
        public event EventHandler CheckedChanged;
        public bool Checked { get { return on; } set { if (on == value) return; on = value; Invalidate(); if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty); } }
        public Toggle()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            Cursor = Cursors.Hand;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : C.Bg);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var p = C.Round(r, r.Height / 2)) using (var b = new SolidBrush(on ? (Enabled ? C.Green : C.Gray) : C.Gray)) g.FillPath(b, p);
            float d = Height - 6, x = on ? Width - d - 3.5f : 3f;
            g.FillEllipse(Brushes.White, x, 2.5f, d, d);
        }
        protected override void OnClick(EventArgs e) { base.OnClick(e); Checked = !Checked; }
    }

    // ---------------------------------------------------------------- Kaydırıcı
    class Slider : Control
    {
        public int Min = 0, Max = 10; int val;
        public event EventHandler ValueChanged;
        public int Value { get { return val; } set { value = Math.Max(Min, Math.Min(Max, value)); if (val == value) return; val = value; Invalidate(); if (ValueChanged != null) ValueChanged(this, EventArgs.Empty); } }
        public Slider()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Cursor = Cursors.Hand;
        }
        float K { get { return Height / 2f; } }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : C.Bg);
            float cy = Height / 2f, x0 = K, x1 = Width - K, t = (val - Min) / (float)(Max - Min), x = x0 + t * (x1 - x0);
            float th = Math.Max(4, Height / 5f);
            using (var p = C.Round(new RectangleF(x0, cy - th / 2, x1 - x0, th), th / 2)) using (var b = new SolidBrush(C.Field)) g.FillPath(b, p);
            using (var p = C.Round(new RectangleF(x0, cy - th / 2, Math.Max(th, x - x0), th), th / 2)) using (var b = new SolidBrush(Enabled ? C.Accent : C.Gray)) g.FillPath(b, p);
            float d = Height - 4;
            g.FillEllipse(Enabled ? Brushes.White : Brushes.Gray, x - d / 2, 2, d, d);
        }
        void Set(int mx) { float t = (mx - K) / (Width - 2 * K); Value = (int)Math.Round(Min + t * (Max - Min)); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) Set(e.X); }
        protected override void OnMouseMove(MouseEventArgs e) { if (e.Button == MouseButtons.Left) Set(e.X); }
    }

    // ---------------------------------------------------------------- Seviye göstergesi (sürükleyerek eşik ayarlanır)
    class Meter : Control
    {
        public float Level = -100, Partner = -200, Threshold = -45; public int State; public bool Active;
        public event Action<float> ThresholdDragged;
        const float Min = -80, Max = 0;

        public Meter()
        {
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Cursor = Cursors.SizeWE;
        }

        float BarH { get { return Height * 0.55f; } }
        float Px(float db) { db = Math.Max(Min, Math.Min(Max, db)); return 2 + (db - Min) / (Max - Min) * (Width - 4); }
        float Db(int x) { return Min + (x - 2f) / (Width - 4) * (Max - Min); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : C.Bg);
            float bh = BarH, r = bh / 4;
            var bar = new RectangleF(0, 0, Width - 1, bh);
            using (var p = C.Round(bar, r)) using (var b = new SolidBrush(C.Field)) g.FillPath(b, p);

            if (Active && Level > Min)
            {
                Color fc = State == 1 ? C.Green : State == 2 ? C.Orange : C.Gray;
                float w = Px(Level) - 3;
                if (w > 4) using (var p = C.Round(new RectangleF(3, 3, w, bh - 6), r * 0.7f)) using (var b = new SolidBrush(fc)) g.FillPath(b, p);
            }
            if (Active && Partner > -150)
            {
                float px = Px(Partner);
                using (var p = new Pen(C.Orange, 2) { DashStyle = DashStyle.Dot }) g.DrawLine(p, px, 3, px, bh - 3);
            }

            float tx = Px(Threshold);
            using (var p = new Pen(Color.White, 2.5f)) g.DrawLine(p, tx, 0, tx, bh);
            float ts = bh * 0.3f;
            g.FillPolygon(Brushes.White, new[] { new PointF(tx - ts, bh + ts * 1.2f), new PointF(tx + ts, bh + ts * 1.2f), new PointF(tx, bh + 1) });

            using (var f = new Font("Segoe UI", 7.5f))
            using (var b = new SolidBrush(C.Dim))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center })
            {
                foreach (int d in new[] { -60, -40, -20 })
                {
                    float x = Px(d);
                    if (Math.Abs(x - tx) < Width * 0.1f) continue;
                    g.DrawString(d + " dB", f, b, new RectangleF(x - 40, bh + 4, 80, Height - bh - 4), sf);
                }
            }
        }

        void Drag(int x) { Threshold = (float)Math.Round(Math.Max(-75, Math.Min(-5, Db(x)))); if (ThresholdDragged != null) ThresholdDragged(Threshold); Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) Drag(e.X); }
        protected override void OnMouseMove(MouseEventArgs e) { if (e.Button == MouseButtons.Left) Drag(e.X); }
    }

    // ---------------------------------------------------------------- Ayarlar (%APPDATA%\SesKalkani\ayarlar.ini)
    class Settings
    {
        public string In = "", Out = ""; public float Thr = -45, Margin = 8; public bool Partner = true, AutoStart = false, WinStart = true;
        static string FilePath { get { return Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SesKalkani"), "ayarlar.ini"); } }

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                var ci = CultureInfo.InvariantCulture;
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    int i = line.IndexOf('='); if (i < 0) continue;
                    string k = line.Substring(0, i), v = line.Substring(i + 1);
                    switch (k)
                    {
                        case "in": s.In = v; break;
                        case "out": s.Out = v; break;
                        case "thr": s.Thr = float.Parse(v, ci); break;
                        case "margin": s.Margin = float.Parse(v, ci); break;
                        case "partner": s.Partner = v == "1"; break;
                        case "autostart": s.AutoStart = v == "1"; break;
                        case "winstart": s.WinStart = v == "1"; break;
                    }
                }
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                var ci = CultureInfo.InvariantCulture;
                File.WriteAllLines(FilePath, new[] {
                    "in=" + In, "out=" + Out, "thr=" + Thr.ToString(ci), "margin=" + Margin.ToString(ci),
                    "partner=" + (Partner ? "1" : "0"), "autostart=" + (AutoStart ? "1" : "0"), "winstart=" + (WinStart ? "1" : "0") });
            }
            catch { }
        }
    }

    // ---------------------------------------------------------------- Windows ile başlat (HKCU\...\Run)
    static class WinStartup
    {
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run", Name = "SesKalkani";
        public static void Apply(bool on)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(Key))
                {
                    if (on) k.SetValue(Name, "\"" + Application.ExecutablePath + "\" --arka");
                    else if (k.GetValue(Name) != null) k.DeleteValue(Name, false);
                }
            }
            catch { }
        }
    }

    // ---------------------------------------------------------------- Ana pencere
    class MainForm : Form
    {
        readonly AudioEngine eng = new AudioEngine();
        readonly Settings st = Settings.Load();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 33 };

        ComboBox cbIn, cbOut; Button btnStart, btnCal; Meter meter;
        Label lblState, lblThr, lblHint, lblPartner, lblMarginVal, lblCal;
        Label lnkVb; Toggle tgPartner, tgAuto, tgWin; NotifyIcon tray; MenuItem miToggle;
        bool startHidden, exiting, trayTipShown; Slider slMargin;
        float shown = -100, k = 1;
        int calStep; float calBleed; DateTime calEnd;
        const string CalHint = "Beyaz çizgiyi sürükle: solunda kalan sesler Discord'a gitmez.";

        // Yazı tipleri (punto cinsinden; Windows ekran ölçeğine göre kendisi büyür)
        readonly Font fTitle = new Font("Segoe UI Semibold", 15f);
        readonly Font fSub = new Font("Segoe UI", 9f);
        readonly Font fCap = new Font("Segoe UI Semibold", 8f);
        readonly Font fBody = new Font("Segoe UI", 9.5f);
        readonly Font fBold = new Font("Segoe UI Semibold", 10.5f);
        readonly Font fSmall = new Font("Segoe UI", 8.5f);
        readonly Font fBtn = new Font("Segoe UI Semibold", 11.5f);

        int S(float v) { return (int)Math.Round(v * k); }
        Rectangle R(float x, float y, float w, float h) { return new Rectangle(S(x), S(y), S(w), S(h)); }

        public MainForm()
        {
            AutoScaleMode = AutoScaleMode.None;
            using (var g = CreateGraphics()) k = g.DpiX / 96f;

            Text = "Ses Kalkanı " + VersionText(CurrentVersion);
            FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false;
            BackColor = C.Bg; ForeColor = C.Text; Font = fBody;
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = MakeIcon(); } catch { }

            const int M = 20, W = 420, P = 16, IW = W - 2 * P; // kenar boşluğu, kart genişliği, iç boşluk

            // ---- Başlık
            Lbl(this, "Ses Kalkanı", fTitle, C.Text, M, 14, 260, 32);
            Lbl(this, "Discord'a sadece senin sesin gitsin.", fSub, C.Dim, M + 1, 46, 250, 18);
            lblUpdate = LinkLbl(this, "", M + W - 170, 46, 170, 18);
            lblUpdate.ForeColor = C.Green; lblUpdate.Visible = false;
            lblUpdate.Click += (s, e) => OpenUpdate();
            var help = LinkLbl(this, "Yardım", M + W - 80, 22, 80, 18);
            help.Click += (s, e) => OpenHelp();

            // ---- Kart 1: cihazlar
            var c1 = AddCard(M, 76, W, 148);
            Lbl(c1, "MİKROFONUN", fCap, C.Dim, P, 14, IW, 16);
            cbIn = Combo(c1, P, 32, IW);
            Lbl(c1, "SANAL MİKROFON  →  DISCORD", fCap, C.Dim, P, 72, IW, 16);
            cbOut = Combo(c1, P, 90, IW);
            lblHint = Lbl(c1, "", fSmall, C.Dim, P, 122, IW, 18);
            lnkVb = LinkLbl(c1, "VB-Cable'ı indir", P + IW - 110, 122, 110, 18);
            lnkVb.Visible = false; lnkVb.BringToFront();
            lnkVb.Click += (s, e) => { try { Process.Start("https://vb-audio.com/Cable/"); } catch { } };

            // ---- Başlat düğmesi
            btnStart = Btn(this, "BAŞLAT", fBtn, C.Accent, M, 238, W, 46);
            btnStart.Click += (s, e) => Toggle();

            // ---- Kart 2: durum + eşik
            var c2 = AddCard(M, 298, W, 172);
            lblState = Lbl(c2, "", fBold, C.Dim, P, 14, IW, 22);
            meter = new Meter { Bounds = R(P, 44, IW, 44), Threshold = st.Thr };
            meter.ThresholdDragged += SetThreshold;
            c2.Controls.Add(meter);
            lblThr = Lbl(c2, "", fBody, C.Text, P, 100, 200, 30);
            lblThr.TextAlign = ContentAlignment.MiddleLeft;
            btnCal = Btn(c2, "Otomatik ayarla", fBody, C.Gray, P + IW - 150, 100, 150, 30);
            btnCal.Click += (s, e) => StartCalibration();
            lblCal = Lbl(c2, CalHint, fSmall, C.Dim, P, 136, IW, 32);

            // ---- Kart 3: kardeş modu
            var c3 = AddCard(M, 484, W, 132);
            tgPartner = new Toggle { Bounds = R(P, 15, 40, 22) };
            c3.Controls.Add(tgPartner);
            Lbl(c3, "Kardeş modu", fBold, C.Text, P + 50, 14, 200, 24).TextAlign = ContentAlignment.MiddleLeft;
            lblPartner = Lbl(c3, "", fSmall, C.Dim, P, 46, IW, 18);
            Lbl(c3, "Hassasiyet farkı", fSmall, C.Dim, P, 74, 200, 18);
            lblMarginVal = Lbl(c3, "", fSmall, C.Text, P + IW - 80, 74, 80, 18);
            lblMarginVal.TextAlign = ContentAlignment.MiddleRight;
            slMargin = new Slider { Min = 3, Max = 20, Bounds = R(P, 96, IW, 22) };
            slMargin.Value = Math.Max(3, Math.Min(20, (int)st.Margin));
            c3.Controls.Add(slMargin);

            // ---- Alt: başlangıç seçenekleri
            tgWin = new Toggle { Bounds = R(M, 632, 34, 18) };
            Controls.Add(tgWin);
            Lbl(this, "Windows açılınca başlat", fSmall, C.Dim, M + 42, 631, 300, 20).TextAlign = ContentAlignment.MiddleLeft;
            tgAuto = new Toggle { Bounds = R(M, 660, 34, 18) };
            Controls.Add(tgAuto);
            Lbl(this, "Program açılınca dinlemeyi hemen başlat", fSmall, C.Dim, M + 42, 659, 300, 20).TextAlign = ContentAlignment.MiddleLeft;

            ClientSize = new Size(S(M * 2 + W), S(696));

            // ---- Değerler ve olaylar
            tgPartner.Checked = st.Partner; tgAuto.Checked = st.AutoStart; tgWin.Checked = st.WinStart;
            WinStartup.Apply(st.WinStart); // exe taşındıysa yolu da günceller
            tgWin.CheckedChanged += (s, e) => { st.WinStart = tgWin.Checked; WinStartup.Apply(st.WinStart); st.Save(); };
            tgPartner.CheckedChanged += (s, e) => { st.Partner = tgPartner.Checked; ApplyPartner(); st.Save(); };
            tgAuto.CheckedChanged += (s, e) => { st.AutoStart = tgAuto.Checked; st.Save(); };
            slMargin.ValueChanged += (s, e) => { st.Margin = slMargin.Value; eng.Gate.MarginDb = st.Margin; UpdateLabels(); st.Save(); };

            eng.Gate.ThresholdDb = st.Thr; eng.Gate.MarginDb = st.Margin;
            LoadDevices();
            ApplyPartner();
            UpdateLabels();

            ActiveControl = btnStart;
            timer.Tick += (s, e) => Tick();
            timer.Start();
            SetupTray();
            HandleCreated += (s, e) => CheckUpdateAsync();
            updTimer.Tick += (s, e) => CheckUpdateAsync();
            updTimer.Start();
            Shown += (s, e) => { if (st.AutoStart) TryAutoStart(false); };
            FormClosing += OnClosing;
        }

        // ---- Bildirim alanı (saatin yanı)
        void SetupTray()
        {
            miToggle = new MenuItem("Başlat", (s, e) => Toggle());
            miUpdate = new MenuItem("Yeni sürümü indir", (s, e) => OpenUpdate()) { Visible = false };
            var menu = new ContextMenu(new[] {
                new MenuItem("Ses Kalkanı'nı göster", (s, e) => ShowWindow()) { DefaultItem = true },
                miToggle,
                miUpdate,
                new MenuItem("-"),
                new MenuItem("Çıkış", (s, e) => ExitApp()) });
            menu.Popup += (s, e) => miToggle.Text = eng.Running ? "Durdur" : "Başlat";
            tray = new NotifyIcon { Icon = Icon, Text = "Ses Kalkanı", ContextMenu = menu, Visible = true };
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowWindow(); };
            tray.BalloonTipClicked += (s, e) => { if (balloonIsUpdate) OpenUpdate(); };
            tray.BalloonTipClosed += (s, e) => balloonIsUpdate = false;
        }

        // ---- Güncelleme kontrolü: sadece haber verir, kendisi bir şey indirmez
        const string RepoUrl = "https://github.com/Mehmetsndl/ses-kalkani";
        const string LatestApi = "https://api.github.com/repos/Mehmetsndl/ses-kalkani/releases/latest";
        readonly System.Windows.Forms.Timer updTimer = new System.Windows.Forms.Timer { Interval = 6 * 60 * 60 * 1000 };
        Label lblUpdate; MenuItem miUpdate;
        string updateUrl = RepoUrl + "/releases/latest", notifiedVersion;
        bool balloonIsUpdate;

        static Version CurrentVersion { get { return Assembly.GetExecutingAssembly().GetName().Version; } }
        static Version Norm(Version v) { return new Version(v.Major, v.Minor, Math.Max(0, v.Build)); }
        static string VersionText(Version v) { return Norm(v).ToString(3); }

        void CheckUpdateAsync()
        {
            var t = new Thread(() =>
            {
                try
                {
                    ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
                    string json;
                    using (var wc = new WebClient())
                    {
                        wc.Encoding = Encoding.UTF8;
                        wc.Headers[HttpRequestHeader.UserAgent] = "SesKalkani/" + VersionText(CurrentVersion);
                        wc.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
                        json = wc.DownloadString(LatestApi);
                    }
                    var m = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"v?([0-9][^\"]*)\"");
                    if (!m.Success) return;
                    Version latest;
                    if (!Version.TryParse(m.Groups[1].Value, out latest)) return;
                    if (Norm(latest) <= Norm(CurrentVersion)) return;
                    var u = Regex.Match(json, "\"html_url\"\\s*:\\s*\"([^\"]*/releases/tag/[^\"]*)\"");
                    string url = u.Success ? u.Groups[1].Value : RepoUrl + "/releases/latest";
                    BeginInvoke(new Action(() => ShowUpdate(VersionText(latest), url)));
                }
                catch { } // internet yoksa sessizce geç
            }) { IsBackground = true, Name = "GuncellemeKontrol" };
            t.Start();
        }

        void ShowUpdate(string ver, string url)
        {
            updateUrl = url;
            lblUpdate.Text = "↑ Yeni sürüm mevcut: v" + ver;
            lblUpdate.Visible = true;
            miUpdate.Text = "Yeni sürümü indir (v" + ver + ")";
            miUpdate.Visible = true;
            if (notifiedVersion != ver)
            {
                notifiedVersion = ver;
                balloonIsUpdate = true;
                try { tray.ShowBalloonTip(8000, "Ses Kalkanı: yeni sürüm mevcut", "v" + ver + " yayınlandı. İndirme sayfasını açmak için tıkla.", ToolTipIcon.Info); } catch { }
            }
        }

        void OpenUpdate() { try { Process.Start(updateUrl); } catch { } }

        public void ShowWindow()
        {
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate(); BringToFront();
        }

        void ExitApp() { exiting = true; Close(); }

        void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (!exiting && e.CloseReason == CloseReason.UserClosing)
            {
                // Çarpıya basıldı: kapanma, simge alanına küçül
                e.Cancel = true;
                Hide();
                if (!trayTipShown)
                {
                    trayTipShown = true; balloonIsUpdate = false;
                    try { tray.ShowBalloonTip(3000, "Ses Kalkanı arka planda çalışıyor", "Saatin yanındaki simgeye tıklayarak açabilirsin. Kapatmak için sağ tık → Çıkış.", ToolTipIcon.Info); } catch { }
                }
                return;
            }
            timer.Stop(); updTimer.Stop(); eng.Stop(); eng.Link.Stop(); st.Save();
            tray.Visible = false; tray.Dispose();
        }

        // Windows ile açılınca pencere gösterilmez, doğrudan simge alanında başlar
        public void StartHidden() { startHidden = true; }
        protected override void SetVisibleCore(bool value)
        {
            if (startHidden)
            {
                startHidden = false;
                if (!IsHandleCreated) CreateHandle();
                base.SetVisibleCore(false);
                BeginInvoke(new Action(() => TryAutoStart(true)));
                return;
            }
            base.SetVisibleCore(value);
        }

        // Windows yeni açılmışken ses cihazları hazır olmayabilir: sessizce birkaç kez dener
        int autoTries;
        void TryAutoStart(bool quiet)
        {
            if (eng.Running) return;
            string err = StartEngine();
            if (err == null || !quiet) { if (err != null) MessageBox.Show(this, err, "Ses Kalkanı", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (++autoTries < 12)
            {
                var t = new System.Windows.Forms.Timer { Interval = 5000 };
                t.Tick += (s, e) => { t.Stop(); t.Dispose(); TryAutoStart(true); };
                t.Start();
            }
            else try { balloonIsUpdate = false; tray.ShowBalloonTip(5000, "Ses Kalkanı başlatılamadı", err, ToolTipIcon.Warning); } catch { }
        }

        // ---- Küçük yardımcılar
        Card AddCard(int x, int y, int w, int h) { var c = new Card { Bounds = R(x, y, w, h), Radius = S(10) }; Controls.Add(c); return c; }

        Label Lbl(Control parent, string text, Font f, Color col, float x, float y, float w, float h)
        {
            var l = new Label { Text = text, Font = f, ForeColor = col, BackColor = Color.Transparent, AutoSize = false, Bounds = R(x, y, w, h), UseMnemonic = false };
            parent.Controls.Add(l);
            return l;
        }

        Label LinkLbl(Control parent, string text, float x, float y, float w, float h)
        {
            var l = Lbl(parent, text, fSmall, C.Link, x, y, w, h);
            l.TextAlign = ContentAlignment.MiddleRight; l.Cursor = Cursors.Hand;
            l.MouseEnter += (s, e) => l.Font = new Font(fSmall, FontStyle.Underline);
            l.MouseLeave += (s, e) => l.Font = fSmall;
            return l;
        }

        Button Btn(Control parent, string text, Font f, Color bg, float x, float y, float w, float h)
        {
            var b = new Button { Text = text, Font = f, BackColor = bg, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Bounds = R(x, y, w, h), Cursor = Cursors.Hand, UseMnemonic = false };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = ControlPaint.Light(bg, 0.15f);
            parent.Controls.Add(b);
            return b;
        }

        ComboBox Combo(Control parent, float x, float y, float w)
        {
            var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Font = fBody, Location = new Point(S(x), S(y)), Width = S(w), FlatStyle = FlatStyle.Flat, BackColor = C.Field, ForeColor = C.Text };
            c.SelectedIndexChanged += (s, e) => DevicesChanged();
            parent.Controls.Add(c);
            return c;
        }

        void OpenHelp()
        {
            try
            {
                string p = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "KURULUM.txt");
                if (File.Exists(p)) { Process.Start(p); return; }
            }
            catch { }
            MessageBox.Show(this,
                "1) VB-Cable kur ve bilgisayarı yeniden başlat.\n" +
                "2) Mikrofonun: kulaklığın. Sanal mikrofon: CABLE Input.\n" +
                "3) BAŞLAT'a bas, sonra Otomatik ayarla.\n" +
                "4) Discord → Ses ve Görüntü → Giriş Cihazı: CABLE Output.\n" +
                "5) Kardeşinde de aynısını yap, Kardeş modu ikinizde de açık olsun.", "Ses Kalkanı — Yardım");
        }

        // ---- Cihazlar
        void LoadDevices()
        {
            cbIn.Items.Clear(); cbOut.Items.Clear();
            foreach (var d in Devices.Inputs()) cbIn.Items.Add(d);
            foreach (var d in Devices.Outputs()) cbOut.Items.Add(d);

            cbIn.SelectedIndex = cbIn.Items.Count > 0 ? 0 : -1;
            for (int i = 0; i < cbIn.Items.Count; i++)
                if (st.In != "" && ((DevItem)cbIn.Items[i]).Name == st.In) cbIn.SelectedIndex = i;

            int pick = -1;
            for (int i = 0; i < cbOut.Items.Count; i++)
            {
                string n = ((DevItem)cbOut.Items[i]).Name;
                if (st.Out != "" && n == st.Out) { pick = i; break; }
                if (pick < 0 && n.IndexOf("CABLE Input", StringComparison.OrdinalIgnoreCase) >= 0) pick = i;
            }
            if (pick < 0)
                for (int i = 0; i < cbOut.Items.Count; i++)
                    if (IsCable((DevItem)cbOut.Items[i])) { pick = i; break; }
            if (pick >= 0) cbOut.SelectedIndex = pick;
            DevicesChanged();
        }

        static bool IsCable(DevItem d) { return d != null && d.Name.IndexOf("CABLE", StringComparison.OrdinalIgnoreCase) >= 0; }

        void DevicesChanged()
        {
            if (lblHint == null) return;
            var o = cbOut.SelectedItem as DevItem; var i = cbIn.SelectedItem as DevItem;
            bool hasCable = false;
            foreach (DevItem d in cbOut.Items) if (IsCable(d)) hasCable = true;
            lnkVb.Visible = !hasCable;
            if (!hasCable) { lblHint.ForeColor = C.Red; lblHint.Text = "VB-Cable kurulu değil."; }
            else if (!IsCable(o)) { lblHint.ForeColor = C.Orange; lblHint.Text = "Buraya \"CABLE Input\" seçilmeli."; }
            else if (IsCable(i)) { lblHint.ForeColor = C.Orange; lblHint.Text = "Mikrofon olarak kulaklığını seç, CABLE'ı değil."; }
            else { lblHint.ForeColor = C.Dim; lblHint.Text = "Discord'da giriş cihazı: CABLE Output"; }
            if (o != null) st.Out = o.Name;
            if (i != null) st.In = i.Name;
        }

        // ---- Başlat / durdur
        void Toggle()
        {
            if (eng.Running)
            {
                eng.Stop();
                btnStart.Text = "BAŞLAT"; SetBtnColor(btnStart, C.Accent);
                cbIn.Enabled = cbOut.Enabled = true;
            }
            else
            {
                string err = StartEngine();
                if (err != null) MessageBox.Show(this, err, "Ses Kalkanı", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        string StartEngine()
        {
            var i = cbIn.SelectedItem as DevItem; var o = cbOut.SelectedItem as DevItem;
            if (i == null || o == null) return "Önce mikrofonu ve sanal mikrofonu (CABLE Input) seç.";
            string err = eng.Start(i.Id, o.Id, o.Name, true);
            if (err != null) return err;
            btnStart.Text = "DURDUR"; SetBtnColor(btnStart, C.Red);
            cbIn.Enabled = cbOut.Enabled = false;
            st.Save();
            return null;
        }

        static void SetBtnColor(Button b, Color c) { b.BackColor = c; b.FlatAppearance.MouseOverBackColor = ControlPaint.Light(c, 0.15f); }

        void ApplyPartner()
        {
            eng.Gate.PartnerMode = tgPartner.Checked;
            if (tgPartner.Checked) eng.Link.Start(); else eng.Link.Stop();
            slMargin.Enabled = tgPartner.Checked;
            slMargin.Invalidate();
            UpdateLabels();
        }

        void SetThreshold(float db)
        {
            st.Thr = db; eng.Gate.ThresholdDb = db; meter.Threshold = db;
            UpdateLabels(); st.Save();
        }

        void UpdateLabels()
        {
            lblThr.Text = "Eşik:  " + st.Thr.ToString("0") + " dB";
            lblMarginVal.Text = slMargin.Value + " dB";
        }

        // ---- Otomatik ayar: 1) sen sus, kardeşin konuşsun  2) sen konuş
        void StartCalibration()
        {
            if (!eng.Running) { MessageBox.Show(this, "Önce BAŞLAT'a bas.", "Ses Kalkanı"); return; }
            calStep = 1; calEnd = DateTime.Now.AddSeconds(5);
            eng.StartCalibration(5000);
            btnCal.Enabled = false;
        }

        static float Pct(List<float> l, double p)
        {
            if (l == null || l.Count == 0) return -100;
            var a = l.ToArray(); Array.Sort(a);
            return a[Math.Min(a.Length - 1, (int)(p * a.Length))];
        }

        void CalibrationTick()
        {
            int left = Math.Max(0, (int)Math.Ceiling((calEnd - DateTime.Now).TotalSeconds));
            if (calStep == 1)
            {
                lblCal.ForeColor = C.Orange;
                lblCal.Text = "Adım 1/2 — SEN SUS, kardeşin normal sesiyle konuşsun.  (" + left + ")";
                if (!eng.Calibrating)
                {
                    calBleed = Pct(eng.TakeCalibration(), 0.98);
                    calStep = 2; calEnd = DateTime.Now.AddSeconds(5);
                    eng.StartCalibration(5000);
                }
            }
            else if (calStep == 2)
            {
                lblCal.ForeColor = C.Green;
                lblCal.Text = "Adım 2/2 — ŞİMDİ SEN KONUŞ, normal sesinle bir şeyler anlat.  (" + left + ")";
                if (!eng.Calibrating)
                {
                    float me = Pct(eng.TakeCalibration(), 0.70);
                    calStep = 0; btnCal.Enabled = true;
                    float gap = me - calBleed;
                    if (gap >= 8)
                    {
                        float thr = (float)Math.Round(calBleed + Math.Min(gap * 0.4f, 12f));
                        SetThreshold(Math.Max(-75, Math.Min(-10, thr)));
                        lblCal.ForeColor = C.Green;
                        lblCal.Text = "Tamam! Eşik " + st.Thr.ToString("0") + " dB yapıldı. (Kardeşin: " + calBleed.ToString("0") + " dB, sen: " + me.ToString("0") + " dB)";
                    }
                    else
                    {
                        SetThreshold(Math.Max(-75, Math.Min(-10, (float)Math.Round(calBleed + 3))));
                        lblCal.ForeColor = C.Orange;
                        lblCal.Text = "Sesleriniz çok yakın, eşik tek başına yetmez. Kardeş modunu ikinizde de açın, mikrofonu ağzına yaklaştır.";
                    }
                }
            }
        }

        // ---- Ekranı güncelle (saniyede ~30 kez)
        string lastState = "", lastPartner = ""; bool lastOn;

        void UpdateProtectHint()
        {
            if (!eng.Running) { tip.SetToolTip(lblHint, ""); DevicesChanged(); return; }
            lnkVb.Visible = false;
            if (eng.Protected) { lblHint.ForeColor = C.Green; lblHint.Text = "✓  Yayın koruması açık: sesin yayına karışmaz."; }
            else { lblHint.ForeColor = C.Orange; lblHint.Text = "Yayın koruması çalışmıyor — yayında oyun penceresini paylaş."; }
            tip.SetToolTip(lblHint, eng.ProtectInfo);
        }
        readonly ToolTip tip = new ToolTip();
        void Tick()
        {
            if (calStep != 0) CalibrationTick();
            if (eng.Error != null)
            {
                var e = eng.Error; eng.Error = null;
                if (eng.Running) Toggle();
                MessageBox.Show(this, e + "\nTekrar BAŞLAT'a basabilirsin.", "Ses Kalkanı");
            }

            bool on = eng.Running;
            if (tray != null)
            {
                string tt = on ? "Ses Kalkanı — çalışıyor" : "Ses Kalkanı — durduruldu";
                if (tray.Text != tt) tray.Text = tt;
            }
            if (!Visible) return;
            float lvl = on ? eng.Gate.Level : -100;
            shown = Math.Max(lvl, shown - 1.2f);
            meter.Active = on; meter.Level = shown; meter.State = eng.Gate.State;
            meter.Partner = on && tgPartner.Checked ? eng.Gate.PartnerLevel : -200;
            meter.Invalidate();

            if (on != lastOn) { lastOn = on; UpdateProtectHint(); }

            string s; Color sc;
            if (!on) { sc = C.Dim; s = "Kapalı — BAŞLAT'a bas"; }
            else if (eng.Gate.State == 1) { sc = C.Green; s = "●  Mikrofon açık — sesin gidiyor"; }
            else if (eng.Gate.State == 2) { sc = C.Orange; s = "●  Kardeşin konuşuyor — susturuldu"; }
            else { sc = C.Dim; s = "●  Sessiz"; }
            if (s != lastState) { lblState.Text = s; lblState.ForeColor = sc; lastState = s; }

            Color pc;
            if (!tgPartner.Checked) { pc = C.Dim; s = "Kapalı — sadece eşik kullanılıyor."; }
            else if (eng.Link.Error != null) { pc = C.Red; s = eng.Link.Error; }
            else if (eng.Link.Connected) { pc = C.Green; s = "✓  Bağlandı: " + eng.Link.PartnerName; }
            else { pc = on ? C.Orange : C.Dim; s = on ? "Kardeşin bekleniyor… (onda da BAŞLAT'a basılı olmalı)" : "BAŞLAT'a basınca kardeşinin bilgisayarını arar."; }
            if (s != lastPartner) { lblPartner.Text = s; lblPartner.ForeColor = pc; lastPartner = s; }
        }

        static Icon MakeIcon()
        {
            var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var path = new GraphicsPath();
                path.AddLines(new[] { new PointF(16, 2), new PointF(29, 7), new PointF(27, 19), new PointF(16, 30), new PointF(5, 19), new PointF(3, 7) });
                path.CloseFigure();
                using (var b = new SolidBrush(C.Accent)) g.FillPath(b, path);
                using (var p = new Pen(Color.White, 2.5f)) { g.DrawLine(p, 11, 12, 11, 20); g.DrawLine(p, 16, 9, 16, 23); g.DrawLine(p, 21, 12, 21, 20); }
            }
            return Icon.FromHandle(bmp.GetHicon());
        }

#if PREVIEW
        // Önizleme için: arayüzü "çalışıyor" gibi gösterir
        public void FakeRunning(int state, float level, float partner, bool connected)
        {
            timer.Stop();
            btnStart.Text = "DURDUR"; SetBtnColor(btnStart, C.Red);
            meter.Active = true; meter.Level = level; meter.State = state; meter.Partner = partner; meter.Invalidate();
            lblState.Text = state == 1 ? "●  Mikrofon açık — sesin gidiyor" : "●  Kardeşin konuşuyor — susturuldu";
            lblState.ForeColor = state == 1 ? C.Green : C.Orange;
            lblPartner.Text = connected ? "✓  Bağlandı: KARDES-PC" : "Kardeşin bekleniyor… (onda da BAŞLAT'a basılı olmalı)";
            lblPartner.ForeColor = connected ? C.Green : C.Orange;
            ShowUpdate("1.2.0", RepoUrl);
            lnkVb.Visible = false; lblHint.ForeColor = C.Green; lblHint.Text = "✓  Yayın koruması açık: sesin yayına karışmaz.";
        }
#endif
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
#if PREVIEW
            Preview.Run(args); return;
#else
            bool created;
            using (var m = new Mutex(true, "SesKalkani_TekKopya", out created))
            using (var showEv = new EventWaitHandle(false, EventResetMode.AutoReset, "SesKalkani_Goster"))
            {
                if (!created) { showEv.Set(); return; } // zaten açık: mevcut pencereyi öne getir
                try { Win.SetProcessDPIAware(); } catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += (s, e) => MessageBox.Show("Beklenmeyen hata: " + e.Exception.Message, "Ses Kalkanı");
                var f = new MainForm();
                if (Array.IndexOf(args, "--arka") >= 0) f.StartHidden();
                var listener = new Thread(() =>
                {
                    while (true)
                    {
                        showEv.WaitOne();
                        try { f.BeginInvoke(new Action(f.ShowWindow)); } catch { }
                    }
                }) { IsBackground = true };
                listener.Start();
                Application.Run(f);
            }
#endif
        }
    }
}
