using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Management;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using ActionReplayGBX.Model;

[assembly: AssemblyTitle("ActionReplayGBX")]
[assembly: AssemblyProduct("ActionReplayGBX")]
[assembly: AssemblyCompany("ActionReplayGBX project")]
[assembly: AssemblyDescription("ActionReplayGBX v1.2.31.1 CSharp XPC editor")]
[assembly: AssemblyVersion("1.2.31.1")]
[assembly: AssemblyFileVersion("1.2.31.1")]
[assembly: AssemblyInformationalVersion("1.2.31.1-ui-driver-fix")]

internal sealed class MainForm : Form
{
    private const string DevicePrefix = "USB\\VID_05FD&PID_DAAE";

    private CodeDB pcDb = new CodeDB();
    private CodeDB arDb = new CodeDB();
    private string currentPath;
    private readonly Stack<CodeDB> undo = new Stack<CodeDB>();
    private readonly Stack<CodeDB> redo = new Stack<CodeDB>();
    private bool loading;
    private bool busy;

    private readonly CheckedListBox pcGames = new CheckedListBox();
    private readonly CheckedListBox pcCodes = new CheckedListBox();
    private readonly CheckedListBox arGames = new CheckedListBox();
    private readonly CheckedListBox arCodes = new CheckedListBox();
    private readonly GroupBox pcGamesBox = new GroupBox();
    private readonly GroupBox pcCodesBox = new GroupBox();
    private readonly GroupBox arGamesBox = new GroupBox();
    private readonly GroupBox arCodesBox = new GroupBox();
    private readonly TextBox gameName = new TextBox();
    private readonly TextBox cheatName = new TextBox();
    private readonly TextBox codeText = new TextBox();
    private readonly CheckBox masterCheck = new CheckBox();
    private readonly Label deviceStatus = new Label();
    private readonly Label recommendation = new Label();
    private readonly Label footerLeft = new Label();
    private readonly Label footerRight = new Label();
    private readonly Button undoButton = new Button();
    private readonly Button redoButton = new Button();
    private readonly Button languageButton = new Button();
    private readonly StringBuilder operationLog = new StringBuilder();

    internal MainForm()
    {
        Text = "Action Replay GBX v1.2.31.1";
        Width = 1360;
        Height = 900;
        MinimumSize = new Size(1050, 720);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9.5f);
        BackColor = SystemColors.Control;
        AllowDrop = true;
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;

        BuildLayout();

