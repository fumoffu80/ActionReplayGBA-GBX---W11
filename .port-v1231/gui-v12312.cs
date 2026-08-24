using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Management;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ActionReplayGBX.Model;

[assembly: AssemblyTitle("ActionReplayGBX")]
[assembly: AssemblyProduct("ActionReplayGBX")]
[assembly: AssemblyCompany("ActionReplayGBX project")]
[assembly: AssemblyDescription("ActionReplayGBX v1.2.31.2 CSharp v1.2.16 parity UI")]
[assembly: AssemblyVersion("1.2.31.2")]
[assembly: AssemblyFileVersion("1.2.31.2")]
[assembly: AssemblyInformationalVersion("1.2.31.2-v1216-parity")]

internal sealed class MainForm : Form
{
    private const string VersionText = "1.2.31.2";
    private const string DevicePrefix = "USB\\VID_05FD&PID_DAAE";
    private const int HistoryLimit = 50;

    private readonly string exeDir;
    private readonly string dataDir;
    private readonly string backupDir;
    private readonly string logDir;
    private readonly string libraryDir;
    private readonly string cacheDir;
    private readonly string enginePath;
    private readonly string driverPath;

    private CodeDB pcDb = new CodeDB();
    private CodeDB arDb = new CodeDB();
    private string libraryMode = "datel";

    private int activeSource = 0; // 0 PC, 1 AR
    private int pcSelectedGame = -1, pcSelectedCheat = -1;
    private int arSelectedGame = -1, arSelectedCheat = -1;
    private int lastPcKind = 0, lastArKind = 0; // 0 game, 1 code

    private readonly HashSet<string> pcQueuedGames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> pcQueuedCodes = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> arQueuedGames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> arQueuedCodes = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

    private sealed class HistoryEntry
    {
        internal string Label;
        internal CodeDB Pc;
        internal CodeDB Ar;
    }
    private readonly Stack<HistoryEntry> undo = new Stack<HistoryEntry>();
    private readonly Stack<HistoryEntry> redo = new Stack<HistoryEntry>();

    private bool loadingEditor;
    private bool editDirty;
    private bool refreshingChecks;
    private bool suppressSelection;
    private bool busy;
    private int editorMode; // 0 normal, 1 new game, 2 new code
    private int editorSource;
    private int editorGame = -1, editorCheat = -1;

    private bool usbPresent;
    private string usbService = "";
    private string usbName = "";
    private bool deviceConnected;
    private string deviceGame = "";
    private string deviceGameId = "";
    private string usbVersion = "";
    private int remainingStorage = -1;
    private bool autoConnectRunning;
    private int autoRecoveryStage;
    private bool arLoadedThisConnection;

    private readonly StringBuilder operationLog = new StringBuilder();

    private readonly Label titleLabel = new Label();
    private readonly Button languageButton = new Button();
    private readonly Label deviceTitle = new Label();
    private readonly Label deviceNameLabel = new Label();
    private readonly Label deviceDetailsLabel = new Label();
    private readonly Label connectionWarning = new Label();
    private readonly PictureBox boxArt = new PictureBox();
    private readonly Label saveTitle = new Label();

    private readonly Button readButton = new Button();
    private readonly Button writeButton = new Button();
    private readonly Button importButton = new Button();
    private readonly Button exportButton = new Button();
    private readonly Button libraryButton = new Button();
    private readonly Button driverButton = new Button();
    private readonly Button firmwareBackupButton = new Button();
    private readonly Button firmwareUpdateButton = new Button();
    private readonly Button folderButton = new Button();
    private readonly Button undoButton = new Button();
    private readonly Button redoButton = new Button();
    private readonly Button journalButton = new Button();

    private readonly CheckedListBox pcGames = new CheckedListBox();
    private readonly CheckedListBox pcCodes = new CheckedListBox();
    private readonly CheckedListBox arGames = new CheckedListBox();
    private readonly CheckedListBox arCodes = new CheckedListBox();

    private readonly Label pcGamesTitle = new Label();
    private readonly Label pcCodesTitle = new Label();
    private readonly Label arGamesTitle = new Label();
    private readonly Label arCodesTitle = new Label();

    private readonly Button toArButton = new Button();
    private readonly Button toPcButton = new Button();
    private readonly Button newGameButton = new Button();
    private readonly Button deleteGameButton = new Button();
    private readonly Button newCodeButton = new Button();
    private readonly Button deleteCodeButton = new Button();

    private readonly TextBox gameNameText = new TextBox();
    private readonly TextBox cheatNameText = new TextBox();
    private readonly TextBox codeText = new TextBox();
    private readonly CheckBox masterCheck = new CheckBox();
    private readonly Button applyButton = new Button();
    private readonly Button cancelButton = new Button();
    private readonly Label editorHint = new Label();

    private readonly Label transferText = new Label();
    private readonly Label storageText = new Label();
    private readonly ProgressBar transferProgress = new ProgressBar();
    private readonly ProgressBar storageProgress = new ProgressBar();
    private readonly Label bottomStatus = new Label();

    private readonly ToolTip toolTip = new ToolTip();
    private readonly System.Windows.Forms.Timer autoTimer = new System.Windows.Forms.Timer();

    private static readonly Regex PercentRx = new Regex(@"(\d+(?:[.,]\d+)?)\s*%", RegexOptions.Compiled);
    private static readonly Regex FractionRx = new Regex(@"(\d+)\s*/\s*(\d+)", RegexOptions.Compiled);

    internal MainForm()
    {
        exeDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        dataDir = Path.Combine(local, "ActionReplayGBX");
        backupDir = Path.Combine(dataDir, "Backups");
        logDir = Path.Combine(dataDir, "Logs");
        libraryDir = Path.Combine(dataDir, "Library");
        cacheDir = Path.Combine(dataDir, "Cache");
        Directory.CreateDirectory(backupDir);
        Directory.CreateDirectory(logDir);
        Directory.CreateDirectory(libraryDir);
        Directory.CreateDirectory(cacheDir);

        enginePath = Path.Combine(exeDir, "argbx-engine_v1.2.31.2.exe");
        driverPath = Path.Combine(exeDir, "ActionReplayGBX-Driver_v1.2.31.2.exe");

        Text = "Action Replay GBX v" + VersionText + " — " + T("Gestionnaire de codes", "Code Manager");
        Width = 1360;
        Height = Math.Min(930, Screen.PrimaryScreen.WorkingArea.Height);
        MinimumSize = new Size(900, 700);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9.25f);
        BackColor = Color.FromArgb(243, 243, 243);
        AllowDrop = true;
        KeyPreview = true;

        BuildLayout();
        ConfigureEvents();
        ConfigureTooltips();

