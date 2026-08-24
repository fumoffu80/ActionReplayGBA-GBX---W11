using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Management;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("ActionReplayGBX")]
[assembly: AssemblyProduct("ActionReplayGBX")]
[assembly: AssemblyCompany("ActionReplayGBX project")]
[assembly: AssemblyDescription("ActionReplayGBX v1.2.29 CSharp functional port GUI")]
[assembly: AssemblyVersion("1.2.29.0")]
[assembly: AssemblyFileVersion("1.2.29.0")]
[assembly: AssemblyInformationalVersion("1.2.29-port")]

internal sealed class MainForm : Form
{
    private const string DevicePrefix = "USB\\VID_05FD&PID_DAAE";
    private readonly Label status;
    private readonly TextBox log;
    private readonly FlowLayoutPanel buttons;
    private bool busy;

    internal MainForm()
    {
        Text = "ActionReplayGBX v1.2.29";
        Width = 920;
        Height = 650;
        MinimumSize = new Size(820, 560);
        StartPosition = FormStartPosition.CenterScreen;

        Label title = new Label();
        title.Text = "ActionReplayGBX — port C# fonctionnel v1.2.29";
        title.Font = new Font(Font, FontStyle.Bold);
        title.AutoSize = true;
        title.Left = 16;
        title.Top = 14;
        Controls.Add(title);

        status = new Label();
        status.Left = 16;
        status.Top = 44;
        status.Width = 860;
        status.Height = 42;
        Controls.Add(status);

        buttons = new FlowLayoutPanel();
        buttons.Left = 12;
        buttons.Top = 90;
        buttons.Width = 875;
        buttons.Height = 185;
        buttons.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        buttons.AutoScroll = true;
        buttons.WrapContents = true;
        Controls.Add(buttons);

        AddButton("Actualiser", delegate { RefreshDevice(); });
        AddButton("Infos Action Replay", delegate { RunEngine(new string[] { "info" }); });
        AddButton("Configurer WinUSB existant", delegate { RunDriver(); });
        AddButton("Lire base codes → BIN", delegate { DumpCodes(); });
        AddButton("Valider base BIN", delegate { ValidateCodes(); });
        AddButton("Écrire base BIN", delegate { WriteCodes(); });
        AddButton("Sauvegarder SAVE", delegate { DumpSave(); });
        AddButton("Restaurer SAVE", delegate { RestoreSave(); });
        AddButton("Dump Flash 256 Kio", delegate { DumpFirmware(); });
        AddButton("Valider firmware", delegate { ValidateFirmware(); });
        AddButton("Mettre à jour firmware", delegate { WriteFirmware(); });
        AddButton("Déconnecter", delegate { RunEngine(new string[] { "disconnect" }); });

        log = new TextBox();
        log.Left = 16;
        log.Top = 285;
        log.Width = 860;
        log.Height = 300;
        log.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        log.Multiline = true;
        log.ReadOnly = true;
        log.ScrollBars = ScrollBars.Both;
        log.WordWrap = false;
        log.Font = new Font("Consolas", 9.0f);
        Controls.Add(log);

        Label note = new Label();
        note.Left = 16;
        note.Top = 592;
        note.Width = 860;
        note.Height = 36;
        note.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        note.Text = "v1.2.29 : fonctions USB réelles portées. L’éditeur graphique XPC complet de v1.2.16 n’est pas encore porté. Aucune installation automatique de pilote n’est effectuée.";
        Controls.Add(note);

        Shown += delegate { RefreshDevice(); };
    }

    private void AddButton(string text, EventHandler action)
    {
        Button b = new Button();
        b.Text = text;
        b.Width = 190;
        b.Height = 36;
        b.Margin = new Padding(4);
        b.Click += action;
        buttons.Controls.Add(b);
    }

    private static string EnginePath()
    {
        return Path.Combine(Application.StartupPath, "argbx-engine_v1.2.29.exe");
    }

    private static string DriverPath()
    {
        return Path.Combine(Application.StartupPath, "ActionReplayGBX-Driver_v1.2.29.exe");
    }

