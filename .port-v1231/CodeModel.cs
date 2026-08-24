using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

[assembly: AssemblyTitle("ActionReplayGBX Model") ]
[assembly: AssemblyProduct("ActionReplayGBX") ]
[assembly: AssemblyCompany("ActionReplayGBX project") ]
[assembly: AssemblyDescription("ActionReplayGBX XPC/raw database model v1.2.31") ]
[assembly: AssemblyVersion("1.2.31.0") ]
[assembly: AssemblyFileVersion("1.2.31.0") ]

namespace ActionReplayGBX.Model
{
    public sealed class Cheat
    {
        public string Name = "";
        public uint Flags;
        public List<uint> Words = new List<uint>();

        public Cheat Clone()
        {
            Cheat c = new Cheat();
            c.Name = Name;
            c.Flags = Flags;
            c.Words = new List<uint>(Words);
            return c;
        }
    }

    public sealed class Game
    {
        public string Name = "";
        public List<Cheat> Cheats = new List<Cheat>();

        public Game Clone()
        {
            Game g = new Game();
            g.Name = Name;
            foreach (Cheat c in Cheats) g.Cheats.Add(c.Clone());
            return g;
        }
    }

    public sealed class NameIssue
    {
        public int GameIndex;
        public int CheatIndex;
        public string Original = "";
        public string Suggested = "";
    }

    public sealed class MergeStats
    {
        public int AddedGames;
        public int RemovedGames;
        public int AddedCodes;
        public int ReplacedCodes;
        public int DedupedCodes;
    }

    public sealed class ManualMergeResult
    {
        public string MergedName = "";
        public int AddedCodes;
        public int ReplacedCodes;
        public int DedupedCodes;
    }

    public sealed class CodeDB
    {
        public List<Game> Games = new List<Game>();

        public int CheatCount()
        {
            int n = 0;
            foreach (Game g in Games) n += g.Cheats.Count;
            return n;
        }

        public CodeDB Clone()
        {
            CodeDB db = new CodeDB();
            foreach (Game g in Games) db.Games.Add(g.Clone());
            return db;
        }

        public byte[] Blob()
        {
            if (Games.Count == 0) throw new InvalidDataException("database contains no games");
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter w = new BinaryWriter(ms))
            {
                w.Write((uint)Games.Count);
                w.Write((uint)CheatCount());
                foreach (Game g in Games)
                {
                    byte[] gn = CodeModel.FixedName(g.Name);
                    w.Write((uint)g.Cheats.Count);
                    w.Write(gn);
                    foreach (Cheat c in g.Cheats)
                    {
                        byte[] cn = CodeModel.FixedName(c.Name);
                        if (c.Words.Count == 0) throw new InvalidDataException("cheat '" + c.Name + "' has no code lines");
                        if ((c.Words.Count & 1) != 0) throw new InvalidDataException("cheat '" + c.Name + "' contains an odd number of words");
                        if ((long)c.Words.Count >= (1L << 30)) throw new InvalidDataException("cheat '" + c.Name + "' is too large");
                        uint flags = c.Flags & 3u;
                        if ((c.Flags & 1u) != 0) flags = 3u;
                        uint header = (uint)c.Words.Count | ((flags & 3u) << 30);
                        w.Write(header);
                        w.Write(cn);
                        foreach (uint word in c.Words) w.Write(word);
                    }
                }
                w.Flush();
                return ms.ToArray();
            }
        }

        public void SaveBlob(string path)
        {
            File.WriteAllBytes(path, Blob());
        }

        public static CodeDB LoadBlob(string path)
        {
            return ParseBlob(File.ReadAllBytes(path));
        }

