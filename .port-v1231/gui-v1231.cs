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
[assembly: AssemblyDescription("ActionReplayGBX v1.2.31 CSharp XPC editor")]
[assembly: AssemblyVersion("1.2.31.0")]
[assembly: AssemblyFileVersion("1.2.31.0")]
[assembly: AssemblyInformationalVersion("1.2.31-xpc-port")]

internal sealed class MainForm : Form
{
    private const string DevicePrefix = "USB\\VID_05FD&PID_DAAE";
    private CodeDB db = new CodeDB();
    private string currentPath;
    private readonly Stack<CodeDB> undo = new Stack<CodeDB>();
    private readonly Stack<CodeDB> redo = new Stack<CodeDB>();
    private bool loadingSelection;
    private bool busy;

    private readonly TabControl tabs = new TabControl();
    private readonly ListBox gameList = new ListBox();
    private readonly ListBox cheatList = new ListBox();
    private readonly TextBox cheatName = new TextBox();
    private readonly TextBox codeText = new TextBox();
    private readonly CheckBox masterCheck = new CheckBox();
    private readonly Label libraryStatus = new Label();
    private readonly Label deviceStatus = new Label();
    private readonly TextBox log = new TextBox();
    private readonly FlowLayoutPanel hardwareButtons = new FlowLayoutPanel();
    private readonly Button undoButton = new Button();
    private readonly Button redoButton = new Button();

    internal MainForm()
    {
        Text = "ActionReplayGBX v1.2.31";
        Width = 1260;
        Height = 790;
        MinimumSize = new Size(980, 650);
        StartPosition = FormStartPosition.CenterScreen;
        AllowDrop = true;
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;

        tabs.Dock = DockStyle.Fill;
        Controls.Add(tabs);
        BuildLibraryTab();
        BuildHardwareTab();

        Shown += delegate
        {
            RefreshDevice();
            string defaultDb = Path.Combine(Application.StartupPath, "PCDatabase.xpc");
            if (File.Exists(defaultDb))
            {
                try { LoadDatabase(CodeDB.LoadXPC(defaultDb), defaultDb, false); }
                catch (Exception ex) { AppendLog("Default XPC load failed: " + ex.Message); RefreshLibrary(); }
            }
            else RefreshLibrary();
        };
    }