    private static string BackupDirectory()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ActionReplayGBX Backups");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private void RefreshDevice()
    {
        if (busy) return;
        try
        {
            string found = null;
            string service = null;
            string name = null;
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT PNPDeviceID, Service, Name FROM Win32_PnPEntity"))
            using (ManagementObjectCollection results = searcher.Get())
            {
                foreach (ManagementObject item in results)
                {
                    string id = Convert.ToString(item["PNPDeviceID"]);
                    if (string.IsNullOrEmpty(id) || !id.StartsWith(DevicePrefix, StringComparison.OrdinalIgnoreCase)) continue;
                    found = id;
                    service = Convert.ToString(item["Service"]);
                    name = Convert.ToString(item["Name"]);
                    break;
                }
            }
            if (found == null)
            {
                status.Text = "Action Replay : non détecté via WMI.";
                return;
            }
            status.Text = "Action Replay détecté — " + (string.IsNullOrEmpty(name) ? "périphérique USB" : name) + " — service : " + (string.IsNullOrEmpty(service) ? "?" : service);
        }
        catch (Exception ex)
        {
            status.Text = "Erreur WMI : " + ex.Message;
        }
    }

    private void SetBusy(bool value, string caption)
    {
        busy = value;
        buttons.Enabled = !value;
        if (!string.IsNullOrEmpty(caption)) status.Text = caption;
        UseWaitCursor = value;
    }

