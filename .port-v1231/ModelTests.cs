using System;
using System.Collections.Generic;
using System.IO;
using ActionReplayGBX.Model;

internal static class ModelTests
{
    private static int passed;
    private static void Assert(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static void Run(string name, Action test) { test(); Console.WriteLine("PASS " + name); passed++; }
    private static Cheat C(string name, uint flags, params uint[] words) { Cheat c = new Cheat(); c.Name = name; c.Flags = flags; c.Words.AddRange(words); return c; }
    private static Game G(string name, params Cheat[] cheats) { Game g = new Game(); g.Name = name; g.Cheats.AddRange(cheats); return g; }

    private static void XpcRoundTrip()
    {
        CodeDB d = new CodeDB();
        d.Games.Add(G("Pokemon Test", C("(M)", 1, 0xD8BAE4D9u, 0x4864DCE5u, 0x0B40BD4Cu, 0x6F70EA2Cu), C("Argent Max", 0, 0x29C78059u, 0x96542194u)));
        CodeDB x = CodeDB.ParseXPC(d.XPC());
        Assert(x.Games.Count == 1 && x.Games[0].Cheats.Count == 2 && x.Games[0].Cheats[0].Flags == 1u, "bad XPC roundtrip");
        CodeDB y = CodeDB.ParseBlob(x.Blob());
        Assert(y.Games[0].Cheats[1].Words[1] == 0x96542194u, "bad blob roundtrip");
    }

    private static void MasterFlagNormalization()
    {
        Game b = G("Pokemon RF", C("(M)", 1, 0x12345678u, 0x9ABCDEF0u), C("B", 0, 3u, 4u));
        CodeDB one = new CodeDB(); one.Games.Add(b);
        CodeDB parsed = CodeDB.ParseBlob(one.Blob());
        Assert(parsed.Games[0].Cheats[0].Flags == 3u, "master flag must normalize to 3 in raw blob");
        CodeDB xpc = CodeDB.ParseXPC(parsed.XPC());
        Assert(xpc.Games[0].Cheats[0].Flags == 1u, "master flag must normalize back to 1 in XPC");
    }

    private static void MasterMerge()
    {
        Game a = G("JEU A", C("(M)", 0, 0x11111111u, 0x22222222u), C("A", 0, 1u, 2u));
        Game b = G("JEU B", C("(m)", 0, 0x11111111u, 0x22222222u), C("B", 0, 3u, 4u));
        Assert(CodeModel.SameMasterCode(a, b), "(M)/(m) master names must be equivalent");
        CodeDB db = new CodeDB(); db.Games.Add(a); db.Games.Add(b);
        MergeStats st = db.CoalesceEquivalentGames();
        Assert(st.RemovedGames == 1 && db.Games.Count == 1, "master merge failed");
    }

    private static void RubisKnownVariant()
    {
        Game ar = G("PKMN RUBIS", C("(m)", 3, 0x0E4F01E3u, 0x4458DB1Cu, 0x90A6E9C3u, 0x2D8D03E3u), C("Masterball 1e obj PC", 0, 1u, 2u));
        Game xpc = G("Pokemon Rubis", C("(M)", 1, 0x0E4E01E3u, 0x4458DB1Cu, 0x90A6E9C3u, 0x2D8D03E3u), C("Argent Max", 0, 3u, 4u));
        Assert(CodeModel.CanonicalGameName(ar.Name) == CodeModel.CanonicalGameName(xpc.Name), "Rubis aliases not canonicalized");
        Assert(CodeModel.SameMasterCode(ar, xpc), "known Datel master variant not equivalent");
        CodeDB db = new CodeDB(); db.Games.Add(ar); db.Games.Add(xpc);
        MergeStats st = db.CoalesceEquivalentGames();
        Assert(st.RemovedGames == 1 && db.Games.Count == 1 && db.Games[0].Cheats.Count == 3, "Rubis merge wrong");
        Assert(db.Games[0].Cheats[0].Words[0] == 0x0E4F01E3u, "existing AR master payload must be preserved");
    }

    private static void SafeNames()
    {
        string a = CodeModel.ArSafeName("TONY HAWK PRO SKATER2 EU");
        string b = CodeModel.ArSafeName("TONY HAWK PRO SKATER3 EU");
        Assert(a.Length <= 20 && b.Length <= 20 && a != b, "AR-safe long names collided");
        Assert(CodeModel.ArSafeName("PKMN RUBIS") == "PKMN RUBIS", "short safe name changed");
        CodeDB db = new CodeDB(); db.Games.Add(G("Jeu avec un nom beaucoup trop long pour AR", C("Code avec un nom beaucoup trop long", 0, 1u, 2u)));
        List<NameIssue> issues = db.FindNameIssues();
        Assert(issues.Count == 2, "expected game + cheat name issues");
        db.ApplyNameFixes(issues);
        Assert(db.FindNameIssues().Count == 0, "suggested name fixes are not safe");
    }

    private static void CodeText()
    {
        List<uint> w = CodeModel.ParseCodeText("921D5598 0000D301\n121D55980000E003; D2000000:00000000");
        Assert(w.Count == 6, "code parser wrong word count");
        string s = CodeModel.FormatCodeText(w);
        Assert(s == "921D5598 0000D301\r\n121D5598 0000E003\r\nD2000000 00000000", "code formatter mismatch: " + s);
    }

    private static void SortGames()
    {
        CodeDB db = new CodeDB(); db.Games.Add(G("Zelda")); db.Games.Add(G("éclair")); db.Games.Add(G("alpha")); db.Games.Add(G("Beta")); db.Games.Add(G("Àventure"));
        db.SortGames(); string joined = ""; foreach (Game g in db.Games) joined += "|" + g.Name;
        Assert(joined == "|alpha|Àventure|Beta|éclair|Zelda", "alphabetical sort mismatch: " + joined);
    }

    private static string SemanticFingerprint(CodeDB db)
    {
        System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create();
        using (MemoryStream ms = new MemoryStream()) using (BinaryWriter w = new BinaryWriter(ms))
        {
            w.Write(db.Games.Count);
            foreach (Game g in db.Games)
            {
                w.Write(g.Name); w.Write(g.Cheats.Count);
                foreach (Cheat c in g.Cheats) { w.Write(c.Name); w.Write(c.Flags & 3u); w.Write(c.Words.Count); foreach (uint x in c.Words) w.Write(x); }
            }
            w.Flush(); return BitConverter.ToString(sha.ComputeHash(ms.ToArray())).Replace("-", "");
        }
    }

    private static void ValidateBundled(string path, int wantGames, int wantCheats)
    {
        CodeDB db = CodeDB.LoadXPC(path);
        Assert(db.Games.Count == wantGames, path + " game count " + db.Games.Count);
        Assert(db.CheatCount() == wantCheats, path + " cheat count " + db.CheatCount());
        string before = SemanticFingerprint(db); CodeDB xpcAgain = CodeDB.ParseXPC(db.XPC()); string after = SemanticFingerprint(xpcAgain);
        Assert(before == after, path + " semantic XPC roundtrip changed content");
        CodeDB safe = db.ARSafeCopy();
        foreach (Game g in safe.Games) { Assert(CodeModel.IsFixedNameValid(g.Name), path + " unsafe game name " + g.Name); foreach (Cheat c in g.Cheats) Assert(CodeModel.IsFixedNameValid(c.Name), path + " unsafe cheat name " + c.Name); }
        CodeDB rawAgain = CodeDB.ParseBlob(safe.Blob());
        Assert(rawAgain.Games.Count == safe.Games.Count && rawAgain.CheatCount() == safe.CheatCount(), path + " raw blob roundtrip changed counts");
    }

    private static void BundledDatabases()
    {
        string root = Environment.GetEnvironmentVariable("ARGBX_V1216_PAYLOAD");
        if (String.IsNullOrEmpty(root)) root = Environment.GetEnvironmentVariable("ARGBX_DB_PAYLOAD");
        if (String.IsNullOrEmpty(root)) throw new Exception("ARGBX_V1216_PAYLOAD / ARGBX_DB_PAYLOAD is not set");
        ValidateBundled(Path.Combine(root, "PCDatabase-Datel.xpc"), 173, 1886);
        ValidateBundled(Path.Combine(root, "PCDatabase-EuropeMAX-v7.xpc"), 227, 2605);
        ValidateBundled(Path.Combine(root, "PCDatabase.xpc"), 173, 1886);
    }

    private static void CloneIsDeep()
    {
        CodeDB db = new CodeDB(); db.Games.Add(G("A", C("C", 0, 1u, 2u))); CodeDB clone = db.Clone(); clone.Games[0].Name = "B"; clone.Games[0].Cheats[0].Words[0] = 99u;
        Assert(db.Games[0].Name == "A" && db.Games[0].Cheats[0].Words[0] == 1u, "clone aliases original data");
    }

    private static void ManualMergeAndDedupe()
    {
        CodeDB db = new CodeDB();
        db.Games.Add(G("B", C("(M)", 1, 1u, 2u), C("Same A", 0, 3u, 4u)));
        db.Games.Add(G("A", C("(M)", 1, 1u, 2u), C("Same B", 0, 3u, 4u), C("Unique", 0, 5u, 6u)));
        ManualMergeResult r = db.ManualMergeGames(new int[] { 0, 1 });
        Assert(db.Games.Count == 1, "manual merge did not remove duplicate game");
        Assert(db.Games[0].Cheats.Count == 3, "manual merge data dedupe wrong: " + db.Games[0].Cheats.Count);
        Assert(r.MergedName == "B", "manual merge did not preserve first selected physical base name");
    }

    public static int Main(string[] args)
    {
        try
        {
            Run("XPC roundtrip + raw blob", XpcRoundTrip); Run("master flag normalization", MasterFlagNormalization); Run("master merge", MasterMerge);
            Run("Rubis alias/master variant", RubisKnownVariant); Run("AR-safe names + issues", SafeNames); Run("code text parse/format", CodeText);
            Run("alphabetical sort", SortGames); Run("deep clone", CloneIsDeep); Run("manual merge + data dedupe", ManualMergeAndDedupe); Run("bundled XPC databases", BundledDatabases);
            Console.WriteLine("ALL TESTS PASSED: " + passed); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("TEST FAILURE: " + ex.ToString()); return 1; }
    }
}