    private void BuildLibraryTab()
    {
        TabPage page = new TabPage("Bibliothèque XPC / codes");
        tabs.TabPages.Add(page);

        FlowLayoutPanel top = new FlowLayoutPanel();
        top.Dock = DockStyle.Top;
        top.Height = 82;
        top.WrapContents = true;
        top.AutoScroll = true;
        page.Controls.Add(top);

        AddToolButton(top, "Nouveau", delegate { NewDatabase(); });
        AddToolButton(top, "Ouvrir XPC", delegate { OpenXpc(); });
        AddToolButton(top, "Enregistrer XPC", delegate { SaveXpc(false); });
        AddToolButton(top, "Enregistrer sous", delegate { SaveXpc(true); });
        AddToolButton(top, "Importer / fusionner XPC", delegate { ImportXpc(); });
        AddToolButton(top, "Exporter sélection XPC", delegate { ExportSelectedXpc(); });
        AddToolButton(top, "Ouvrir BIN AR", delegate { OpenRawBin(); });
        AddToolButton(top, "Exporter BIN AR", delegate { ExportRawBin(); });
        AddToolButton(top, "Lire AR → éditeur", delegate { ReadArToEditor(); });
        AddToolButton(top, "Écrire éditeur → AR", delegate { WriteEditorToAr(); });

        ConfigureToolButton(undoButton, "Annuler", delegate { Undo(); }); top.Controls.Add(undoButton);
        ConfigureToolButton(redoButton, "Rétablir", delegate { Redo(); }); top.Controls.Add(redoButton);
        AddToolButton(top, "+ Jeu", delegate { AddGame(); });
        AddToolButton(top, "Renommer jeu", delegate { RenameGame(); });
        AddToolButton(top, "Supprimer jeu", delegate { DeleteGames(); });
        AddToolButton(top, "Fusion manuelle", delegate { ManualMerge(); });
        AddToolButton(top, "Fusion par master", delegate { MergeByMaster(); });

        libraryStatus.Dock = DockStyle.Bottom;
        libraryStatus.Height = 28;
        libraryStatus.Padding = new Padding(8, 6, 8, 0);
        page.Controls.Add(libraryStatus);

        SplitContainer outer = new SplitContainer();
        outer.Dock = DockStyle.Fill;
        outer.SplitterDistance = 330;
        outer.Panel1.Padding = new Padding(8);
        outer.Panel2.Padding = new Padding(0, 8, 8, 8);
        page.Controls.Add(outer);
        outer.BringToFront();

        Label gl = new Label(); gl.Text = "Jeux (Ctrl/Shift = multi-sélection)"; gl.Dock = DockStyle.Top; gl.Height = 22;
        gameList.Dock = DockStyle.Fill;
        gameList.SelectionMode = SelectionMode.MultiExtended;
        gameList.SelectedIndexChanged += delegate { RefreshCheatList(); };
        outer.Panel1.Controls.Add(gameList); outer.Panel1.Controls.Add(gl);

        SplitContainer inner = new SplitContainer();
        inner.Dock = DockStyle.Fill;
        inner.SplitterDistance = 330;
        outer.Panel2.Controls.Add(inner);

        Panel cheatPanel = new Panel(); cheatPanel.Dock = DockStyle.Fill; inner.Panel1.Controls.Add(cheatPanel);
        FlowLayoutPanel cheatButtons = new FlowLayoutPanel(); cheatButtons.Dock = DockStyle.Bottom; cheatButtons.Height = 76; cheatButtons.WrapContents = true;
        AddToolButton(cheatButtons, "+ Code", delegate { AddCheat(); });
        AddToolButton(cheatButtons, "Renommer", delegate { RenameCheat(); });
        AddToolButton(cheatButtons, "Supprimer", delegate { DeleteCheat(); });
        cheatList.Dock = DockStyle.Fill; cheatList.SelectedIndexChanged += delegate { LoadSelectedCheat(); };
        Label cl = new Label(); cl.Text = "Codes du jeu"; cl.Dock = DockStyle.Top; cl.Height = 22;
        cheatPanel.Controls.Add(cheatList); cheatPanel.Controls.Add(cheatButtons); cheatPanel.Controls.Add(cl);

        Panel editor = new Panel(); editor.Dock = DockStyle.Fill; editor.Padding = new Padding(10); inner.Panel2.Controls.Add(editor);
        Label nl = new Label(); nl.Text = "Nom du code"; nl.Dock = DockStyle.Top; nl.Height = 22;
        cheatName.Dock = DockStyle.Top; cheatName.Height = 24;
        masterCheck.Text = "Code maître (M)"; masterCheck.Dock = DockStyle.Top; masterCheck.Height = 30;
        Label txl = new Label(); txl.Text = "Lignes de code — XXXXXXXX YYYYYYYY"; txl.Dock = DockStyle.Top; txl.Height = 25;
        codeText.Dock = DockStyle.Fill; codeText.Multiline = true; codeText.ScrollBars = ScrollBars.Both; codeText.WordWrap = false; codeText.Font = new Font("Consolas", 10.0f);
        Button apply = new Button(); apply.Text = "Appliquer les modifications"; apply.Dock = DockStyle.Bottom; apply.Height = 36; apply.Click += delegate { ApplyCheatEdit(); };
        editor.Controls.Add(codeText); editor.Controls.Add(apply); editor.Controls.Add(txl); editor.Controls.Add(masterCheck); editor.Controls.Add(cheatName); editor.Controls.Add(nl);
    }

