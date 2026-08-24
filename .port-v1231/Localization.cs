using System;
using System.IO;
using System.Text;
using System.Windows.Forms;

internal static class LanguageManager
{
    private static string currentLanguage = "fr";

    internal static string CurrentLanguage { get { return currentLanguage; } }
    internal static bool IsFrench { get { return currentLanguage == "fr"; } }

    internal static string SettingsPath
    {
        get
        {
            string overrideDir = Environment.GetEnvironmentVariable("ARGBX_SETTINGS_DIR");
            string dir = String.IsNullOrEmpty(overrideDir)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ActionReplayGBX")
                : overrideDir;
            return Path.Combine(dir, "settings.ini");
        }
    }

    private static bool LoadConfirmedLanguage(out string language)
    {
        language = null;
        try
        {
            if (!File.Exists(SettingsPath)) return false;
            bool confirmed = false;
            string found = null;
            foreach (string raw in File.ReadAllLines(SettingsPath, Encoding.UTF8))
            {
                string line = raw.Trim();
                if (line.StartsWith("language=", StringComparison.OrdinalIgnoreCase))
                {
                    string value = line.Substring(9).Trim().ToLowerInvariant();
                    if (value == "fr" || value == "en") found = value;
                }
                else if (line.StartsWith("language_confirmed=", StringComparison.OrdinalIgnoreCase))
                {
                    string value = line.Substring("language_confirmed=".Length).Trim();
                    confirmed = value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
                }
            }
            if (confirmed && (found == "fr" || found == "en"))
            {
                language = found;
                return true;
            }
        }
        catch { }
        return false;
    }

    internal static void SaveLanguage(string language)
    {
        if (language != "fr" && language != "en") throw new ArgumentException("Unsupported language.");
        string path = SettingsPath;
        string dir = Path.GetDirectoryName(path);
        Directory.CreateDirectory(dir);
        string tmp = path + ".tmp";
        string text = "language=" + language + Environment.NewLine +
                      "language_confirmed=1" + Environment.NewLine;
        File.WriteAllText(tmp, text, new UTF8Encoding(false));
        if (File.Exists(path)) File.Delete(path);
        File.Move(tmp, path);
        currentLanguage = language;
    }

    internal static void Initialize()
    {
        string lang;
        if (!LoadConfirmedLanguage(out lang))
        {
            lang = ShowLanguageDialog();
            SaveLanguage(lang);
        }
        currentLanguage = lang;
    }

    internal static string T(string fr, string en)
    {
        return currentLanguage == "en" ? en : fr;
    }

    internal static void ToggleAndRestart()
    {
        SaveLanguage(currentLanguage == "fr" ? "en" : "fr");
        Application.Restart();
    }

    private static string ShowLanguageDialog()
    {
        using (Form f = new Form())
        {
            f.Text = "Language / Langue";
            f.Width = 470;
            f.Height = 220;
            f.StartPosition = FormStartPosition.CenterScreen;
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.MaximizeBox = false;
            f.MinimizeBox = false;
            f.ControlBox = false;
            f.AutoScaleMode = AutoScaleMode.Dpi;
            f.Font = new System.Drawing.Font("Segoe UI", 10.0f);

            Label title = new Label();
            title.Text = "Choose your language / Choisissez votre langue";
            title.AutoSize = false;
            title.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            title.Left = 22; title.Top = 24; title.Width = 410; title.Height = 42;
            title.Font = new System.Drawing.Font("Segoe UI Semibold", 11.0f);

            Button fr = new Button();
            fr.Text = "Français";
            fr.Left = 60; fr.Top = 92; fr.Width = 155; fr.Height = 48;
            Button en = new Button();
            en.Text = "English";
            en.Left = 245; en.Top = 92; en.Width = 155; en.Height = 48;

            string selected = null;
            fr.Click += delegate { selected = "fr"; f.DialogResult = DialogResult.OK; f.Close(); };
            en.Click += delegate { selected = "en"; f.DialogResult = DialogResult.OK; f.Close(); };
            f.Controls.Add(title); f.Controls.Add(fr); f.Controls.Add(en);
            f.ShowDialog();
            return selected == "en" ? "en" : "fr";
        }
    }

    internal static int SelfTest()
    {
        string oldOverride = Environment.GetEnvironmentVariable("ARGBX_SETTINGS_DIR");
        string dir = Path.Combine(Path.GetTempPath(), "ActionReplayGBX-language-selftest-" + Guid.NewGuid().ToString("N"));
        try
        {
            Environment.SetEnvironmentVariable("ARGBX_SETTINGS_DIR", dir);
            if (File.Exists(SettingsPath)) File.Delete(SettingsPath);

            // An old/unconfirmed language line must NOT count as a first-run choice.
            Directory.CreateDirectory(dir);
            File.WriteAllText(SettingsPath, "language=fr\r\n", new UTF8Encoding(false));
            string value;
            if (LoadConfirmedLanguage(out value)) return 11;

            SaveLanguage("en");
            if (!LoadConfirmedLanguage(out value) || value != "en") return 12;
            if (T("Bonjour", "Hello") != "Hello") return 13;

            SaveLanguage("fr");
            if (!LoadConfirmedLanguage(out value) || value != "fr") return 14;
            if (T("Bonjour", "Hello") != "Bonjour") return 15;

            return 0;
        }
        catch { return 99; }
        finally
        {
            Environment.SetEnvironmentVariable("ARGBX_SETTINGS_DIR", oldOverride);
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }
    }
}