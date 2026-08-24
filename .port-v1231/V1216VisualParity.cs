using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

// Strict visual-parity layer for v1.2.31.2.
// Geometry follows the real Win32 v1.2.16 layout, with one requested addition:
// "Journal / Outils" is appended to toolbar row 2.
internal static class V1216VisualParity
{
    private static MainForm owner;
    private static Control oldRoot;
    private static bool prepared;
    private static bool lastConnected;
    private static bool lastArtVisible;

    private static Label titleLabel, deviceTitle, deviceNameLabel, deviceDetailsLabel, connectionWarning, saveTitle;
    private static Label pcGamesTitle, pcCodesTitle, arGamesTitle, arCodesTitle, editorHint, transferText, storageText, bottomStatus;
    private static Label gameLabel, cheatLabel, formatLabel;
    private static PictureBox boxArt;
    private static Button languageButton, readButton, writeButton, importButton, exportButton, libraryButton, driverButton;
    private static Button firmwareBackupButton, firmwareUpdateButton, folderButton, undoButton, redoButton, journalButton;
    private static Button toArButton, toPcButton, newGameButton, deleteGameButton, newCodeButton, deleteCodeButton;
    private static Button saveExportButton, saveRestoreButton, applyButton, cancelButton;
    private static CheckedListBox pcGames, pcCodes, arGames, arCodes;
    private static TextBox gameNameText, cheatNameText, codeText;
    private static CheckBox masterCheck;
    private static ProgressBar transferProgress, storageProgress;
    private static System.Windows.Forms.Timer watch;

    internal static void Attach(MainForm form)
    {
        if (form == null) return;
        owner = form;

        try
        {
            string icoPath = Path.Combine(Application.StartupPath, "ActionReplayGBX.ico");
            Icon appIcon = null;
            if (File.Exists(icoPath)) appIcon = new Icon(icoPath);
            else appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (appIcon != null) form.Icon = (Icon)appIcon.Clone();
        }
        catch { }

        form.MinimumSize = new Size(900, 700);

        form.Shown += delegate
        {
            Prepare();
            LayoutNow();
        };
        form.Resize += delegate { if (prepared) LayoutNow(); };

        watch = new System.Windows.Forms.Timer();
        watch.Interval = 350;
        watch.Tick += delegate
        {
            if (owner == null || owner.IsDisposed) return;
            if (!prepared) Prepare();
            bool connected = GetBoolField(owner, "deviceConnected");
            bool artVisible = boxArt != null && boxArt.Visible && boxArt.Image != null;
            if (connected != lastConnected || artVisible != lastArtVisible)
            {
                lastConnected = connected;
                lastArtVisible = artVisible;
                LayoutNow();
            }
            InjectJournalClearLog();
        };
        form.Shown += delegate { watch.Start(); };
        form.FormClosed += delegate { if (watch != null) { watch.Stop(); watch.Dispose(); } };
    }