    private void BuildHardwareTab()
    {
        TabPage page = new TabPage("Matériel USB / sauvegardes"); tabs.TabPages.Add(page);
        deviceStatus.Left = 16; deviceStatus.Top = 16; deviceStatus.Width = 1160; deviceStatus.Height = 44; deviceStatus.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; page.Controls.Add(deviceStatus);
        hardwareButtons.Left = 12; hardwareButtons.Top = 66; hardwareButtons.Width = 1180; hardwareButtons.Height = 150; hardwareButtons.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; hardwareButtons.AutoScroll = true; hardwareButtons.WrapContents = true; page.Controls.Add(hardwareButtons);
        AddToolButton(hardwareButtons, "Actualiser", delegate { RefreshDevice(); });
        AddToolButton(hardwareButtons, "Infos Action Replay", delegate { RunEngine(new string[] { "info" }, null); });
        AddToolButton(hardwareButtons, "Configurer WinUSB existant", delegate { RunDriver(); });
        AddToolButton(hardwareButtons, "Dump codes → BIN", delegate { DumpCodesOnly(); });
        AddToolButton(hardwareButtons, "Valider BIN", delegate { ValidateCodesOnly(); });
        AddToolButton(hardwareButtons, "Écrire BIN", delegate { WriteCodesOnly(); });
        AddToolButton(hardwareButtons, "Sauvegarder SAVE", delegate { DumpSave(); });
        AddToolButton(hardwareButtons, "Restaurer SAVE", delegate { RestoreSave(); });
        AddToolButton(hardwareButtons, "Dump Flash 256 Kio", delegate { DumpFirmware(); });
        AddToolButton(hardwareButtons, "Valider firmware", delegate { ValidateFirmware(); });
        AddToolButton(hardwareButtons, "Mettre à jour firmware", delegate { WriteFirmware(); });
        AddToolButton(hardwareButtons, "Déconnecter", delegate { RunEngine(new string[] { "disconnect" }, null); });
        log.Left = 16; log.Top = 228; log.Width = 1160; log.Height = 470; log.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right; log.Multiline = true; log.ReadOnly = true; log.ScrollBars = ScrollBars.Both; log.WordWrap = false; log.Font = new Font("Consolas", 9.0f); page.Controls.Add(log);
    }

    private static void ConfigureToolButton(Button b, string text, EventHandler action)
    {
        b.Text = text; b.Width = 152; b.Height = 32; b.Margin = new Padding(3); b.Click += action;
    }
    private static void AddToolButton(FlowLayoutPanel panel, string text, EventHandler action) { Button b = new Button(); ConfigureToolButton(b, text, action); panel.Controls.Add(b); }

    private int SelectedGameIndex() { return gameList.SelectedIndices.Count == 1 ? gameList.SelectedIndices[0] : -1; }
    private int SelectedCheatIndex() { return cheatList.SelectedIndex; }

    private void PushUndo()
    {
        undo.Push(db.Clone()); redo.Clear(); UpdateUndoButtons();
    }
    private void UpdateUndoButtons() { undoButton.Enabled = undo.Count > 0; redoButton.Enabled = redo.Count > 0; }
    private void Undo() { if (undo.Count == 0) return; redo.Push(db.Clone()); db = undo.Pop(); RefreshLibrary(); }
    private void Redo() { if (redo.Count == 0) return; undo.Push(db.Clone()); db = redo.Pop(); RefreshLibrary(); }