    private static string Quote(string s)
    {
        if (s == null) return "\"\"";
        return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    private static string BuildArguments(string[] args)
    {
        StringBuilder b = new StringBuilder();
        for (int i = 0; i < args.Length; i++)
        {
            if (i != 0) b.Append(' ');
            b.Append(Quote(args[i]));
        }
        return b.ToString();
    }

    private void AppendLog(string text)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<string>(AppendLog), text);
            return;
        }
        log.AppendText(text);
        if (!text.EndsWith(Environment.NewLine, StringComparison.Ordinal)) log.AppendText(Environment.NewLine);
        log.SelectionStart = log.TextLength;
        log.ScrollToCaret();
    }

    private void RunEngine(string[] args)
    {
        RunComponent(EnginePath(), args, BackupDirectory(), null);
    }

    private void RunDriver()
    {
        RunComponent(DriverPath(), new string[] { "--apply" }, Application.StartupPath, delegate { RefreshDevice(); });
    }

    private void RunComponent(string exe, string[] args, string workingDirectory, Action completed)
    {
        if (busy) return;
        if (!File.Exists(exe))
        {
            MessageBox.Show(this, "Composant absent : " + exe, "ActionReplayGBX", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        SetBusy(true, "Opération en cours…");
        AppendLog("> " + Path.GetFileName(exe) + " " + BuildArguments(args));
        ThreadPool.QueueUserWorkItem(delegate
        {
            int exitCode = -1;
            string output = "";
            string error = "";
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = exe;
                psi.Arguments = BuildArguments(args);
                psi.WorkingDirectory = workingDirectory;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi))
                {
                    output = p.StandardOutput.ReadToEnd();
                    error = p.StandardError.ReadToEnd();
                    p.WaitForExit();
                    exitCode = p.ExitCode;
                }
            }
            catch (Exception ex)
            {
                error = ex.ToString();
            }
            if (!string.IsNullOrEmpty(output)) AppendLog(output);
            if (!string.IsNullOrEmpty(error)) AppendLog(error);
            BeginInvoke(new Action(delegate
            {
                SetBusy(false, exitCode == 0 ? "Opération terminée." : "Échec — code " + exitCode + ".");
                if (completed != null) completed();
            }));
        });
    }

    private string ChooseOpen(string title, string filter)
    {
        using (OpenFileDialog d = new OpenFileDialog())
        {
            d.Title = title;
            d.Filter = filter;
            d.CheckFileExists = true;
            return d.ShowDialog(this) == DialogResult.OK ? d.FileName : null;
        }
    }

    private string ChooseSave(string title, string filter, string defaultName)
    {
        using (SaveFileDialog d = new SaveFileDialog())
        {
            d.Title = title;
            d.Filter = filter;
            d.FileName = defaultName;
            d.OverwritePrompt = true;
            return d.ShowDialog(this) == DialogResult.OK ? d.FileName : null;
        }
    }

    private void DumpCodes()
    {
        string path = ChooseSave("Sauvegarder la base binaire Action Replay", "Base binaire (*.bin)|*.bin|Tous les fichiers (*.*)|*.*", "ActionReplayGBX-codes.bin");
        if (path != null) RunEngine(new string[] { "dump-codes", path });
    }

    private void ValidateCodes()
    {
        string path = ChooseOpen("Valider une base binaire Action Replay", "Base binaire (*.bin)|*.bin|Tous les fichiers (*.*)|*.*");
        if (path != null) RunEngine(new string[] { "validate-codes", path });
    }

    private void WriteCodes()
    {
        string path = ChooseOpen("Écrire une base binaire Action Replay", "Base binaire (*.bin)|*.bin|Tous les fichiers (*.*)|*.*");
        if (path == null) return;
        DialogResult r = MessageBox.Show(this,
            "Cette opération écrit la base de codes dans l’Action Replay. L’engine effectuera d’abord un backup, puis une relecture complète et une comparaison byte-for-byte. Continuer ?",
            "Écriture de la base", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (r == DialogResult.Yes) RunEngine(new string[] { "write-codes", path, "--enable-write" });
    }

    private void DumpSave()
    {
        string path = ChooseSave("Sauvegarder la sauvegarde GBA", "Sauvegarde GBA (*.sav)|*.sav|Tous les fichiers (*.*)|*.*", "gba-save.sav");
        if (path != null) RunEngine(new string[] { "dump-save", path });
    }

    private void RestoreSave()
    {
        string path = ChooseOpen("Restaurer une sauvegarde GBA 64 Kio", "Sauvegarde GBA (*.sav)|*.sav|Tous les fichiers (*.*)|*.*");
        if (path == null) return;
        if (new FileInfo(path).Length != 0x10000)
        {
            MessageBox.Show(this, "Le fichier doit faire exactement 64 Kio (65536 octets).", "ActionReplayGBX", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        DialogResult r = MessageBox.Show(this,
            "La sauvegarde présente sur la cartouche sera remplacée. Effectuez une sauvegarde avant de continuer. Restaurer ce fichier ?",
            "Restauration SAVE", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (r == DialogResult.Yes) RunEngine(new string[] { "write-save", path, "--enable-write" });
    }

    private void DumpFirmware()
    {
        string path = ChooseSave("Dump complet Flash Action Replay 256 Kio", "Image Flash (*.bin)|*.bin|Tous les fichiers (*.*)|*.*", "ActionReplayGBX-flash-256K.bin");
        if (path != null) RunEngine(new string[] { "dump-firmware", path });
    }

    private void ValidateFirmware()
    {
        string path = ChooseOpen("Valider un firmware Action Replay", "Firmware (*.gsu;*.bin)|*.gsu;*.bin|Tous les fichiers (*.*)|*.*");
        if (path != null) RunEngine(new string[] { "validate-firmware", path });
    }

    private void WriteFirmware()
    {
        string path = ChooseOpen("Sélectionner le firmware à écrire", "Firmware (*.gsu;*.bin)|*.gsu;*.bin|Tous les fichiers (*.*)|*.*");
        if (path == null) return;
        DialogResult r = MessageBox.Show(this,
            "ATTENTION : l’écriture du firmware peut rendre l’Action Replay inutilisable en cas de fichier incompatible ou de coupure. L’engine vérifiera le GSU/CRC/signature, limitera l’opération aux v3.x/v4.x et effectuera automatiquement un dump complet 256 Kio avant CBW 0x14. Continuer ?",
            "ÉCRITURE FIRMWARE", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (r != DialogResult.Yes) return;
        r = MessageBox.Show(this,
            "Confirmez une seconde fois : ne débranchez ni l’USB ni l’alimentation jusqu’au retour du menu Action Replay.",
            "Confirmation firmware", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (r == DialogResult.Yes) RunEngine(new string[] { "write-firmware", path, "--enable-firmware-write" });
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
