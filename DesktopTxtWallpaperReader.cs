// 编译命令：powershell # & "${env:WINDIR}\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /target:winexe /out:$env:USERPROFILE\Desktop\DesktopTxtWallpaperReader.exe DesktopTxtWallpaperReader.cs
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using System.Linq;

class DesktopTxtWallpaperReader : Form
{
    public static string BOOK_PATH; 
    static string PROGRESS_FILE = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "progress.txt");
    static string STATS_FILE = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "stats.txt"); 
    static string CONFIG_FILE = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "history.ini");

    int fontSize = 24;
    Color bgColor = Color.FromArgb(245, 245, 240); 
    Color textColor = Color.FromArgb(60, 60, 60);  
    Color accentColor = Color.FromArgb(180, 160, 120); 

    bool isSplitMode = false, hideFileList = false, isSearching = false, isJumpPage = false, showHelp = false, showStats = false, showZenMode = false; 
    string jumpInput = ""; int fileIndex = 0, pageIndex = 0, todayTotalSeconds = 0; 
    int searchSelectedIndex = 0; 

    Timer clockTimer = new Timer(), uiTimer = new Timer();
    Dictionary<string, int> progress = new Dictionary<string, int>();
    List<string> files = new List<string>();
    List<int> searchResults = new List<int>();
    List<string> pages = new List<string>();
    Font mainFont, infoFont = new Font("Microsoft YaHei UI", 12, GraphicsUnit.Pixel);
    ListBox fileList; TextBox hiddenSearchBox; 

    public DesktopTxtWallpaperReader()
    {
        this.FormBorderStyle = FormBorderStyle.None;
        this.ShowInTaskbar = false;
        this.DoubleBuffered = true;
        this.Bounds = Screen.PrimaryScreen.Bounds;
        this.BackColor = bgColor;
        this.KeyPreview = true; 
        uiTimer.Interval = 500; uiTimer.Tick += delegate { this.Invalidate(); }; uiTimer.Start();
        clockTimer.Interval = 1000;
        clockTimer.Tick += delegate { if (!showZenMode && !isJumpPage && !isSearching && !showHelp && !showStats) todayTotalSeconds++; };
        clockTimer.Start();
        UpdateFont(); InitUI(); InitHiddenSearch();
    }

    void InitHiddenSearch() {
        hiddenSearchBox = new TextBox();
        hiddenSearchBox.Location = new Point(-2000, -2000);
        hiddenSearchBox.TextChanged += delegate { if (isSearching) UpdateSearchResults(hiddenSearchBox.Text); };
        hiddenSearchBox.KeyDown += (sender, e) => {
            if (e.KeyCode == Keys.Enter) { ConfirmSearchSelection(); e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Up) {
                if (searchResults.Count > 0) searchSelectedIndex = (searchSelectedIndex - 1 + searchResults.Count) % searchResults.Count;
                this.Invalidate(); e.SuppressKeyPress = true;
            } else if (e.KeyCode == Keys.Down) {
                if (searchResults.Count > 0) searchSelectedIndex = (searchSelectedIndex + 1) % searchResults.Count;
                this.Invalidate(); e.SuppressKeyPress = true;
            } else if (e.KeyCode == Keys.Escape) CloseSearch();
        };
        this.Controls.Add(hiddenSearchBox);
    }

    void UpdateSearchResults(string text) {
        searchResults.Clear(); searchSelectedIndex = 0;
        if (string.IsNullOrWhiteSpace(text)) return;
        string[] keywords = text.ToLower().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < files.Count; i++) {
            string fileName = Path.GetFileNameWithoutExtension(files[i]).ToLower();
            bool isMatch = true;
            foreach (var kw in keywords) { if (!fileName.Contains(kw)) { isMatch = false; break; } }
            if (isMatch) searchResults.Add(i);
        }
        this.Invalidate();
    }

    void ConfirmSearchSelection() {
        if (searchResults.Count > 0 && searchSelectedIndex < searchResults.Count) {
            SaveData(); fileIndex = searchResults[searchSelectedIndex]; LoadCurrentFile(); CloseSearch();
        }
    }

    void CloseSearch() { isSearching = false; hiddenSearchBox.Clear(); this.Focus(); this.Invalidate(); }

    void InitUI() {
        fileList = new ListBox(); fileList.Width = 240; fileList.Dock = DockStyle.Right;
        fileList.BackColor = Color.FromArgb(240, 240, 235); fileList.ForeColor = Color.FromArgb(120, 120, 120);
        fileList.BorderStyle = BorderStyle.None; fileList.Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Regular);
        fileList.ItemHeight = 35; fileList.TabStop = false;
        fileList.SelectedIndexChanged += delegate { if (fileList.SelectedIndex >= 0 && fileList.Focused) { SaveData(); fileIndex = fileList.SelectedIndex; LoadCurrentFile(); this.Focus(); } };
        this.Controls.Add(fileList);
    }

    protected override void OnMouseClick(MouseEventArgs e) {
        if (isSearching && searchResults.Count > 0) {
            float sbW = hideFileList ? 0 : 240;
            float startX = (this.Width - sbW) / 2 - 300;
            float listStartY = this.Height / 4 + 80;
            if (e.X >= startX && e.X <= startX + 500) {
                int clickedIdx = (int)((e.Y - listStartY) / 35);
                if (clickedIdx >= 0 && clickedIdx < Math.Min(searchResults.Count, 15)) {
                    searchSelectedIndex = clickedIdx; ConfirmSearchSelection();
                }
            }
        }
    }

    protected override void OnLoad(EventArgs e) { base.OnLoad(e); RestartReader(); SetDesktopBottom(); this.Focus(); }

    public void RestartReader() { RefreshFileList(); LoadData(); if (files.Count > 0) LoadCurrentFile(); this.Invalidate(); }

    void RefreshFileList() {
        files.Clear();
        if (File.Exists(BOOK_PATH) && BOOK_PATH.ToLower().EndsWith(".txt")) { files.Add(BOOK_PATH); }
        else if (Directory.Exists(BOOK_PATH)) { try { string[] found = Directory.GetFiles(BOOK_PATH, "*.txt"); foreach (string f in found) files.Add(f); } catch {} }
        fileList.Items.Clear(); foreach (string f in files) fileList.Items.Add("    " + Path.GetFileNameWithoutExtension(f));
        if (files.Count > 0) fileIndex = 0;
    }

    void UpdateFont() { if (mainFont != null) mainFont.Dispose(); mainFont = new Font("Microsoft YaHei UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel); }

    void LoadCurrentFile() {
        if (fileIndex < 0 || fileIndex >= files.Count) return;
        string rawText = "";
        try {
            byte[] bytes = File.ReadAllBytes(files[fileIndex]);
            rawText = Encoding.UTF8.GetString(bytes);
            if (rawText.Contains("\uFFFD")) rawText = Encoding.GetEncoding(936).GetString(bytes);
        } catch { rawText = "读取失败"; }
        string[] paras = rawText.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        float sbW = hideFileList ? 0 : 240; float renderWidth = isSplitMode ? (this.Width - sbW) / 2 : (this.Width - sbW);
        Paginate(paras, renderWidth);
        if (progress.ContainsKey(files[fileIndex])) pageIndex = progress[files[fileIndex]]; else pageIndex = 0;
        if (pageIndex >= pages.Count) pageIndex = 0;
        fileList.SelectedIndex = fileIndex; this.Invalidate();
    }

    void Paginate(string[] paras, float width) {
        pages.Clear();
        using (Graphics g = this.CreateGraphics()) {
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            float lineHeight = mainFont.GetHeight(g) * 1.8f; float height = (float)this.Height - 160;
            List<string> linesArr = new List<string>();
            foreach (string p in paras) {
                string line = p.TrimEnd(); if (line.Length == 0) { linesArr.Add(""); continue; }
                while (line.Length > 0) {
                    int len = line.Length;
                    while (len > 0 && g.MeasureString(line.Substring(0, len), mainFont).Width > width - 140) len--;
                    if (len == 0) len = 1; linesArr.Add(line.Substring(0, len)); line = line.Substring(len);
                }
            }
            int lpp = (int)(height / lineHeight); if (lpp <= 0) lpp = 1;
            for (int i = 0; i < linesArr.Count; i += lpp) {
                StringBuilder sb = new StringBuilder();
                for (int j = i; j < i + lpp && j < linesArr.Count; j++) sb.AppendLine(linesArr[j]);
                pages.Add(sb.ToString());
            }
        }
    }

    protected override void OnPaint(PaintEventArgs e) {
        Graphics g = e.Graphics; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        if (showZenMode) { DrawZenOverlay(g); return; }
        g.Clear(bgColor); float sbW = hideFileList ? 0 : 240; float usableWidth = (float)this.Width - sbW;
        using (Brush textBrush = new SolidBrush(textColor)) {
            if (!isSplitMode) { if (pageIndex < pages.Count) g.DrawString(pages[pageIndex], mainFont, textBrush, new RectangleF(100, 100, usableWidth - 200, (float)this.Height - 200)); }
            else {
                float mid = usableWidth / 2; g.DrawLine(new Pen(Color.FromArgb(220, 220, 215), 1), mid, 100, mid, (float)this.Height - 120);
                if (pageIndex < pages.Count) g.DrawString(pages[pageIndex], mainFont, textBrush, new RectangleF(100, 100, mid - 140, (float)this.Height - 200));
                if (pageIndex + 1 < pages.Count) g.DrawString(pages[pageIndex + 1], mainFont, textBrush, new RectangleF(mid + 40, 100, mid - 140, (float)this.Height - 200));
            }
            float footY = (float)this.Height - 55; Brush footBrush = new SolidBrush(Color.FromArgb(180, 180, 180));
            string pageText = string.Format("{0} / {1}", pageIndex + 1, pages.Count);
            g.DrawString(pageText, infoFont, footBrush, 100, footY);
            g.DrawString("[  I  ]", infoFont, footBrush, (usableWidth - g.MeasureString("[  I  ]", infoFont).Width) / 2, footY);
            g.DrawString(DateTime.Now.ToString("HH:mm"), infoFont, footBrush, usableWidth - 140, footY);
        }
        if (showHelp) DrawHelpOverlay(g); if (showStats) DrawStatsOverlay(g); if (isSearching) DrawSearchOverlay(g); if (isJumpPage) DrawJumpOverlay(g); 
    }

    void DrawZenOverlay(Graphics g) {
        g.Clear(Color.Black);
        using (Font zenFont = new Font("Microsoft YaHei UI", 32, FontStyle.Bold, GraphicsUnit.Pixel)) {
            StringFormat sf = new StringFormat(); sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center;
            g.DrawString("You are where you are.\nWhat you do next is more important.", zenFont, Brushes.White, new Rectangle(0, 0, this.Width, this.Height), sf);
        }
        using (Font hintFont = new Font("Consolas", 14, FontStyle.Bold, GraphicsUnit.Pixel)) {
            string hint = "--- Press 'C' to Return ---";
            SizeF hintSize = g.MeasureString(hint, hintFont);
            g.DrawString(hint, hintFont, new SolidBrush(Color.FromArgb(130, 130, 130)), (this.Width - hintSize.Width) / 2, this.Height - 120);
        }
    }

    void DrawJumpOverlay(Graphics g) {
        g.FillRectangle(new SolidBrush(Color.FromArgb(230, bgColor)), this.ClientRectangle);
        string prompt = "跳转至页码: " + jumpInput + "█";
        using (Font f = new Font("Microsoft YaHei UI", 32, FontStyle.Bold, GraphicsUnit.Pixel)) {
            SizeF sz = g.MeasureString(prompt, f); float sbW = hideFileList ? 0 : 240;
            g.DrawString(prompt, f, new SolidBrush(accentColor), (this.Width - sbW - sz.Width) / 2, this.Height / 2 - 40);
        }
    }

    void DrawStatsOverlay(Graphics g) {
        g.FillRectangle(new SolidBrush(Color.FromArgb(235, 245, 245, 240)), this.ClientRectangle);
        string timeDisplay = (todayTotalSeconds / 60).ToString() + " 分钟";
        using (Font bigFont = new Font("Microsoft YaHei UI", 48, FontStyle.Bold, GraphicsUnit.Pixel)) {
            SizeF sz = g.MeasureString(timeDisplay, bigFont); float sbW = hideFileList ? 0 : 240;
            g.DrawString(timeDisplay, bigFont, new SolidBrush(accentColor), (this.Width - sbW - sz.Width) / 2, this.Height / 2 - 40);
        }
    }

    void DrawHelpOverlay(Graphics g) {
        g.FillRectangle(new SolidBrush(Color.FromArgb(235, 245, 245, 240)), this.ClientRectangle);
        string content = "Space / B - 翻页\nG - 跳转页码\nF - 搜索书籍\nS - 分屏切换\nH - 列表显隐\nT - 阅读时长\nC - 警醒模式\nZ / X - 字号调整\nR - 回到第一页\nEsc - 返回配置";
        using (Font f = new Font("Microsoft YaHei UI", 16, FontStyle.Regular, GraphicsUnit.Pixel)) {
            float sbW = hideFileList ? 0 : 240;
            g.DrawString(content, f, new SolidBrush(textColor), (this.Width - sbW) / 2 - 100, this.Height / 4);
        }
    }

    void DrawSearchOverlay(Graphics g) {
        g.FillRectangle(new SolidBrush(Color.FromArgb(242, 245, 245, 240)), this.ClientRectangle);
        float sbW = hideFileList ? 0 : 240;
        float startX = (this.Width - sbW) / 2 - 300; float startY = this.Height / 4;
        using (Font titleFont = new Font("Microsoft YaHei UI", 32, FontStyle.Bold, GraphicsUnit.Pixel))
        using (Font listFont = new Font("Microsoft YaHei UI", 18, GraphicsUnit.Pixel))
        using (Brush activeBrush = new SolidBrush(accentColor))
        using (Brush normalBrush = new SolidBrush(Color.FromArgb(140, 140, 140))) {
            string inputDisplay = "> " + (string.IsNullOrEmpty(hiddenSearchBox.Text) ? "输入书名..." : hiddenSearchBox.Text) + "█";
            g.DrawString(inputDisplay, titleFont, new SolidBrush(textColor), startX, startY);
            if (searchResults.Count > 0) {
                float listY = startY + 80;
                for (int i = 0; i < Math.Min(searchResults.Count, 15); i++) {
                    string fileName = Path.GetFileNameWithoutExtension(files[searchResults[i]]);
                    if (i == searchSelectedIndex) {
                        g.FillRectangle(new SolidBrush(Color.FromArgb(30, accentColor)), startX, listY + (i * 35), 600, 32);
                        g.DrawString("● " + fileName, listFont, activeBrush, startX + 10, listY + (i * 35));
                    } else g.DrawString("  " + fileName, listFont, normalBrush, startX + 10, listY + (i * 35));
                }
            }
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData) {
        if (isSearching || isJumpPage) return base.ProcessCmdKey(ref msg, keyData);
        if (keyData == Keys.C) { showZenMode = !showZenMode; this.Invalidate(); return true; }
        if (keyData == Keys.Space && !showZenMode) { pageIndex += isSplitMode ? 2 : 1; if (pageIndex >= pages.Count) pageIndex = pages.Count - 1; this.Invalidate(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e) {
        if (isSearching) return; 
        if (isJumpPage) {
            if (e.KeyCode >= Keys.D0 && e.KeyCode <= Keys.D9) jumpInput += (e.KeyCode - Keys.D0).ToString();
            else if (e.KeyCode >= Keys.NumPad0 && e.KeyCode <= Keys.NumPad9) jumpInput += (e.KeyCode - Keys.NumPad0).ToString();
            else if (e.KeyCode == Keys.Back && jumpInput.Length > 0) jumpInput = jumpInput.Substring(0, jumpInput.Length - 1);
            else if (e.KeyCode == Keys.Enter) { int t; if (int.TryParse(jumpInput, out t)) pageIndex = Math.Max(0, Math.Min(t - 1, pages.Count - 1)); isJumpPage = false; }
            else if (e.KeyCode == Keys.Escape) isJumpPage = false;
            this.Invalidate(); return;
        }
        int step = isSplitMode ? 2 : 1;
        switch (e.KeyCode) {
            case Keys.G: isJumpPage = true; jumpInput = ""; break;
            case Keys.R: pageIndex = 0; break; case Keys.I: showHelp = !showHelp; break;
            case Keys.T: showStats = !showStats; break; 
            case Keys.F: isSearching = true; hiddenSearchBox.Clear(); hiddenSearchBox.Focus(); Application.DoEvents(); hiddenSearchBox.Clear(); break;
            case Keys.B: pageIndex -= step; if (pageIndex < 0) pageIndex = 0; break;
            case Keys.H: hideFileList = !hideFileList; fileList.Visible = !hideFileList; LoadCurrentFile(); break;
            case Keys.S: isSplitMode = !isSplitMode; LoadCurrentFile(); break;
            case Keys.Z: fontSize += 2; UpdateFont(); LoadCurrentFile(); break;
            case Keys.X: fontSize -= 2; if (fontSize < 12) fontSize = 12; UpdateFont(); LoadCurrentFile(); break;
            case Keys.Escape: SaveData(); this.Hide(); Program.Boot(); break;
        }
        this.Invalidate();
    }

    void SaveData() {
        if (files.Count == 0 || fileIndex >= files.Count) return;
        progress[files[fileIndex]] = pageIndex;
        List<string> pLines = new List<string>(); foreach (KeyValuePair<string, int> kv in progress) pLines.Add(kv.Key + "|" + kv.Value);
        File.WriteAllLines(PROGRESS_FILE, pLines.ToArray());
        File.WriteAllLines(STATS_FILE, new string[] { DateTime.Now.ToString("yyyy-MM-dd") + "|" + (todayTotalSeconds / 60).ToString() });
        List<string> history = new List<string>(); if (File.Exists(CONFIG_FILE)) history.AddRange(File.ReadAllLines(CONFIG_FILE));
        history.Remove(BOOK_PATH); history.Insert(0, BOOK_PATH); if (history.Count > 10) history = history.GetRange(0, 10);
        File.WriteAllLines(CONFIG_FILE, history.ToArray());
    }

    void LoadData() {
        if (File.Exists(PROGRESS_FILE)) foreach (string line in File.ReadAllLines(PROGRESS_FILE)) { string[] p = line.Split('|'); if (p.Length == 2) progress[p[0]] = int.Parse(p[1]); }
        if (File.Exists(STATS_FILE)) {
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            foreach (string line in File.ReadAllLines(STATS_FILE)) { string[] p = line.Split('|'); if (p.Length == 2 && p[0] == today) todayTotalSeconds = int.Parse(p[1]) * 60; }
        }
    }

    void SetDesktopBottom() {
        IntPtr progman = FindWindow("Progman", null);
        IntPtr r; SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0, 100, out r);
        IntPtr workerW = IntPtr.Zero;
        EnumWindows(delegate (IntPtr h, IntPtr p) { if (FindWindowEx(h, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero) workerW = FindWindowEx(IntPtr.Zero, h, "WorkerW", null); return true; }, IntPtr.Zero);
        if (workerW != IntPtr.Zero) SetParent(this.Handle, workerW);
    }

    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc p, IntPtr l); delegate bool EnumWindowsProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern IntPtr FindWindow(string c, string n); [DllImport("user32.dll")] static extern IntPtr FindWindowEx(IntPtr p, IntPtr c, string cn, string wn);
    [DllImport("user32.dll")] static extern IntPtr SetParent(IntPtr c, IntPtr p); [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr h, int m, IntPtr w, IntPtr l, int f, int t, out IntPtr r);
}

class Program {
    static DesktopTxtWallpaperReader reader;
    static string CONFIG_FILE = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "history.ini");
    [STAThread] static void Main() { Application.EnableVisualStyles(); Boot(); Application.Run(); }
    public static void Boot() {
        List<string> history = new List<string>(); if (File.Exists(CONFIG_FILE)) history.AddRange(File.ReadAllLines(CONFIG_FILE));
        history = history.Distinct().Where(p => {
            if (File.Exists(p) && p.ToLower().EndsWith(".txt")) return true;
            if (Directory.Exists(p)) { try { return Directory.GetFiles(p, "*.txt").Length > 0; } catch { return false; } }
            return false;
        }).ToList();
        if (history.Count > 10) history = history.GetRange(0, 10); File.WriteAllLines(CONFIG_FILE, history.ToArray());
        string defaultPath = (history.Count > 0) ? history[0] : Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        using (BootDialog boot = new BootDialog(defaultPath, history)) {
            boot.TopMost = true;
            if (boot.ShowDialog() == DialogResult.OK) {
                DesktopTxtWallpaperReader.BOOK_PATH = boot.SelectedPath;
                if (reader == null || reader.IsDisposed) reader = new DesktopTxtWallpaperReader();
                reader.RestartReader(); reader.Show();
            } else { Application.Exit(); }
        }
    }
}

class BootDialog : Form {
    public string SelectedPath; private List<string> history; private TextBox txtPath;
    public BootDialog(string defaultPath, List<string> hist) {
        this.SelectedPath = defaultPath; this.history = hist; this.Size = new Size(860, 520); this.BackColor = Color.FromArgb(245, 245, 240);
        this.FormBorderStyle = FormBorderStyle.None; this.StartPosition = FormStartPosition.CenterScreen; this.KeyPreview = true;
        Label lblTitle = new Label { Text = "往昔书卷", Font = new Font("Microsoft YaHei UI", 16, FontStyle.Bold), Location = new Point(45, 30), AutoSize = true, ForeColor = Color.FromArgb(80, 80, 80) };
        Label lblHistTitle = new Label { Text = "--- 按数字键 1-0 立即入座 ---", Font = new Font("Microsoft YaHei UI", 9), ForeColor = Color.FromArgb(160, 160, 150), Location = new Point(45, 75), AutoSize = true };
        this.Controls.AddRange(new Control[] { lblTitle, lblHistTitle });
        for (int i = 0; i < history.Count; i++) {
            string keyHint = (i + 1 == 10) ? "0" : (i + 1).ToString();
            Label l = new Label { Text = string.Format("{0}. {1}", keyHint, Path.GetFileName(history[i])), Font = new Font("Consolas", 9f), ForeColor = Color.FromArgb(140, 140, 130), Location = new Point(55, 105 + (i * 26)), AutoSize = true };
            this.Controls.Add(l);
        }
        Label lblEditTitle = new Label { Text = "路径寻踪 (D-目录 / F-文件 / Enter-阅读 / Esc-退出)", Font = new Font("Microsoft YaHei UI", 9), ForeColor = Color.FromArgb(180, 160, 120), Location = new Point(45, 380), AutoSize = true };
        txtPath = new TextBox { Text = defaultPath, Location = new Point(45, 410), Width = 300, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 9) };
        this.Controls.AddRange(new Control[] { lblEditTitle, txtPath });
        int rightPageX = 475;
        Label lblAboutTitle = new Label { Text = "卷前寄语", Font = new Font("Microsoft YaHei UI", 16, FontStyle.Bold), Location = new Point(rightPageX, 30), AutoSize = true, ForeColor = Color.FromArgb(80, 80, 80) };
        Label lblIntro = new Label { Text = "此卷宗旨在将桌面化为书斋。\n\n大隐于市，于繁杂工作中\n得片刻之宁静。\n支持TXT格式卷宗，\n通过快捷键可得极致之阅读体验。\n\n愿君于此间偶得所得。", Font = new Font("Microsoft YaHei UI", 10.5f), ForeColor = Color.FromArgb(100, 100, 100), Location = new Point(rightPageX, 105), Width = 320, Height = 250 };
        Label lblMeta = new Label { Text = "版本：v2.9 (Fast Boot)\n设计：SJY\n日期：2025年冬\n\n--- 按 Esc 键离席退出 ---", Font = new Font("Microsoft YaHei UI", 9), ForeColor = Color.FromArgb(160, 160, 150), Location = new Point(rightPageX, 365), AutoSize = true };
        this.Controls.AddRange(new Control[] { lblAboutTitle, lblIntro, lblMeta });
        
        Action tryStart = () => { if (CheckPathHasTxt(txtPath.Text)) { this.SelectedPath = txtPath.Text; this.DialogResult = DialogResult.OK; this.Close(); } else { MessageBox.Show("路径无效。"); } };
        
        // 核心修改：输入框拦截数字键，直接启动
        txtPath.KeyDown += (s, e) => {
            bool isDigit = (e.KeyCode >= Keys.D0 && e.KeyCode <= Keys.D9) || (e.KeyCode >= Keys.NumPad0 && e.KeyCode <= Keys.NumPad9);
            if (isDigit) {
                e.SuppressKeyPress = true; // 防止数字进入文本框
                int idx = (e.KeyCode == Keys.D0 || e.KeyCode == Keys.NumPad0) ? 9 : (e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D9 ? e.KeyCode - Keys.D1 : e.KeyCode - Keys.NumPad1);
                if (idx >= 0 && idx < history.Count) {
                    if (CheckPathHasTxt(history[idx])) {
                        this.SelectedPath = history[idx];
                        this.DialogResult = DialogResult.OK;
                        this.Close();
                    } else MessageBox.Show("该历史书卷已无法寻得。");
                }
                return;
            }
            if (e.KeyCode == Keys.Enter) tryStart();
            else if (e.KeyCode == Keys.Escape) Application.Exit();
            else if (e.KeyCode == Keys.D && !e.Control) { e.SuppressKeyPress = true; using (FolderBrowserDialog fbd = new FolderBrowserDialog()) if (fbd.ShowDialog() == DialogResult.OK) txtPath.Text = fbd.SelectedPath; }
            else if (e.KeyCode == Keys.F && !e.Control) { e.SuppressKeyPress = true; using (OpenFileDialog ofd = new OpenFileDialog { Filter = "文本|*.txt" }) if (ofd.ShowDialog() == DialogResult.OK) txtPath.Text = ofd.FileName; }
        };
    }
    private bool CheckPathHasTxt(string path) {
        if (string.IsNullOrEmpty(path)) return false;
        if (File.Exists(path) && path.ToLower().EndsWith(".txt")) return true;
        if (Directory.Exists(path)) { try { return Directory.GetFiles(path, "*.txt").Length > 0; } catch { return false; } }
        return false;
    }
    protected override void OnPaint(PaintEventArgs e) { 
        Graphics g = e.Graphics; g.DrawRectangle(new Pen(Color.FromArgb(200, 200, 190), 3), 0, 0, Width-1, Height-1);
        Pen spinePen = new Pen(Color.FromArgb(220, 220, 210), 1) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
        g.DrawLine(spinePen, Width/2, 40, Width/2, Height-40);
    }
}