    private void NewDatabase()
    {
        if (db.Games.Count != 0 && MessageBox.Show(this, "Créer une base vide ? Les modifications non enregistrées seront perdues.", "Nouvelle base", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        db = new CodeDB(); currentPath = null; undo.Clear(); redo.Clear(); RefreshLibrary();
    }

    private void LoadDatabase(CodeDB loaded, string path, bool rememberUndo)
    {
        if (rememberUndo) PushUndo(); else { undo.Clear(); redo.Clear(); }
        db = loaded; currentPath = path; RefreshLibrary();
    }

    private void RefreshLibrary()
    {
        loadingSelection = true;
        try
        {
            int oldGame = gameList.SelectedIndex;
            gameList.Items.Clear();
            foreach (Game g in db.Games) gameList.Items.Add(g.Name);
            if (gameList.Items.Count > 0) gameList.SelectedIndex = Math.Min(Math.Max(oldGame, 0), gameList.Items.Count - 1);
            else { cheatList.Items.Clear(); ClearCheatEditor(); }
            string file = String.IsNullOrEmpty(currentPath) ? "non enregistrée" : Path.GetFileName(currentPath);
            int nameIssues = db.FindNameIssues().Count;
            libraryStatus.Text = String.Format("{0} — {1} jeux / {2} codes{3}", file, db.Games.Count, db.CheatCount(), nameIssues == 0 ? "" : " — " + nameIssues + " nom(s) à adapter pour l’AR");
            Text = "ActionReplayGBX v1.2.31 — " + file;
            UpdateUndoButtons();
        }
        finally { loadingSelection = false; }
        RefreshCheatList();
    }

    private void RefreshCheatList()
    {
        if (loadingSelection) return;
        loadingSelection = true;
        try
        {
            cheatList.Items.Clear();
            int gi = SelectedGameIndex();
            if (gi < 0 || gi >= db.Games.Count) { ClearCheatEditor(); return; }
            foreach (Cheat c in db.Games[gi].Cheats) cheatList.Items.Add(c.Name + (((c.Flags & 1u) != 0 || CodeModel.LooksLikeMasterName(c.Name)) ? "  [M]" : ""));
            if (cheatList.Items.Count > 0) cheatList.SelectedIndex = 0; else ClearCheatEditor();
        }
        finally { loadingSelection = false; }
        LoadSelectedCheat();
    }

    private void LoadSelectedCheat()
    {
        if (loadingSelection) return;
        int gi = SelectedGameIndex(); int ci = SelectedCheatIndex();
        if (gi < 0 || ci < 0 || gi >= db.Games.Count || ci >= db.Games[gi].Cheats.Count) { ClearCheatEditor(); return; }
        loadingSelection = true;
        try
        {
            Cheat c = db.Games[gi].Cheats[ci]; cheatName.Text = c.Name; masterCheck.Checked = (c.Flags & 1u) != 0 || CodeModel.LooksLikeMasterName(c.Name); codeText.Text = CodeModel.FormatCodeText(c.Words);
        }
        finally { loadingSelection = false; }
    }

    private void ClearCheatEditor() { cheatName.Text = ""; masterCheck.Checked = false; codeText.Text = ""; }

    private void AddGame()
    {
        string name = PromptText("Ajouter un jeu", "Nom du jeu :", ""); if (name == null) return;
        if (name.Trim().Length == 0) { Error("Le nom ne peut pas être vide."); return; }
        PushUndo(); Game g = new Game(); g.Name = name.Trim(); db.Games.Add(g); db.SortGames(); RefreshLibrary(); SelectGameByName(g.Name);
    }
    private void RenameGame()
    {
        int gi = SelectedGameIndex(); if (gi < 0) { Error("Sélectionne un seul jeu."); return; }
        string name = PromptText("Renommer le jeu", "Nouveau nom :", db.Games[gi].Name); if (name == null) return;
        if (name.Trim().Length == 0) { Error("Le nom ne peut pas être vide."); return; }
        PushUndo(); db.Games[gi].Name = name.Trim(); db.SortGames(); RefreshLibrary(); SelectGameByName(name.Trim());
    }
    private void DeleteGames()
    {
        if (gameList.SelectedIndices.Count == 0) return;
        if (MessageBox.Show(this, "Supprimer les jeux sélectionnés et tous leurs codes ?", "Suppression", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        PushUndo(); List<int> ix = new List<int>(); foreach (int i in gameList.SelectedIndices) ix.Add(i); ix.Sort(); ix.Reverse(); foreach (int i in ix) db.Games.RemoveAt(i); RefreshLibrary();
    }
    private void SelectGameByName(string name) { for (int i = 0; i < gameList.Items.Count; i++) if (String.Equals(Convert.ToString(gameList.Items[i]), name, StringComparison.OrdinalIgnoreCase)) { gameList.SelectedIndex = i; break; } }

    private void AddCheat()
    {
        int gi = SelectedGameIndex(); if (gi < 0) { Error("Sélectionne un jeu."); return; }
        string name = PromptText("Ajouter un code", "Nom du code :", "Nouveau code"); if (name == null) return;
        PushUndo(); Cheat c = new Cheat(); c.Name = name.Trim().Length == 0 ? "Nouveau code" : name.Trim(); db.Games[gi].Cheats.Add(c); RefreshCheatList(); cheatList.SelectedIndex = cheatList.Items.Count - 1;
    }
    private void RenameCheat()
    {
        int gi = SelectedGameIndex(); int ci = SelectedCheatIndex(); if (gi < 0 || ci < 0) return;
        string name = PromptText("Renommer le code", "Nouveau nom :", db.Games[gi].Cheats[ci].Name); if (name == null || name.Trim().Length == 0) return;
        PushUndo(); db.Games[gi].Cheats[ci].Name = name.Trim(); RefreshCheatList(); cheatList.SelectedIndex = Math.Min(ci, cheatList.Items.Count - 1);
    }
    private void DeleteCheat()
    {
        int gi = SelectedGameIndex(); int ci = SelectedCheatIndex(); if (gi < 0 || ci < 0) return;
        if (MessageBox.Show(this, "Supprimer ce code ?", "Suppression", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        PushUndo(); db.Games[gi].Cheats.RemoveAt(ci); RefreshCheatList();
    }
    private void ApplyCheatEdit()
    {
        int gi = SelectedGameIndex(); int ci = SelectedCheatIndex(); if (gi < 0 || ci < 0) { Error("Sélectionne un code."); return; }
        string name = cheatName.Text.Trim(); if (name.Length == 0) { Error("Le nom du code ne peut pas être vide."); return; }
        List<uint> words;
        try { words = CodeModel.ParseCodeText(codeText.Text); }
        catch (Exception ex) { Error("Code invalide : " + ex.Message); return; }
        PushUndo(); Cheat c = db.Games[gi].Cheats[ci]; c.Name = name; c.Words = words; if (masterCheck.Checked) c.Flags |= 1u; else c.Flags &= ~1u; RefreshCheatList(); cheatList.SelectedIndex = Math.Min(ci, cheatList.Items.Count - 1);
    }

    private void ManualMerge()
    {
        if (gameList.SelectedIndices.Count < 2) { Error("Sélectionne au moins deux jeux avec Ctrl/Shift."); return; }
        List<int> ix = new List<int>(); StringBuilder names = new StringBuilder(); foreach (int i in gameList.SelectedIndices) { ix.Add(i); names.AppendLine(db.Games[i].Name); }
        if (MessageBox.Show(this, "Fusionner ces jeux ?\r\n\r\n" + names.ToString(), "Fusion manuelle", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        PushUndo(); ManualMergeResult r = db.ManualMergeGames(ix); RefreshLibrary(); SelectGameByName(r.MergedName); MessageBox.Show(this, String.Format("Fusion terminée : +{0} codes, {1} remplacés, {2} doublons de données supprimés.", r.AddedCodes, r.ReplacedCodes, r.DedupedCodes), "Fusion", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void MergeByMaster()
    {
        List<List<string>> groups = db.PreviewMasterCodeMerges(); if (groups.Count == 0) { MessageBox.Show(this, "Aucun groupe partageant le même master code.", "Fusion master", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        StringBuilder text = new StringBuilder(); foreach (List<string> grp in groups) { text.AppendLine("• " + String.Join("  +  ", grp.ToArray())); }
        if (MessageBox.Show(this, "Groupes qui seront fusionnés uniquement par master code :\r\n\r\n" + text.ToString() + "\r\nContinuer ?", "Fusion par master code", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        PushUndo(); MergeStats st = db.CoalesceByMasterCode(); RefreshLibrary(); MessageBox.Show(this, String.Format("{0} jeux fusionnés, +{1} codes, {2} remplacés.", st.RemovedGames, st.AddedCodes, st.ReplacedCodes), "Fusion master", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void OpenXpc() { string p = ChooseOpen("Ouvrir une base XPC", "Base XPC (*.xpc)|*.xpc|Tous les fichiers (*.*)|*.*"); if (p == null) return; try { LoadDatabase(CodeDB.LoadXPC(p), p, false); } catch (Exception ex) { Error(ex.Message); } }
    private void OpenRawBin() { string p = ChooseOpen("Ouvrir une base binaire Action Replay", "Base binaire (*.bin)|*.bin|Tous les fichiers (*.*)|*.*"); if (p == null) return; try { LoadDatabase(CodeDB.LoadBlob(p), p, false); } catch (Exception ex) { Error(ex.Message); } }
    private void SaveXpc(bool forceDialog)
    {
        string p = currentPath; if (forceDialog || String.IsNullOrEmpty(p) || !p.EndsWith(".xpc", StringComparison.OrdinalIgnoreCase)) p = ChooseSave("Enregistrer la base XPC", "Base XPC (*.xpc)|*.xpc", String.IsNullOrEmpty(currentPath) ? "PCDatabase.xpc" : Path.GetFileNameWithoutExtension(currentPath) + ".xpc"); if (p == null) return;
        try { db.SaveXPC(p); currentPath = p; RefreshLibrary(); }
        catch (Exception ex) { Error(ex.Message); }
    }
    private void ImportXpc()
    {
        string p = ChooseOpen("Importer et fusionner une base XPC", "Base XPC (*.xpc)|*.xpc|Tous les fichiers (*.*)|*.*"); if (p == null) return;
        try { CodeDB incoming = CodeDB.LoadXPC(p); HandleNameIssues(incoming, true); PushUndo(); MergeStats st = db.Merge(incoming); RefreshLibrary(); MessageBox.Show(this, String.Format("Import terminé : +{0} jeux, +{1} codes, {2} codes remplacés.", st.AddedGames, st.AddedCodes, st.ReplacedCodes), "Import XPC", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        catch (Exception ex) { Error(ex.Message); }
    }
    private void ExportSelectedXpc()
    {
        if (gameList.SelectedIndices.Count == 0) { Error("Sélectionne un ou plusieurs jeux."); return; }
        string p = ChooseSave("Exporter les jeux sélectionnés", "Base XPC (*.xpc)|*.xpc", "Selection.xpc"); if (p == null) return;
        try { CodeDB outDb = new CodeDB(); foreach (int i in gameList.SelectedIndices) outDb.Games.Add(db.Games[i].Clone()); outDb.SaveXPC(p); }
        catch (Exception ex) { Error(ex.Message); }
    }
    private void ExportRawBin()
    {
        if (db.Games.Count == 0) { Error("La base est vide."); return; }
        if (!ConfirmArNameChanges()) return;
        string p = ChooseSave("Exporter la base binaire Action Replay", "Base binaire (*.bin)|*.bin", "ActionReplayGBX-codes.bin"); if (p == null) return;
        try { CodeDB safe = db.ARSafeCopy(); File.WriteAllBytes(p, safe.Blob()); MessageBox.Show(this, String.Format("BIN créé : {0} jeux / {1} codes.", safe.Games.Count, safe.CheatCount()), "Export BIN", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        catch (Exception ex) { Error(ex.Message); }
    }

    private void HandleNameIssues(bool ask) { HandleNameIssues(db, ask); }
    private void HandleNameIssues(CodeDB target, bool ask)
    {
        List<NameIssue> issues = target.FindNameIssues(); if (issues.Count == 0) return;
        StringBuilder sb = new StringBuilder(); int show = Math.Min(issues.Count, 8); for (int i = 0; i < show; i++) sb.AppendLine(issues[i].Original + "  →  " + issues[i].Suggested); if (issues.Count > show) sb.AppendLine("… et " + (issues.Count - show) + " autre(s)");
        if (!ask || MessageBox.Show(this, issues.Count + " nom(s) dépassent le champ AR Latin-1 de 20 octets.\r\n\r\n" + sb.ToString() + "\r\nAppliquer ces noms sûrs maintenant ?", "Compatibilité Action Replay", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) target.ApplyNameFixes(issues);
    }
    private bool ConfirmArNameChanges()
    {
        List<NameIssue> issues = db.FindNameIssues(); if (issues.Count == 0) return true;
        return MessageBox.Show(this, issues.Count + " nom(s) seront raccourcis de manière stable pour le champ Action Replay de 20 octets. La base XPC en mémoire restera inchangée. Continuer ?", "Noms Action Replay", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
    }

    private void ReadArToEditor()
    {
        string tmp = Path.Combine(BackupDirectory(), "argbx-read-codes-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".bin");
        RunEngine(new string[] { "dump-codes", tmp }, delegate { try { LoadDatabase(CodeDB.LoadBlob(tmp), tmp, false); tabs.SelectedIndex = 0; } catch (Exception ex) { Error("Lecture terminée mais parsing impossible : " + ex.Message); } });
    }
    private void WriteEditorToAr()
    {
        if (db.Games.Count == 0) { Error("La base est vide."); return; } if (!ConfirmArNameChanges()) return;
        if (MessageBox.Show(this, "Écrire la base actuellement ouverte dans l’Action Replay ? L’engine fera un backup puis une relecture byte-for-byte.", "Écriture Action Replay", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        try { CodeDB safe = db.ARSafeCopy(); string tmp = Path.Combine(BackupDirectory(), "argbx-editor-write-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".bin"); File.WriteAllBytes(tmp, safe.Blob()); RunEngine(new string[] { "write-codes", tmp, "--enable-write" }, null); tabs.SelectedIndex = 1; }
        catch (Exception ex) { Error(ex.Message); }
    }

    private void RefreshDevice()
    {
        if (busy) return;
        try
        {
            string found = null, service = null, name = null;
            using (ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT PNPDeviceID, Service, Name FROM Win32_PnPEntity")) using (ManagementObjectCollection results = s.Get())
            foreach (ManagementObject item in results) { string id = Convert.ToString(item["PNPDeviceID"]); if (String.IsNullOrEmpty(id) || !id.StartsWith(DevicePrefix, StringComparison.OrdinalIgnoreCase)) continue; found = id; service = Convert.ToString(item["Service"]); name = Convert.ToString(item["Name"]); break; }
            deviceStatus.Text = found == null ? "Action Replay : non détecté via WMI." : "Action Replay détecté — " + (String.IsNullOrEmpty(name) ? "périphérique USB" : name) + " — service : " + (String.IsNullOrEmpty(service) ? "?" : service);
        }
        catch (Exception ex) { deviceStatus.Text = "Erreur WMI : " + ex.Message; }
    }

    private static string EnginePath() { return Path.Combine(Application.StartupPath, "argbx-engine_v1.2.29.exe"); }
    private static string DriverPath() { return Path.Combine(Application.StartupPath, "ActionReplayGBX-Driver_v1.2.29.exe"); }
    private static string BackupDirectory() { string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ActionReplayGBX Backups"); Directory.CreateDirectory(d); return d; }
    private void SetBusy(bool value, string caption) { busy = value; hardwareButtons.Enabled = !value; if (!String.IsNullOrEmpty(caption)) deviceStatus.Text = caption; UseWaitCursor = value; }
    private static string Quote(string s) { if (s == null) return "\"\""; return "\"" + s.Replace("\"", "\\\"") + "\""; }
    private static string BuildArguments(string[] args) { StringBuilder b = new StringBuilder(); for (int i = 0; i < args.Length; i++) { if (i != 0) b.Append(' '); b.Append(Quote(args[i])); } return b.ToString(); }
    private void AppendLog(string text) { if (InvokeRequired) { BeginInvoke(new Action<string>(AppendLog), text); return; } log.AppendText(text); if (!text.EndsWith(Environment.NewLine, StringComparison.Ordinal)) log.AppendText(Environment.NewLine); log.SelectionStart = log.TextLength; log.ScrollToCaret(); }
    private void RunEngine(string[] args, Action completed) { RunComponent(EnginePath(), args, BackupDirectory(), completed); }
    private void RunDriver() { RunComponent(DriverPath(), new string[] { "--apply" }, Application.StartupPath, delegate { RefreshDevice(); }); }
    private void RunComponent(string exe, string[] args, string work, Action completed)
    {
        if (busy) return; if (!File.Exists(exe)) { Error("Composant absent : " + exe); return; }
        SetBusy(true, "Opération en cours…"); AppendLog("> " + Path.GetFileName(exe) + " " + BuildArguments(args));
        ThreadPool.QueueUserWorkItem(delegate
        {
            int exit = -1; string output = "", error = "";
            try { ProcessStartInfo psi = new ProcessStartInfo(); psi.FileName = exe; psi.Arguments = BuildArguments(args); psi.WorkingDirectory = work; psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.RedirectStandardOutput = true; psi.RedirectStandardError = true; using (Process p = Process.Start(psi)) { output = p.StandardOutput.ReadToEnd(); error = p.StandardError.ReadToEnd(); p.WaitForExit(); exit = p.ExitCode; } }
            catch (Exception ex) { error = ex.ToString(); }
            if (!String.IsNullOrEmpty(output)) AppendLog(output); if (!String.IsNullOrEmpty(error)) AppendLog(error);
            BeginInvoke(new Action(delegate { SetBusy(false, exit == 0 ? "Opération terminée." : "Échec — code " + exit + "."); if (exit == 0 && completed != null) completed(); }));
        });
    }

    private void DumpCodesOnly() { string p = ChooseSave("Sauvegarder la base binaire Action Replay", "Base binaire (*.bin)|*.bin", "ActionReplayGBX-codes.bin"); if (p != null) RunEngine(new string[] { "dump-codes", p }, null); }
    private void ValidateCodesOnly() { string p = ChooseOpen("Valider une base binaire", "Base binaire (*.bin)|*.bin|Tous les fichiers (*.*)|*.*"); if (p != null) RunEngine(new string[] { "validate-codes", p }, null); }
    private void WriteCodesOnly() { string p = ChooseOpen("Écrire une base binaire", "Base binaire (*.bin)|*.bin|Tous les fichiers (*.*)|*.*"); if (p != null && MessageBox.Show(this, "Écrire ce BIN dans l’Action Replay ? Backup + vérification automatique.", "Écriture", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes) RunEngine(new string[] { "write-codes", p, "--enable-write" }, null); }
    private void DumpSave() { string p = ChooseSave("Sauvegarder la SAVE", "Sauvegarde GBA (*.sav)|*.sav", "gba-save.sav"); if (p != null) RunEngine(new string[] { "dump-save", p }, null); }
    private void RestoreSave() { string p = ChooseOpen("Restaurer une SAVE 64 Kio", "Sauvegarde GBA (*.sav)|*.sav|Tous les fichiers (*.*)|*.*"); if (p == null) return; if (new FileInfo(p).Length != 0x10000) { Error("Le fichier doit faire exactement 65536 octets."); return; } if (MessageBox.Show(this, "Remplacer la sauvegarde de la cartouche ?", "Restauration SAVE", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes) RunEngine(new string[] { "write-save", p, "--enable-write" }, null); }
    private void DumpFirmware() { string p = ChooseSave("Dump complet Flash 256 Kio", "Image Flash (*.bin)|*.bin", "ActionReplayGBX-flash-256K.bin"); if (p != null) RunEngine(new string[] { "dump-firmware", p }, null); }
    private void ValidateFirmware() { string p = ChooseOpen("Valider un firmware", "Firmware (*.gsu;*.bin)|*.gsu;*.bin|Tous les fichiers (*.*)|*.*"); if (p != null) RunEngine(new string[] { "validate-firmware", p }, null); }
    private void WriteFirmware()
    {
        string p = ChooseOpen("Sélectionner le firmware", "Firmware (*.gsu;*.bin)|*.gsu;*.bin|Tous les fichiers (*.*)|*.*"); if (p == null) return;
        if (MessageBox.Show(this, "ATTENTION : l’écriture firmware n’a pas encore été validée sur matériel dans le port C#. Continuer malgré le risque ?", "ÉCRITURE FIRMWARE", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        if (MessageBox.Show(this, "Dernière confirmation : ne débranchez ni USB ni alimentation jusqu’au retour du menu.", "Confirmation firmware", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes) RunEngine(new string[] { "write-firmware", p, "--enable-firmware-write" }, null);
    }

    private string ChooseOpen(string title, string filter) { using (OpenFileDialog d = new OpenFileDialog()) { d.Title = title; d.Filter = filter; d.CheckFileExists = true; return d.ShowDialog(this) == DialogResult.OK ? d.FileName : null; } }
    private string ChooseSave(string title, string filter, string name) { using (SaveFileDialog d = new SaveFileDialog()) { d.Title = title; d.Filter = filter; d.FileName = name; d.OverwritePrompt = true; return d.ShowDialog(this) == DialogResult.OK ? d.FileName : null; } }
    private void Error(string text) { MessageBox.Show(this, text, "ActionReplayGBX", MessageBoxButtons.OK, MessageBoxIcon.Error); }

    private string PromptText(string title, string label, string value)
    {
        using (Form f = new Form())
        {
            f.Text = title; f.Width = 480; f.Height = 160; f.StartPosition = FormStartPosition.CenterParent; f.FormBorderStyle = FormBorderStyle.FixedDialog; f.MinimizeBox = false; f.MaximizeBox = false;
            Label l = new Label(); l.Text = label; l.Left = 12; l.Top = 12; l.Width = 430;
            TextBox t = new TextBox(); t.Left = 12; t.Top = 36; t.Width = 440; t.Text = value;
            Button ok = new Button(); ok.Text = "OK"; ok.DialogResult = DialogResult.OK; ok.Left = 286; ok.Top = 72; ok.Width = 80;
            Button cancel = new Button(); cancel.Text = "Annuler"; cancel.DialogResult = DialogResult.Cancel; cancel.Left = 372; cancel.Top = 72; cancel.Width = 80;
            f.Controls.Add(l); f.Controls.Add(t); f.Controls.Add(ok); f.Controls.Add(cancel); f.AcceptButton = ok; f.CancelButton = cancel;
            if (f.ShowDialog(this) != DialogResult.OK) return null; return t.Text;
        }
    }

    private void OnDragEnter(object sender, DragEventArgs e) { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; }
    private void OnDragDrop(object sender, DragEventArgs e)
    {
        string[] files = e.Data.GetData(DataFormats.FileDrop) as string[]; if (files == null || files.Length == 0) return; string p = files[0];
        try { if (p.EndsWith(".xpc", StringComparison.OrdinalIgnoreCase)) LoadDatabase(CodeDB.LoadXPC(p), p, false); else if (p.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)) LoadDatabase(CodeDB.LoadBlob(p), p, false); else Error("Dépose un fichier .xpc ou .bin."); }
        catch (Exception ex) { Error(ex.Message); }
    }
}

internal static class Program
{
    [STAThread]
    public static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }
}