    private static void Prepare()
    {
        if (prepared || owner == null || owner.IsDisposed) return;

        titleLabel = F<Label>("titleLabel");
        languageButton = F<Button>("languageButton");
        deviceTitle = F<Label>("deviceTitle");
        deviceNameLabel = F<Label>("deviceNameLabel");
        deviceDetailsLabel = F<Label>("deviceDetailsLabel");
        connectionWarning = F<Label>("connectionWarning");
        boxArt = F<PictureBox>("boxArt");
        saveTitle = F<Label>("saveTitle");

        readButton = F<Button>("readButton");
        writeButton = F<Button>("writeButton");
        importButton = F<Button>("importButton");
        exportButton = F<Button>("exportButton");
        libraryButton = F<Button>("libraryButton");
        driverButton = F<Button>("driverButton");
        firmwareBackupButton = F<Button>("firmwareBackupButton");
        firmwareUpdateButton = F<Button>("firmwareUpdateButton");
        folderButton = F<Button>("folderButton");
        undoButton = F<Button>("undoButton");
        redoButton = F<Button>("redoButton");
        journalButton = F<Button>("journalButton");

        pcGames = F<CheckedListBox>("pcGames");
        pcCodes = F<CheckedListBox>("pcCodes");
        arGames = F<CheckedListBox>("arGames");
        arCodes = F<CheckedListBox>("arCodes");
        pcGamesTitle = F<Label>("pcGamesTitle");
        pcCodesTitle = F<Label>("pcCodesTitle");
        arGamesTitle = F<Label>("arGamesTitle");
        arCodesTitle = F<Label>("arCodesTitle");

        toArButton = F<Button>("toArButton");
        toPcButton = F<Button>("toPcButton");
        newGameButton = F<Button>("newGameButton");
        deleteGameButton = F<Button>("deleteGameButton");
        newCodeButton = F<Button>("newCodeButton");
        deleteCodeButton = F<Button>("deleteCodeButton");

        gameNameText = F<TextBox>("gameNameText");
        cheatNameText = F<TextBox>("cheatNameText");
        codeText = F<TextBox>("codeText");
        masterCheck = F<CheckBox>("masterCheck");
        applyButton = F<Button>("applyButton");
        cancelButton = F<Button>("cancelButton");
        editorHint = F<Label>("editorHint");

        transferText = F<Label>("transferText");
        storageText = F<Label>("storageText");
        transferProgress = F<ProgressBar>("transferProgress");
        storageProgress = F<ProgressBar>("storageProgress");
        bottomStatus = F<Label>("bottomStatus");

        saveExportButton = FindButton(new string[] { "Exporter la sauvegarde", "Export save" });
        saveRestoreButton = FindButton(new string[] { "Restaurer une sauvegarde", "Restore save" });
        gameLabel = FindLabel(new string[] { "Nom du jeu", "Game name" });
        cheatLabel = FindLabel(new string[] { "Nom du code", "Code name" });
        formatLabel = FindLabel(new string[] {
            "Codes Action Replay — format XXXXXXXX YYYYYYYY",
            "Action Replay codes — format XXXXXXXX YYYYYYYY"
        });

        if (journalButton != null) journalButton.Text = LanguageManager.IsFrench ? "Journal / Outils" : "Log / Tools";
        if (firmwareBackupButton != null) firmwareBackupButton.Text = LanguageManager.IsFrench ? "Sauvegarde Firmware" : "Firmware backup";
        if (firmwareUpdateButton != null) firmwareUpdateButton.Text = LanguageManager.IsFrench ? "Mise à jour Firmware" : "Firmware update";

        owner.Font = new Font("Segoe UI", 9.75f, FontStyle.Regular, GraphicsUnit.Point);
        if (titleLabel != null) titleLabel.Font = new Font("Segoe UI", 18f, FontStyle.Bold, GraphicsUnit.Point);
        SetSectionFont(deviceTitle); SetSectionFont(saveTitle); SetSectionFont(pcGamesTitle); SetSectionFont(pcCodesTitle); SetSectionFont(arGamesTitle); SetSectionFont(arCodesTitle);
        if (deviceNameLabel != null) deviceNameLabel.Font = new Font("Segoe UI", 10f, FontStyle.Bold, GraphicsUnit.Point);
        if (connectionWarning != null) connectionWarning.Font = new Font("Segoe UI", 9.25f, FontStyle.Bold, GraphicsUnit.Point);
        if (codeText != null) codeText.Font = new Font("Consolas", 10f, FontStyle.Regular, GraphicsUnit.Point);

        List<Control> visible = new List<Control>();
        Add(visible, titleLabel); Add(visible, languageButton); Add(visible, deviceTitle); Add(visible, deviceNameLabel);
        Add(visible, deviceDetailsLabel); Add(visible, connectionWarning); Add(visible, boxArt); Add(visible, saveTitle);
        Add(visible, saveExportButton); Add(visible, saveRestoreButton);
        Add(visible, readButton); Add(visible, writeButton); Add(visible, importButton); Add(visible, exportButton); Add(visible, libraryButton);
        Add(visible, undoButton); Add(visible, redoButton); Add(visible, driverButton); Add(visible, firmwareBackupButton); Add(visible, firmwareUpdateButton); Add(visible, folderButton); Add(visible, journalButton);
        Add(visible, pcGamesTitle); Add(visible, pcCodesTitle); Add(visible, arGamesTitle); Add(visible, arCodesTitle);
        Add(visible, pcGames); Add(visible, pcCodes); Add(visible, arGames); Add(visible, arCodes); Add(visible, toArButton); Add(visible, toPcButton);
        Add(visible, newGameButton); Add(visible, deleteGameButton); Add(visible, newCodeButton); Add(visible, deleteCodeButton);
        Add(visible, gameLabel); Add(visible, cheatLabel); Add(visible, gameNameText); Add(visible, cheatNameText); Add(visible, masterCheck); Add(visible, editorHint);
        Add(visible, formatLabel); Add(visible, codeText); Add(visible, applyButton); Add(visible, cancelButton);
        Add(visible, transferText); Add(visible, storageText); Add(visible, transferProgress); Add(visible, storageProgress); Add(visible, bottomStatus);

        if (owner.Controls.Count > 0) oldRoot = owner.Controls[0];
        owner.SuspendLayout();
        for (int i = 0; i < visible.Count; i++)
        {
            Control c = visible[i];
            c.Dock = DockStyle.None;
            c.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            owner.Controls.Add(c);
            c.BringToFront();
        }
        if (oldRoot != null && !visible.Contains(oldRoot)) oldRoot.Visible = false;
        owner.ResumeLayout(false);

        lastConnected = GetBoolField(owner, "deviceConnected");
        lastArtVisible = boxArt != null && boxArt.Visible && boxArt.Image != null;
        prepared = true;
    }

