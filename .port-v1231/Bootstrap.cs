using System;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static class BootstrapProgram
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args != null)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (String.Equals(args[i], "--self-test-language", StringComparison.OrdinalIgnoreCase))
                    return LanguageManager.SelfTest();
            }
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        LanguageManager.Initialize();

        MainForm form = new MainForm();
        V12312ParityBridge.Attach(form);
        V1216VisualParity.Attach(form);
        Application.Run(form);
        return 0;
    }
}

// v1.2.31.2 parity bridge.
// Keeps the large v1.2.16-style GUI source stable while restoring the remaining
// driver/GUID verification and automatic Windows PnP recovery behaviour.
internal static class V12312ParityBridge
{
    private static DateTime recoveryDue = DateTime.MinValue;
    private static bool recoveryRestartRunning;

    private sealed class ProcessResult
    {
        internal int ExitCode;
        internal string Output;
    }

    internal static void Attach(MainForm form)
    {
        if (form == null) return;

        Button driver = GetField<Button>(form, "driverButton");
        if (driver != null)
        {
            driver.Click += async delegate
            {
                await VerifyAfterDriverRepair(form, driver);
            };
        }

        System.Windows.Forms.Timer journalWatch = new System.Windows.Forms.Timer();
        journalWatch.Interval = 350;
        journalWatch.Tick += delegate { InjectJournalDiagnostic(form); };

        System.Windows.Forms.Timer recoveryWatch = new System.Windows.Forms.Timer();
        recoveryWatch.Interval = 450;
        recoveryWatch.Tick += async delegate { await CheckAutomaticUsbRecovery(form); };

        form.Shown += delegate
        {
            journalWatch.Start();
            recoveryWatch.Start();
        };
        form.FormClosed += delegate
        {
            journalWatch.Stop();
            recoveryWatch.Stop();
            journalWatch.Dispose();
            recoveryWatch.Dispose();
        };
    }

    private static async Task CheckAutomaticUsbRecovery(MainForm form)
    {
        if (form == null || form.IsDisposed || recoveryRestartRunning) return;

        bool usbPresent = GetBoolField(form, "usbPresent");
        bool connected = GetBoolField(form, "deviceConnected");
        bool busy = GetBoolField(form, "busy");
        int stage = GetIntField(form, "autoRecoveryStage");

        if (connected || !usbPresent || stage == 0)
        {
            recoveryDue = DateTime.MinValue;
            return;
        }

        // Stage 1 means the normal engine retry and the --recover pipe reset have both
        // failed. v1.2.16 then allowed a short grace period before asking Windows to
        // restart only this PnP device. Do not reinstall WinUSB and do not touch data.
        if (stage != 1 || busy) return;

        if (recoveryDue == DateTime.MinValue)
        {
            recoveryDue = DateTime.UtcNow.AddSeconds(6);
            SetActivity(form, LanguageManager.IsFrench
                ? "AR toujours non prêt — redémarrage USB Windows automatique dans quelques secondes…"
                : "AR still not ready — automatic Windows USB restart in a few seconds…");
            return;
        }

        if (DateTime.UtcNow < recoveryDue) return;

        string driverPath = GetField<string>(form, "driverPath");
        if (String.IsNullOrEmpty(driverPath))
        {
            recoveryDue = DateTime.MinValue;
            SetIntField(form, "autoRecoveryStage", 3);
            return;
        }

        recoveryRestartRunning = true;
        recoveryDue = DateTime.MinValue;
        SetIntField(form, "autoRecoveryStage", 2);
        SetActivity(form, LanguageManager.IsFrench
            ? "Redémarrage logiciel du périphérique USB… Windows peut demander une autorisation."
            : "Restarting the USB device in Windows… Windows may request permission.");
        AppendLog(form, "Automatic USB recovery: requesting driver helper --restart-only.");

        try
        {
            ProcessResult rr = await RunCaptured(driverPath, "--restart-only");
            AppendLog(form, "Automatic USB/PnP recovery:\r\n" + rr.Output + "\r\nexit=" + rr.ExitCode);
            if (rr.ExitCode == 0)
            {
                await Task.Delay(1800);
                SetIntField(form, "autoRecoveryStage", 0);
                SetActivity(form, LanguageManager.IsFrench
                    ? "Périphérique USB redémarré par Windows — reconnexion automatique…"
                    : "USB device restarted by Windows — reconnecting automatically…");
            }
            else
            {
                // Stage 3 prevents repeated UAC prompts. A physical unplug/replug or a
                // successful later connection resets the normal recovery state in MainForm.
                SetIntField(form, "autoRecoveryStage", 3);
                SetActivity(form, LanguageManager.IsFrench
                    ? "La récupération USB Windows n’a pas abouti — débranche/rebranche l’USB lorsque la GBA est au menu AR."
                    : "Windows USB recovery did not complete — unplug/replug USB while the GBA is at the AR menu.");
            }
        }
        finally
        {
            recoveryRestartRunning = false;
        }
    }