        Shown += delegate
        {
            LoadDefaultDatabase();
            RefreshDeviceStatus();
            RefreshAll();
        };
    }

    private string T(string fr, string en) { return LanguageManager.T(fr, en); }

    private void BuildLayout()
    {
        TableLayoutPanel root = new TableLayoutPanel();
        root.Dock = DockStyle.Fill;
        root.Padding = new Padding(12, 8, 12, 10);
        root.ColumnCount = 1;
        root.RowCount = 6;
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 220f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58f));
        Controls.Add(root);

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildConnectionStrip(), 0, 1);
        root.Controls.Add(BuildActionGrid(), 0, 2);
        root.Controls.Add(BuildLibraryArea(), 0, 3);
        root.Controls.Add(BuildEditorArea(), 0, 4);
        root.Controls.Add(BuildFooter(), 0, 5);
    }

    private Control BuildHeader()
    {
        Panel p = new Panel();
        p.Dock = DockStyle.Fill;

        Label title = new Label();
        title.Text = "Action Replay GBX v1.2.31.1";
        title.Font = new Font("Segoe UI Semibold", 18f);
        title.AutoSize = true;
        title.Location = new Point(2, 7);
        p.Controls.Add(title);

        languageButton.Text = LanguageManager.IsFrench ? "FR" : "EN";
        languageButton.Width = 62;
        languageButton.Height = 31;
        languageButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        languageButton.Location = new Point(p.Width - 66, 8);
        languageButton.Click += delegate { LanguageManager.ToggleAndRestart(); };
        p.Controls.Add(languageButton);
        p.Resize += delegate { languageButton.Left = Math.Max(0, p.ClientSize.Width - languageButton.Width - 2); };
        return p;
    }

    private Control BuildConnectionStrip()
    {
        TableLayoutPanel strip = new TableLayoutPanel();
        strip.Dock = DockStyle.Fill;
        strip.ColumnCount = 2;
        strip.RowCount = 1;
        strip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 61f));
        strip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 39f));

        Panel left = new Panel(); left.Dock = DockStyle.Fill;
        Label h = new Label();
        h.Text = T("JEU CONNECTÉ", "CONNECTED GAME");
        h.ForeColor = Color.FromArgb(0, 102, 204);
        h.Font = new Font("Segoe UI Semibold", 10.5f);
        h.AutoSize = true; h.Left = 2; h.Top = 2;
        deviceStatus.Left = 2; deviceStatus.Top = 25; deviceStatus.Height = 22; deviceStatus.AutoSize = true; deviceStatus.Font = new Font("Segoe UI Semibold", 9.5f);
        recommendation.Left = 2; recommendation.Top = 48; recommendation.Height = 25; recommendation.AutoSize = false; recommendation.Width = 790; recommendation.ForeColor = Color.DarkRed;
        recommendation.Text = T("Branche l'Action Replay en USB. Si WinUSB est actif mais l'AR n'est pas accessible, utilise « Pilote ».", "Connect the Action Replay by USB. If WinUSB is active but AR is not accessible, use “Driver”.");
        left.Controls.Add(h); left.Controls.Add(deviceStatus); left.Controls.Add(recommendation);
        left.Resize += delegate { recommendation.Width = Math.Max(100, left.ClientSize.Width - 8); };

        TableLayoutPanel save = new TableLayoutPanel(); save.Dock = DockStyle.Fill; save.ColumnCount = 2; save.RowCount = 2;
        save.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f)); save.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        save.RowStyles.Add(new RowStyle(SizeType.Absolute, 27f)); save.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        Label sh = new Label(); sh.Text = T("SAUVEGARDE DU JEU CONNECTÉ", "CONNECTED GAME SAVE"); sh.ForeColor = Color.FromArgb(0, 102, 204); sh.Font = new Font("Segoe UI Semibold", 10.5f); sh.Dock = DockStyle.Fill; sh.TextAlign = ContentAlignment.MiddleLeft;
        save.Controls.Add(sh, 0, 0); save.SetColumnSpan(sh, 2);
        AddGridButton(save, T("Exporter la sauvegarde", "Export save"), 0, 1, delegate { DumpSave(); });
        AddGridButton(save, T("Restaurer une sauvegarde", "Restore save"), 1, 1, delegate { RestoreSave(); });

        strip.Controls.Add(left, 0, 0); strip.Controls.Add(save, 1, 0);
        return strip;
    }

    private Control BuildActionGrid()
    {
        TableLayoutPanel g = new TableLayoutPanel();
        g.Dock = DockStyle.Fill;
        g.ColumnCount = 9;
        g.RowCount = 2;
        for (int i = 0; i < 9; i++) g.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 11.111f));
        g.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
        g.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));

        AddGridButton(g, T("Lire / actualiser l'AR", "Read / refresh AR"), 0, 0, delegate { ReadAr(); });
        AddGridButton(g, T("Écrire l'AR", "Write AR"), 1, 0, delegate { WriteAr(); });
        AddGridButton(g, T("Importer .xpc", "Import .xpc"), 2, 0, delegate { ImportXpc(); });
        AddGridButton(g, T("Exporter .xpc", "Export .xpc"), 3, 0, delegate { ExportXpc(); });
        AddGridButton(g, T("Choix bibliothèque", "Choose library"), 4, 0, delegate { ChooseLibrary(); });
        AddGridButton(g, T("Pilote", "Driver"), 5, 0, delegate { RunDriver(); });
        AddGridButton(g, T("Sauvegarde Firmware", "Firmware backup"), 6, 0, delegate { DumpFirmware(); });
        AddGridButton(g, T("Mise à jour Firmware", "Firmware update"), 7, 0, delegate { WriteFirmware(); });
        AddGridButton(g, T("Dossier", "Folder"), 8, 0, delegate { OpenBackupFolder(); });

        ConfigureGridButton(undoButton, T("← Annuler", "← Undo"), delegate { Undo(); }); g.Controls.Add(undoButton, 0, 1);
        ConfigureGridButton(redoButton, T("Rétablir →", "Redo →"), delegate { Redo(); }); g.Controls.Add(redoButton, 1, 1);
        AddGridButton(g, T("+ Nouveau jeu", "+ New game"), 2, 1, delegate { AddGame(); });
        AddGridButton(g, T("Supprimer jeu", "Delete game"), 3, 1, delegate { DeleteGame(); });
        AddGridButton(g, T("+ Nouveau code", "+ New code"), 4, 1, delegate { AddCheat(); });
        AddGridButton(g, T("Supprimer code", "Delete code"), 5, 1, delegate { DeleteCheat(); });
        AddGridButton(g, T("Fusion par master", "Merge by master"), 6, 1, delegate { MergeByMaster(); });
        AddGridButton(g, T("Enregistrer XPC", "Save XPC"), 7, 1, delegate { SaveXpc(false); });
        AddGridButton(g, T("Journal / outils", "Log / tools"), 8, 1, delegate { ShowLog(); });
        return g;
    }

    private Control BuildLibraryArea()
    {
        TableLayoutPanel area = new TableLayoutPanel();
        area.Dock = DockStyle.Fill;
        area.Padding = new Padding(0, 5, 0, 5);
        area.ColumnCount = 5;
        area.RowCount = 1;
        area.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 19f));
        area.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 27f));
        area.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 8f));
        area.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 19f));
        area.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 27f));

        ConfigureListBox(pcGames); ConfigureListBox(pcCodes); ConfigureListBox(arGames); ConfigureListBox(arCodes);
        pcGames.SelectedIndexChanged += delegate { RefreshPcCodes(); };
        pcCodes.SelectedIndexChanged += delegate { LoadSelectedPcCheat(); };
        arGames.SelectedIndexChanged += delegate { RefreshArCodes(); };

        pcGamesBox.Text = T("Bibliothèque PC — Jeux", "PC library — Games"); pcGamesBox.Dock = DockStyle.Fill; pcGamesBox.Controls.Add(pcGames);
        pcCodesBox.Text = T("Codes", "Codes"); pcCodesBox.Dock = DockStyle.Fill; pcCodesBox.Controls.Add(pcCodes);
        arGamesBox.Text = T("Action Replay — Jeux", "Action Replay — Games"); arGamesBox.Dock = DockStyle.Fill; arGamesBox.Controls.Add(arGames);
        arCodesBox.Text = T("Codes Action Replay", "Action Replay codes"); arCodesBox.Dock = DockStyle.Fill; arCodesBox.Controls.Add(arCodes);

        TableLayoutPanel transfer = new TableLayoutPanel(); transfer.Dock = DockStyle.Fill; transfer.RowCount = 4; transfer.ColumnCount = 1;
        transfer.RowStyles.Add(new RowStyle(SizeType.Percent, 35f)); transfer.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f)); transfer.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f)); transfer.RowStyles.Add(new RowStyle(SizeType.Percent, 65f));
        Button toAr = new Button(); toAr.Text = "PC → AR"; toAr.Dock = DockStyle.Fill; toAr.Margin = new Padding(5); toAr.Click += delegate { TransferPcToAr(); };
        Button toPc = new Button(); toPc.Text = "AR → PC"; toPc.Dock = DockStyle.Fill; toPc.Margin = new Padding(5); toPc.Click += delegate { TransferArToPc(); };
        transfer.Controls.Add(toAr, 0, 1); transfer.Controls.Add(toPc, 0, 2);

        area.Controls.Add(pcGamesBox, 0, 0); area.Controls.Add(pcCodesBox, 1, 0); area.Controls.Add(transfer, 2, 0); area.Controls.Add(arGamesBox, 3, 0); area.Controls.Add(arCodesBox, 4, 0);
        return area;
    }

    private Control BuildEditorArea()
    {
        GroupBox box = new GroupBox();
        box.Text = T("Éditeur de code", "Code editor");
        box.Dock = DockStyle.Fill;

        TableLayoutPanel e = new TableLayoutPanel(); e.Dock = DockStyle.Fill; e.Padding = new Padding(8, 5, 8, 8); e.ColumnCount = 4; e.RowCount = 3;
        e.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95f)); e.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48f)); e.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95f)); e.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52f));
        e.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f)); e.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f)); e.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        Label gl = new Label(); gl.Text = T("Nom du jeu", "Game name"); gl.Dock = DockStyle.Fill; gl.TextAlign = ContentAlignment.MiddleLeft;
        Label cl = new Label(); cl.Text = T("Nom du code", "Code name"); cl.Dock = DockStyle.Fill; cl.TextAlign = ContentAlignment.MiddleLeft;
        gameName.Dock = DockStyle.Fill; cheatName.Dock = DockStyle.Fill;
        masterCheck.Text = T("Code maître (M)", "Master code (M)"); masterCheck.Dock = DockStyle.Fill;
        Button apply = new Button(); apply.Text = T("Enregistrer les modifications", "Save changes"); apply.Dock = DockStyle.Fill; apply.Click += delegate { ApplyEditor(); };
        codeText.Dock = DockStyle.Fill; codeText.Multiline = true; codeText.ScrollBars = ScrollBars.Both; codeText.WordWrap = false; codeText.Font = new Font("Consolas", 10f);

        e.Controls.Add(gl, 0, 0); e.Controls.Add(gameName, 1, 0); e.Controls.Add(cl, 2, 0); e.Controls.Add(cheatName, 3, 0);
        e.Controls.Add(masterCheck, 0, 1); e.SetColumnSpan(masterCheck, 2); e.Controls.Add(apply, 3, 1);
        Label fmt = new Label(); fmt.Text = T("Codes Action Replay — format XXXXXXXX YYYYYYYY", "Action Replay codes — format XXXXXXXX YYYYYYYY"); fmt.Dock = DockStyle.Fill; fmt.TextAlign = ContentAlignment.MiddleLeft; e.Controls.Add(fmt, 0, 2);
        e.Controls.Add(codeText, 1, 2); e.SetColumnSpan(codeText, 3);
        box.Controls.Add(e);
        return box;
    }

    private Control BuildFooter()
    {
        TableLayoutPanel f = new TableLayoutPanel(); f.Dock = DockStyle.Fill; f.ColumnCount = 2; f.RowCount = 2;
        f.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f)); f.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        f.RowStyles.Add(new RowStyle(SizeType.Percent, 50f)); f.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
        footerLeft.Dock = DockStyle.Fill; footerRight.Dock = DockStyle.Fill; footerLeft.TextAlign = ContentAlignment.MiddleLeft; footerRight.TextAlign = ContentAlignment.MiddleLeft;
        Label hint = new Label(); hint.Dock = DockStyle.Fill; hint.TextAlign = ContentAlignment.MiddleLeft; hint.Text = T("Coche des jeux/codes pour les transferts. Sans case cochée, la sélection courante est utilisée.", "Check games/codes for transfers. With no checked item, the current selection is used.");
        Button refresh = new Button(); refresh.Text = T("Actualiser état USB", "Refresh USB status"); refresh.Dock = DockStyle.Right; refresh.Width = 180; refresh.Click += delegate { RefreshDeviceStatus(); };
        f.Controls.Add(footerLeft, 0, 0); f.Controls.Add(footerRight, 1, 0); f.Controls.Add(hint, 0, 1); f.Controls.Add(refresh, 1, 1);
        return f;
    }

    private static void ConfigureListBox(CheckedListBox list)
    {
        list.Dock = DockStyle.Fill;
        list.CheckOnClick = true;
        list.IntegralHeight = false;
        list.HorizontalScrollbar = true;
    }

    private static void ConfigureGridButton(Button b, string text, EventHandler handler)
    {
        b.Text = text; b.Dock = DockStyle.Fill; b.Margin = new Padding(4, 3, 4, 3); b.Click += handler;
    }

    private static void AddGridButton(TableLayoutPanel p, string text, int col, int row, EventHandler handler)
    {
        Button b = new Button(); ConfigureGridButton(b, text, handler); p.Controls.Add(b, col, row);
    }

    private void LoadDefaultDatabase()
    {
        string p = Path.Combine(Application.StartupPath, "PCDatabase.xpc");
        if (!File.Exists(p)) return;
        try { pcDb = CodeDB.LoadXPC(p); currentPath = p; }
        catch (Exception ex) { Error(T("Impossible de charger la bibliothèque par défaut : ", "Unable to load default library: ") + ex.Message); }
    }

    private void RefreshAll()
    {
        RefreshPcGames(); RefreshArGames(); UpdateFooter(); UpdateUndoButtons();
    }

    private void RefreshPcGames()
    {
        if (loading) return;
        loading = true;
        try
        {
            int selected = pcGames.SelectedIndex;
            pcGames.Items.Clear();
            foreach (Game g in pcDb.Games) pcGames.Items.Add(g.Name + "  (" + g.Cheats.Count + ")", false);
            if (pcGames.Items.Count > 0) pcGames.SelectedIndex = Math.Min(Math.Max(selected, 0), pcGames.Items.Count - 1);
        }
        finally { loading = false; }
        RefreshPcCodes();
    }

    private void RefreshPcCodes()
    {
        if (loading) return;
        loading = true;
        try
        {
            pcCodes.Items.Clear();
            int gi = pcGames.SelectedIndex;
            if (gi >= 0 && gi < pcDb.Games.Count)
            {
                Game g = pcDb.Games[gi]; pcCodesBox.Text = T("Codes — ", "Codes — ") + g.Name + " (" + g.Cheats.Count + ")";
                foreach (Cheat c in g.Cheats) pcCodes.Items.Add(c.Name + (((c.Flags & 1u) != 0 || CodeModel.LooksLikeMasterName(c.Name)) ? "  [M]" : ""), false);
                if (pcCodes.Items.Count > 0) pcCodes.SelectedIndex = 0;
            }
            else { pcCodesBox.Text = T("Codes", "Codes"); ClearEditor(); }
        }
        finally { loading = false; }
        LoadSelectedPcCheat();
    }

    private void RefreshArGames()
    {
        if (loading) return;
        loading = true;
        try
        {
            int selected = arGames.SelectedIndex;
            arGames.Items.Clear();
            foreach (Game g in arDb.Games) arGames.Items.Add(g.Name + "  (" + g.Cheats.Count + ")", false);
            if (arGames.Items.Count > 0) arGames.SelectedIndex = Math.Min(Math.Max(selected, 0), arGames.Items.Count - 1);
        }
        finally { loading = false; }
        RefreshArCodes();
    }

    private void RefreshArCodes()
    {
        if (loading) return;
        loading = true;
        try
        {
            arCodes.Items.Clear();
            int gi = arGames.SelectedIndex;
            if (gi >= 0 && gi < arDb.Games.Count)
            {
                Game g = arDb.Games[gi]; arCodesBox.Text = T("Codes Action Replay — ", "Action Replay codes — ") + g.Name;
                foreach (Cheat c in g.Cheats) arCodes.Items.Add(c.Name + (((c.Flags & 1u) != 0 || CodeModel.LooksLikeMasterName(c.Name)) ? "  [M]" : ""), false);
                if (arCodes.Items.Count > 0) arCodes.SelectedIndex = 0;
            }
            else arCodesBox.Text = T("Codes Action Replay", "Action Replay codes");
        }
        finally { loading = false; }
    }

    private void LoadSelectedPcCheat()
    {
        if (loading) return;
        int gi = pcGames.SelectedIndex, ci = pcCodes.SelectedIndex;
        if (gi < 0 || ci < 0 || gi >= pcDb.Games.Count || ci >= pcDb.Games[gi].Cheats.Count) { ClearEditor(); return; }
        Cheat c = pcDb.Games[gi].Cheats[ci];
        gameName.Text = pcDb.Games[gi].Name; cheatName.Text = c.Name; masterCheck.Checked = (c.Flags & 1u) != 0 || CodeModel.LooksLikeMasterName(c.Name); codeText.Text = CodeModel.FormatCodeText(c.Words);
    }

    private void ClearEditor() { gameName.Text = ""; cheatName.Text = ""; masterCheck.Checked = false; codeText.Text = ""; }

    private void PushUndo() { undo.Push(pcDb.Clone()); redo.Clear(); UpdateUndoButtons(); }
    private void UpdateUndoButtons() { undoButton.Enabled = undo.Count > 0; redoButton.Enabled = redo.Count > 0; }
    private void Undo() { if (undo.Count == 0) return; redo.Push(pcDb.Clone()); pcDb = undo.Pop(); RefreshPcGames(); UpdateFooter(); UpdateUndoButtons(); }
    private void Redo() { if (redo.Count == 0) return; undo.Push(pcDb.Clone()); pcDb = redo.Pop(); RefreshPcGames(); UpdateFooter(); UpdateUndoButtons(); }

    private void ApplyEditor()
    {
        int gi = pcGames.SelectedIndex, ci = pcCodes.SelectedIndex;
        if (gi < 0 || ci < 0) { Error(T("Sélectionne un code dans la bibliothèque PC.", "Select a code in the PC library.")); return; }
        string gn = gameName.Text.Trim(), cn = cheatName.Text.Trim();
        if (gn.Length == 0 || cn.Length == 0) { Error(T("Les noms du jeu et du code ne peuvent pas être vides.", "Game and code names cannot be empty.")); return; }
        List<uint> words;
        try { words = CodeModel.ParseCodeText(codeText.Text); }
        catch (Exception ex) { Error(T("Code invalide : ", "Invalid code: ") + ex.Message); return; }
        PushUndo();
        Game g = pcDb.Games[gi]; Cheat c = g.Cheats[ci]; g.Name = gn; c.Name = cn; c.Words = words;
        if (masterCheck.Checked) c.Flags |= 1u; else c.Flags &= ~1u;
        pcDb.SortGames(); RefreshPcGames(); SelectPcGameByName(gn); UpdateFooter();
    }

    private void AddGame()
    {
        string name = PromptText(T("Nouveau jeu", "New game"), T("Nom du jeu :", "Game name:"), "");
        if (name == null || name.Trim().Length == 0) return;
        PushUndo(); Game g = new Game(); g.Name = name.Trim(); pcDb.Games.Add(g); pcDb.SortGames(); RefreshPcGames(); SelectPcGameByName(g.Name); UpdateFooter();
    }

    private void DeleteGame()
    {
        int gi = pcGames.SelectedIndex; if (gi < 0) return;
        if (MessageBox.Show(this, T("Supprimer ce jeu et tous ses codes ?", "Delete this game and all its codes?"), T("Suppression", "Delete"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        PushUndo(); pcDb.Games.RemoveAt(gi); RefreshPcGames(); UpdateFooter();
    }

    private void AddCheat()
    {
        int gi = pcGames.SelectedIndex; if (gi < 0) return;
        string name = PromptText(T("Nouveau code", "New code"), T("Nom du code :", "Code name:"), T("Nouveau code", "New code"));
        if (name == null || name.Trim().Length == 0) return;
        PushUndo(); Cheat c = new Cheat(); c.Name = name.Trim(); pcDb.Games[gi].Cheats.Add(c); RefreshPcCodes(); pcCodes.SelectedIndex = pcCodes.Items.Count - 1; UpdateFooter();
    }

    private void DeleteCheat()
    {
        int gi = pcGames.SelectedIndex, ci = pcCodes.SelectedIndex; if (gi < 0 || ci < 0) return;
        if (MessageBox.Show(this, T("Supprimer ce code ?", "Delete this code?"), T("Suppression", "Delete"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        PushUndo(); pcDb.Games[gi].Cheats.RemoveAt(ci); RefreshPcCodes(); UpdateFooter();
    }

    private void MergeByMaster()
    {
        List<List<string>> groups = pcDb.PreviewMasterCodeMerges();
        if (groups.Count == 0) { Info(T("Aucun groupe partageant le même master code.", "No groups share the same master code.")); return; }
        if (MessageBox.Show(this, T("Fusionner automatiquement les jeux partageant le même master code ?", "Automatically merge games sharing the same master code?"), T("Fusion par master", "Merge by master"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        PushUndo(); pcDb.CoalesceByMasterCode(); RefreshPcGames(); UpdateFooter();
    }

    private void SelectPcGameByName(string name)
    {
        for (int i = 0; i < pcDb.Games.Count; i++) if (String.Equals(pcDb.Games[i].Name, name, StringComparison.OrdinalIgnoreCase)) { pcGames.SelectedIndex = i; return; }
    }

    private static List<int> TransferGameIndices(CheckedListBox list)
    {
        List<int> r = new List<int>(); foreach (int i in list.CheckedIndices) r.Add(i); if (r.Count == 0 && list.SelectedIndex >= 0) r.Add(list.SelectedIndex); return r;
    }

    private CodeDB BuildSelection(CodeDB source, CheckedListBox gamesList, CheckedListBox codesList)
    {
        CodeDB selected = new CodeDB();
        List<int> gamesIx = TransferGameIndices(gamesList);
        foreach (int gi in gamesIx)
        {
            if (gi < 0 || gi >= source.Games.Count) continue;
            Game original = source.Games[gi]; Game g = new Game(); g.Name = original.Name;
            bool useCheckedCodes = gamesIx.Count == 1 && codesList.CheckedIndices.Count > 0;
            if (useCheckedCodes)
            {
                foreach (int ci in codesList.CheckedIndices) if (ci >= 0 && ci < original.Cheats.Count) g.Cheats.Add(original.Cheats[ci].Clone());
            }
            else foreach (Cheat c in original.Cheats) g.Cheats.Add(c.Clone());
            selected.Games.Add(g);
        }
        return selected;
    }

    private void TransferPcToAr()
    {
        CodeDB sel = BuildSelection(pcDb, pcGames, pcCodes); if (sel.Games.Count == 0) return;
        arDb.Merge(sel); RefreshArGames(); UpdateFooter();
    }

    private void TransferArToPc()
    {
        CodeDB sel = BuildSelection(arDb, arGames, arCodes); if (sel.Games.Count == 0) return;
        PushUndo(); pcDb.Merge(sel); RefreshPcGames(); UpdateFooter();
    }

    private void ReadAr()
    {
        string tmp = Path.Combine(BackupDirectory(), "argbx-read-codes-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".bin");
        RunEngine(new string[] { "dump-codes", tmp }, delegate
        {
            try { arDb = CodeDB.LoadBlob(tmp); RefreshArGames(); UpdateFooter(); }
            catch (Exception ex) { Error(T("Lecture terminée mais parsing impossible : ", "Read completed but parsing failed: ") + ex.Message); }
        });
    }

    private void WriteAr()
    {
        if (arDb.Games.Count == 0) { Error(T("La colonne Action Replay est vide. Lis l'AR ou transfère d'abord des jeux depuis la bibliothèque PC.", "The Action Replay column is empty. Read AR or transfer games from the PC library first.")); return; }
        if (MessageBox.Show(this, T("Écrire la colonne Action Replay dans l'appareil ? L'engine fera un backup puis une vérification byte-for-byte.", "Write the Action Replay column to the device? The engine will create a backup and verify byte-for-byte."), T("Écriture Action Replay", "Action Replay write"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        try
        {
            CodeDB safe = arDb.ARSafeCopy(); string tmp = Path.Combine(BackupDirectory(), "argbx-write-codes-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".bin"); File.WriteAllBytes(tmp, safe.Blob()); RunEngine(new string[] { "write-codes", tmp, "--enable-write" }, null);
        }
        catch (Exception ex) { Error(ex.Message); }
    }

    private void ImportXpc()
    {
        string p = ChooseOpen(T("Importer une base XPC", "Import an XPC database"), "XPC (*.xpc)|*.xpc|*.*|*.*"); if (p == null) return;
        try { CodeDB incoming = CodeDB.LoadXPC(p); PushUndo(); pcDb.Merge(incoming); RefreshPcGames(); UpdateFooter(); }
        catch (Exception ex) { Error(ex.Message); }
    }

    private void ExportXpc()
    {
        string p = ChooseSave(T("Exporter la bibliothèque XPC", "Export XPC library"), "XPC (*.xpc)|*.xpc", "PCDatabase.xpc"); if (p == null) return;
        try { pcDb.SaveXPC(p); currentPath = p; UpdateFooter(); }
        catch (Exception ex) { Error(ex.Message); }
    }

    private void SaveXpc(bool forceDialog)
    {
        string p = currentPath;
        if (forceDialog || String.IsNullOrEmpty(p) || !p.EndsWith(".xpc", StringComparison.OrdinalIgnoreCase)) p = ChooseSave(T("Enregistrer la bibliothèque XPC", "Save XPC library"), "XPC (*.xpc)|*.xpc", "PCDatabase.xpc");
        if (p == null) return;
        try { pcDb.SaveXPC(p); currentPath = p; UpdateFooter(); }
        catch (Exception ex) { Error(ex.Message); }
    }

    private void ChooseLibrary()
    {
        using (Form f = new Form())
        {
            f.Text = T("Choix bibliothèque", "Choose library"); f.Width = 460; f.Height = 260; f.StartPosition = FormStartPosition.CenterParent; f.FormBorderStyle = FormBorderStyle.FixedDialog; f.MaximizeBox = false; f.MinimizeBox = false; f.AutoScaleMode = AutoScaleMode.Dpi; f.Font = Font;
            string[] files = new string[] { "PCDatabase.xpc", "PCDatabase-Datel.xpc", "PCDatabase-EuropeMAX-v7.xpc" };
            string[] labels = new string[] { T("Bibliothèque PC principale", "Main PC library"), "Datel", "Europe MAX v7" };
            for (int i = 0; i < files.Length; i++)
            {
                Button b = new Button(); b.Left = 25; b.Top = 20 + i * 52; b.Width = 400; b.Height = 40; b.Text = labels[i] + " — " + files[i]; string path = Path.Combine(Application.StartupPath, files[i]);
                b.Click += delegate(object sender, EventArgs e) { Button clicked = (Button)sender; string fn = Convert.ToString(clicked.Tag); try { pcDb = CodeDB.LoadXPC(fn); currentPath = fn; undo.Clear(); redo.Clear(); f.DialogResult = DialogResult.OK; f.Close(); } catch (Exception ex) { Error(ex.Message); } }; b.Tag = path; f.Controls.Add(b);
            }
            f.ShowDialog(this); RefreshPcGames(); UpdateFooter(); UpdateUndoButtons();
        }
    }

    private void RefreshDeviceStatus()
    {
        try
        {
            string found = null, service = null, name = null;
            using (ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT PNPDeviceID, Service, Name FROM Win32_PnPEntity"))
            using (ManagementObjectCollection results = s.Get())
            foreach (ManagementObject item in results)
            {
                string id = Convert.ToString(item["PNPDeviceID"]); if (String.IsNullOrEmpty(id) || !id.StartsWith(DevicePrefix, StringComparison.OrdinalIgnoreCase)) continue;
                found = id; service = Convert.ToString(item["Service"]); name = Convert.ToString(item["Name"]); break;
            }
            if (found == null) deviceStatus.Text = T("Aucun Action Replay USB détecté", "No Action Replay USB detected");
            else deviceStatus.Text = T("Action Replay détecté — ", "Action Replay detected — ") + (String.IsNullOrEmpty(name) ? "USB" : name) + " — service : " + (String.IsNullOrEmpty(service) ? "?" : service);
        }
        catch (Exception ex) { deviceStatus.Text = T("Erreur WMI : ", "WMI error: ") + ex.Message; }
    }

    private static string EnginePath() { return Path.Combine(Application.StartupPath, "argbx-engine_v1.2.31.1.exe"); }
    private static string DriverPath() { return Path.Combine(Application.StartupPath, "ActionReplayGBX-Driver_v1.2.31.1.exe"); }
    private static string BackupDirectory() { string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ActionReplayGBX Backups"); Directory.CreateDirectory(d); return d; }
    private static string Quote(string s) { if (s == null) return "\"\""; return "\"" + s.Replace("\"", "\\\"") + "\""; }
    private static string BuildArguments(string[] args) { StringBuilder b = new StringBuilder(); for (int i = 0; i < args.Length; i++) { if (i != 0) b.Append(' '); b.Append(Quote(args[i])); } return b.ToString(); }

    private void RunDriver()
    {
        RunComponent(DriverPath(), new string[] { "--apply" }, Application.StartupPath, delegate
        {
            RefreshDeviceStatus();
            RunEngine(new string[] { "info" }, null);
        });
    }

    private void RunEngine(string[] args, Action completed) { RunComponent(EnginePath(), args, BackupDirectory(), completed); }

    private void RunComponent(string exe, string[] args, string work, Action completed)
    {
        if (busy) return; if (!File.Exists(exe)) { Error(T("Composant absent : ", "Missing component: ") + exe); return; }
        busy = true; UseWaitCursor = true; footerLeft.Text = T("Opération en cours…", "Operation in progress…");
        string command = "> " + Path.GetFileName(exe) + " " + BuildArguments(args); AppendLog(command);
        ThreadPool.QueueUserWorkItem(delegate
        {
            int exit = -1; string output = "", error = "";
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(); psi.FileName = exe; psi.Arguments = BuildArguments(args); psi.WorkingDirectory = work; psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi)) { output = p.StandardOutput.ReadToEnd(); error = p.StandardError.ReadToEnd(); p.WaitForExit(); exit = p.ExitCode; }
            }
            catch (Exception ex) { error = ex.ToString(); }
            if (!String.IsNullOrWhiteSpace(output)) AppendLog(output); if (!String.IsNullOrWhiteSpace(error)) AppendLog(error);
            BeginInvoke(new Action(delegate
            {
                busy = false; UseWaitCursor = false; footerLeft.Text = exit == 0 ? T("Opération terminée.", "Operation completed.") : T("Échec — code ", "Failure — code ") + exit;
                if (exit == 0 && completed != null) completed();
                else if (exit != 0 && !String.IsNullOrWhiteSpace(error)) MessageBox.Show(this, error, "ActionReplayGBX", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }));
        });
    }

    private void AppendLog(string text)
    {
        lock (operationLog) { operationLog.AppendLine("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + text.TrimEnd()); }
    }

    private void ShowLog()
    {
        using (Form f = new Form())
        {
            f.Text = T("Journal / outils", "Log / tools"); f.Width = 900; f.Height = 560; f.StartPosition = FormStartPosition.CenterParent; f.AutoScaleMode = AutoScaleMode.Dpi; f.Font = Font;
            TextBox t = new TextBox(); t.Dock = DockStyle.Fill; t.Multiline = true; t.ReadOnly = true; t.ScrollBars = ScrollBars.Both; t.WordWrap = false; t.Font = new Font("Consolas", 9f); lock (operationLog) t.Text = operationLog.ToString();
            FlowLayoutPanel b = new FlowLayoutPanel(); b.Dock = DockStyle.Top; b.Height = 44;
            Button info = new Button(); info.Text = T("Infos Action Replay", "Action Replay info"); info.Width = 155; info.Height = 32; info.Click += delegate { f.Close(); RunEngine(new string[] { "info" }, null); };
            Button dump = new Button(); dump.Text = T("Dump codes → BIN", "Dump codes → BIN"); dump.Width = 155; dump.Height = 32; dump.Click += delegate { string p = ChooseSave(T("Sauvegarder la base binaire", "Save binary database"), "BIN (*.bin)|*.bin", "ActionReplayGBX-codes.bin"); if (p != null) { f.Close(); RunEngine(new string[] { "dump-codes", p }, null); } };
            Button disconnect = new Button(); disconnect.Text = T("Déconnecter", "Disconnect"); disconnect.Width = 140; disconnect.Height = 32; disconnect.Click += delegate { f.Close(); RunEngine(new string[] { "disconnect" }, null); };
            b.Controls.Add(info); b.Controls.Add(dump); b.Controls.Add(disconnect); f.Controls.Add(t); f.Controls.Add(b); f.ShowDialog(this);
        }
    }

    private void DumpSave() { string p = ChooseSave(T("Exporter la sauvegarde", "Export save"), "GBA SAVE (*.sav)|*.sav", "gba-save.sav"); if (p != null) RunEngine(new string[] { "dump-save", p }, null); }
    private void RestoreSave() { string p = ChooseOpen(T("Restaurer une sauvegarde", "Restore save"), "GBA SAVE (*.sav)|*.sav|*.*|*.*"); if (p == null) return; if (new FileInfo(p).Length != 0x10000) { Error(T("Le fichier doit faire exactement 65536 octets.", "The file must be exactly 65536 bytes.")); return; } if (MessageBox.Show(this, T("Remplacer la sauvegarde de la cartouche ?", "Replace cartridge save data?"), T("Restauration SAVE", "SAVE restore"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes) RunEngine(new string[] { "write-save", p, "--enable-write" }, null); }
    private void DumpFirmware() { string p = ChooseSave(T("Sauvegarde complète Flash 256 Kio", "Full 256 KiB Flash backup"), "BIN (*.bin)|*.bin", "ActionReplayGBX-flash-256K.bin"); if (p != null) RunEngine(new string[] { "dump-firmware", p }, null); }
    private void WriteFirmware() { string p = ChooseOpen(T("Sélectionner le firmware", "Select firmware"), "Firmware (*.gsu;*.bin)|*.gsu;*.bin|*.*|*.*"); if (p == null) return; if (MessageBox.Show(this, T("L'écriture firmware reste une opération à risque. Continuer ?", "Firmware writing remains a risky operation. Continue?"), T("ÉCRITURE FIRMWARE", "FIRMWARE WRITE"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return; RunEngine(new string[] { "write-firmware", p, "--enable-firmware-write" }, null); }
    private void OpenBackupFolder() { try { Process.Start(new ProcessStartInfo(BackupDirectory()) { UseShellExecute = true }); } catch (Exception ex) { Error(ex.Message); } }

    private void UpdateFooter()
    {
        string pcFile = String.IsNullOrEmpty(currentPath) ? T("non enregistrée", "unsaved") : Path.GetFileName(currentPath);
        footerLeft.Text = pcFile + " — " + pcDb.Games.Count + T(" jeux / ", " games / ") + pcDb.CheatCount() + T(" codes", " codes");
        footerRight.Text = T("Action Replay : ", "Action Replay: ") + arDb.Games.Count + T(" jeux / ", " games / ") + arDb.CheatCount() + T(" codes", " codes");
        Text = "Action Replay GBX v1.2.31.1 — " + pcFile;
    }

    private string ChooseOpen(string title, string filter) { using (OpenFileDialog d = new OpenFileDialog()) { d.Title = title; d.Filter = filter; d.CheckFileExists = true; return d.ShowDialog(this) == DialogResult.OK ? d.FileName : null; } }
    private string ChooseSave(string title, string filter, string name) { using (SaveFileDialog d = new SaveFileDialog()) { d.Title = title; d.Filter = filter; d.FileName = name; d.OverwritePrompt = true; return d.ShowDialog(this) == DialogResult.OK ? d.FileName : null; } }
    private void Error(string text) { MessageBox.Show(this, text, "ActionReplayGBX", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    private void Info(string text) { MessageBox.Show(this, text, "ActionReplayGBX", MessageBoxButtons.OK, MessageBoxIcon.Information); }

    private string PromptText(string title, string label, string value)
    {
        using (Form f = new Form())
        {
            f.Text = title; f.Width = 500; f.Height = 175; f.StartPosition = FormStartPosition.CenterParent; f.FormBorderStyle = FormBorderStyle.FixedDialog; f.MinimizeBox = false; f.MaximizeBox = false; f.AutoScaleMode = AutoScaleMode.Dpi; f.Font = Font;
            Label l = new Label(); l.Text = label; l.Left = 14; l.Top = 14; l.Width = 445;
            TextBox t = new TextBox(); t.Left = 14; t.Top = 39; t.Width = 455; t.Text = value;
            Button ok = new Button(); ok.Text = "OK"; ok.DialogResult = DialogResult.OK; ok.Left = 300; ok.Top = 78; ok.Width = 80;
            Button cancel = new Button(); cancel.Text = T("Annuler", "Cancel"); cancel.DialogResult = DialogResult.Cancel; cancel.Left = 389; cancel.Top = 78; cancel.Width = 80;
            f.Controls.Add(l); f.Controls.Add(t); f.Controls.Add(ok); f.Controls.Add(cancel); f.AcceptButton = ok; f.CancelButton = cancel;
            return f.ShowDialog(this) == DialogResult.OK ? t.Text : null;
        }
    }

    private void OnDragEnter(object sender, DragEventArgs e) { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; }
    private void OnDragDrop(object sender, DragEventArgs e)
    {
        string[] files = e.Data.GetData(DataFormats.FileDrop) as string[]; if (files == null || files.Length == 0) return; string p = files[0];
        try
        {
            if (p.EndsWith(".xpc", StringComparison.OrdinalIgnoreCase)) { pcDb = CodeDB.LoadXPC(p); currentPath = p; undo.Clear(); redo.Clear(); RefreshPcGames(); UpdateFooter(); }
            else Error(T("Dépose un fichier .xpc.", "Drop an .xpc file."));
        }
        catch (Exception ex) { Error(ex.Message); }
    }
}