    private static void LayoutNow()
    {
        if (!prepared || owner == null || owner.IsDisposed) return;
        int width = owner.ClientSize.Width;
        int height = owner.ClientSize.Height;
        if (width < 1 || height < 1) return;

        int outer = 14;
        int usable = width - 2 * outer;
        if (usable < 872) usable = 872;
        int margin = outer;
        if (usable > 2400) { usable = 2400; margin = (width - usable) / 2; }
        int contentRight = margin + usable;

        Move(titleLabel, margin, 8, usable - 76, 34);
        Move(languageButton, contentRight - 64, 9, 64, 30);

        int infoY = 48;
        int sectionGap = 18;
        int leftW = usable * 61 / 100;
        int rightW = usable - leftW - sectionGap;
        if (rightW < 330) { rightW = 330; leftW = usable - rightW - sectionGap; }
        int rightX = margin + leftW + sectionGap;

        bool connected = GetBoolField(owner, "deviceConnected");
        int artReserve = connected ? 104 : 0;
        int deviceTextX = margin + artReserve;
        int deviceTextW = leftW - artReserve;
        if (deviceTextW < 240) deviceTextW = 240;
        if (connected) Move(boxArt, margin, infoY, 92, 120); else Move(boxArt, margin, infoY, 0, 0);
        Move(deviceTitle, deviceTextX, infoY, deviceTextW, 18);
        Move(deviceNameLabel, deviceTextX, infoY + 20, deviceTextW, 24);
        Move(deviceDetailsLabel, deviceTextX, infoY + 45, deviceTextW, 20);
        Move(connectionWarning, deviceTextX, infoY + 70, contentRight - deviceTextX, 46);
        Move(saveTitle, rightX, infoY, rightW, 18);
        int saveGap = 8;
        int saveBW = (rightW - saveGap) / 2;
        Move(saveExportButton, rightX, infoY + 22, saveBW, 34);
        Move(saveRestoreButton, rightX + saveBW + saveGap, infoY + 22, saveBW, 34);

        int toolbarY = 170;
        int buttonH = 34;
        int gap = 7;
        Control[] row1 = new Control[] { readButton, writeButton, importButton, exportButton, libraryButton };
        LayoutEqualRow(row1, margin, toolbarY, usable, gap, buttonH);
        Control[] row2 = new Control[] { undoButton, redoButton, driverButton, firmwareBackupButton, firmwareUpdateButton, folderButton, journalButton };
        LayoutEqualRow(row2, margin, toolbarY + buttonH + gap, usable, gap, buttonH);

        int mainY = toolbarY + 2 * buttonH + gap + 12;
        int bottomH = 102;
        int statusTop = height - bottomH;
        int bodyH = statusTop - mainY;
        if (bodyH < 330) bodyH = 330;

        int actionH = 34;
        int editorH = 248;
        if (bodyH < 650) editorH = 220;
        if (bodyH > 900) editorH = 270;
        int listAreaH = bodyH - editorH - actionH - 18;
        if (listAreaH < 150)
        {
            listAreaH = 150;
            editorH = bodyH - listAreaH - actionH - 18;
            if (editorH < 145) editorH = 145;
        }

        int groupGap = 10;
        int railW = usable > 1450 ? 116 : 104;
        int groupW = (usable - railW - 2 * groupGap) / 2;
        int innerGap = 8;
        int gameW = groupW * 41 / 100;
        int codeW = groupW - gameW - innerGap;
        int pcX = margin;
        int pcCodeX = pcX + gameW + innerGap;
        int railX = pcX + groupW + groupGap;
        int arX = railX + railW + groupGap;
        int arCodeX = arX + gameW + innerGap;
        int titleH = 24;
        int listY = mainY + titleH;
        int listH = listAreaH - titleH;

        Move(pcGamesTitle, pcX, mainY, gameW, titleH);
        Move(pcCodesTitle, pcCodeX, mainY, codeW, titleH);
        Move(arGamesTitle, arX, mainY, gameW, titleH);
        Move(arCodesTitle, arCodeX, mainY, codeW, titleH);
        Move(pcGames, pcX, listY, gameW, listH);
        Move(pcCodes, pcCodeX, listY, codeW, listH);
        Move(arGames, arX, listY, gameW, listH);
        Move(arCodes, arCodeX, listY, codeW, listH);
        int transferButtonW = railW - 12;
        Move(toArButton, railX + 6, listY + listH / 2 - 47, transferButtonW, 38);
        Move(toPcButton, railX + 6, listY + listH / 2 + 9, transferButtonW, 38);

        int actionY = mainY + listAreaH + 7;
        int actionGap = 8;
        int actionW = (usable - actionGap * 3) / 4;
        Move(newGameButton, margin, actionY, actionW, actionH);
        Move(deleteGameButton, margin + actionW + actionGap, actionY, actionW, actionH);
        Move(newCodeButton, margin + 2 * (actionW + actionGap), actionY, actionW, actionH);
        Move(deleteCodeButton, margin + 3 * (actionW + actionGap), actionY, contentRight - (margin + 3 * (actionW + actionGap)), actionH);

        int editY = actionY + actionH + 8;
        int halfGap = 18;
        int halfW = (usable - halfGap) / 2;
        int rightEdit = margin + halfW + halfGap;
        int labelH = 18;
        int fieldH = 30;
        Move(gameLabel, margin, editY, halfW, labelH);
        Move(cheatLabel, rightEdit, editY, halfW, labelH);
        int fieldY = editY + labelH + 2;
        Move(gameNameText, margin, fieldY, halfW, fieldH);
        Move(cheatNameText, rightEdit, fieldY, halfW, fieldH);

        int optionY = fieldY + fieldH + 7;
        int masterW = 190;
        int applyW = 230;
        int cancelW = 105;
        Move(masterCheck, margin, optionY, masterW, 30);
        if (cancelButton != null && cancelButton.Visible)
        {
            Move(applyButton, contentRight - applyW - cancelW - 8, optionY, applyW, 30);
            Move(cancelButton, contentRight - cancelW, optionY, cancelW, 30);
        }
        else
        {
            Move(applyButton, contentRight - applyW, optionY, applyW, 30);
            Move(cancelButton, contentRight, optionY, 0, 0);
        }
        int hintX = margin + masterW + 10;
        int rightReserved = applyW + ((cancelButton != null && cancelButton.Visible) ? cancelW + 8 : 0);
        int hintW = contentRight - rightReserved - 10 - hintX;
        if (hintW < 40) hintW = 40;
        Move(editorHint, hintX, optionY + 5, hintW, 20);

        int codeLabelY = optionY + 36;
        Move(formatLabel, margin, codeLabelY, usable, 19);
        int codeY = codeLabelY + 22;
        int codeBottom = mainY + listAreaH + 7 + actionH + 8 + editorH;
        if (codeBottom > statusTop - 5) codeBottom = statusTop - 5;
        int codeH = codeBottom - codeY;
        if (codeH < 55) codeH = 55;
        Move(codeText, margin, codeY, usable, codeH);

        int baseY = height - bottomH + 4;
        int halfStatus = usable / 2 - 6;
        Move(transferText, margin, baseY, halfStatus, 18);
        Move(storageText, margin + usable / 2 + 6, baseY, halfStatus, 18);
        Move(transferProgress, margin, baseY + 19, halfStatus, 16);
        Move(storageProgress, margin + usable / 2 + 6, baseY + 19, halfStatus, 16);
        Move(bottomStatus, margin, baseY + 42, usable, 50);

        owner.Invalidate(true);
    }