    private static async Task VerifyAfterDriverRepair(MainForm form, Button driver)
    {
        // The original async repair handler disables this button while Windows/libwdi is
        // working. Wait for that flow to finish, then perform independent read-only checks.
        await Task.Delay(900);
        for (int i = 0; i < 50 && !form.IsDisposed && !driver.Enabled; i++) await Task.Delay(400);
        if (form.IsDisposed) return;

        string driverPath = GetField<string>(form, "driverPath");
        string enginePath = GetField<string>(form, "enginePath");
        if (String.IsNullOrEmpty(driverPath) || String.IsNullOrEmpty(enginePath)) return;

        ProcessResult ds = await RunCaptured(driverPath, "");
        AppendLog(form, "Post-driver WinUSB/GUID verification:\r\n" + ds.Output + "\r\nexit=" + ds.ExitCode);
        if (ds.ExitCode != 0)
        {
            SetActivity(form, LanguageManager.IsFrench
                ? "Pilote appliqué — WinUSB/GUID non encore confirmé"
                : "Driver applied — WinUSB/GUID not confirmed yet");
            return;
        }

        ProcessResult info = await RunCaptured(enginePath, "\"info\"");
        AppendLog(form, "Post-driver engine info verification:\r\n" + info.Output + "\r\nexit=" + info.ExitCode);
        if (info.ExitCode == 0)
        {
            SetActivity(form, LanguageManager.IsFrench
                ? "Pilote vérifié : WinUSB + GUID + protocole Action Replay"
                : "Driver verified: WinUSB + GUID + Action Replay protocol");
        }
        else
        {
            SetActivity(form, LanguageManager.IsFrench
                ? "WinUSB + GUID vérifiés — protocole Action Replay pas encore prêt"
                : "WinUSB + GUID verified — Action Replay protocol not ready yet");
        }
    }

