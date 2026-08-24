using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

internal static class BoxArtTests
{
    [STAThread]
    public static int Main(string[] args)
    {
        string old = Environment.GetEnvironmentVariable("ARGBX_SETTINGS_DIR");
        string temp = Path.Combine(Path.GetTempPath(), "ActionReplayGBX-boxart-selftest-" + Guid.NewGuid().ToString("N"));
        MainForm form = null;
        try
        {
            Environment.SetEnvironmentVariable("ARGBX_SETTINGS_DIR", temp);
            LanguageManager.SaveLanguage("fr");
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            form = new MainForm();
            V1216VisualParity.Attach(form);
            form.Show();
            Application.DoEvents();

            // Exercise the same state as a real connected cartridge. The visual parity
            // layer only reserves the 92x120 box-art rectangle while deviceConnected is true.
            SetField(form, "deviceConnected", true);
            SetField(form, "deviceGameId", "BPRE");
            SetField(form, "deviceGame", "Pokemon - FireRed Version (USA, Europe) (BPRE)");
            MethodInfo layout = typeof(V1216VisualParity).GetMethod("LayoutNow", BindingFlags.Static | BindingFlags.NonPublic);
            if (layout == null) return Fail(9, "LayoutNow missing");
            layout.Invoke(null, null);
            Application.DoEvents();

            string outFile = args != null && args.Length > 0 ? args[0] : Path.Combine(temp, "BPRE.png");
            Directory.CreateDirectory(Path.GetDirectoryName(outFile));
            string detail;
            bool ok = V12314BoxArtFix.SelfTestDownloadAndApply(form, outFile, out detail);
            Console.WriteLine(detail);
            if (!ok) return Fail(10, "BPRE network download/apply failed: " + detail);
            if (!File.Exists(outFile) || new FileInfo(outFile).Length < 1000) return Fail(11, "Downloaded image file missing or too small");

            PictureBox box = Field<PictureBox>(form, "boxArt");
            if (box == null) return Fail(12, "boxArt PictureBox missing");
            if (box.Image == null) return Fail(13, "boxArt.Image is null after real network download");
            if (!box.Visible) return Fail(14, "boxArt PictureBox is not visible after apply");
            if (box.Image.Width != 92 || box.Image.Height != 120) return Fail(15, "boxArt image dimensions are not 92x120: " + box.Image.Size);
            if (box.Width != 92 || box.Height != 120) return Fail(16, "boxArt control is not laid out at 92x120: " + box.Bounds);

            // Verify actual image pixels, not merely a non-null Image reference.
            int varied = 0;
            using (Bitmap rendered = new Bitmap(92, 120))
            using (Graphics g = Graphics.FromImage(rendered))
            {
                g.Clear(Color.FromArgb(243, 243, 243));
                g.DrawImage(box.Image, new Rectangle(0, 0, 92, 120));
                for (int y = 0; y < rendered.Height; y += 2)
                    for (int x = 0; x < rendered.Width; x += 2)
                    {
                        Color p = rendered.GetPixel(x, y);
                        int delta = Math.Abs(p.R - 243) + Math.Abs(p.G - 243) + Math.Abs(p.B - 243);
                        if (delta > 24) varied++;
                    }
            }
            Console.WriteLine("Rendered box-art varied samples=" + varied);
            if (varied < 200) return Fail(17, "box-art pixels are blank/nearly blank");

            Console.WriteLine("PASS box-art network + decode + 92x120 PictureBox apply + pixels");
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

    private static void SetField(MainForm form, string name, object value)
    {
        FieldInfo f = typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        if (f == null) throw new MissingFieldException(typeof(MainForm).FullName, name);
        f.SetValue(form, value);
    }

    private static int Fail(int code, string text)
    {
        Console.Error.WriteLine("FAIL " + code + ": " + text);
        return code;
    }
}
