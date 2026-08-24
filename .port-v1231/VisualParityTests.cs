using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

internal static class VisualParityTests
{
    [STAThread]
    public static int Main(string[] args)
    {
        string old = Environment.GetEnvironmentVariable("ARGBX_SETTINGS_DIR");
        string temp = Path.Combine(Path.GetTempPath(), "ActionReplayGBX-visual-selftest-" + Guid.NewGuid().ToString("N"));
        MainForm form = null;
        try
        {
            Environment.SetEnvironmentVariable("ARGBX_SETTINGS_DIR", temp);
            LanguageManager.SaveLanguage("fr");
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            form = new MainForm();
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(0, 0);
            V1216VisualParity.Attach(form);

            // Exercise the production Shown lifecycle, then use a complete window size
            // that fits the Windows CI desktop so the screenshot cannot be clipped.
            form.Show();
            Application.DoEvents();
            form.ClientSize = new Size(1000, 700);
            form.Location = new Point(0, 0);
            Application.DoEvents();

            MethodInfo layout = typeof(V1216VisualParity).GetMethod("LayoutNow", BindingFlags.Static | BindingFlags.NonPublic);
            if (layout == null) return Fail(10, "Visual parity LayoutNow method missing");
            layout.Invoke(null, null);
            Application.DoEvents();

            Button read = Field<Button>(form, "readButton");
            Button write = Field<Button>(form, "writeButton");
            Button journal = Field<Button>(form, "journalButton");
            Button firmwareBackup = Field<Button>(form, "firmwareBackupButton");
            Button firmwareUpdate = Field<Button>(form, "firmwareUpdateButton");
            CheckedListBox pcGames = Field<CheckedListBox>(form, "pcGames");
            CheckedListBox pcCodes = Field<CheckedListBox>(form, "pcCodes");
            Label title = Field<Label>(form, "titleLabel");
            ProgressBar transfer = Field<ProgressBar>(form, "transferProgress");
            TextBox code = Field<TextBox>(form, "codeText");

            if (read == null || write == null || journal == null || pcGames == null || pcCodes == null || title == null || transfer == null || code == null)
                return Fail(11, "Required visual controls missing");
            if (!Object.ReferenceEquals(read.Parent, form) || !Object.ReferenceEquals(pcGames.Parent, form))
                return Fail(12, "v1.2.16 flat layout was not applied to the shown window");
            if (!read.Visible || !write.Visible || !journal.Visible || !pcGames.Visible || !pcCodes.Visible || !title.Visible || !code.Visible)
                return Fail(13, "One or more v1.2.16 controls are not visible after Show");
            if (read.Top != 170 || write.Top != 170) return Fail(14, "Toolbar row 1 geometry mismatch: " + read.Bounds + " / " + write.Bounds);
            if (journal.Top != 211) return Fail(15, "Journal / Outils is not on toolbar row 2: " + journal.Bounds);
            if (journal.Right > form.ClientSize.Width - 14) return Fail(16, "Journal / Outils is clipped: " + journal.Bounds);
            if (journal.Text != "Journal / Outils") return Fail(17, "Wrong Journal caption: " + journal.Text);
            if (firmwareBackup == null || firmwareBackup.Text != "Sauvegarde Firmware") return Fail(18, "Firmware backup caption mismatch");
            if (firmwareUpdate == null || firmwareUpdate.Text != "Mise à jour Firmware") return Fail(19, "Firmware update caption mismatch");
            if (title.Left != 14 || title.Top != 8) return Fail(20, "Header geometry mismatch: " + title.Bounds);
            if (pcGames.Top != 281 || pcCodes.Top != 281) return Fail(21, "List geometry mismatch: " + pcGames.Bounds + " / " + pcCodes.Bounds);
            if (transfer.Bottom > form.ClientSize.Height) return Fail(22, "Bottom transfer area outside client bounds");
            if (code.Width < 960) return Fail(23, "Code editor not using full v1.2.16 width: " + code.Bounds);
            if (!form.Text.Contains("v1.2.31.3")) return Fail(24, "Window title is not v1.2.31.3: " + form.Text);

            string png = args != null && args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "ActionReplayGBX_UI_v1.2.31.3.png");
            using (Bitmap bmp = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
            {
                form.DrawToBitmap(bmp, new Rectangle(Point.Empty, form.ClientSize));
                int varied = 0;
                Color bg = form.BackColor;
                for (int y = 0; y < bmp.Height; y += 4)
                {
                    for (int x = 0; x < bmp.Width; x += 4)
                    {
                        Color p = bmp.GetPixel(x, y);
                        int delta = Math.Abs(p.R - bg.R) + Math.Abs(p.G - bg.G) + Math.Abs(p.B - bg.B);
                        if (delta > 24) varied++;
                    }
                }
                if (varied < 500) return Fail(25, "Rendered UI is blank or nearly blank; varied samples=" + varied);
                bmp.Save(png, System.Drawing.Imaging.ImageFormat.Png);
                Console.WriteLine("Rendered varied samples=" + varied);
            }

            Console.WriteLine("PASS visual parity (complete shown window)");
            Console.WriteLine("Screenshot: " + png);
            Console.WriteLine("client=" + form.ClientSize + " read=" + read.Bounds + " journal=" + journal.Bounds + " pcGames=" + pcGames.Bounds + " code=" + code.Bounds);
            form.Close();
            form.Dispose();
            form = null;
            return 0;
        }
        catch (TargetInvocationException ex)
        {
            return Fail(90, ex.InnerException == null ? ex.ToString() : ex.InnerException.ToString());
        }
        catch (Exception ex)
        {
            return Fail(99, ex.ToString());
        }
        finally
        {
            try { if (form != null && !form.IsDisposed) { form.Close(); form.Dispose(); } } catch { }
            Environment.SetEnvironmentVariable("ARGBX_SETTINGS_DIR", old);
            try { if (Directory.Exists(temp)) Directory.Delete(temp, true); } catch { }
        }
    }

    private static T Field<T>(MainForm form, string name) where T : class
    {
        FieldInfo f = typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        return f == null ? null : f.GetValue(form) as T;
    }

    private static int Fail(int code, string text)
    {
        Console.Error.WriteLine("FAIL " + code + ": " + text);
        return code;
    }
}