        public static CodeDB ParseBlob(byte[] data)
        {
            if (data == null || data.Length < 8) throw new InvalidDataException("binary database is too short");
            int p = 0;
            uint games = CodeModel.ReadU32(data, ref p, "database is truncated");
            uint declared = CodeModel.ReadU32(data, ref p, "database is truncated");
            if (games > 100000u || declared > 10000000u) throw new InvalidDataException("invalid database counters");
            CodeDB db = new CodeDB();
            int total = 0;
            for (uint gi = 0; gi < games; gi++)
            {
                uint h = CodeModel.ReadU32(data, ref p, "database is truncated");
                CodeModel.Need(data, p, 20, "truncated game name");
                Game g = new Game();
                g.Name = CodeModel.DecodeFixedName(data, p, 20);
                p += 20;
                uint count = h & 0x3FFFFFFFu;
                if (count > 1000000u) throw new InvalidDataException("too many cheats in one game");
                for (uint ci = 0; ci < count; ci++)
                {
                    uint ch = CodeModel.ReadU32(data, ref p, "database is truncated");
                    CodeModel.Need(data, p, 20, "truncated cheat name");
                    Cheat c = new Cheat();
                    c.Name = CodeModel.DecodeFixedName(data, p, 20);
                    c.Flags = (ch >> 30) & 3u;
                    p += 20;
                    int words = checked((int)(ch & 0x3FFFFFFFu));
                    if ((words & 1) != 0) throw new InvalidDataException("odd word count for '" + c.Name + "'");
                    long bytes = (long)words * 4L;
                    if (bytes > int.MaxValue) throw new InvalidDataException("code data is too large");
                    CodeModel.Need(data, p, (int)bytes, "truncated code data");
                    for (int i = 0; i < words; i++)
                    {
                        c.Words.Add(CodeModel.ReadU32(data, ref p, "truncated code data"));
                    }
                    g.Cheats.Add(c);
                    total++;
                }
                db.Games.Add(g);
            }
            if (p != data.Length) throw new InvalidDataException((data.Length - p) + " extra bytes at end of database");
            if (total != (int)declared) throw new InvalidDataException("inconsistent code count: header=" + declared + ", parsed=" + total);
            db.SortGames();
            return db;
        }

