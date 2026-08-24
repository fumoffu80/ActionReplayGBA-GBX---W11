using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static class V12314BoxArtFix
{
    private static MainForm owner;
    private static System.Windows.Forms.Timer watch;
    private static readonly object sync = new object();
    private static string loadingGame = "";
    private static string displayedGame = "";
    private static string lastFailureGame = "";

    internal static void Attach(MainForm form)
    {
        if (form == null) return;
        owner = form;
        EnableModernTls();

        watch = new System.Windows.Forms.Timer();
        watch.Interval = 650;
        watch.Tick += delegate { CheckForBoxArt(); };
        form.Shown += delegate
        {
            EnableModernTls();
            watch.Start();
            CheckForBoxArt();
        };
        form.FormClosed += delegate
        {
            if (watch != null)
            {
                watch.Stop();
                watch.Dispose();
                watch = null;
            }
        };
    }

    private static void EnableModernTls()
    {
        try
        {
            // TLS 1.2 = 3072. Use the numeric value so the source also compiles against
            // older .NET Framework reference assemblies while Windows 11 negotiates TLS 1.2.
            ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol | (SecurityProtocolType)3072;
            ServicePointManager.Expect100Continue = false;
        }
        catch { }
    }

    private static void CheckForBoxArt()
    {
        if (owner == null || owner.IsDisposed) return;

        bool connected = GetBool(owner, "deviceConnected");
        string gid = (GetString(owner, "deviceGameId") ?? "").Trim().ToUpperInvariant();
        string game = GetString(owner, "deviceGame") ?? "";
        PictureBox box = GetField<PictureBox>(owner, "boxArt");
        string cacheDir = GetString(owner, "cacheDir") ?? "";

        if (!connected || !IsValidGameId(gid))
        {
            displayedGame = "";
            if (box != null) box.Visible = false;
            return;
        }

        if (box != null && box.Image != null && String.Equals(displayedGame, gid, StringComparison.OrdinalIgnoreCase))
        {
            box.Visible = true;
            return;
        }

        lock (sync)
        {
            if (String.Equals(loadingGame, gid, StringComparison.OrdinalIgnoreCase)) return;
            loadingGame = gid;
        }

        Task.Factory.StartNew(delegate
        {
            try
            {
                string baseCache = String.IsNullOrWhiteSpace(cacheDir)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ActionReplayGBX", "Cache")
                    : cacheDir;
                string dir = Path.Combine(baseCache, "BoxArt");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, gid + ".png");
                string source;
                bool ok = EnsureBoxArtFile(gid, game, file, out source);
                if (!ok)
                {
                    if (!String.Equals(lastFailureGame, gid, StringComparison.OrdinalIgnoreCase))
                    {
                        lastFailureGame = gid;
                        AppendLog("Box art: no image found for " + gid + " (" + game + "). HTTPS/TLS was attempted with the v1.2.16 GameDB + Libretro sources.");
                    }
                    return;
                }

                ApplyImage(gid, file, source);
            }
            finally
            {
                lock (sync)
                {
                    if (String.Equals(loadingGame, gid, StringComparison.OrdinalIgnoreCase)) loadingGame = "";
                }
            }
        });
    }

    private static void ApplyImage(string gid, string file, string source)
    {
        Image clone = null;
        try
        {
            using (Image img = Image.FromFile(file)) clone = new Bitmap(img);
        }
        catch (Exception ex)
        {
            AppendLog("Box art: cached image could not be decoded for " + gid + ": " + ex.Message);
            return;
        }

        if (owner == null || owner.IsDisposed)
        {
            if (clone != null) clone.Dispose();
            return;
        }

        try
        {
            owner.BeginInvoke((MethodInvoker)delegate
            {
                try
                {
                    if (!GetBool(owner, "deviceConnected") || !String.Equals(GetString(owner, "deviceGameId"), gid, StringComparison.OrdinalIgnoreCase))
                    {
                        clone.Dispose();
                        return;
                    }
                    PictureBox box = GetField<PictureBox>(owner, "boxArt");
                    if (box == null)
                    {
                        clone.Dispose();
                        return;
                    }
                    Image old = box.Image;
                    box.Image = clone;
                    box.SizeMode = PictureBoxSizeMode.Zoom;
                    box.Visible = true;
                    displayedGame = gid;
                    lastFailureGame = "";
                    if (old != null && !Object.ReferenceEquals(old, clone)) old.Dispose();
                    AppendLog("Box art: displayed " + gid + (String.IsNullOrWhiteSpace(source) ? "" : " from " + source));
                    InvokePrivate(owner, "RefreshDeviceUi");
                }
                catch (Exception ex)
                {
                    try { clone.Dispose(); } catch { }
                    AppendLog("Box art: UI apply failed for " + gid + ": " + ex.Message);
                }
            });
        }
        catch
        {
            try { clone.Dispose(); } catch { }
        }
    }

    private static bool EnsureBoxArtFile(string gid, string deviceName, string file, out string source)
    {
        source = "";
        if (TryValidateImageFile(file))
        {
            source = "cache";
            return true;
        }

        try { if (File.Exists(file)) File.Delete(file); } catch { }

        List<string> titles = BuildTitleCandidates(gid, deviceName);
        for (int i = 0; i < titles.Count; i++)
        {
            string safe = LibretroName(titles[i]);
            if (String.IsNullOrWhiteSpace(safe)) continue;
            string seg = Uri.EscapeDataString(safe + ".png");
            string[] urls = new string[]
            {
                "https://thumbnails.libretro.com/Nintendo%20-%20Game%20Boy%20Advance/Named_Boxarts/" + seg,
                "https://raw.githubusercontent.com/libretro-thumbnails/Nintendo_-_Game_Boy_Advance/master/Named_Boxarts/" + seg
            };
            for (int j = 0; j < urls.Length; j++)
            {
                string error;
                byte[] data = DownloadBytes(urls[j], 8 * 1024 * 1024, out error);
                if (data == null || data.Length < 100)
                {
                    if (!String.IsNullOrWhiteSpace(error)) AppendLog("Box art HTTP: " + error + " — " + urls[j]);
                    continue;
                }

                try
                {
                    using (MemoryStream ms = new MemoryStream(data, false))
                    using (Image img = Image.FromStream(ms, true, true))
                    using (Bitmap fitted = FitImage(img, 92, 120))
                    {
                        string tmp = file + ".tmp.png";
                        fitted.Save(tmp, System.Drawing.Imaging.ImageFormat.Png);
                        if (File.Exists(file)) File.Delete(file);
                        File.Move(tmp, file);
                    }
                    if (TryValidateImageFile(file))
                    {
                        source = urls[j];
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    AppendLog("Box art decode/save: " + ex.Message + " — " + urls[j]);
                }
            }
        }
        return false;
    }

    private static List<string> BuildTitleCandidates(string gid, string deviceName)
    {
        List<string> result = new List<string>();
        Action<string> add = delegate(string s)
        {
            if (String.IsNullOrWhiteSpace(s)) return;
            string v = s.Trim();
            for (int i = 0; i < result.Count; i++)
                if (String.Equals(result[i], v, StringComparison.OrdinalIgnoreCase)) return;
            result.Add(v);
        };

        string root = "https://raw.githubusercontent.com/niemasd/GameDB-GBA/main/games/" + Uri.EscapeDataString(gid) + "/";
        string err;
        string release = DownloadText(root + "release_name.txt", out err);
        if (!String.IsNullOrWhiteSpace(release)) { add(release); add(ShortGameTitle(release)); }
        else if (!String.IsNullOrWhiteSpace(err)) AppendLog("Box art metadata: " + err + " — " + root + "release_name.txt");
        string title = DownloadText(root + "title.txt", out err);
        if (!String.IsNullOrWhiteSpace(title)) { add(title); add(ShortGameTitle(title)); }
        else if (!String.IsNullOrWhiteSpace(err)) AppendLog("Box art metadata: " + err + " — " + root + "title.txt");

        string clean = Regex.Replace(deviceName ?? "", @"\s*\([A-Za-z0-9]{4}\)\s*$", "").Trim();
        add(clean);
        add(ShortGameTitle(clean));
        return result;
    }

    private static string DownloadText(string url, out string error)
    {
        byte[] b = DownloadBytes(url, 32768, out error);
        if (b == null) return null;
        string s = Encoding.UTF8.GetString(b).Trim();
        return s.Length == 0 ? null : s;
    }

    private static byte[] DownloadBytes(string url, int maxBytes, out string error)
    {
        error = "";
        EnableModernTls();
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                HttpWebRequest r = (HttpWebRequest)WebRequest.Create(url);
                r.Method = "GET";
                r.UserAgent = "ActionReplayGBX-W11/1.2.31.4";
                r.Accept = "*/*";
                r.AllowAutoRedirect = true;
                r.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                r.Timeout = 8000;
                r.ReadWriteTimeout = 8000;
                r.KeepAlive = false;
                using (HttpWebResponse resp = (HttpWebResponse)r.GetResponse())
                {
                    int status = (int)resp.StatusCode;
                    if (status < 200 || status >= 300)
                    {
                        error = "HTTP " + status;
                        continue;
                    }
                    using (Stream s = resp.GetResponseStream())
                    using (MemoryStream ms = new MemoryStream())
                    {
                        byte[] buf = new byte[16384];
                        int total = 0;
                        while (true)
                        {
                            int n = s.Read(buf, 0, buf.Length);
                            if (n <= 0) break;
                            total += n;
                            if (total > maxBytes)
                            {
                                error = "response exceeds " + maxBytes + " bytes";
                                return null;
                            }
                            ms.Write(buf, 0, n);
                        }
                        return ms.ToArray();
                    }
                }
            }
            catch (WebException ex)
            {
                HttpWebResponse resp = ex.Response as HttpWebResponse;
                error = resp != null ? "HTTP " + (int)resp.StatusCode + " " + resp.StatusCode : ex.Status + ": " + ex.Message;
                if (attempt < 2) System.Threading.Thread.Sleep(250);
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                if (attempt < 2) System.Threading.Thread.Sleep(250);
            }
        }
        return null;
    }

    private static bool TryValidateImageFile(string path)
    {
        try
        {
            FileInfo fi = new FileInfo(path);
            if (!fi.Exists || fi.Length < 100) return false;
            using (Image img = Image.FromFile(path)) return img.Width > 0 && img.Height > 0;
        }
        catch { return false; }
    }

    private static Bitmap FitImage(Image src, int w, int h)
    {
        Bitmap dst = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(dst))
        {
            g.Clear(Color.FromArgb(243, 243, 243));
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            double scale = Math.Min((double)w / src.Width, (double)h / src.Height);
            int dw = Math.Max(1, (int)Math.Round(src.Width * scale));
            int dh = Math.Max(1, (int)Math.Round(src.Height * scale));
            g.DrawImage(src, (w - dw) / 2, (h - dh) / 2, dw, dh);
        }
        return dst;
    }

    private static string LibretroName(string s)
    {
        if (String.IsNullOrWhiteSpace(s)) return "";
        char[] bad = "&*/:<>?\\|\"".ToCharArray();
        for (int i = 0; i < bad.Length; i++) s = s.Replace(bad[i], '_');
        return s.Trim();
    }

    private static string ShortGameTitle(string s)
    {
        if (String.IsNullOrWhiteSpace(s)) return "";
        s = s.Trim();
        int i = s.IndexOf(" (", StringComparison.Ordinal);
        return i > 0 ? s.Substring(0, i).Trim() : s;
    }

    private static bool IsValidGameId(string gid)
    {
        if (gid == null || gid.Length != 4) return false;
        for (int i = 0; i < gid.Length; i++)
        {
            char c = gid[i];
            if (!((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))) return false;
        }
        return true;
    }

    internal static bool SelfTestDownloadAndApply(MainForm form, string outputPath, out string detail)
    {
        detail = "";
        try
        {
            EnableModernTls();
            string source;
            if (!EnsureBoxArtFile("BPRE", "Pokemon - FireRed Version (USA, Europe) (BPRE)", outputPath, out source))
            {
                detail = "download failed";
                return false;
            }
            PictureBox box = GetField<PictureBox>(form, "boxArt");
            if (box == null)
            {
                detail = "PictureBox missing";
                return false;
            }
            using (Image img = Image.FromFile(outputPath))
            {
                Image clone = new Bitmap(img);
                Image old = box.Image;
                box.Image = clone;
                box.SizeMode = PictureBoxSizeMode.Zoom;
                box.Visible = true;
                if (old != null) old.Dispose();
            }
            detail = "source=" + source + "; file=" + new FileInfo(outputPath).Length + " bytes; image=" + box.Image.Width + "x" + box.Image.Height + "; visible=" + box.Visible;
            return box.Image != null && box.Image.Width == 92 && box.Image.Height == 120 && box.Visible;
        }
        catch (Exception ex)
        {
            detail = ex.ToString();
            return false;
        }
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

    private static string GetString(object obj, string name)
    {
        try
        {
            FieldInfo f = obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            object v = f == null ? null : f.GetValue(obj);
            return v as string;
        }
        catch { return null; }
    }

    private static bool GetBool(object obj, string name)
    {
        try
        {
            FieldInfo f = obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            object v = f == null ? null : f.GetValue(obj);
            return v is bool && (bool)v;
        }
        catch { return false; }
    }

    private static void InvokePrivate(MainForm form, string name)
    {
        try
        {
            MethodInfo m = typeof(MainForm).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (m != null) m.Invoke(form, null);
        }
        catch { }
    }

    private static void AppendLog(string text)
    {
        try
        {
            if (owner == null || owner.IsDisposed) return;
            MethodInfo m = typeof(MainForm).GetMethod("AppendLog", BindingFlags.Instance | BindingFlags.NonPublic);
            if (m != null) m.Invoke(owner, new object[] { text });
        }
        catch { }
    }
}