    private static void InjectJournalDiagnostic(MainForm owner)
    {
        if (owner == null || owner.IsDisposed) return;

        Form journal = null;
        foreach (Form f in Application.OpenForms)
        {
            if (Object.ReferenceEquals(f, owner)) continue;
            string t = f.Text ?? "";
            if (t.StartsWith("Journal / outils", StringComparison.OrdinalIgnoreCase) ||
                t.StartsWith("Log / tools", StringComparison.OrdinalIgnoreCase))
            {
                journal = f;
                break;
            }
        }
        if (journal == null || ContainsNamedControl(journal, "v12312-guid-status")) return;

        FlowLayoutPanel bar = FindJournalButtonBar(journal);
        if (bar == null) return;

        Button status = new Button();
        status.Name = "v12312-guid-status";
        status.Text = "WinUSB / GUID";
        status.AutoSize = true;
        status.Click += async delegate
        {
            string driverPath = GetField<string>(owner, "driverPath");
            string enginePath = GetField<string>(owner, "enginePath");
            if (String.IsNullOrEmpty(driverPath)) return;

            ProcessResult ds = await RunCaptured(driverPath, "");
            StringBuilder text = new StringBuilder();
            text.AppendLine("Driver / WinUSB / GUID — exit=" + ds.ExitCode);
            text.AppendLine(ds.Output);

            if (ds.ExitCode == 0 && !String.IsNullOrEmpty(enginePath))
            {
                ProcessResult info = await RunCaptured(enginePath, "\"info\"");
                text.AppendLine();
                text.AppendLine("engine info — exit=" + info.ExitCode);
                text.AppendLine(info.Output);
            }

            AppendLog(owner, "Journal WinUSB/GUID diagnostic:\r\n" + text.ToString());
            MessageBox.Show(journal, text.ToString(),
                LanguageManager.IsFrench ? "État WinUSB / GUID" : "WinUSB / GUID status",
                MessageBoxButtons.OK,
                ds.ExitCode == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        };

        bar.WrapContents = false;
        bar.AutoScroll = true;
        bar.Controls.Add(status);
    }

    private static FlowLayoutPanel FindJournalButtonBar(Control root)
    {
        FlowLayoutPanel best = null;
        foreach (Control c in root.Controls)
        {
            FlowLayoutPanel p = c as FlowLayoutPanel;
            if (p != null)
            {
                bool hasRefresh = false;
                bool hasInfo = false;
                foreach (Control child in p.Controls)
                {
                    string tx = child.Text ?? "";
                    if (tx == "Actualiser" || tx == "Refresh") hasRefresh = true;
                    if (tx.StartsWith("Infos AR", StringComparison.OrdinalIgnoreCase) || tx.StartsWith("AR info", StringComparison.OrdinalIgnoreCase)) hasInfo = true;
                }
                if (hasRefresh && hasInfo) return p;
                if (best == null) best = p;
            }
            FlowLayoutPanel nested = FindJournalButtonBar(c);
            if (nested != null) return nested;
        }
        return best;
    }

    private static bool ContainsNamedControl(Control root, string name)
    {
        if (String.Equals(root.Name, name, StringComparison.Ordinal)) return true;
        foreach (Control c in root.Controls) if (ContainsNamedControl(c, name)) return true;
        return false;
    }

    private static T GetField<T>(object obj, string name) where T : class
    {
        try
        {
            FieldInfo f = obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            return f == null ? null : f.GetValue(obj) as T;
        }
        catch { return null; }
    }

    private static object GetFieldValue(object obj, string name)
    {
        try
        {
            FieldInfo f = obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            return f == null ? null : f.GetValue(obj);
        }
        catch { return null; }
    }

    private static bool GetBoolField(object obj, string name)
    {
        object v = GetFieldValue(obj, name);
        return v is bool && (bool)v;
    }

    private static int GetIntField(object obj, string name)
    {
        object v = GetFieldValue(obj, name);
        return v is int ? (int)v : 0;
    }

    private static void SetIntField(object obj, string name, int value)
    {
        try
        {
            FieldInfo f = obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (f != null && f.FieldType == typeof(int)) f.SetValue(obj, value);
        }
        catch { }
    }

    private static void AppendLog(MainForm form, string text)
    {
        try
        {
            MethodInfo m = typeof(MainForm).GetMethod("AppendLog", BindingFlags.Instance | BindingFlags.NonPublic);
            if (m != null) m.Invoke(form, new object[] { text });
        }
        catch { }
    }

    private static void SetActivity(MainForm form, string text)
    {
        try
        {
            MethodInfo m = typeof(MainForm).GetMethod("SetActivity", BindingFlags.Instance | BindingFlags.NonPublic);
            if (m != null) m.Invoke(form, new object[] { text });
        }
        catch { }
    }

    private static Task<ProcessResult> RunCaptured(string file, string arguments)
    {
        return Task.Factory.StartNew(delegate
        {
            ProcessResult r = new ProcessResult();
            r.ExitCode = -1;
            r.Output = "";
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = file;
                psi.Arguments = arguments ?? "";
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi))
                {
                    string stdout = p.StandardOutput.ReadToEnd();
                    string stderr = p.StandardError.ReadToEnd();
                    p.WaitForExit();
                    r.ExitCode = p.ExitCode;
                    r.Output = (stdout + (String.IsNullOrWhiteSpace(stderr) ? "" : Environment.NewLine + stderr)).Trim();
                }
            }
            catch (Exception ex)
            {
                r.Output = ex.ToString();
            }
            return r;
        });
    }
}