    private static void InjectJournalClearLog()
    {
        if (owner == null || owner.IsDisposed) return;
        Form journal = null;
        foreach (Form f in Application.OpenForms)
        {
            if (Object.ReferenceEquals(f, owner)) continue;
            string t = f.Text ?? "";
            if (t.StartsWith("Journal / outils", StringComparison.OrdinalIgnoreCase) ||
                t.StartsWith("Journal / Outils", StringComparison.OrdinalIgnoreCase) ||
                t.StartsWith("Log / tools", StringComparison.OrdinalIgnoreCase) ||
                t.StartsWith("Log / Tools", StringComparison.OrdinalIgnoreCase))
            {
                journal = f;
                break;
            }
        }
        if (journal == null) return;
        journal.Text = (LanguageManager.IsFrench ? "Journal / Outils" : "Log / Tools") + " — ActionReplayGBX v1.2.31.2";
        if (ContainsNamed(journal, "v12312-clear-log")) return;

        FlowLayoutPanel bar = FindButtonBar(journal);
        if (bar == null) return;
        Button clear = new Button();
        clear.Name = "v12312-clear-log";
        clear.Text = LanguageManager.IsFrench ? "Effacer log" : "Clear log";
        clear.AutoSize = true;
        clear.Click += delegate
        {
            if (MessageBox.Show(journal,
                LanguageManager.IsFrench ? "Effacer complètement le journal ?" : "Clear the entire log?",
                LanguageManager.IsFrench ? "Effacer log" : "Clear log",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                string path = GetLogPath();
                if (!String.IsNullOrEmpty(path)) File.WriteAllText(path, "", new UTF8Encoding(false));
                FieldInfo f = typeof(MainForm).GetField("operationLog", BindingFlags.Instance | BindingFlags.NonPublic);
                if (f != null)
                {
                    StringBuilder sb = f.GetValue(owner) as StringBuilder;
                    if (sb != null) sb.Length = 0;
                }
                TextBox logBox = FindLogTextBox(journal);
                if (logBox != null) logBox.Clear();
            }
            catch (Exception ex)
            {
                MessageBox.Show(journal, ex.Message, "Action Replay GBX", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
        bar.WrapContents = false;
        bar.AutoScroll = true;
        bar.Controls.Add(clear);
    }

    private static string GetLogPath()
    {
        try
        {
            PropertyInfo p = typeof(MainForm).GetProperty("LogPath", BindingFlags.Instance | BindingFlags.NonPublic);
            if (p != null) return Convert.ToString(p.GetValue(owner, null));
        }
        catch { }
        return null;
    }

    private static TextBox FindLogTextBox(Control root)
    {
        TextBox best = null;
        int area = -1;
        foreach (Control c in AllControls(root))
        {
            TextBox t = c as TextBox;
            if (t == null || !t.Multiline || !t.ReadOnly) continue;
            int a = t.Width * t.Height;
            if (a > area) { best = t; area = a; }
        }
        return best;
    }

    private static FlowLayoutPanel FindButtonBar(Control root)
    {
        foreach (Control c in AllControls(root))
        {
            FlowLayoutPanel p = c as FlowLayoutPanel;
            if (p == null) continue;
            foreach (Control child in p.Controls)
            {
                string tx = child.Text ?? "";
                if (tx == "Actualiser" || tx == "Refresh") return p;
            }
        }
        return null;
    }

    private static void LayoutEqualRow(Control[] controls, int x, int y, int totalW, int gap, int h)
    {
        int count = controls.Length;
        int bw = (totalW - (count - 1) * gap) / count;
        int cur = x;
        for (int i = 0; i < count; i++)
        {
            int w = (i == count - 1) ? x + totalW - cur : bw;
            Move(controls[i], cur, y, w, h);
            cur += w + gap;
        }
    }

    private static void SetSectionFont(Label l)
    {
        if (l != null) l.Font = new Font("Segoe UI", 11.25f, FontStyle.Bold, GraphicsUnit.Point);
    }

    private static void Move(Control c, int x, int y, int w, int h)
    {
        if (c == null) return;
        c.Bounds = new Rectangle(x, y, Math.Max(0, w), Math.Max(0, h));
    }

    private static void Add(List<Control> list, Control c)
    {
        if (c != null && !list.Contains(c)) list.Add(c);
    }

    private static T F<T>(string name) where T : class
    {
        try
        {
            FieldInfo f = typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            return f == null ? null : f.GetValue(owner) as T;
        }
        catch { return null; }
    }

    private static bool GetBoolField(object obj, string name)
    {
        try
        {
            FieldInfo f = obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            object v = f == null ? null : f.GetValue(obj);
            return v is bool && (bool)v;
        }
        catch { return false; }
    }

    private static Button FindButton(string[] texts)
    {
        foreach (Control c in AllControls(owner))
        {
            Button b = c as Button;
            if (b != null && MatchText(b.Text, texts)) return b;
        }
        return null;
    }

    private static Label FindLabel(string[] texts)
    {
        foreach (Control c in AllControls(owner))
        {
            Label l = c as Label;
            if (l != null && MatchText(l.Text, texts)) return l;
        }
        return null;
    }

    private static bool MatchText(string text, string[] choices)
    {
        for (int i = 0; i < choices.Length; i++)
            if (String.Equals(text ?? "", choices[i], StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static bool ContainsNamed(Control root, string name)
    {
        foreach (Control c in AllControls(root)) if (String.Equals(c.Name, name, StringComparison.Ordinal)) return true;
        return false;
    }

    private static IEnumerable<Control> AllControls(Control root)
    {
        List<Control> outv = new List<Control>();
        Collect(root, outv);
        return outv;
    }

    private static void Collect(Control root, List<Control> outv)
    {
        if (root == null) return;
        foreach (Control c in root.Controls)
        {
            outv.Add(c);
            Collect(c, outv);
        }
    }
}