        public byte[] XPC()
        {
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter w = new BinaryWriter(ms))
            {
                w.Write((uint)14);
                w.Write(Encoding.ASCII.GetBytes("SharkPortCODES"));
                w.Write(new byte[12]);
                w.Write((uint)Games.Count);
                foreach (Game g in Games)
                {
                    CodeModel.WriteLpString(w, g.Name);
                    w.Write((uint)g.Cheats.Count);
                    foreach (Cheat c in g.Cheats)
                    {
                        CodeModel.WriteLpString(w, c.Name);
                        w.Write((uint)0);
                        uint xpcFlags = c.Flags & 3u;
                        if ((c.Flags & 1u) != 0) xpcFlags = 1u;
                        w.Write(xpcFlags);
                        w.Write((uint)c.Words.Count);
                        foreach (uint word in c.Words)
                        {
                            CodeModel.WriteLpString(w, word.ToString("x8"));
                        }
                    }
                }
                w.Flush();
                return ms.ToArray();
            }
        }

        public void SaveXPC(string path)
        {
            File.WriteAllBytes(path, XPC());
        }

        public static CodeDB LoadXPC(string path)
        {
            return ParseXPC(File.ReadAllBytes(path));
        }

        public static CodeDB ParseXPC(byte[] data)
        {
            if (data == null) throw new ArgumentNullException("data");
            int p = 0;
            uint n = CodeModel.ReadU32(data, ref p, "truncated XPC");
            if (n != 14u) throw new InvalidDataException("missing SharkPortCODES XPC signature");
            CodeModel.Need(data, p, 14, "truncated XPC signature");
            if (Encoding.ASCII.GetString(data, p, 14) != "SharkPortCODES") throw new InvalidDataException("missing SharkPortCODES XPC signature");
            p += 14;
            CodeModel.Need(data, p, 12, "truncated XPC header");
            p += 12;
            uint games = CodeModel.ReadU32(data, ref p, "truncated XPC");
            if (games > 10000u) throw new InvalidDataException("invalid XPC game count");
            CodeDB db = new CodeDB();
            for (uint gi = 0; gi < games; gi++)
            {
                Game g = new Game();
                g.Name = CodeModel.ReadLpString(data, ref p);
                uint cnt = CodeModel.ReadU32(data, ref p, "truncated XPC");
                if (cnt > 100000u) throw new InvalidDataException("invalid XPC cheat count");
                for (uint ci = 0; ci < cnt; ci++)
                {
                    string desc = CodeModel.ReadLpString(data, ref p);
                    uint extra = CodeModel.ReadU32(data, ref p, "truncated XPC");
                    if (extra > (1u << 20)) throw new InvalidDataException("invalid XPC block");
                    CodeModel.Need(data, p, (int)extra, "invalid XPC block");
                    p += (int)extra;
                    uint flags = CodeModel.ReadU32(data, ref p, "truncated XPC");
                    uint words = CodeModel.ReadU32(data, ref p, "truncated XPC");
                    if (words > 1000000u) throw new InvalidDataException("XPC code is too large");
                    Cheat c = new Cheat();
                    c.Name = desc;
                    c.Flags = flags & 3u;
                    if ((c.Flags & 1u) != 0 || CodeModel.LooksLikeMasterName(desc)) c.Flags |= 1u;
                    for (uint wi = 0; wi < words; wi++)
                    {
                        string s = CodeModel.ReadLpString(data, ref p);
                        if (s.Length != 8) throw new InvalidDataException("invalid XPC word '" + s + "'");
                        uint v;
                        if (!UInt32.TryParse(s, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out v))
                            throw new InvalidDataException("invalid XPC word '" + s + "'");
                        c.Words.Add(v);
                    }
                    if ((c.Words.Count & 1) != 0) throw new InvalidDataException("XPC cheat '" + desc + "': odd word count");
                    g.Cheats.Add(c);
                }
                db.Games.Add(g);
            }
            if (p != data.Length) throw new InvalidDataException("XPC contains " + (data.Length - p) + " unread bytes");
            db.SortGames();
            return db;
        }

        public void SortGames()
        {
            if (Games.Count < 2) return;
            for (int i = 1; i < Games.Count; i++)
            {
                Game cur = Games[i];
                int j = i - 1;
                while (j >= 0 && CodeModel.CompareGameNames(Games[j].Name, cur.Name) > 0)
                {
                    Games[j + 1] = Games[j];
                    j--;
                }
                Games[j + 1] = cur;
            }
        }

        public CodeDB ARSafeCopy()
        {
            CodeDB output = new CodeDB();
            foreach (Game g in Games) output.Games.Add(CodeModel.ArSafeGame(g));
            output.CoalesceEquivalentGames();
            output.SortGames();
            return output;
        }

        public MergeStats CoalesceEquivalentGames()
        {
            MergeStats st = new MergeStats();
            List<Game> output = new List<Game>();
            foreach (Game source in Games)
            {
                int gi = -1;
                for (int i = 0; i < output.Count; i++)
                {
                    if (CodeModel.SameGameIdentity(output[i], source)) { gi = i; break; }
                }
                if (gi < 0)
                {
                    output.Add(source);
                    continue;
                }
                int added;
                int replaced;
                CodeModel.MergeGameInto(output[gi], source, out added, out replaced);
                st.AddedCodes += added;
                st.ReplacedCodes += replaced;
                st.RemovedGames++;
            }
            Games = output;
            SortGames();
            return st;
        }

        public MergeStats Merge(CodeDB other)
        {
            if (other == null) throw new ArgumentNullException("other");
            MergeStats st = new MergeStats();
            foreach (Game source in other.Games)
            {
                int gi = -1;
                for (int i = 0; i < Games.Count; i++)
                {
                    if (CodeModel.SameGameIdentity(Games[i], source)) { gi = i; break; }
                }
                if (gi < 0)
                {
                    Games.Add(source.Clone());
                    st.AddedGames++;
                    st.AddedCodes += source.Cheats.Count;
                    continue;
                }
                int added;
                int replaced;
                CodeModel.MergeGameInto(Games[gi], source, out added, out replaced);
                st.AddedCodes += added;
                st.ReplacedCodes += replaced;
            }
            SortGames();
            return st;
        }

        public MergeStats CoalesceByMasterCode()
        {
            MergeStats st = new MergeStats();
            List<Game> output = new List<Game>();
            foreach (Game source in Games)
            {
                int gi = -1;
                for (int i = 0; i < output.Count; i++)
                {
                    if (CodeModel.SameGameIdentityMasterOnly(output[i], source)) { gi = i; break; }
                }
                if (gi < 0)
                {
                    output.Add(source);
                    continue;
                }
                int added;
                int replaced;
                CodeModel.MergeGameInto(output[gi], source, out added, out replaced);
                st.AddedCodes += added;
                st.ReplacedCodes += replaced;
                st.RemovedGames++;
            }
            foreach (Game g in output) st.DedupedCodes += CodeModel.DedupeCheatsByData(g);
            Games = output;
            SortGames();
            return st;
        }

        public List<List<string>> PreviewMasterCodeMerges()
        {
            List<List<string>> groups = new List<List<string>>();
            List<Game> reps = new List<Game>();
            foreach (Game g in Games)
            {
                int gi = -1;
                for (int i = 0; i < reps.Count; i++)
                {
                    if (CodeModel.SameGameIdentityMasterOnly(reps[i], g)) { gi = i; break; }
                }
                if (gi < 0)
                {
                    reps.Add(g);
                    List<string> one = new List<string>();
                    one.Add(g.Name);
                    groups.Add(one);
                }
                else groups[gi].Add(g.Name);
            }
            List<List<string>> result = new List<List<string>>();
            foreach (List<string> group in groups) if (group.Count > 1) result.Add(group);
            return result;
        }

        public ManualMergeResult ManualMergeGames(IList<int> indices)
        {
            if (indices == null || indices.Count < 2) throw new InvalidOperationException("select at least 2 games to merge");
            List<int> sorted = new List<int>(indices);
            sorted.Sort();
            for (int i = 0; i < sorted.Count; i++)
            {
                if (sorted[i] < 0 || sorted[i] >= Games.Count) throw new InvalidOperationException("invalid merge selection");
                if (i > 0 && sorted[i] == sorted[i - 1]) throw new InvalidOperationException("duplicate merge selection");
            }
            Game merged = Games[sorted[0]].Clone();
            ManualMergeResult result = new ManualMergeResult();
            result.MergedName = merged.Name;
            for (int k = 1; k < sorted.Count; k++)
            {
                int added;
                int replaced;
                CodeModel.MergeGameInto(merged, Games[sorted[k]], out added, out replaced);
                result.AddedCodes += added;
                result.ReplacedCodes += replaced;
            }
            result.DedupedCodes = CodeModel.DedupeCheatsByData(merged);
            HashSet<int> remove = new HashSet<int>(sorted);
            List<Game> output = new List<Game>();
            for (int i = 0; i < Games.Count; i++) if (!remove.Contains(i)) output.Add(Games[i]);
            output.Add(merged);
            Games = output;
            SortGames();
            return result;
        }

        public List<NameIssue> FindNameIssues()
        {
            List<NameIssue> issues = new List<NameIssue>();
            for (int gi = 0; gi < Games.Count; gi++)
            {
                Game g = Games[gi];
                if (!CodeModel.IsFixedNameValid(g.Name))
                {
                    NameIssue issue = new NameIssue();
                    issue.GameIndex = gi;
                    issue.CheatIndex = -1;
                    issue.Original = g.Name;
                    issue.Suggested = CodeModel.SuggestSafeName(g.Name);
                    issues.Add(issue);
                }
                for (int ci = 0; ci < g.Cheats.Count; ci++)
                {
                    Cheat c = g.Cheats[ci];
                    if (CodeModel.LooksLikeMasterName(c.Name) || (c.Flags & 1u) != 0) continue;
                    if (!CodeModel.IsFixedNameValid(c.Name))
                    {
                        NameIssue issue = new NameIssue();
                        issue.GameIndex = gi;
                        issue.CheatIndex = ci;
                        issue.Original = c.Name;
                        issue.Suggested = CodeModel.SuggestSafeName(c.Name);
                        issues.Add(issue);
                    }
                }
            }
            return issues;
        }

        public void ApplyNameFixes(IEnumerable<NameIssue> issues)
        {
            if (issues == null) return;
            foreach (NameIssue issue in issues)
            {
                if (issue.GameIndex < 0 || issue.GameIndex >= Games.Count) continue;
                if (issue.CheatIndex < 0)
                {
                    Games[issue.GameIndex].Name = issue.Suggested;
                    continue;
                }
                if (issue.CheatIndex >= Games[issue.GameIndex].Cheats.Count) continue;
                Games[issue.GameIndex].Cheats[issue.CheatIndex].Name = issue.Suggested;
            }
        }
    }

    public static class CodeModel
    {
        private static readonly Regex HexToken = new Regex("[0-9a-fA-F]{8,16}", RegexOptions.Compiled);
        private static readonly uint[] CrcTable = BuildCrcTable();

        internal static void Need(byte[] data, int offset, int count, string message)
        {
            if (offset < 0 || count < 0 || offset > data.Length - count) throw new InvalidDataException(message);
        }

        internal static uint ReadU32(byte[] data, ref int p, string message)
        {
            Need(data, p, 4, message);
            uint v = (uint)(data[p] | (data[p + 1] << 8) | (data[p + 2] << 16) | (data[p + 3] << 24));
            p += 4;
            return v;
        }

        internal static byte[] Latin1Encode(string s)
        {
            if (s == null) s = "";
            byte[] b = new byte[s.Length];
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] > 255) throw new InvalidDataException("character is not Latin-1 compatible: '" + s[i] + "'");
                b[i] = (byte)s[i];
            }
            return b;
        }

        internal static string Latin1Decode(byte[] data, int offset, int count)
        {
            char[] c = new char[count];
            for (int i = 0; i < count; i++) c[i] = (char)data[offset + i];
            return new string(c);
        }

        internal static byte[] FixedName(string s)
        {
            string trimmed = (s ?? "").Trim();
            byte[] b = Latin1Encode(trimmed);
            if (b.Length == 0) throw new InvalidDataException("empty name");
            if (b.Length > 20) throw new InvalidDataException("'" + s + "' exceeds 20 Latin-1 characters");
            byte[] output = new byte[20];
            for (int i = 0; i < output.Length; i++) output[i] = 0x20;
            Buffer.BlockCopy(b, 0, output, 0, b.Length);
            return output;
        }

        public static bool IsFixedNameValid(string s)
        {
            try { FixedName(s); return true; }
            catch { return false; }
        }

        internal static string DecodeFixedName(byte[] data, int offset, int count)
        {
            int end = offset + count;
            while (end > offset && (data[end - 1] == 0 || data[end - 1] == 0x20)) end--;
            return Latin1Decode(data, offset, end - offset);
        }

        internal static void WriteLpString(BinaryWriter w, string s)
        {
            byte[] b = Latin1Encode(s ?? "");
            w.Write((uint)b.Length);
            w.Write(b);
        }

        internal static string ReadLpString(byte[] data, ref int p)
        {
            uint n = ReadU32(data, ref p, "truncated XPC");
            if (n > (1u << 20)) throw new InvalidDataException("invalid XPC string");
            Need(data, p, (int)n, "invalid XPC string");
            string s = Latin1Decode(data, p, (int)n);
            p += (int)n;
            return s;
        }

        private static uint[] BuildCrcTable()
        {
            uint[] table = new uint[256];
            for (uint i = 0; i < 256u; i++)
            {
                uint c = i;
                for (int j = 0; j < 8; j++) c = (c & 1u) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[i] = c;
            }
            return table;
        }

        private static uint Crc32(byte[] data)
        {
            uint c = 0xFFFFFFFFu;
            for (int i = 0; i < data.Length; i++) c = CrcTable[(c ^ data[i]) & 0xFFu] ^ (c >> 8);
            return c ^ 0xFFFFFFFFu;
        }

        public static string ArSafeName(string s)
        {
            string trimmed = (s ?? "").Trim();
            byte[] b = Latin1Encode(trimmed);
            if (b.Length == 0) throw new InvalidDataException("empty name");
            if (b.Length <= 20) return trimmed;
            uint h = Crc32(Encoding.UTF8.GetBytes(trimmed.ToLowerInvariant())) & 0xFFFFFu;
            string suffix = "~" + h.ToString("X5");
            int prefixBytes = 20 - suffix.Length;
            return Latin1Decode(b, 0, prefixBytes) + suffix;
        }

        internal static Game ArSafeGame(Game g)
        {
            Game output = new Game();
            output.Name = ArSafeName(g.Name);
            foreach (Cheat c in g.Cheats)
            {
                Cheat cc = c.Clone();
                if (LooksLikeMasterName(c.Name) || (c.Flags & 1u) != 0) cc.Name = "(M)";
                else cc.Name = ArSafeName(c.Name);
                output.Cheats.Add(cc);
            }
            return output;
        }

        private static string NormalizeName(string s)
        {
            return (s ?? "").Trim().ToLowerInvariant();
        }

        private static string ReplaceAccentsForSort(string s)
        {
            return s.Replace("é", "e").Replace("è", "e").Replace("ê", "e").Replace("ë", "e")
                .Replace("à", "a").Replace("â", "a").Replace("ä", "a").Replace("á", "a")
                .Replace("î", "i").Replace("ï", "i").Replace("í", "i")
                .Replace("ô", "o").Replace("ö", "o").Replace("ó", "o")
                .Replace("ù", "u").Replace("û", "u").Replace("ü", "u").Replace("ú", "u")
                .Replace("ç", "c").Replace("ñ", "n");
        }

        private static string AlphabeticGameSortKey(string s)
        {
            return ReplaceAccentsForSort(NormalizeName(s));
        }

        internal static int CompareGameNames(string a, string b)
        {
            string ka = AlphabeticGameSortKey(a);
            string kb = AlphabeticGameSortKey(b);
            int c = StringComparer.Ordinal.Compare(ka, kb);
            if (c != 0) return c;
            return StringComparer.Ordinal.Compare(NormalizeName(a), NormalizeName(b));
        }

        public static string CanonicalGameName(string s)
        {
            string n = NormalizeName(s);
            n = n.Replace("é", "e").Replace("è", "e").Replace("ê", "e").Replace("ë", "e")
                .Replace("à", "a").Replace("â", "a").Replace("ä", "a")
                .Replace("î", "i").Replace("ï", "i").Replace("ô", "o").Replace("ö", "o")
                .Replace("ù", "u").Replace("û", "u").Replace("ü", "u").Replace("ç", "c")
                .Replace("_", "").Replace("-", "").Replace(" ", "").Replace("\t", "")
                .Replace(".", "").Replace(":", "").Replace("'", "").Replace("’", "")
                .Replace("(", "").Replace(")", "");
            if (n.StartsWith("pkmn", StringComparison.Ordinal)) n = "pokemon" + n.Substring(4);
            n = n.Replace("pokemonrf", "pokemonrougefeu");
            n = n.Replace("pokemonvf", "pokemonvertfeuille");
            return n;
        }

        private static string NormalizeMasterName(string s)
        {
            string n = NormalizeName(s);
            return n.Replace(" ", "").Replace("\t", "").Replace("_", "").Replace("-", "")
                .Replace("(", "").Replace(")", "").Replace(":", "").Replace(".", "")
                .Replace("î", "i").Replace("ï", "i").Replace("â", "a")
                .Replace("é", "e").Replace("è", "e").Replace("ê", "e");
        }

        public static bool LooksLikeMasterName(string s)
        {
            string n = NormalizeMasterName(s);
            return n == "m" || n == "master" || n == "mastercode" || n == "codemaitre" || n == "codemaster";
        }

        public static List<uint> MasterWords(Game g)
        {
            if (g == null) return null;
            foreach (Cheat c in g.Cheats) if (LooksLikeMasterName(c.Name) && c.Words.Count > 0) return c.Words;
            foreach (Cheat c in g.Cheats) if ((c.Flags & 1u) != 0 && c.Words.Count > 0) return c.Words;
            return null;
        }

        public static bool SameWords(IList<uint> a, IList<uint> b)
        {
            if (a == null || b == null || a.Count == 0 || a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
            return true;
        }

        public static bool EquivalentMasterWords(IList<uint> a, IList<uint> b)
        {
            if (SameWords(a, b)) return true;
            if (a == null || b == null || a.Count == 0 || a.Count != b.Count) return false;
            if (a.Count >= 2 && (a[0] ^ b[0]) == 0x00010000u)
            {
                for (int i = 1; i < a.Count; i++) if (a[i] != b[i]) return false;
                return true;
            }
            return false;
        }

        public static bool SameMasterCode(Game a, Game b)
        {
            return EquivalentMasterWords(MasterWords(a), MasterWords(b));
        }

        internal static bool SameGameIdentity(Game a, Game b)
        {
            if (NormalizeName(a.Name) == NormalizeName(b.Name) || CanonicalGameName(a.Name) == CanonicalGameName(b.Name)) return true;
            return SameMasterCode(a, b);
        }

        internal static bool SameGameIdentityMasterOnly(Game a, Game b)
        {
            List<uint> aw = MasterWords(a);
            List<uint> bw = MasterWords(b);
            return aw != null && bw != null && aw.Count > 0 && bw.Count > 0 && SameMasterCode(a, b);
        }

        internal static void MergeGameInto(Game dst, Game src, out int addedCodes, out int replacedCodes)
        {
            addedCodes = 0;
            replacedCodes = 0;
            foreach (Cheat sourceCheat in src.Cheats)
            {
                int ci = -1;
                for (int i = 0; i < dst.Cheats.Count; i++)
                {
                    if (NormalizeName(dst.Cheats[i].Name) == NormalizeName(sourceCheat.Name)) { ci = i; break; }
                }
                if (ci >= 0)
                {
                    bool dstMaster = LooksLikeMasterName(dst.Cheats[ci].Name) || (dst.Cheats[ci].Flags & 1u) != 0;
                    bool srcMaster = LooksLikeMasterName(sourceCheat.Name) || (sourceCheat.Flags & 1u) != 0;
                    if (dstMaster && srcMaster) continue;
                    dst.Cheats[ci] = sourceCheat.Clone();
                    replacedCodes++;
                }
                else
                {
                    dst.Cheats.Add(sourceCheat.Clone());
                    addedCodes++;
                }
            }
            DedupeCheatsByData(dst);
        }

        internal static int DedupeCheatsByData(Game g)
        {
            int removed = 0;
            List<Cheat> kept = new List<Cheat>();
            foreach (Cheat c in g.Cheats)
            {
                bool dup = false;
                foreach (Cheat k in kept)
                {
                    if (SameWords(c.Words, k.Words)) { dup = true; break; }
                }
                if (dup) { removed++; continue; }
                kept.Add(c);
            }
            g.Cheats = kept;
            return removed;
        }

        public static List<uint> ParseCodeText(string s)
        {
            if (s == null) s = "";
            StringBuilder normalized = new StringBuilder(s.Length);
            foreach (char ch in s)
            {
                if (Char.IsWhiteSpace(ch) || ch == ',' || ch == ';' || ch == '-' || ch == ':') normalized.Append(' ');
                else normalized.Append(ch);
            }
            MatchCollection raw = HexToken.Matches(normalized.ToString());
            List<string> tokens = new List<string>();
            foreach (Match m in raw)
            {
                string t = m.Value;
                if (t.Length == 16)
                {
                    tokens.Add(t.Substring(0, 8));
                    tokens.Add(t.Substring(8, 8));
                }
                else if (t.Length == 8) tokens.Add(t);
            }
            if (tokens.Count == 0) throw new InvalidDataException("no hexadecimal code detected");
            if ((tokens.Count & 1) != 0) throw new InvalidDataException("each line must contain two 8-digit hexadecimal values");
            List<uint> output = new List<uint>();
            foreach (string t in tokens)
            {
                uint v;
                if (!UInt32.TryParse(t, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out v))
                    throw new InvalidDataException("invalid hexadecimal code '" + t + "'");
                output.Add(v);
            }
            return output;
        }

        public static string FormatCodeText(IList<uint> words)
        {
            if (words == null) return "";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i + 1 < words.Count; i += 2)
            {
                if (i > 0) sb.Append("\r\n");
                sb.Append(words[i].ToString("X8"));
                sb.Append(' ');
                sb.Append(words[i + 1].ToString("X8"));
            }
            return sb.ToString();
        }

        private static string Latin1Sanitize(string s)
        {
            if (s == null) return "";
            s = s.Replace("\u2018", "'").Replace("\u2019", "'").Replace("\u201c", "\"").Replace("\u201d", "\"")
                .Replace("\u2013", "-").Replace("\u2014", "-").Replace("\u2026", "...");
            StringBuilder sb = new StringBuilder();
            foreach (char c in s) if (c <= 255) sb.Append(c);
            return sb.ToString();
        }

        public static string SuggestSafeName(string s)
        {
            string clean = Latin1Sanitize(s).Trim();
            if (clean.Length == 0) clean = "?";
            try { return ArSafeName(clean); }
            catch { return "?"; }
        }
    }
}