        Shown += delegate
        {
            LoadLibraryMode(false);
            RefreshWmi();
            RefreshAll();
            AppendLog("Action Replay GBX v" + VersionText + " started — data: " + dataDir);
            autoTimer.Interval = 2500;
            autoTimer.Tick += delegate { AutoTick(); };
            autoTimer.Start();
            BeginAutoConnect(true);
        };
        FormClosing += delegate { if (editDirty) CommitEditor(false); };
    }

    private string T(string fr, string en) { return LanguageManager.T(fr, en); }

    private void BuildLayout()
    {
        SuspendLayout();

        TableLayoutPanel root = new TableLayoutPanel();
        root.Dock = DockStyle.Fill;
        root.Padding = new Padding(14, 8, 14, 10);
        root.ColumnCount = 1;
        root.RowCount = 6;
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 126f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 205f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82f));
        Controls.Add(root);

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildDeviceArea(), 0, 1);
        root.Controls.Add(BuildToolbar(), 0, 2);
        root.Controls.Add(BuildListsArea(), 0, 3);
        root.Controls.Add(BuildEditor(), 0, 4);
        root.Controls.Add(BuildBottomArea(), 0, 5);

        ResumeLayout(true);
    }

    private Control BuildHeader()
    {
        Panel p = new Panel(); p.Dock = DockStyle.Fill;
        titleLabel.Text = "Action Replay GBX v" + VersionText;
        titleLabel.Font = new Font("Segoe UI Semibold", 18f);
        titleLabel.AutoSize = true;
        titleLabel.Location = new Point(0, 5);
        p.Controls.Add(titleLabel);

        languageButton.Text = LanguageManager.IsFrench ? "FR" : "EN";
        languageButton.Size = new Size(62, 31);
        languageButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        languageButton.Location = new Point(1000, 5);
        p.Controls.Add(languageButton);
        p.Resize += delegate { languageButton.Left = Math.Max(0, p.ClientSize.Width - languageButton.Width); };
        return p;
    }

    private Control BuildDeviceArea()
    {
        TableLayoutPanel outer = new TableLayoutPanel();
        outer.Dock = DockStyle.Fill;
        outer.ColumnCount = 2;
        outer.RowCount = 1;
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 61f));
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 39f));

        Panel left = new Panel(); left.Dock = DockStyle.Fill;
        boxArt.Size = new Size(92, 120);
        boxArt.Location = new Point(0, 0);
        boxArt.SizeMode = PictureBoxSizeMode.Zoom;
        boxArt.BackColor = BackColor;
        boxArt.Visible = false;
        left.Controls.Add(boxArt);

        deviceTitle.Text = T("JEU CONNECTÉ", "CONNECTED GAME");
        deviceTitle.ForeColor = Color.FromArgb(0, 102, 204);
        deviceTitle.Font = new Font("Segoe UI Semibold", 10.5f);
        deviceTitle.AutoSize = true;
        deviceTitle.Location = new Point(0, 0);
        left.Controls.Add(deviceTitle);

        deviceNameLabel.Font = new Font("Segoe UI Semibold", 10f);
        deviceNameLabel.AutoSize = false;
        deviceNameLabel.Location = new Point(0, 24);
        deviceNameLabel.Height = 24;
        deviceNameLabel.Width = 700;
        left.Controls.Add(deviceNameLabel);

        deviceDetailsLabel.AutoSize = false;
        deviceDetailsLabel.Location = new Point(0, 48);
        deviceDetailsLabel.Height = 24;
        deviceDetailsLabel.Width = 700;
        left.Controls.Add(deviceDetailsLabel);

        connectionWarning.ForeColor = Color.FromArgb(210, 0, 0);
        connectionWarning.Font = new Font("Segoe UI Semibold", 9.25f);
        connectionWarning.AutoSize = false;
        connectionWarning.Location = new Point(0, 75);
        connectionWarning.Height = 46;
        connectionWarning.Width = 700;
        left.Controls.Add(connectionWarning);

        left.Resize += delegate
        {
            int x = boxArt.Visible ? 102 : 0;
            deviceTitle.Left = x;
            deviceNameLabel.Left = x;
            deviceDetailsLabel.Left = x;
            connectionWarning.Left = x;
            int w = Math.Max(100, left.ClientSize.Width - x - 5);
            deviceNameLabel.Width = w;
            deviceDetailsLabel.Width = w;
            connectionWarning.Width = w;
        };

        TableLayoutPanel savePanel = new TableLayoutPanel();
        savePanel.Dock = DockStyle.Fill;
        savePanel.ColumnCount = 2;
        savePanel.RowCount = 2;
        savePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        savePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        savePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
        savePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
        saveTitle.Text = T("SAUVEGARDE DU JEU CONNECTÉ", "CONNECTED GAME SAVE");
        saveTitle.ForeColor = Color.FromArgb(0, 102, 204);
        saveTitle.Font = new Font("Segoe UI Semibold", 10.5f);
        saveTitle.Dock = DockStyle.Fill;
        saveTitle.TextAlign = ContentAlignment.MiddleLeft;
        savePanel.Controls.Add(saveTitle, 0, 0); savePanel.SetColumnSpan(saveTitle, 2);
        Button exportSave = MakeButton(T("Exporter la sauvegarde", "Export save"), delegate { DumpSave(); });
        Button restoreSave = MakeButton(T("Restaurer une sauvegarde", "Restore save"), delegate { RestoreSave(); });
        savePanel.Controls.Add(exportSave, 0, 1);
        savePanel.Controls.Add(restoreSave, 1, 1);

        outer.Controls.Add(left, 0, 0);
        outer.Controls.Add(savePanel, 1, 0);
        return outer;
    }

    private Control BuildToolbar()
    {
        TableLayoutPanel g = new TableLayoutPanel();
        g.Dock = DockStyle.Fill;
        g.ColumnCount = 9;
        g.RowCount = 2;
        for (int i = 0; i < 9; i++) g.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 11.111f));
        g.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
        g.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));

        ConfigureButton(readButton, T("Lire / actualiser l'AR", "Read / refresh AR"), delegate { ReadAr(); });
        ConfigureButton(writeButton, T("Écrire l'AR", "Write AR"), delegate { WriteAr(); });
        ConfigureButton(importButton, T("Importer .xpc", "Import .xpc"), delegate { ImportXpc(); });
        ConfigureButton(exportButton, T("Exporter .xpc", "Export .xpc"), delegate { ExportCurrentXpc(); });
        ConfigureButton(libraryButton, T("Choix bibliothèque", "Choose library"), delegate { ShowLibraryMenu(); });
        ConfigureButton(driverButton, T("Pilote", "Driver"), delegate { RunDriverRepair(); });
        ConfigureButton(firmwareBackupButton, T("Sauvegarde Firmware", "Firmware backup"), delegate { DumpFirmware(); });
        ConfigureButton(firmwareUpdateButton, T("Mise à jour Firmware", "Firmware update"), delegate { WriteFirmware(); });
        ConfigureButton(folderButton, T("Dossier", "Folder"), delegate { OpenDataFolder(); });

        g.Controls.Add(readButton, 0, 0);
        g.Controls.Add(writeButton, 1, 0);
        g.Controls.Add(importButton, 2, 0);
        g.Controls.Add(exportButton, 3, 0);
        g.Controls.Add(libraryButton, 4, 0);
        g.Controls.Add(driverButton, 5, 0);
        g.Controls.Add(firmwareBackupButton, 6, 0);
        g.Controls.Add(firmwareUpdateButton, 7, 0);
        g.Controls.Add(folderButton, 8, 0);

        ConfigureButton(undoButton, T("◄ Annuler", "◄ Undo"), delegate { Undo(); });
        ConfigureButton(redoButton, T("Rétablir ►", "Redo ►"), delegate { Redo(); });
        ConfigureButton(journalButton, T("Journal / outils", "Log / tools"), delegate { ShowJournal(); });
        g.Controls.Add(undoButton, 0, 1);
        g.Controls.Add(redoButton, 1, 1);
        g.Controls.Add(journalButton, 8, 1);
        return g;
    }

    private Control BuildListsArea()
    {
        TableLayoutPanel area = new TableLayoutPanel();
        area.Dock = DockStyle.Fill;
        area.Padding = new Padding(0, 4, 0, 2);
        area.ColumnCount = 5;
        area.RowCount = 2;
        area.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 19f));
        area.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 27f));
        area.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 8f));
        area.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 19f));
        area.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 27f));
        area.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        area.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));

        ConfigureCheckedList(pcGames);
        ConfigureCheckedList(pcCodes);
        ConfigureCheckedList(arGames);
        ConfigureCheckedList(arCodes);

        area.Controls.Add(BuildListPanel(pcGamesTitle, pcGames), 0, 0);
        area.Controls.Add(BuildListPanel(pcCodesTitle, pcCodes), 1, 0);

        TableLayoutPanel transferRail = new TableLayoutPanel();
        transferRail.Dock = DockStyle.Fill;
        transferRail.RowCount = 5;
        transferRail.ColumnCount = 1;
        transferRail.RowStyles.Add(new RowStyle(SizeType.Percent, 35f));
        transferRail.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));
        transferRail.RowStyles.Add(new RowStyle(SizeType.Absolute, 10f));
        transferRail.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));
        transferRail.RowStyles.Add(new RowStyle(SizeType.Percent, 65f));
        ConfigureButton(toArButton, "PC → AR", delegate { TransferPcToAr(); });
        ConfigureButton(toPcButton, "AR → PC", delegate { TransferArToPc(); });
        transferRail.Controls.Add(toArButton, 0, 1);
        transferRail.Controls.Add(toPcButton, 0, 3);
        area.Controls.Add(transferRail, 2, 0);

        area.Controls.Add(BuildListPanel(arGamesTitle, arGames), 3, 0);
        area.Controls.Add(BuildListPanel(arCodesTitle, arCodes), 4, 0);

        ConfigureButton(newGameButton, T("+ Nouveau jeu", "+ New game"), delegate { NewGame(); });
        ConfigureButton(deleteGameButton, T("Supprimer jeu", "Delete game"), delegate { DeleteSelectedGame(activeSource); });
        ConfigureButton(newCodeButton, T("+ Nouveau code", "+ New code"), delegate { NewCode(); });
        ConfigureButton(deleteCodeButton, T("Supprimer code", "Delete code"), delegate { DeleteSelectedCode(activeSource); });
        area.Controls.Add(newGameButton, 0, 1);
        area.Controls.Add(deleteGameButton, 1, 1);
        area.Controls.Add(newCodeButton, 3, 1);
        area.Controls.Add(deleteCodeButton, 4, 1);
        return area;
    }

    private Control BuildListPanel(Label title, CheckedListBox list)
    {
        TableLayoutPanel p = new TableLayoutPanel();
        p.Dock = DockStyle.Fill;
        p.RowCount = 2;
        p.ColumnCount = 1;
        p.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
        p.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        title.ForeColor = Color.FromArgb(0, 102, 204);
        title.Font = new Font("Segoe UI Semibold", 10.5f);
        title.Dock = DockStyle.Fill;
        title.TextAlign = ContentAlignment.MiddleLeft;
        p.Controls.Add(title, 0, 0);
        p.Controls.Add(list, 0, 1);
        return p;
    }

    private Control BuildEditor()
    {
        GroupBox box = new GroupBox();
        box.Text = T("Éditeur de code", "Code editor");
        box.Dock = DockStyle.Fill;

        TableLayoutPanel e = new TableLayoutPanel();
        e.Dock = DockStyle.Fill;
        e.Padding = new Padding(7, 3, 7, 7);
        e.ColumnCount = 4;
        e.RowCount = 3;
        e.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105f));
        e.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        e.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105f));
        e.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        e.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
        e.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
        e.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        Label gameLabel = new Label(); gameLabel.Text = T("Nom du jeu", "Game name"); gameLabel.Dock = DockStyle.Fill; gameLabel.TextAlign = ContentAlignment.MiddleLeft;
        Label cheatLabel = new Label(); cheatLabel.Text = T("Nom du code", "Code name"); cheatLabel.Dock = DockStyle.Fill; cheatLabel.TextAlign = ContentAlignment.MiddleLeft;
        gameNameText.Dock = DockStyle.Fill;
        cheatNameText.Dock = DockStyle.Fill;
        masterCheck.Text = T("Code maître (M)", "Master code (M)");
        masterCheck.Dock = DockStyle.Fill;
        editorHint.Text = T("Clic droit : actions avancées  •  Survole les boutons pour obtenir de l’aide  •  Suppr = supprimer",
                            "Right-click: advanced actions  •  Hover buttons for help  •  Del = delete");
        editorHint.Dock = DockStyle.Fill;
        editorHint.TextAlign = ContentAlignment.MiddleLeft;
        ConfigureButton(applyButton, T("Enregistrer les modifications", "Save changes"), delegate { CommitEditor(true); });
        ConfigureButton(cancelButton, T("Annuler", "Cancel"), delegate { CancelEditor(); });
        cancelButton.Visible = false;

        codeText.Dock = DockStyle.Fill;
        codeText.Multiline = true;
        codeText.AcceptsReturn = true;
        codeText.ScrollBars = ScrollBars.Both;
        codeText.WordWrap = false;
        codeText.Font = new Font("Consolas", 10f);

        e.Controls.Add(gameLabel, 0, 0); e.Controls.Add(gameNameText, 1, 0);
        e.Controls.Add(cheatLabel, 2, 0); e.Controls.Add(cheatNameText, 3, 0);
        e.Controls.Add(masterCheck, 0, 1);
        e.Controls.Add(editorHint, 1, 1); e.SetColumnSpan(editorHint, 2);
        TableLayoutPanel applyPanel = new TableLayoutPanel(); applyPanel.Dock = DockStyle.Fill; applyPanel.ColumnCount = 2; applyPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 72f)); applyPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28f));
        applyPanel.Controls.Add(applyButton, 0, 0); applyPanel.Controls.Add(cancelButton, 1, 0);
        e.Controls.Add(applyPanel, 3, 1);

        Label fmt = new Label();
        fmt.Text = T("Codes Action Replay — format XXXXXXXX YYYYYYYY", "Action Replay codes — format XXXXXXXX YYYYYYYY");
        fmt.Dock = DockStyle.Fill; fmt.TextAlign = ContentAlignment.TopLeft;
        e.Controls.Add(fmt, 0, 2);
        e.Controls.Add(codeText, 1, 2); e.SetColumnSpan(codeText, 3);
        box.Controls.Add(e);
        return box;
    }

    private Control BuildBottomArea()
    {
        TableLayoutPanel b = new TableLayoutPanel();
        b.Dock = DockStyle.Fill;
        b.ColumnCount = 2;
        b.RowCount = 3;
        b.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        b.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        b.RowStyles.Add(new RowStyle(SizeType.Absolute, 20f));
        b.RowStyles.Add(new RowStyle(SizeType.Absolute, 18f));
        b.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        transferText.Text = T("Transfert : prêt", "Transfer: ready");
        storageText.Text = T("Mémoire codes AR : —", "AR code memory: —");
        transferText.Dock = DockStyle.Fill;
        storageText.Dock = DockStyle.Fill;
        transferProgress.Dock = DockStyle.Fill; transferProgress.Maximum = 1000;
        storageProgress.Dock = DockStyle.Fill; storageProgress.Maximum = 1000;
        bottomStatus.Dock = DockStyle.Fill;
        bottomStatus.AutoEllipsis = true;

        b.Controls.Add(transferText, 0, 0);
        b.Controls.Add(storageText, 1, 0);
        b.Controls.Add(transferProgress, 0, 1);
        b.Controls.Add(storageProgress, 1, 1);
        b.Controls.Add(bottomStatus, 0, 2); b.SetColumnSpan(bottomStatus, 2);
        return b;
    }

    private void ConfigureEvents()
    {
        languageButton.Click += delegate { LanguageManager.ToggleAndRestart(); };

        pcGames.SelectedIndexChanged += delegate { OnGameSelectionChanged(0); };
        pcCodes.SelectedIndexChanged += delegate { OnCodeSelectionChanged(0); };
        arGames.SelectedIndexChanged += delegate { OnGameSelectionChanged(1); };
        arCodes.SelectedIndexChanged += delegate { OnCodeSelectionChanged(1); };

        pcGames.ItemCheck += delegate(object s, ItemCheckEventArgs e) { OnGameItemCheck(0, e); };
        pcCodes.ItemCheck += delegate(object s, ItemCheckEventArgs e) { OnCodeItemCheck(0, e); };
        arGames.ItemCheck += delegate(object s, ItemCheckEventArgs e) { OnGameItemCheck(1, e); };
        arCodes.ItemCheck += delegate(object s, ItemCheckEventArgs e) { OnCodeItemCheck(1, e); };

        pcGames.MouseDown += delegate(object s, MouseEventArgs e) { HandleListMouseDown(0, 0, pcGames, e); };
        pcCodes.MouseDown += delegate(object s, MouseEventArgs e) { HandleListMouseDown(0, 1, pcCodes, e); };
        arGames.MouseDown += delegate(object s, MouseEventArgs e) { HandleListMouseDown(1, 0, arGames, e); };
        arCodes.MouseDown += delegate(object s, MouseEventArgs e) { HandleListMouseDown(1, 1, arCodes, e); };

        pcGames.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Delete) { DeleteSelectedGame(0); e.Handled = true; } };
        pcCodes.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Delete) { DeleteSelectedCode(0); e.Handled = true; } };
        arGames.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Delete) { DeleteSelectedGame(1); e.Handled = true; } };
        arCodes.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Delete) { DeleteSelectedCode(1); e.Handled = true; } };

        gameNameText.TextChanged += delegate { MarkEditorDirty(); };
        cheatNameText.TextChanged += delegate { MarkEditorDirty(); };
        codeText.TextChanged += delegate { MarkEditorDirty(); };
        masterCheck.CheckedChanged += delegate { OnMasterChanged(); };
        codeText.Leave += delegate { NormalizeCodeText(); };

        DragEnter += delegate(object s, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0 && (files[0].EndsWith(".xpc", StringComparison.OrdinalIgnoreCase) || files[0].EndsWith(".bin", StringComparison.OrdinalIgnoreCase)))
                    e.Effect = DragDropEffects.Copy;
            }
        };
        DragDrop += delegate(object s, DragEventArgs e)
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Length == 0) return;
            if (files[0].EndsWith(".xpc", StringComparison.OrdinalIgnoreCase)) ImportXpcPath(files[0]);
            else if (files[0].EndsWith(".bin", StringComparison.OrdinalIgnoreCase)) OpenBinIntoAr(files[0]);
        };
    }

    private void ConfigureTooltips()
    {
        toolTip.AutoPopDelay = 9000;
        toolTip.InitialDelay = 650;
        toolTip.ReshowDelay = 150;
        toolTip.SetToolTip(readButton, T("Lit les informations et la base de codes de l'Action Replay.", "Reads Action Replay information and code database."));
        toolTip.SetToolTip(writeButton, T("Écrit la base Action Replay en mémoire avec backup et vérification byte-for-byte.", "Writes the in-memory Action Replay database with backup and byte-for-byte verification."));
        toolTip.SetToolTip(importButton, T("Importe un fichier .xpc vers la Bibliothèque PC ou l'Action Replay.", "Imports an .xpc file into the PC Library or Action Replay."));
        toolTip.SetToolTip(exportButton, T("Exporte la base active au format XPC.", "Exports the active database as XPC."));
        toolTip.SetToolTip(libraryButton, T("Choisit Datel, Europe MAX v7 ou une bibliothèque personnalisée.", "Chooses Datel, Europe MAX v7 or a custom library."));
        toolTip.SetToolTip(driverButton, T("Installe/répare WinUSB, attend la réénumération Windows puis vérifie réellement l'accès à l'AR.", "Installs/repairs WinUSB, waits for Windows re-enumeration, then verifies real AR access."));
        toolTip.SetToolTip(firmwareBackupButton, T("Sauvegarde complète lecture seule de la Flash 256 Kio.", "Read-only full 256 KiB Flash backup."));
        toolTip.SetToolTip(firmwareUpdateButton, T("Validation et mise à jour firmware avec backup préalable obligatoire.", "Firmware validation/update with mandatory prior backup."));
        toolTip.SetToolTip(folderButton, T("Ouvre les données, sauvegardes, journaux, bibliothèques et cache.", "Opens data, backups, logs, libraries and cache."));
        toolTip.SetToolTip(journalButton, T("Journal détaillé, diagnostics USB/WinUSB et outils lecture seule.", "Detailed log, USB/WinUSB diagnostics and read-only tools."));
        toolTip.SetToolTip(toArButton, T("Transfère les jeux/codes cochés (ou la sélection courante) vers la base AR en mémoire.", "Transfers checked games/codes (or current selection) to the in-memory AR database."));
        toolTip.SetToolTip(toPcButton, T("Copie la sélection Action Replay vers la bibliothèque PC active.", "Copies the Action Replay selection to the active PC library."));
    }

    private Button MakeButton(string text, EventHandler onClick)
    {
        Button b = new Button(); ConfigureButton(b, text, onClick); return b;
    }

    private void ConfigureButton(Button b, string text, EventHandler onClick)
    {
        b.Text = text;
        b.Dock = DockStyle.Fill;
        b.Margin = new Padding(4, 3, 4, 3);
        b.Click += onClick;
    }

    private void ConfigureCheckedList(CheckedListBox l)
    {
        l.Dock = DockStyle.Fill;
        l.CheckOnClick = true;
        l.IntegralHeight = false;
        l.HorizontalScrollbar = true;
        l.BorderStyle = BorderStyle.FixedSingle;
    }

    private string Key(string s) { return (s ?? "").Trim().ToLowerInvariant(); }

    private CodeDB Db(int src) { return src == 0 ? pcDb : arDb; }
    private int SelectedGame(int src) { return src == 0 ? pcSelectedGame : arSelectedGame; }
    private int SelectedCheat(int src) { return src == 0 ? pcSelectedCheat : arSelectedCheat; }

    private void SetSelected(int src, int gi, int ci)
    {
        if (src == 0) { pcSelectedGame = gi; pcSelectedCheat = ci; }
        else { arSelectedGame = gi; arSelectedCheat = ci; }
    }

    private void ActivateSource(int src)
    {
        activeSource = src;
        UpdateTitles();
    }

    private void UpdateTitles()
    {
        pcGamesTitle.Text = (activeSource == 0 ? "▶ " : "") + T("Bibliothèque PC — Jeux", "PC library — Games");
        arGamesTitle.Text = (activeSource == 1 ? "▶ " : "") + T("Action Replay — Jeux", "Action Replay — Games");
        CodeDB pd = pcDb; CodeDB ad = arDb;
        pcCodesTitle.Text = T("Codes", "Codes") + (pcSelectedGame >= 0 && pcSelectedGame < pd.Games.Count ? " — " + pd.Games[pcSelectedGame].Name + " (" + pd.Games[pcSelectedGame].Cheats.Count + ")" : "");
        arCodesTitle.Text = T("Codes Action Replay", "Action Replay codes") + (arSelectedGame >= 0 && arSelectedGame < ad.Games.Count ? " — " + ad.Games[arSelectedGame].Name : "");
    }

    private void RefreshAll()
    {
        RefreshPcGames();
        RefreshArGames();
        RefreshEditor();
        RefreshDeviceUi();
        RefreshBottomStatus();
        UpdateHistoryButtons();
    }

    private void RefreshPcGames()
    {
        refreshingChecks = true;
        suppressSelection = true;
        try
        {
            pcGames.Items.Clear();
            for (int i = 0; i < pcDb.Games.Count; i++)
            {
                Game g = pcDb.Games[i];
                bool checkedState = IsGameInAr(g) || pcQueuedGames.Contains(Key(g.Name));
                pcGames.Items.Add(g.Name + "  (" + g.Cheats.Count + ")", checkedState);
            }
            if (pcSelectedGame >= pcDb.Games.Count) pcSelectedGame = pcDb.Games.Count - 1;
            if (pcSelectedGame >= 0) pcGames.SelectedIndex = pcSelectedGame;
        }
        finally { suppressSelection = false; refreshingChecks = false; }
        RefreshPcCodes();
    }

    private void RefreshPcCodes()
    {
        refreshingChecks = true;
        suppressSelection = true;
        try
        {
            pcCodes.Items.Clear();
            if (pcSelectedGame >= 0 && pcSelectedGame < pcDb.Games.Count)
            {
                Game g = pcDb.Games[pcSelectedGame];
                HashSet<string> codeSet; pcQueuedCodes.TryGetValue(Key(g.Name), out codeSet);
                bool whole = pcQueuedGames.Contains(Key(g.Name)) || IsGameInAr(g);
                for (int i = 0; i < g.Cheats.Count; i++)
                {
                    Cheat c = g.Cheats[i];
                    bool isMaster = CodeModel.LooksLikeMasterName(c.Name) || (c.Flags & 1u) != 0;
                    bool ch = whole || IsCodeInAr(g, c) || (codeSet != null && codeSet.Contains(Key(c.Name)));
                    pcCodes.Items.Add((isMaster ? "[M]  " : "") + c.Name, ch);
                }
                if (pcSelectedCheat >= g.Cheats.Count) pcSelectedCheat = -1;
                if (pcSelectedCheat >= 0) pcCodes.SelectedIndex = pcSelectedCheat;
            }
        }
        finally { suppressSelection = false; refreshingChecks = false; }
        UpdateTitles();
    }

    private void RefreshArGames()
    {
        refreshingChecks = true;
        suppressSelection = true;
        try
        {
            arGames.Items.Clear();
            for (int i = 0; i < arDb.Games.Count; i++)
            {
                Game g = arDb.Games[i];
                arGames.Items.Add(g.Name + "  (" + g.Cheats.Count + ")", arQueuedGames.Contains(Key(g.Name)));
            }
            if (arSelectedGame >= arDb.Games.Count) arSelectedGame = arDb.Games.Count - 1;
            if (arSelectedGame >= 0) arGames.SelectedIndex = arSelectedGame;
        }
        finally { suppressSelection = false; refreshingChecks = false; }
        RefreshArCodes();
    }

    private void RefreshArCodes()
    {
        refreshingChecks = true;
        suppressSelection = true;
        try
        {
            arCodes.Items.Clear();
            if (arSelectedGame >= 0 && arSelectedGame < arDb.Games.Count)
            {
                Game g = arDb.Games[arSelectedGame];
                HashSet<string> set; arQueuedCodes.TryGetValue(Key(g.Name), out set);
                bool whole = arQueuedGames.Contains(Key(g.Name));
                for (int i = 0; i < g.Cheats.Count; i++)
                {
                    Cheat c = g.Cheats[i];
                    bool isMaster = CodeModel.LooksLikeMasterName(c.Name) || (c.Flags & 1u) != 0;
                    bool ch = whole || (set != null && set.Contains(Key(c.Name)));
                    arCodes.Items.Add((isMaster ? "[M]  " : "") + c.Name, ch);
                }
                if (arSelectedCheat >= g.Cheats.Count) arSelectedCheat = -1;
                if (arSelectedCheat >= 0) arCodes.SelectedIndex = arSelectedCheat;
            }
        }
        finally { suppressSelection = false; refreshingChecks = false; }
        UpdateTitles();
    }

    private bool IsGameInAr(Game g)
    {
        for (int i = 0; i < arDb.Games.Count; i++)
            if (CodeModel.CanonicalGameName(arDb.Games[i].Name) == CodeModel.CanonicalGameName(g.Name) || CodeModel.SameMasterCode(arDb.Games[i], g)) return true;
        return false;
    }

    private bool IsCodeInAr(Game g, Cheat c)
    {
        for (int i = 0; i < arDb.Games.Count; i++)
        {
            Game ag = arDb.Games[i];
            if (!(CodeModel.CanonicalGameName(ag.Name) == CodeModel.CanonicalGameName(g.Name) || CodeModel.SameMasterCode(ag, g))) continue;
            for (int j = 0; j < ag.Cheats.Count; j++)
                if (Key(ag.Cheats[j].Name) == Key(c.Name)) return true;
        }
        return false;
    }

    private void OnGameSelectionChanged(int src)
    {
        if (suppressSelection) return;
        CheckedListBox list = src == 0 ? pcGames : arGames;
        int newIndex = list.SelectedIndex;
        int old = SelectedGame(src);
        if (newIndex == old) return;
        if (!ConfirmPendingEdit())
        {
            suppressSelection = true;
            list.SelectedIndex = old;
            suppressSelection = false;
            return;
        }
        ActivateSource(src);
        SetSelected(src, newIndex, -1);
        if (src == 0) { lastPcKind = 0; RefreshPcCodes(); }
        else { lastArKind = 0; RefreshArCodes(); }
        RefreshEditor();
        RefreshBottomStatus();
    }

    private void OnCodeSelectionChanged(int src)
    {
        if (suppressSelection) return;
        CheckedListBox list = src == 0 ? pcCodes : arCodes;
        int newIndex = list.SelectedIndex;
        int old = SelectedCheat(src);
        if (newIndex == old) return;
        if (!ConfirmPendingEdit())
        {
            suppressSelection = true;
            list.SelectedIndex = old;
            suppressSelection = false;
            return;
        }
        ActivateSource(src);
        SetSelected(src, SelectedGame(src), newIndex);
        if (src == 0) lastPcKind = 1; else lastArKind = 1;
        RefreshEditor();
    }

    private void OnGameItemCheck(int src, ItemCheckEventArgs e)
    {
        if (refreshingChecks) return;
        CodeDB d = Db(src);
        if (e.Index < 0 || e.Index >= d.Games.Count) return;
        Game g = d.Games[e.Index];
        bool check = e.NewValue == CheckState.Checked;

        if (src == 0)
        {
            if (IsGameInAr(g))
            {
                BeginInvoke((MethodInvoker)delegate { RefreshPcGames(); });
                SetActivity(T("Ce jeu est déjà présent dans l'Action Replay", "This game is already present in Action Replay"));
                return;
            }
            if (check) pcQueuedGames.Add(Key(g.Name)); else pcQueuedGames.Remove(Key(g.Name));
            if (pcSelectedGame == e.Index) BeginInvoke((MethodInvoker)RefreshPcCodes);
        }
        else
        {
            if (check) arQueuedGames.Add(Key(g.Name)); else arQueuedGames.Remove(Key(g.Name));
            if (arSelectedGame == e.Index) BeginInvoke((MethodInvoker)RefreshArCodes);
        }
        BeginInvoke((MethodInvoker)RefreshBottomStatus);
    }

    private void OnCodeItemCheck(int src, ItemCheckEventArgs e)
    {
        if (refreshingChecks) return;
        int gi = SelectedGame(src);
        CodeDB d = Db(src);
        if (gi < 0 || gi >= d.Games.Count || e.Index < 0 || e.Index >= d.Games[gi].Cheats.Count) return;
        Game g = d.Games[gi]; Cheat c = g.Cheats[e.Index];
        bool check = e.NewValue == CheckState.Checked;
        Dictionary<string, HashSet<string>> map = src == 0 ? pcQueuedCodes : arQueuedCodes;
        string gk = Key(g.Name);
        HashSet<string> set;
        if (!map.TryGetValue(gk, out set)) { set = new HashSet<string>(StringComparer.OrdinalIgnoreCase); map[gk] = set; }
        if (check) set.Add(Key(c.Name)); else set.Remove(Key(c.Name));
        RefreshBottomStatus();
    }

    private void HandleListMouseDown(int src, int kind, CheckedListBox list, MouseEventArgs e)
    {
        int idx = list.IndexFromPoint(e.Location);
        if (idx >= 0) list.SelectedIndex = idx;
        if (e.Button != MouseButtons.Right) return;
        ShowContextMenu(src, kind, list, e.Location);
    }

    private void ShowContextMenu(int src, int kind, Control owner, Point point)
    {
        CodeDB d = Db(src);
        int gi = SelectedGame(src), ci = SelectedCheat(src);
        ContextMenuStrip m = new ContextMenuStrip();
        if (gi >= 0 && gi < d.Games.Count)
        {
            if (kind == 0)
            {
                m.Items.Add(T("Cocher/décocher pour l'envoi vers l'AR / la fusion", "Check/uncheck for AR transfer / merging"), null,
                    delegate { ToggleGameCheck(src, gi); });
                if (src == 0) m.Items.Add(T("Envoyer ce jeu vers l'AR", "Send this game to AR"), null, delegate { TransferOne(src, 0); });
                else m.Items.Add(T("Copier ce jeu vers la bibliothèque PC", "Copy this game to PC library"), null, delegate { TransferOne(src, 0); });
            }
            else if (ci >= 0 && ci < d.Games[gi].Cheats.Count)
            {
                m.Items.Add(T("Cocher/décocher ce code", "Check/uncheck this code"), null, delegate { ToggleCodeCheck(src, gi, ci); });
                if (src == 0) m.Items.Add(T("Envoyer ce code vers l'AR", "Send this code to AR"), null, delegate { TransferOne(src, 1); });
                else m.Items.Add(T("Copier ce code vers la bibliothèque PC", "Copy this code to PC library"), null, delegate { TransferOne(src, 1); });
            }

            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(T("Modifier dans l'éditeur", "Edit in editor"), null, delegate { ActivateSource(src); RefreshEditor(); });
            if (kind == 0) m.Items.Add(T("Exporter ce jeu en .xpc", "Export this game as .xpc"), null, delegate { ExportSelectedXpc(src, 0); });
            else if (ci >= 0) m.Items.Add(T("Exporter ce code en .xpc", "Export this code as .xpc"), null, delegate { ExportSelectedXpc(src, 1); });
            m.Items.Add(kind == 0 ? T("Supprimer ce jeu", "Delete this game") : T("Supprimer ce code", "Delete this code"), null,
                delegate { if (kind == 0) DeleteSelectedGame(src); else DeleteSelectedCode(src); });
        }

        m.Items.Add(new ToolStripSeparator());
        m.Items.Add(T("Nouveau jeu", "New game"), null, delegate { ActivateSource(src); NewGame(); });
        if (gi >= 0 && gi < d.Games.Count) m.Items.Add(T("Nouveau code dans ce jeu", "New code in this game"), null, delegate { ActivateSource(src); NewCode(); });
        m.Items.Add(new ToolStripSeparator());

        List<int> checkedGames = CheckedGameIndices(src);
        if (kind == 0 && checkedGames.Count >= 2)
            m.Items.Add(String.Format(T("Fusionner les {0} jeux cochés…", "Merge the {0} checked games…"), checkedGames.Count), null, delegate { MergeChecked(src); });
        m.Items.Add(T("Fusionner les jeux identiques (nom/master)", "Merge identical games (name/master)"), null, delegate { CoalesceEquivalent(src); });
        m.Items.Add(T("Fusionner par master code identique (auto, strict)…", "Merge by identical master code (auto, strict)…"), null, delegate { CoalesceMaster(src); });
        m.Show(owner, point);
    }

    private void ToggleGameCheck(int src, int gi)
    {
        CodeDB d = Db(src); if (gi < 0 || gi >= d.Games.Count) return;
        string k = Key(d.Games[gi].Name);
        HashSet<string> set = src == 0 ? pcQueuedGames : arQueuedGames;
        if (set.Contains(k)) set.Remove(k); else set.Add(k);
        if (src == 0) RefreshPcGames(); else RefreshArGames();
    }

    private void ToggleCodeCheck(int src, int gi, int ci)
    {
        CodeDB d = Db(src); if (gi < 0 || gi >= d.Games.Count || ci < 0 || ci >= d.Games[gi].Cheats.Count) return;
        Dictionary<string, HashSet<string>> map = src == 0 ? pcQueuedCodes : arQueuedCodes;
        string gk = Key(d.Games[gi].Name), ck = Key(d.Games[gi].Cheats[ci].Name);
        HashSet<string> set; if (!map.TryGetValue(gk, out set)) { set = new HashSet<string>(StringComparer.OrdinalIgnoreCase); map[gk] = set; }
        if (set.Contains(ck)) set.Remove(ck); else set.Add(ck);
        if (src == 0) RefreshPcCodes(); else RefreshArCodes();
    }

    private List<int> CheckedGameIndices(int src)
    {
        CodeDB d = Db(src); HashSet<string> q = src == 0 ? pcQueuedGames : arQueuedGames;
        List<int> outv = new List<int>();
        for (int i = 0; i < d.Games.Count; i++) if (q.Contains(Key(d.Games[i].Name))) outv.Add(i);
        return outv;
    }

    private void PushHistory(string label)
    {
        undo.Push(new HistoryEntry { Label = label, Pc = pcDb.Clone(), Ar = arDb.Clone() });
        while (undo.Count > HistoryLimit)
        {
            HistoryEntry[] a = undo.ToArray();
            undo.Clear();
            for (int i = a.Length - 2; i >= 0; i--) undo.Push(a[i]);
        }
        redo.Clear();
        UpdateHistoryButtons();
    }

    private void Undo()
    {
        if (undo.Count == 0 || !ConfirmPendingEdit()) return;
        HistoryEntry cur = new HistoryEntry { Label = "redo", Pc = pcDb.Clone(), Ar = arDb.Clone() };
        HistoryEntry e = undo.Pop(); redo.Push(cur);
        pcDb = e.Pc; arDb = e.Ar;
        ClearQueues(); SavePcLibrary();
        SetActivity(T("Annulé : ", "Undone: ") + e.Label);
        RefreshAll();
    }

    private void Redo()
    {
        if (redo.Count == 0 || !ConfirmPendingEdit()) return;
        HistoryEntry cur = new HistoryEntry { Label = "undo", Pc = pcDb.Clone(), Ar = arDb.Clone() };
        HistoryEntry e = redo.Pop(); undo.Push(cur);
        pcDb = e.Pc; arDb = e.Ar;
        ClearQueues(); SavePcLibrary();
        SetActivity(T("Rétabli", "Redone"));
        RefreshAll();
    }

    private void UpdateHistoryButtons()
    {
        undoButton.Enabled = undo.Count > 0;
        redoButton.Enabled = redo.Count > 0;
    }

    private void ClearQueues()
    {
        pcQueuedGames.Clear(); pcQueuedCodes.Clear(); arQueuedGames.Clear(); arQueuedCodes.Clear();
    }

    private void NewGame()
    {
        if (!ConfirmPendingEdit()) return;
        editorMode = 1; editorSource = activeSource; editorGame = -1; editorCheat = -1;
        loadingEditor = true;
        gameNameText.Enabled = true; gameNameText.Text = "";
        cheatNameText.Enabled = false; cheatNameText.Text = "";
        codeText.Enabled = false; codeText.Text = "";
        masterCheck.Enabled = false; masterCheck.Checked = false;
        loadingEditor = false;
        editDirty = true;
        applyButton.Text = T("Créer le jeu", "Create game");
        cancelButton.Visible = true;
        gameNameText.Focus();
    }

    private void NewCode()
    {
        if (!ConfirmPendingEdit()) return;
        int gi = SelectedGame(activeSource);
        CodeDB d = Db(activeSource);
        if (gi < 0 || gi >= d.Games.Count)
        {
            MessageBox.Show(this, T("Sélectionne un jeu.", "Select a game."), "Action Replay GBX", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        editorMode = 2; editorSource = activeSource; editorGame = gi; editorCheat = -1;
        loadingEditor = true;
        gameNameText.Enabled = true; gameNameText.Text = d.Games[gi].Name;
        cheatNameText.Enabled = true; cheatNameText.Text = "";
        codeText.Enabled = true; codeText.Text = "";
        masterCheck.Enabled = true; masterCheck.Checked = false;
        loadingEditor = false;
        editDirty = true;
        applyButton.Text = T("Créer le code", "Create code");
        cancelButton.Visible = true;
        cheatNameText.Focus();
    }

    private void RefreshEditor()
    {
        if (editorMode != 0) return;
        loadingEditor = true;
        try
        {
            CodeDB d = Db(activeSource);
            int gi = SelectedGame(activeSource), ci = SelectedCheat(activeSource);
            editorGame = gi; editorCheat = ci; editorSource = activeSource;
            if (gi < 0 || gi >= d.Games.Count)
            {
                gameNameText.Text = ""; cheatNameText.Text = ""; codeText.Text = ""; masterCheck.Checked = false;
                gameNameText.Enabled = cheatNameText.Enabled = codeText.Enabled = masterCheck.Enabled = applyButton.Enabled = false;
                return;
            }
            gameNameText.Enabled = true; applyButton.Enabled = true;
            gameNameText.Text = d.Games[gi].Name;
            if (ci < 0 || ci >= d.Games[gi].Cheats.Count)
            {
                cheatNameText.Text = ""; codeText.Text = ""; masterCheck.Checked = false;
                cheatNameText.Enabled = codeText.Enabled = masterCheck.Enabled = false;
            }
            else
            {
                Cheat c = d.Games[gi].Cheats[ci];
                bool master = CodeModel.LooksLikeMasterName(c.Name) || (c.Flags & 1u) != 0;
                masterCheck.Enabled = true; masterCheck.Checked = master;
                cheatNameText.Text = master ? "(M)" : c.Name;
                cheatNameText.Enabled = !master;
                codeText.Enabled = true; codeText.Text = CodeModel.FormatCodeText(c.Words);
            }
            editDirty = false;
            applyButton.Text = T("Enregistrer les modifications", "Save changes");
            cancelButton.Visible = false;
        }
        finally { loadingEditor = false; }
    }

    private void MarkEditorDirty()
    {
        if (loadingEditor) return;
        editDirty = true;
        cancelButton.Visible = true;
    }

    private void OnMasterChanged()
    {
        if (loadingEditor) return;
        loadingEditor = true;
        if (masterCheck.Checked)
        {
            cheatNameText.Text = "(M)";
            cheatNameText.Enabled = false;
        }
        else
        {
            if (String.Equals(cheatNameText.Text.Trim(), "(M)", StringComparison.OrdinalIgnoreCase)) cheatNameText.Text = "";
            cheatNameText.Enabled = true;
        }
        loadingEditor = false;
        MarkEditorDirty();
    }

    private void NormalizeCodeText()
    {
        if (!codeText.Enabled || String.IsNullOrWhiteSpace(codeText.Text)) return;
        try
        {
            string normalized = CodeModel.FormatCodeText(CodeModel.ParseCodeText(codeText.Text));
            if (normalized != codeText.Text)
            {
                loadingEditor = true; codeText.Text = normalized; loadingEditor = false; editDirty = true;
            }
        }
        catch { }
    }

    private bool CommitEditor(bool showError)
    {
        if (!editDirty) return true;
        try
        {
            CodeDB d = Db(editorSource);
            if (editorMode == 1)
            {
                string name = gameNameText.Text.Trim();
                if (name.Length == 0) throw new InvalidDataException(T("Le nom du jeu ne peut pas être vide.", "Game name cannot be empty."));
                PushHistory(T("Création jeu", "Create game"));
                Game g = new Game(); g.Name = name; d.Games.Add(g); d.SortGames();
                int gi = FindGame(d, name); SetSelected(editorSource, gi, -1);
            }
            else if (editorMode == 2)
            {
                if (editorGame < 0 || editorGame >= d.Games.Count) throw new InvalidDataException(T("Jeu invalide.", "Invalid game."));
                string cname = masterCheck.Checked ? "(M)" : cheatNameText.Text.Trim();
                if (cname.Length == 0) throw new InvalidDataException(T("Le nom du code ne peut pas être vide.", "Code name cannot be empty."));
                List<uint> words = CodeModel.ParseCodeText(codeText.Text);
                PushHistory(T("Création code", "Create code"));
                Cheat c = new Cheat(); c.Name = cname; c.Flags = masterCheck.Checked ? 1u : 0u; c.Words.AddRange(words);
                d.Games[editorGame].Cheats.Add(c);
                SetSelected(editorSource, editorGame, d.Games[editorGame].Cheats.Count - 1);
            }
            else
            {
                if (editorGame < 0 || editorGame >= d.Games.Count) return true;
                string gname = gameNameText.Text.Trim();
                if (gname.Length == 0) throw new InvalidDataException(T("Le nom du jeu ne peut pas être vide.", "Game name cannot be empty."));
                PushHistory(T("Modification", "Edit"));
                d.Games[editorGame].Name = gname;
                if (editorCheat >= 0 && editorCheat < d.Games[editorGame].Cheats.Count)
                {
                    Cheat c = d.Games[editorGame].Cheats[editorCheat];
                    string cname = masterCheck.Checked ? "(M)" : cheatNameText.Text.Trim();
                    if (cname.Length == 0) throw new InvalidDataException(T("Le nom du code ne peut pas être vide.", "Code name cannot be empty."));
                    c.Name = cname; c.Flags = masterCheck.Checked ? (c.Flags | 1u) : (c.Flags & ~1u);
                    c.Words = CodeModel.ParseCodeText(codeText.Text);
                }
                d.SortGames();
                int gi = FindGame(d, gname); SetSelected(editorSource, gi, editorCheat);
            }

            d.CoalesceEquivalentGames();
            d.SortGames();
            editorMode = 0; editDirty = false;
            if (editorSource == 0) SavePcLibrary();
            ClearQueues();
            RefreshAll();
            return true;
        }
        catch (Exception ex)
        {
            if (showError) MessageBox.Show(this, ex.Message, T("Modification", "Edit"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
    }

    private int FindGame(CodeDB d, string name)
    {
        for (int i = 0; i < d.Games.Count; i++) if (Key(d.Games[i].Name) == Key(name)) return i;
        return d.Games.Count > 0 ? 0 : -1;
    }

    private void CancelEditor()
    {
        editorMode = 0; editDirty = false;
        RefreshEditor();
        SetActivity(T("Modifications annulées", "Changes discarded"));
    }

    private bool ConfirmPendingEdit()
    {
        if (!editDirty) return true;
        DialogResult r = MessageBox.Show(this,
            T("Les modifications en cours ne sont pas enregistrées.\r\n\r\nOui = enregistrer avant de continuer\r\nNon = abandonner ces modifications\r\nAnnuler = revenir à l'édition en cours",
              "Current changes are not saved.\r\n\r\nYes = save before continuing\r\nNo = discard changes\r\nCancel = return to editing"),
            T("Modifications non enregistrées", "Unsaved changes"),
            MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
        if (r == DialogResult.Yes) return CommitEditor(true);
        if (r == DialogResult.No) { CancelEditor(); return true; }
        return false;
    }

    private void DeleteSelectedGame(int src)
    {
        if (!ConfirmPendingEdit()) return;
        CodeDB d = Db(src); int gi = SelectedGame(src);
        if (gi < 0 || gi >= d.Games.Count) return;
        if (MessageBox.Show(this, T("Supprimer ce jeu et tous ses codes ?", "Delete this game and all its codes?"), T("Suppression", "Delete"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        PushHistory(T("Suppression jeu", "Delete game"));
        d.Games.RemoveAt(gi); d.SortGames(); SetSelected(src, Math.Min(gi, d.Games.Count - 1), -1);
        if (src == 0) SavePcLibrary();
        ClearQueues(); RefreshAll();
    }

    private void DeleteSelectedCode(int src)
    {
        if (!ConfirmPendingEdit()) return;
        CodeDB d = Db(src); int gi = SelectedGame(src), ci = SelectedCheat(src);
        if (gi < 0 || gi >= d.Games.Count || ci < 0 || ci >= d.Games[gi].Cheats.Count) return;
        if (MessageBox.Show(this, T("Supprimer ce code ?", "Delete this code?"), T("Suppression", "Delete"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        PushHistory(T("Suppression code", "Delete code"));
        d.Games[gi].Cheats.RemoveAt(ci); SetSelected(src, gi, -1);
        if (src == 0) SavePcLibrary();
        ClearQueues(); RefreshAll();
    }

    private void TransferPcToAr()
    {
        if (!ConfirmPendingEdit()) return;
        bool hasQueued = pcQueuedGames.Count > 0;
        foreach (HashSet<string> s in pcQueuedCodes.Values) if (s.Count > 0) hasQueued = true;
        PushHistory(T("Envoi vers AR", "Send to AR"));
        int games = 0, codes = 0;
        if (hasQueued)
        {
            for (int gi = 0; gi < pcDb.Games.Count; gi++)
            {
                Game g = pcDb.Games[gi]; string gk = Key(g.Name);
                if (pcQueuedGames.Contains(gk))
                {
                    CodeDB one = new CodeDB(); one.Games.Add(g.Clone());
                    arDb.Merge(one.ARSafeCopy()); games++; continue;
                }
                HashSet<string> set; if (!pcQueuedCodes.TryGetValue(gk, out set) || set.Count == 0) continue;
                Game copy = new Game(); copy.Name = g.Name;
                for (int ci = 0; ci < g.Cheats.Count; ci++) if (set.Contains(Key(g.Cheats[ci].Name))) { copy.Cheats.Add(g.Cheats[ci].Clone()); codes++; }
                EnsureMaster(g, copy);
                CodeDB oneCode = new CodeDB(); oneCode.Games.Add(copy);
                arDb.Merge(oneCode.ARSafeCopy());
            }
        }
        else TransferOneInternal(0, lastPcKind);

        arDb.SortGames(); pcQueuedGames.Clear(); pcQueuedCodes.Clear();
        SetActivity(String.Format(T("Sélection envoyée en mémoire vers l'AR : {0} jeu(x), {1} code(s) — clique Écrire l'AR pour appliquer",
                                    "Selection copied to AR memory: {0} game(s), {1} code(s) — click Write AR to apply"), games, codes));
        RefreshAll();
    }

    private void TransferArToPc()
    {
        if (!ConfirmPendingEdit()) return;
        PushHistory(T("Copie AR vers PC", "Copy AR to PC"));
        TransferOneInternal(1, lastArKind);
        SavePcLibrary(); pcDb.SortGames(); RefreshAll();
    }

    private void TransferOne(int src, int kind)
    {
        if (!ConfirmPendingEdit()) return;
        PushHistory(T("Transfert", "Transfer"));
        TransferOneInternal(src, kind);
        if (src == 1) SavePcLibrary();
        RefreshAll();
    }

    private void TransferOneInternal(int src, int kind)
    {
        CodeDB source = Db(src); CodeDB dest = src == 0 ? arDb : pcDb;
        int gi = SelectedGame(src), ci = SelectedCheat(src);
        if (gi < 0 || gi >= source.Games.Count) return;
        Game g = source.Games[gi];
        Game copy = new Game(); copy.Name = g.Name;
        if (kind == 1 && ci >= 0 && ci < g.Cheats.Count)
        {
            copy.Cheats.Add(g.Cheats[ci].Clone());
            EnsureMaster(g, copy);
        }
        else
        {
            foreach (Cheat c in g.Cheats) copy.Cheats.Add(c.Clone());
        }
        CodeDB one = new CodeDB(); one.Games.Add(copy);
        if (src == 0) one = one.ARSafeCopy();
        dest.Merge(one); dest.SortGames();
        SetActivity(src == 0 ? T("Sélection copiée vers l'Action Replay en mémoire.", "Selection copied to in-memory Action Replay.") :
                                 T("Sélection copiée vers la bibliothèque PC.", "Selection copied to PC library."));
    }

    private void EnsureMaster(Game source, Game subset)
    {
        bool hasMaster = false;
        foreach (Cheat c in subset.Cheats) if (CodeModel.LooksLikeMasterName(c.Name) || (c.Flags & 1u) != 0) hasMaster = true;
        if (hasMaster) return;
        foreach (Cheat c in source.Cheats)
        {
            if (CodeModel.LooksLikeMasterName(c.Name) || (c.Flags & 1u) != 0)
            {
                subset.Cheats.Insert(0, c.Clone()); return;
            }
        }
    }

    private void MergeChecked(int src)
    {
        if (!ConfirmPendingEdit()) return;
        List<int> ids = CheckedGameIndices(src);
        if (ids.Count < 2) return;
        CodeDB d = Db(src);
        StringBuilder list = new StringBuilder();
        foreach (int i in ids) list.AppendLine("• " + d.Games[i].Name);
        if (MessageBox.Show(this, T("Fusionner les jeux cochés ?\r\n\r\n", "Merge checked games?\r\n\r\n") + list.ToString(),
            T("Fusionner les jeux cochés", "Merge checked games"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        PushHistory(T("Fusion manuelle", "Manual merge"));
        ManualMergeResult r = d.ManualMergeGames(ids);
        if (src == 0) SavePcLibrary();
        ClearQueues();
        SetActivity(String.Format(T("Fusion terminée : +{0} code(s), {1} remplacé(s), {2} doublon(s) retiré(s).",
                                    "Merge complete: +{0} code(s), {1} replaced, {2} duplicate(s) removed."),
                                  r.AddedCodes, r.ReplacedCodes, r.DedupedCodes));
        RefreshAll();
    }

    private void CoalesceEquivalent(int src)
    {
        if (!ConfirmPendingEdit()) return;
        PushHistory(T("Fusion nom/master", "Name/master merge"));
        MergeStats r = Db(src).CoalesceEquivalentGames();
        if (src == 0) SavePcLibrary();
        ClearQueues();
        SetActivity(String.Format(T("{0} doublon(s) fusionné(s).", "{0} duplicate(s) merged."), r.RemovedGames));
        RefreshAll();
    }

    private void CoalesceMaster(int src)
    {
        if (!ConfirmPendingEdit()) return;
        CodeDB d = Db(src);
        List<List<string>> groups = d.PreviewMasterCodeMerges();
        if (groups.Count == 0)
        {
            MessageBox.Show(this, T("Aucun jeu ne partage un master code identique.", "No games share an identical master code."), T("Fusion par master", "Master merge"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        StringBuilder sb = new StringBuilder();
        foreach (List<string> g in groups)
        {
            sb.AppendLine(String.Join("  +  ", g.ToArray()));
            sb.AppendLine();
        }
        if (MessageBox.Show(this, T("Groupes proposés :\r\n\r\n", "Proposed groups:\r\n\r\n") + sb.ToString(),
            T("Fusion par master code", "Merge by master code"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        PushHistory(T("Fusion master stricte", "Strict master merge"));
        MergeStats r = d.CoalesceByMasterCode();
        if (src == 0) SavePcLibrary();
        ClearQueues();
        SetActivity(String.Format(T("{0} jeu(x) fusionné(s), {1} doublon(s) de code retiré(s).", "{0} game(s) merged, {1} duplicate code(s) removed."), r.RemovedGames, r.DedupedCodes));
        RefreshAll();
    }

    private void ShowLibraryMenu()
    {
        if (!ConfirmPendingEdit()) return;
        ContextMenuStrip m = new ContextMenuStrip();
        ToolStripMenuItem datel = new ToolStripMenuItem(T("Datel 3.3 officielle (par défaut)", "Official Datel 3.3 (default)"));
        ToolStripMenuItem max = new ToolStripMenuItem(T("Europe MAX v7 — compatibilité incertaine", "Europe MAX v7 — uncertain compatibility"));
        datel.Checked = libraryMode == "datel"; max.Checked = libraryMode == "maxv7";
        datel.Click += delegate { SwitchLibrary("datel"); };
        max.Click += delegate
        {
            if (MessageBox.Show(this, T("La compatibilité réelle de tous les codes Europe MAX v7 avec ton Action Replay européen n'est pas garantie.\r\n\r\nCharger quand même ?", "Real compatibility of all Europe MAX v7 codes with your European Action Replay is not guaranteed.\r\n\r\nLoad anyway?"),
                T("Compatibilité incertaine", "Uncertain compatibility"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) SwitchLibrary("maxv7");
        };
        m.Items.Add(datel); m.Items.Add(max);

        List<string> customs = ListCustomLibraries();
        if (customs.Count > 0)
        {
            m.Items.Add(new ToolStripSeparator());
            foreach (string slug in customs)
            {
                string copy = slug;
                ToolStripMenuItem it = new ToolStripMenuItem(slug); it.Checked = libraryMode == "custom:" + slug;
                it.Click += delegate { SwitchLibrary("custom:" + copy); };
                m.Items.Add(it);
            }
        }
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add(T("+ Nouvelle bibliothèque…", "+ New library…"), null, delegate { CreateNewLibrary(); });
        m.Items.Add(String.Format(T("Réinitialiser « {0} » à l'origine…", "Reset '{0}' to original…"), LibraryDisplayName(libraryMode)), null, delegate { ResetLibrary(); });
        m.Show(libraryButton, new Point(0, libraryButton.Height));
    }

    private string LibraryStatePath { get { return Path.Combine(dataDir, "library.ini"); } }

    private void LoadLibraryMode(bool announce)
    {
        string mode = "datel";
        try
        {
            if (File.Exists(LibraryStatePath))
            {
                foreach (string line in File.ReadAllLines(LibraryStatePath))
                    if (line.StartsWith("pc_database=", StringComparison.OrdinalIgnoreCase)) mode = line.Substring("pc_database=".Length).Trim();
            }
        }
        catch { }
        try { LoadLibrary(mode); }
        catch { LoadLibrary("datel"); }
        if (announce) SetActivity(String.Format(T("Bibliothèque « {0} » chargée : {1} jeux / {2} codes", "Library '{0}' loaded: {1} games / {2} codes"), LibraryDisplayName(libraryMode), pcDb.Games.Count, pcDb.CheatCount()));
    }

    private void SaveLibraryState()
    {
        Directory.CreateDirectory(dataDir);
        File.WriteAllText(LibraryStatePath, "pc_database=" + libraryMode + Environment.NewLine, new UTF8Encoding(false));
    }

    private string BundledLibraryPath(string mode)
    {
        if (mode == "maxv7") return Path.Combine(exeDir, "PCDatabase-EuropeMAX-v7.xpc");
        return Path.Combine(exeDir, "PCDatabase-Datel.xpc");
    }

    private string PersistentLibraryPath(string mode)
    {
        if (mode.StartsWith("custom:", StringComparison.OrdinalIgnoreCase))
            return Path.Combine(libraryDir, "Custom-" + mode.Substring(7) + ".xpc");
        if (mode == "maxv7") return Path.Combine(libraryDir, "PCDatabase-EuropeMAX-v7.xpc");
        return Path.Combine(libraryDir, "PCDatabase-Datel.xpc");
    }

    private void LoadLibrary(string mode)
    {
        string path = PersistentLibraryPath(mode);
        if (!File.Exists(path))
        {
            if (mode.StartsWith("custom:", StringComparison.OrdinalIgnoreCase))
            {
                CodeDB empty = new CodeDB(); empty.SaveXPC(path);
            }
            else File.Copy(BundledLibraryPath(mode), path, true);
        }
        pcDb = CodeDB.LoadXPC(path); pcDb.SortGames(); libraryMode = mode; SaveLibraryState();
        pcSelectedGame = pcDb.Games.Count > 0 ? 0 : -1; pcSelectedCheat = -1; activeSource = 0;
        pcQueuedGames.Clear(); pcQueuedCodes.Clear();
        RefreshAll();
    }

    private void SwitchLibrary(string mode)
    {
        if (!ConfirmPendingEdit()) return;
        try { LoadLibrary(mode); SetActivity(T("Bibliothèque chargée : ", "Library loaded: ") + LibraryDisplayName(mode)); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, T("Bibliothèque", "Library"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private string LibraryDisplayName(string mode)
    {
        if (mode == "datel") return "Datel 3.3";
        if (mode == "maxv7") return "Europe MAX v7";
        if (mode.StartsWith("custom:", StringComparison.OrdinalIgnoreCase)) return mode.Substring(7);
        return mode;
    }

    private List<string> ListCustomLibraries()
    {
        List<string> r = new List<string>();
        if (!Directory.Exists(libraryDir)) return r;
        foreach (string p in Directory.GetFiles(libraryDir, "Custom-*.xpc"))
        {
            string n = Path.GetFileNameWithoutExtension(p);
            if (n.StartsWith("Custom-", StringComparison.OrdinalIgnoreCase)) r.Add(n.Substring(7));
        }
        r.Sort(StringComparer.CurrentCultureIgnoreCase); return r;
    }

    private string SanitizeSlug(string name)
    {
        StringBuilder sb = new StringBuilder();
        foreach (char c in name.Trim())
        {
            if (Char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_') sb.Append(c);
        }
        string s = sb.ToString().Trim().Replace(' ', '_');
        return s.Length > 40 ? s.Substring(0, 40) : s;
    }

    private void CreateNewLibrary()
    {
        string name = PromptText(T("Nouvelle bibliothèque", "New library"), T("Nom de la nouvelle bibliothèque PC (vide au départ) :", "Name of the new PC library (starts empty):"), "");
        if (name == null) return;
        string slug = SanitizeSlug(name);
        if (slug.Length == 0) return;
        string mode = "custom:" + slug;
        string path = PersistentLibraryPath(mode);
        if (File.Exists(path))
        {
            MessageBox.Show(this, T("Une bibliothèque de ce nom existe déjà.", "A library with this name already exists."), T("Nouvelle bibliothèque", "New library"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        new CodeDB().SaveXPC(path); LoadLibrary(mode);
        SetActivity(T("Nouvelle bibliothèque créée : ", "New library created: ") + slug);
    }

    private void ResetLibrary()
    {
        if (MessageBox.Show(this, String.Format(T("Réinitialiser « {0} » à son état d'origine ?\r\n\r\nToutes les modifications seront perdues.", "Reset '{0}' to its original state?\r\n\r\nAll changes will be lost."), LibraryDisplayName(libraryMode)),
            T("Réinitialiser la bibliothèque", "Reset library"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        string path = PersistentLibraryPath(libraryMode);
        if (libraryMode.StartsWith("custom:", StringComparison.OrdinalIgnoreCase)) new CodeDB().SaveXPC(path);
        else File.Copy(BundledLibraryPath(libraryMode), path, true);
        LoadLibrary(libraryMode);
    }

    private void SavePcLibrary()
    {
        try { pcDb.SaveXPC(PersistentLibraryPath(libraryMode)); }
        catch (Exception ex) { AppendLog("Save PC library failed: " + ex.Message); }
    }

    private void ImportXpc()
    {
        using (OpenFileDialog o = new OpenFileDialog())
        {
            o.Filter = "Action Replay XPC (*.xpc)|*.xpc|All files (*.*)|*.*";
            if (o.ShowDialog(this) == DialogResult.OK) ImportXpcPath(o.FileName);
        }
    }

    private void ImportXpcPath(string path)
    {
        if (!ConfirmPendingEdit()) return;
        CodeDB incoming;
        try { incoming = CodeDB.LoadXPC(path); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, T("Importer XPC", "Import XPC"), MessageBoxButtons.OK, MessageBoxIcon.Error); return; }

        DialogResult dst = MessageBox.Show(this, T("Importer vers la Bibliothèque PC ?\r\n\r\nOui = Bibliothèque PC\r\nNon = Action Replay\r\nAnnuler = ne rien faire",
                                                    "Import into PC Library?\r\n\r\nYes = PC Library\r\nNo = Action Replay\r\nCancel = do nothing"),
                                           T("Destination import XPC", "XPC import destination"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (dst == DialogResult.Cancel) return;
        int dest = dst == DialogResult.Yes ? 0 : 1;
        if (dest == 1 && !ResolveNameIssues(incoming)) return;
        PushHistory(T("Import XPC", "XPC import"));
        MergeStats st = Db(dest).Merge(incoming);
        Db(dest).SortGames();
        if (dest == 0) SavePcLibrary();
        ClearQueues();
        SetActivity(String.Format(T("XPC importé : +{0} jeu(x), +{1} code(s), {2} remplacé(s).", "XPC imported: +{0} game(s), +{1} code(s), {2} replaced."), st.AddedGames, st.AddedCodes, st.ReplacedCodes));
        RefreshAll();
    }

    private bool ResolveNameIssues(CodeDB incoming)
    {
        List<NameIssue> issues = incoming.FindNameIssues();
        if (issues.Count == 0) return true;
        DialogResult r = MessageBox.Show(this,
            String.Format(T("{0} nom(s) dépassent le champ AR Latin-1 de 20 octets.\r\n\r\nOui = correction automatique\r\nNon = revoir manuellement\r\nAnnuler = abandonner l'import",
                            "{0} name(s) exceed the 20-byte AR Latin-1 field.\r\n\r\nYes = automatic correction\r\nNo = review manually\r\nCancel = abort import"), issues.Count),
            T("Noms Action Replay", "Action Replay names"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
        if (r == DialogResult.Cancel) return false;
        if (r == DialogResult.Yes)
        {
            incoming.ApplyNameFixes(issues); return true;
        }
        foreach (NameIssue issue in issues)
        {
            string prompt = issue.CheatIndex < 0 ? T("Nom du jeu :", "Game name:") : T("Nom du code :", "Code name:");
            string v = PromptText(T("Correction manuelle", "Manual correction"), prompt + "\r\n" + issue.Original, issue.Suggested);
            if (v == null) return false;
            issue.Suggested = CodeModel.SuggestSafeName(v);
        }
        incoming.ApplyNameFixes(issues);
        return true;
    }

    private void ExportCurrentXpc()
    {
        CodeDB d = Db(activeSource);
        using (SaveFileDialog s = new SaveFileDialog())
        {
            s.Filter = "Action Replay XPC (*.xpc)|*.xpc";
            s.FileName = activeSource == 0 ? LibraryDisplayName(libraryMode) + ".xpc" : "ActionReplay.xpc";
            if (s.ShowDialog(this) != DialogResult.OK) return;
            try { d.SaveXPC(s.FileName); SetActivity(T("XPC exporté.", "XPC exported.")); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message); }
        }
    }

    private void ExportSelectedXpc(int src, int kind)
    {
        CodeDB d = Db(src); int gi = SelectedGame(src), ci = SelectedCheat(src);
        if (gi < 0 || gi >= d.Games.Count) return;
        CodeDB outDb = new CodeDB(); Game g = new Game(); g.Name = d.Games[gi].Name;
        if (kind == 1 && ci >= 0 && ci < d.Games[gi].Cheats.Count)
        {
            g.Cheats.Add(d.Games[gi].Cheats[ci].Clone());
            EnsureMaster(d.Games[gi], g);
        }
        else foreach (Cheat c in d.Games[gi].Cheats) g.Cheats.Add(c.Clone());
        outDb.Games.Add(g);
        using (SaveFileDialog s = new SaveFileDialog())
        {
            s.Filter = "Action Replay XPC (*.xpc)|*.xpc"; s.FileName = g.Name + ".xpc";
            if (s.ShowDialog(this) == DialogResult.OK) outDb.SaveXPC(s.FileName);
        }
    }

    private void OpenBinIntoAr(string path)
    {
        if (!ConfirmPendingEdit()) return;
        try
        {
            PushHistory(T("Ouverture BIN", "Open BIN"));
            arDb = CodeDB.LoadBlob(path); arDb.CoalesceEquivalentGames(); arDb.SortGames();
            arSelectedGame = arDb.Games.Count > 0 ? 0 : -1; arSelectedCheat = -1; activeSource = 1;
            ClearQueues(); RefreshAll();
            SetActivity(T("Base BIN ouverte dans l'éditeur.", "BIN database opened in editor."));
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, T("Base BIN", "BIN database"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private async void ReadAr()
    {
        if (busy || !ConfirmPendingEdit()) return;
        bool ok = await ConnectInfo(false);
        if (!ok) return;
        await ReadArDatabase(false);
    }

    private async Task<bool> ConnectInfo(bool automatic)
    {
        if (!File.Exists(enginePath))
        {
            if (!automatic) MessageBox.Show(this, T("Moteur absent : ", "Missing engine: ") + enginePath);
            return false;
        }
        SetBusy(true, automatic ? T("Connexion automatique…", "Automatic connection…") : T("Lecture des informations…", "Reading information…"));
        ProcessResult r = await RunProcess(enginePath, QuoteArg("info"), true);
        if (r.ExitCode == 0)
        {
            ParseInfo(r.Output);
            deviceConnected = true; usbPresent = true; autoRecoveryStage = 0;
            RefreshDeviceUi(); RefreshBottomStatus();
            SetBusy(false, T("Action Replay connecté", "Action Replay connected"));
            RequestBoxArt();
            return true;
        }

        string low = r.Output.ToLowerInvariant();
        if (automatic && autoRecoveryStage == 0 && (low.Contains("semaphore") || low.Contains("winusb_writepipe") || low.Contains("winusb_readpipe")))
        {
            autoRecoveryStage = 1;
            SetActivity(T("AR détecté mais non prêt — récupération des pipes WinUSB…", "AR detected but not ready — recovering WinUSB pipes…"));
            ProcessResult rr = await RunProcess(enginePath, QuoteArg("info") + " --recover", true);
            if (rr.ExitCode == 0)
            {
                ParseInfo(rr.Output); deviceConnected = true; autoRecoveryStage = 0; RefreshDeviceUi(); RequestBoxArt(); return true;
            }
        }

        deviceConnected = false;
        SetBusy(false, T("Action Replay non prêt", "Action Replay not ready"));
        RefreshDeviceUi(); RefreshBottomStatus();
        if (!automatic)
            MessageBox.Show(this, FriendlyFailure(r.Output), "Action Replay GBX", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return false;
    }

    private async Task ReadArDatabase(bool automatic)
    {
        string temp = Path.Combine(Path.GetTempPath(), "ActionReplayGBX-current-" + Guid.NewGuid().ToString("N") + ".bin");
        SetBusy(true, T("Lecture de la base Action Replay…", "Reading Action Replay database…"));
        transferProgress.Value = 0;
        ProcessResult r = await RunProcess(enginePath, QuoteArg("dump-codes") + " " + QuoteArg(temp), true);
        if (r.ExitCode != 0)
        {
            SetBusy(false, T("Erreur lecture AR", "AR read error"));
            if (!automatic) MessageBox.Show(this, FriendlyFailure(r.Output), "Action Replay GBX", MessageBoxButtons.OK, MessageBoxIcon.Error);
            TryDelete(temp); return;
        }
        try
        {
            arDb = CodeDB.LoadBlob(temp);
            MergeStats st = arDb.CoalesceEquivalentGames();
            arDb.SortGames();
            arSelectedGame = arDb.Games.Count > 0 ? 0 : -1; arSelectedCheat = -1; activeSource = 1;
            arLoadedThisConnection = true;
            transferProgress.Value = 1000;
            SetActivity(st.RemovedGames > 0 ? String.Format(T("Base AR chargée — {0} doublon(s) fusionné(s) en mémoire ; écrire l'AR pour appliquer", "AR database loaded — {0} duplicate(s) merged in memory; write AR to apply"), st.RemovedGames)
                                                    : String.Format(T("Base AR chargée : {0} jeu(x), {1} code(s)", "AR database loaded: {0} game(s), {1} code(s)"), arDb.Games.Count, arDb.CheatCount()));
            RefreshAll();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, T("La base a été lue mais son analyse a échoué :\r\n", "The database was read but parsing failed:\r\n") + ex.Message, "Action Replay GBX", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { TryDelete(temp); SetBusy(false, T("Lecture terminée", "Read complete")); }
    }

    private async void WriteAr()
    {
        if (busy || !ConfirmPendingEdit()) return;
        if (arDb.Games.Count == 0)
        {
            MessageBox.Show(this, T("La base Action Replay en mémoire est vide.", "The in-memory Action Replay database is empty.")); return;
        }
        if (MessageBox.Show(this, T("Écrire la base actuellement affichée dans l'Action Replay ?\r\n\r\nLe moteur créera un backup puis vérifiera la relecture octet par octet.",
                                    "Write the currently displayed database to Action Replay?\r\n\r\nThe engine will create a backup and verify the read-back byte-for-byte."),
                            T("Écriture Action Replay", "Action Replay write"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        CodeDB writeDb = arDb;
        List<NameIssue> issues = writeDb.FindNameIssues();
        if (issues.Count > 0)
        {
            if (MessageBox.Show(this, String.Format(T("{0} nom(s) seront raccourcis uniquement pour l'écriture matérielle. Continuer ?", "{0} name(s) will be shortened only for hardware writing. Continue?"), issues.Count),
                T("Noms Action Replay", "Action Replay names"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            writeDb = arDb.ARSafeCopy();
        }
        string temp = Path.Combine(Path.GetTempPath(), "ActionReplayGBX-write-" + Guid.NewGuid().ToString("N") + ".bin");
        try { writeDb.SortGames(); writeDb.SaveBlob(temp); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message); return; }

        SetBusy(true, T("Écriture de l'Action Replay…", "Writing Action Replay…"));
        transferProgress.Value = 0;
        ProcessResult r = await RunProcess(enginePath, QuoteArg("write-codes") + " " + QuoteArg(temp) + " --enable-write", true);
        TryDelete(temp);
        SetBusy(false, r.ExitCode == 0 ? T("Écriture terminée et vérifiée", "Write completed and verified") : T("Erreur écriture", "Write error"));
        if (r.ExitCode == 0)
        {
            transferProgress.Value = 1000; ParseRemainingStorage(r.Output);
            MessageBox.Show(this, T("Écriture réussie. La base a été relue et vérifiée octet par octet.", "Write successful. The database was read back and verified byte-for-byte."), "Action Replay GBX", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else MessageBox.Show(this, FriendlyFailure(r.Output), "Action Replay GBX", MessageBoxButtons.OK, MessageBoxIcon.Error);
        RefreshBottomStatus();
    }

    private async void DumpSave()
    {
        if (busy) return;
        using (SaveFileDialog s = new SaveFileDialog())
        {
            s.Filter = "GBA save (*.sav)|*.sav|All files (*.*)|*.*";
            s.FileName = (String.IsNullOrEmpty(deviceGameId) ? "gba-save" : deviceGameId) + ".sav";
            if (s.ShowDialog(this) != DialogResult.OK) return;
            SetBusy(true, T("Sauvegarde GBA…", "GBA save backup…"));
            ProcessResult r = await RunProcess(enginePath, QuoteArg("dump-save") + " " + QuoteArg(s.FileName), true);
            SetBusy(false, r.ExitCode == 0 ? T("Sauvegarde GBA exportée", "GBA save exported") : T("Erreur sauvegarde", "Save error"));
            if (r.ExitCode != 0) MessageBox.Show(this, FriendlyFailure(r.Output));
        }
    }

    private async void RestoreSave()
    {
        if (busy) return;
        using (OpenFileDialog o = new OpenFileDialog())
        {
            o.Filter = "GBA save (*.sav)|*.sav|All files (*.*)|*.*";
            if (o.ShowDialog(this) != DialogResult.OK) return;
            if (new FileInfo(o.FileName).Length != 65536)
            {
                MessageBox.Show(this, T("Le fichier doit faire exactement 65536 octets.", "The file must be exactly 65536 bytes.")); return;
            }
            if (MessageBox.Show(this, T("Remplacer la sauvegarde de la cartouche ?", "Replace the cartridge save data?"), T("Restauration SAVE", "SAVE restore"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            SetBusy(true, T("Restauration SAVE…", "Restoring SAVE…"));
            ProcessResult r = await RunProcess(enginePath, QuoteArg("write-save") + " " + QuoteArg(o.FileName) + " --enable-write", true);
            SetBusy(false, r.ExitCode == 0 ? T("Sauvegarde GBA restaurée", "GBA save restored") : T("Erreur restauration", "Restore error"));
            if (r.ExitCode != 0) MessageBox.Show(this, FriendlyFailure(r.Output));
        }
    }

    private async void DumpFirmware()
    {
        if (busy) return;
        using (SaveFileDialog s = new SaveFileDialog())
        {
            s.Filter = "Flash image (*.bin)|*.bin"; s.FileName = "ActionReplayGBX-flash-256K.bin";
            if (s.ShowDialog(this) != DialogResult.OK) return;
            SetBusy(true, T("Sauvegarde firmware 256 Kio…", "256 KiB firmware backup…"));
            transferProgress.Value = 0;
            ProcessResult r = await RunProcess(enginePath, QuoteArg("dump-firmware") + " " + QuoteArg(s.FileName), true);
            SetBusy(false, r.ExitCode == 0 ? T("Firmware sauvegardé et vérifié", "Firmware saved and verified") : T("Erreur firmware", "Firmware error"));
            if (r.ExitCode == 0 && File.Exists(s.FileName) && new FileInfo(s.FileName).Length == 262144)
                MessageBox.Show(this, T("Firmware sauvegardé avec succès (256 Kio vérifiés).", "Firmware saved successfully (256 KiB verified)."), "Action Replay GBX");
            else if (r.ExitCode != 0) MessageBox.Show(this, FriendlyFailure(r.Output));
        }
    }

    private async void WriteFirmware()
    {
        if (busy) return;
        using (OpenFileDialog o = new OpenFileDialog())
        {
            o.Filter = "Firmware Datel (*.gsu;*.bin)|*.gsu;*.bin|All files (*.*)|*.*";
            if (o.ShowDialog(this) != DialogResult.OK) return;
            SetBusy(true, T("Validation firmware…", "Validating firmware…"));
            ProcessResult v = await RunProcess(enginePath, QuoteArg("validate-firmware") + " " + QuoteArg(o.FileName), true);
            SetBusy(false, T("Validation terminée", "Validation complete"));
            if (v.ExitCode != 0) { MessageBox.Show(this, FriendlyFailure(v.Output)); return; }
            if (MessageBox.Show(this, T("ATTENTION : l'écriture firmware reste l'opération la plus risquée.\r\nUne sauvegarde complète 256 Kio sera créée automatiquement avant l'écriture.\r\n\r\nContinuer ?",
                                        "WARNING: firmware writing remains the riskiest operation.\r\nA full 256 KiB backup will be created automatically before writing.\r\n\r\nContinue?"),
                                T("ÉCRITURE FIRMWARE", "FIRMWARE WRITE"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            if (MessageBox.Show(this, T("Dernière confirmation : ne débranche ni USB ni alimentation jusqu'au retour du menu Action Replay.", "Final confirmation: do not disconnect USB or power until the Action Replay menu returns."),
                                T("Confirmation firmware", "Firmware confirmation"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            SetBusy(true, T("Mise à jour firmware…", "Firmware update…"));
            ProcessResult r = await RunProcess(enginePath, QuoteArg("write-firmware") + " " + QuoteArg(o.FileName) + " --enable-firmware-write", true);
            SetBusy(false, r.ExitCode == 0 ? T("Mise à jour firmware transmise", "Firmware update transferred") : T("Erreur firmware", "Firmware error"));
            if (r.ExitCode == 0)
                MessageBox.Show(this, T("Le firmware a été transmis et le CRC32 accepté. Ne coupe pas la GBA et ne débranche pas l'USB tant que le menu AR n'est pas revenu.",
                                        "Firmware was transferred and CRC32 accepted. Do not power off the GBA or disconnect USB until the AR menu returns."));
            else MessageBox.Show(this, FriendlyFailure(r.Output));
        }
    }

    private async void RunDriverRepair()
    {
        if (busy) return;
        if (!File.Exists(driverPath)) { MessageBox.Show(this, T("Composant pilote absent : ", "Missing driver component: ") + driverPath); return; }
        SetBusy(true, T("Installation / réparation WinUSB…", "Installing / repairing WinUSB…"));
        ProcessResult r = await RunProcess(driverPath, "--apply", true);
        AppendLog(r.Output);
        bool ok = false;
        for (int attempt = 1; attempt <= 8; attempt++)
        {
            SetActivity(String.Format(T("Pilote appliqué — attente de la réénumération Windows ({0}/8)…", "Driver applied — waiting for Windows re-enumeration ({0}/8)…"), attempt));
            await Task.Delay(attempt <= 3 ? 900 : 1600);
            RefreshWmi();
            if (String.Equals(usbService, "WINUSB", StringComparison.OrdinalIgnoreCase))
            {
                ProcessResult info = await RunProcess(enginePath, QuoteArg("info"), false);
                if (info.ExitCode == 0)
                {
                    ParseInfo(info.Output); deviceConnected = true; ok = true; break;
                }
            }
        }
        SetBusy(false, ok ? T("Pilote configuré et Action Replay accessible", "Driver configured and Action Replay accessible")
                          : T("Pilote appliqué — Action Replay pas encore accessible", "Driver applied — Action Replay not accessible yet"));
        RefreshDeviceUi(); RefreshBottomStatus();
        if (ok)
        {
            RequestBoxArt();
            if (!arLoadedThisConnection) await ReadArDatabase(true);
        }
        else
        {
            MessageBox.Show(this,
                T("WinUSB a été installé/réparé, mais l'interface ActionReplayGBX n'est pas encore accessible après plusieurs réessais.\r\n\r\nLaisse la GBA au menu Action Replay, puis clique « Lire / actualiser l'AR ». Un débranchement/rebranchement USB peut encore être nécessaire si Windows n'a pas terminé la réénumération.",
                  "WinUSB was installed/repaired, but the ActionReplayGBX interface is still not accessible after several retries.\r\n\r\nLeave the GBA on the Action Replay menu, then click “Read / refresh AR”. An unplug/replug may still be needed if Windows has not finished re-enumeration."),
                T("Pilote", "Driver"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void AutoTick()
    {
        if (busy || autoConnectRunning) return;
        RefreshWmi();
        RefreshDeviceUi();
        if (!deviceConnected && usbPresent && String.Equals(usbService, "WINUSB", StringComparison.OrdinalIgnoreCase)) BeginAutoConnect(false);
    }

    private async void BeginAutoConnect(bool startup)
    {
        if (autoConnectRunning || busy) return;
        RefreshWmi();
        if (!usbPresent || !String.Equals(usbService, "WINUSB", StringComparison.OrdinalIgnoreCase)) { RefreshDeviceUi(); return; }
        autoConnectRunning = true;
        bool ok = await ConnectInfo(true);
        if (ok && !arLoadedThisConnection) await ReadArDatabase(true);
        autoConnectRunning = false;
    }

    private void RefreshWmi()
    {
        try
        {
            bool found = false;
            using (ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT PNPDeviceID, Service, Name FROM Win32_PnPEntity"))
            using (ManagementObjectCollection c = s.Get())
            {
                foreach (ManagementObject o in c)
                {
                    string id = Convert.ToString(o["PNPDeviceID"]);
                    if (String.IsNullOrEmpty(id) || !id.StartsWith(DevicePrefix, StringComparison.OrdinalIgnoreCase)) continue;
                    found = true;
                    usbService = Convert.ToString(o["Service"]);
                    usbName = Convert.ToString(o["Name"]);
                    break;
                }
            }
            if (!found)
            {
                usbPresent = false; usbService = ""; usbName = "";
                if (deviceConnected) { deviceConnected = false; arLoadedThisConnection = false; }
            }
            else usbPresent = true;
        }
        catch (Exception ex) { AppendLog("WMI: " + ex.Message); }
    }

    private void ParseInfo(string outp)
    {
        foreach (string raw in outp.Replace("\r", "").Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith("Version:", StringComparison.OrdinalIgnoreCase)) usbVersion = line.Substring(8).Trim();
            else if (line.StartsWith("Remaining storage:", StringComparison.OrdinalIgnoreCase))
            {
                Match m = Regex.Match(line, @"(\d+)"); int n; if (m.Success && Int32.TryParse(m.Groups[1].Value, out n)) remainingStorage = n;
            }
            else if (line.StartsWith("Game:", StringComparison.OrdinalIgnoreCase))
            {
                deviceGame = line.Substring(5).Trim();
                Match m = Regex.Match(deviceGame, @"\(([A-Za-z0-9]{4})\)\s*$");
                deviceGameId = m.Success ? m.Groups[1].Value.ToUpperInvariant() : "";
            }
        }
    }

    private void ParseRemainingStorage(string outp)
    {
        foreach (string raw in outp.Replace("\r", "").Split('\n'))
        {
            Match m = Regex.Match(raw, @"(?:Remaining storage:|Post-write storage query OK:)\s*(\d+)");
            int n; if (m.Success && Int32.TryParse(m.Groups[1].Value, out n)) remainingStorage = n;
        }
    }

    private void RefreshDeviceUi()
    {
        deviceTitle.Text = T("JEU CONNECTÉ", "CONNECTED GAME");
        saveTitle.Text = T("SAUVEGARDE DU JEU CONNECTÉ", "CONNECTED GAME SAVE");

        if (deviceConnected)
        {
            string name = deviceGame;
            if (deviceGameId.Length > 0)
            {
                int i = name.LastIndexOf(" (" + deviceGameId + ")", StringComparison.OrdinalIgnoreCase);
                if (i > 0) name = name.Substring(0, i);
            }
            if (String.IsNullOrWhiteSpace(name)) name = T("Jeu non identifié", "Unknown game");
            deviceNameLabel.Text = name;
            string details = "";
            if (deviceGameId.Length > 0) details += "ID: " + deviceGameId + "   •   ";
            details += "USB " + (String.IsNullOrEmpty(usbVersion) ? "—" : usbVersion);
            if (remainingStorage >= 0) details += "   •   " + T("Espace libre AR", "AR free space") + ": " + remainingStorage + " " + T("octets", "bytes");
            deviceDetailsLabel.Text = details;
            connectionWarning.Visible = false;
        }
        else
        {
            boxArt.Visible = false;
            if (usbPresent)
            {
                deviceNameLabel.Text = T("Action Replay détecté", "Action Replay detected") + " — " + (String.IsNullOrWhiteSpace(usbName) ? "GBA Link" : usbName) + " — service : " + (String.IsNullOrWhiteSpace(usbService) ? "—" : usbService);
                deviceDetailsLabel.Text = String.Equals(usbService, "WINUSB", StringComparison.OrdinalIgnoreCase)
                    ? T("WinUSB est actif, mais le protocole Action Replay ne répond pas encore.", "WinUSB is active, but Action Replay protocol is not responding yet.")
                    : T("Le périphérique USB est présent mais WinUSB n'est pas actif sur cette instance.", "USB device is present but WinUSB is not active on this instance.");
            }
            else
            {
                deviceNameLabel.Text = T("Aucun Action Replay USB détecté", "No Action Replay USB device detected");
                deviceDetailsLabel.Text = T("Travail hors ligne disponible • Le pilote peut être installé/réparé avec « Pilote ».", "Offline work available • Driver can be installed/repaired with “Driver”.");
            }
            connectionWarning.Text = T("⚠ RECOMMANDATION : branche l’USB et allume la GBA jusqu’au menu Action Replay avant de lancer le logiciel. Si l’AR reste non connecté, laisse la récupération automatique agir ou débranche/rebranche l’USB une fois la GBA prête.",
                                       "⚠ RECOMMENDATION: connect USB and power the GBA on to the Action Replay menu before starting the app. If AR remains disconnected, let automatic recovery run or unplug/replug USB once the GBA is ready.");
            connectionWarning.Visible = true;
        }
    }

    private void RefreshBottomStatus()
    {
        string conn = deviceConnected ? T("Connecté / répond", "Connected / responding") : usbPresent ? T("USB détecté", "USB detected") : T("Non détecté", "Not detected");
        string free = remainingStorage >= 0 ? remainingStorage + " " + T("octets", "bytes") : "—";
        string activity = String.IsNullOrWhiteSpace(transferText.Text) ? T("Prêt", "Ready") : transferText.Text;
        bottomStatus.Text = String.Format("{0}: {1}   |   {2}: {3}   |   {4}: {5}\r\n{6}: {7} {8} / {9} {10}   |   {11}: {12} {8} / {13} {10}",
            T("Action Replay", "Action Replay"), conn, T("Espace libre", "Free space"), free, T("État", "Status"), activity,
            T("Cartouche", "Device DB"), arDb.Games.Count, T("jeux", "games"), arDb.CheatCount(), T("codes", "codes"),
            T("Bibliothèque PC", "PC library"), pcDb.Games.Count, pcDb.CheatCount());

        int used = 0;
        try { if (arDb.Games.Count > 0) used = arDb.Blob().Length; } catch { }
        if (deviceConnected && remainingStorage >= 0 && used + remainingStorage > 0)
        {
            int cap = used + remainingStorage;
            int p = (int)Math.Round(1000.0 * used / cap);
            storageProgress.Value = Math.Max(0, Math.Min(1000, p));
            storageText.Text = String.Format(T("Mémoire codes AR : {0} / {1} octets utilisés ({2:0.0}%)", "AR code memory: {0} / {1} bytes used ({2:0.0}%)"), used, cap, 100.0 * used / cap);
        }
        else
        {
            storageProgress.Value = 0;
            storageText.Text = T("Mémoire codes AR : connecte puis lis l'Action Replay pour calculer l'occupation", "AR code memory: connect and read Action Replay to calculate usage");
        }
    }

    private void SetActivity(string text)
    {
        if (InvokeRequired) { BeginInvoke((MethodInvoker)delegate { SetActivity(text); }); return; }
        transferText.Text = text;
        RefreshBottomStatus();
    }

    private void SetBusy(bool value, string status)
    {
        if (InvokeRequired) { BeginInvoke((MethodInvoker)delegate { SetBusy(value, status); }); return; }
        busy = value;
        readButton.Enabled = !value; writeButton.Enabled = !value; driverButton.Enabled = !value;
        firmwareBackupButton.Enabled = !value; firmwareUpdateButton.Enabled = !value;
        SetActivity(status);
    }

    private void OnProcessLine(string line)
    {
        if (String.IsNullOrWhiteSpace(line)) return;
        string localized = line;
        if (LanguageManager.IsFrench)
        {
            if (localized.StartsWith("Writing code DB:", StringComparison.OrdinalIgnoreCase)) localized = "Écriture de la base :" + localized.Substring("Writing code DB:".Length);
            else if (localized.StartsWith("Firmware update:", StringComparison.OrdinalIgnoreCase)) localized = "Mise à jour du firmware :" + localized.Substring("Firmware update:".Length);
            else if (localized.StartsWith("Firmware:", StringComparison.OrdinalIgnoreCase)) localized = "Lecture du firmware :" + localized.Substring("Firmware:".Length);
            else if (localized.StartsWith("Games:", StringComparison.OrdinalIgnoreCase)) localized = "Lecture des jeux :" + localized.Substring("Games:".Length);
        }
        SetActivity(localized);

        Match f = FractionRx.Match(line);
        if (f.Success)
        {
            int a, b; if (Int32.TryParse(f.Groups[1].Value, out a) && Int32.TryParse(f.Groups[2].Value, out b) && b > 0)
                BeginInvoke((MethodInvoker)delegate { transferProgress.Value = Math.Max(0, Math.Min(1000, a * 1000 / b)); });
        }
        else
        {
            Match p = PercentRx.Match(line);
            double v;
            if (p.Success && Double.TryParse(p.Groups[1].Value.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v))
                BeginInvoke((MethodInvoker)delegate { transferProgress.Value = Math.Max(0, Math.Min(1000, (int)Math.Round(v * 10))); });
        }
    }

    private sealed class ProcessResult { internal int ExitCode; internal string Output; }

    private Task<ProcessResult> RunProcess(string file, string arguments, bool captureProgress)
    {
        return Task.Factory.StartNew(delegate
        {
            StringBuilder all = new StringBuilder();
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = file; psi.Arguments = arguments;
            psi.UseShellExecute = false; psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
            using (Process p = new Process())
            {
                p.StartInfo = psi; p.Start();
                Task tOut = Task.Factory.StartNew(delegate { ReadStream(p.StandardOutput, all, captureProgress); });
                Task tErr = Task.Factory.StartNew(delegate { ReadStream(p.StandardError, all, captureProgress); });
                p.WaitForExit(); Task.WaitAll(tOut, tErr);
                string output = all.ToString().Trim();
                AppendLog(Path.GetFileName(file) + " " + arguments + Environment.NewLine + output + Environment.NewLine + "exit=" + p.ExitCode);
                return new ProcessResult { ExitCode = p.ExitCode, Output = output };
            }
        });
    }

    private void ReadStream(StreamReader r, StringBuilder all, bool progress)
    {
        StringBuilder line = new StringBuilder();
        char[] one = new char[1];
        while (true)
        {
            int n = r.Read(one, 0, 1); if (n <= 0) break;
            char c = one[0];
            if (c == '\r' || c == '\n')
            {
                if (line.Length > 0)
                {
                    string s = line.ToString(); lock (all) { all.AppendLine(s); }
                    if (progress) OnProcessLine(s);
                    line.Length = 0;
                }
            }
            else line.Append(c);
        }
        if (line.Length > 0)
        {
            string s = line.ToString(); lock (all) { all.AppendLine(s); }
            if (progress) OnProcessLine(s);
        }
    }

    private string QuoteArg(string s)
    {
        if (s == null) return "\"\"";
        return "\"" + s.Replace("\"", "\\\"") + "\"";
    }

    private string FriendlyFailure(string output)
    {
        string low = (output ?? "").ToLowerInvariant();
        string text = T("L'opération a échoué.", "The operation failed.");
        if (low.Contains("semaphore timeout")) text = T("L'Action Replay est détecté mais ne répond pas. Laisse la GBA allumée jusqu'au menu Action Replay.", "Action Replay is detected but not responding. Leave the GBA powered on to the Action Replay menu.");
        else if (low.Contains("not found through winusb")) text = T("Action Replay non détecté via l'interface WinUSB/GUID. Utilise « Pilote », puis laisse Windows terminer la réénumération.", "Action Replay was not detected through WinUSB/GUID. Use “Driver”, then let Windows finish re-enumeration.");
        if (!String.IsNullOrWhiteSpace(output)) text += "\r\n\r\n" + T("Détail technique :", "Technical detail:") + "\r\n" + (output.Length > 1800 ? output.Substring(0, 1800) + "…" : output);
        text += "\r\n\r\n" + T("Journal :", "Log:") + "\r\n" + LogPath;
        return text;
    }

    private string LogPath { get { return Path.Combine(logDir, "ActionReplayGBX.log"); } }

    private void AppendLog(string text)
    {
        try
        {
            string line = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + (text ?? "").Trim() + Environment.NewLine;
            lock (operationLog) { operationLog.Append(line); }
            Directory.CreateDirectory(logDir); File.AppendAllText(LogPath, line, new UTF8Encoding(false));
        }
        catch { }
    }

    private void ShowJournal()
    {
        Form f = new Form();
        f.Text = T("Journal / outils", "Log / tools") + " — ActionReplayGBX v" + VersionText;
        f.Width = 900; f.Height = 620; f.StartPosition = FormStartPosition.CenterParent; f.AutoScaleMode = AutoScaleMode.Dpi; f.Font = Font;
        TableLayoutPanel root = new TableLayoutPanel(); root.Dock = DockStyle.Fill; root.Padding = new Padding(8); root.RowCount = 3; root.ColumnCount = 1;
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62f)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
        Label diag = new Label(); diag.Dock = DockStyle.Fill; diag.Text = DiagnosticText();
        TextBox log = new TextBox(); log.Dock = DockStyle.Fill; log.Multiline = true; log.ReadOnly = true; log.ScrollBars = ScrollBars.Both; log.WordWrap = false; log.Font = new Font("Consolas", 9f);
        try { log.Text = File.Exists(LogPath) ? File.ReadAllText(LogPath) : operationLog.ToString(); } catch { log.Text = operationLog.ToString(); }
        FlowLayoutPanel buttons = new FlowLayoutPanel(); buttons.Dock = DockStyle.Fill; buttons.FlowDirection = FlowDirection.LeftToRight;
        Button refresh = new Button(); refresh.Text = T("Actualiser", "Refresh"); refresh.AutoSize = true; refresh.Click += delegate { RefreshWmi(); diag.Text = DiagnosticText(); try { log.Text = File.Exists(LogPath) ? File.ReadAllText(LogPath) : operationLog.ToString(); } catch { } };
        Button info = new Button(); info.Text = T("Infos AR (lecture seule)", "AR info (read-only)"); info.AutoSize = true; info.Click += async delegate { ProcessResult r = await RunProcess(enginePath, QuoteArg("info"), false); MessageBox.Show(f, r.Output, "engine info"); };
        Button openLogs = new Button(); openLogs.Text = T("Ouvrir dossier Logs", "Open Logs folder"); openLogs.AutoSize = true; openLogs.Click += delegate { Process.Start("explorer.exe", QuoteArg(logDir)); };
        Button copy = new Button(); copy.Text = T("Copier le journal", "Copy log"); copy.AutoSize = true; copy.Click += delegate { if (!String.IsNullOrEmpty(log.Text)) Clipboard.SetText(log.Text); };
        buttons.Controls.Add(refresh); buttons.Controls.Add(info); buttons.Controls.Add(openLogs); buttons.Controls.Add(copy);
        root.Controls.Add(diag, 0, 0); root.Controls.Add(log, 0, 1); root.Controls.Add(buttons, 0, 2); f.Controls.Add(root); f.ShowDialog(this);
    }

    private string DiagnosticText()
    {
        return "WMI: " + (usbPresent ? T("détecté", "detected") : T("non détecté", "not detected")) +
               "   |   Name: " + (String.IsNullOrWhiteSpace(usbName) ? "—" : usbName) +
               "   |   Service: " + (String.IsNullOrWhiteSpace(usbService) ? "—" : usbService) + Environment.NewLine +
               "Engine: " + enginePath + Environment.NewLine +
               "Log: " + LogPath;
    }

    private void OpenDataFolder()
    {
        try { Process.Start("explorer.exe", QuoteArg(dataDir)); } catch { }
    }

    private void RequestBoxArt()
    {
        if (!deviceConnected || String.IsNullOrEmpty(deviceGameId)) { boxArt.Visible = false; return; }
        string gid = deviceGameId; string gameName = deviceGame;
        Task.Factory.StartNew(delegate
        {
            string cache = Path.Combine(cacheDir, "BoxArt"); Directory.CreateDirectory(cache);
            string file = Path.Combine(cache, gid + ".png");
            if (!File.Exists(file)) TryDownloadBoxArt(gid, gameName, file);
            if (!File.Exists(file)) return;
            try
            {
                using (Image img = Image.FromFile(file))
                {
                    Image clone = new Bitmap(img);
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (boxArt.Image != null) boxArt.Image.Dispose();
                        boxArt.Image = clone; boxArt.Visible = true;
                        RefreshDeviceUi();
                    });
                }
            }
            catch { }
        });
    }

    private void TryDownloadBoxArt(string gid, string deviceName, string file)
    {
        List<string> titles = new List<string>();
        Action<string> add = delegate(string s)
        {
            if (String.IsNullOrWhiteSpace(s)) return;
            foreach (string x in titles) if (String.Equals(x, s.Trim(), StringComparison.OrdinalIgnoreCase)) return;
            titles.Add(s.Trim());
        };
        string baseUrl = "https://raw.githubusercontent.com/niemasd/GameDB-GBA/main/games/" + Uri.EscapeDataString(gid) + "/";
        string t = DownloadText(baseUrl + "release_name.txt"); if (t != null) { add(t); add(ShortGameTitle(t)); }
        t = DownloadText(baseUrl + "title.txt"); if (t != null) { add(t); add(ShortGameTitle(t)); }
        string cleanDevice = Regex.Replace(deviceName ?? "", @"\s*\([A-Za-z0-9]{4}\)\s*$", "").Trim();
        add(cleanDevice); add(ShortGameTitle(cleanDevice));

        foreach (string title in titles)
        {
            string n = LibretroName(title);
            string seg = Uri.EscapeDataString(n + ".png");
            string[] urls = new string[]
            {
                "https://thumbnails.libretro.com/Nintendo%20-%20Game%20Boy%20Advance/Named_Boxarts/" + seg,
                "https://raw.githubusercontent.com/libretro-thumbnails/Nintendo_-_Game_Boy_Advance/master/Named_Boxarts/" + seg
            };
            foreach (string url in urls)
            {
                byte[] data = DownloadBytes(url, 8 * 1024 * 1024);
                if (data == null || data.Length < 100) continue;
                try
                {
                    string tmp = Path.GetTempFileName();
                    File.WriteAllBytes(tmp, data);
                    using (Image img = Image.FromFile(tmp))
                    {
                        using (Bitmap fitted = FitImage(img, 92, 120))
                            fitted.Save(file, System.Drawing.Imaging.ImageFormat.Png);
                    }
                    TryDelete(tmp);
                    return;
                }
                catch { }
            }
        }
    }

    private string DownloadText(string url)
    {
        byte[] b = DownloadBytes(url, 32768); if (b == null) return null;
        string s = Encoding.UTF8.GetString(b).Trim(); return s.Length == 0 ? null : s;
    }

    private byte[] DownloadBytes(string url, int max)
    {
        try
        {
            HttpWebRequest r = (HttpWebRequest)WebRequest.Create(url);
            r.Timeout = 5000; r.ReadWriteTimeout = 5000; r.UserAgent = "ActionReplayGBX-W11/" + VersionText;
            using (HttpWebResponse resp = (HttpWebResponse)r.GetResponse())
            using (Stream s = resp.GetResponseStream())
            using (MemoryStream ms = new MemoryStream())
            {
                byte[] buf = new byte[8192]; int total = 0;
                while (true)
                {
                    int n = s.Read(buf, 0, buf.Length); if (n <= 0) break;
                    total += n; if (total > max) return null; ms.Write(buf, 0, n);
                }
                return ms.ToArray();
            }
        }
        catch { return null; }
    }

    private Bitmap FitImage(Image src, int w, int h)
    {
        Bitmap dst = new Bitmap(w, h);
        using (Graphics g = Graphics.FromImage(dst))
        {
            g.Clear(BackColor);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            double scale = Math.Min((double)w / src.Width, (double)h / src.Height);
            int dw = Math.Max(1, (int)Math.Round(src.Width * scale));
            int dh = Math.Max(1, (int)Math.Round(src.Height * scale));
            g.DrawImage(src, (w - dw) / 2, (h - dh) / 2, dw, dh);
        }
        return dst;
    }

    private string LibretroName(string s)
    {
        char[] bad = "&*/:<>?\\|\"".ToCharArray();
        foreach (char c in bad) s = s.Replace(c, '_');
        return s.Trim();
    }

    private string ShortGameTitle(string s)
    {
        int i = s.IndexOf(" (", StringComparison.Ordinal); return i > 0 ? s.Substring(0, i).Trim() : s.Trim();
    }

    private string PromptText(string title, string label, string def)
    {
        using (Form f = new Form())
        {
            f.Text = title; f.Width = 500; f.Height = 180; f.StartPosition = FormStartPosition.CenterParent; f.FormBorderStyle = FormBorderStyle.FixedDialog; f.MaximizeBox = false; f.MinimizeBox = false; f.Font = Font;
            Label l = new Label(); l.Text = label; l.Left = 15; l.Top = 12; l.Width = 450; l.Height = 42;
            TextBox t = new TextBox(); t.Text = def; t.Left = 15; t.Top = 58; t.Width = 450;
            Button ok = new Button(); ok.Text = "OK"; ok.Left = 280; ok.Top = 94; ok.Width = 85; ok.DialogResult = DialogResult.OK;
            Button cancel = new Button(); cancel.Text = T("Annuler", "Cancel"); cancel.Left = 380; cancel.Top = 94; cancel.Width = 85; cancel.DialogResult = DialogResult.Cancel;
            f.Controls.Add(l); f.Controls.Add(t); f.Controls.Add(ok); f.Controls.Add(cancel); f.AcceptButton = ok; f.CancelButton = cancel;
            return f.ShowDialog(this) == DialogResult.OK ? t.Text.Trim() : null;
        }
    }

    private void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
}
