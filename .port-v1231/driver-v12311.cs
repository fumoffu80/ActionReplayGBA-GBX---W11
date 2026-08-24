using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.Win32;

[assembly: AssemblyTitle("ActionReplayGBX Driver Helper")]
[assembly: AssemblyProduct("ActionReplayGBX")]
[assembly: AssemblyCompany("ActionReplayGBX project")]
[assembly: AssemblyDescription("ActionReplayGBX v1.2.31.1 WinUSB installer and repair")]
[assembly: AssemblyVersion("1.2.31.1")]
[assembly: AssemblyFileVersion("1.2.31.1")]
[assembly: AssemblyInformationalVersion("1.2.31.1-driver")]

internal sealed class DeviceInfo12311
{
    internal string InstanceId;
    internal string Service;
    internal string Name;
}

internal static class DriverProgram12311
{
    private const string DevicePrefix = "USB\\VID_05FD&PID_DAAE";
    private const string InterfaceGuid = "{325DDF96-938C-11D3-9E34-0080C82727F4}";
    private const long WdiSize = 6404608L;
    private const string WdiGitBlobSha1 = "44e91f82ede2fec7262b774e608160ee402e8a2d";

    private static DeviceInfo12311 FindDevice()
    {
        using (ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT PNPDeviceID, Service, Name FROM Win32_PnPEntity"))
        using (ManagementObjectCollection results = s.Get())
        {
            foreach (ManagementObject item in results)
            {
                string id = Convert.ToString(item["PNPDeviceID"]);
                if (String.IsNullOrEmpty(id) || !id.StartsWith(DevicePrefix, StringComparison.OrdinalIgnoreCase)) continue;
                DeviceInfo12311 d = new DeviceInfo12311();
                d.InstanceId = id;
                d.Service = Convert.ToString(item["Service"]);
                d.Name = Convert.ToString(item["Name"]);
                return d;
            }
        }
        return null;
    }

    private static string GitBlobSha1(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        byte[] header = Encoding.ASCII.GetBytes("blob " + data.Length + "\0");
        using (SHA1 sha = SHA1.Create())
        {
            sha.TransformBlock(header, 0, header.Length, null, 0);
            sha.TransformFinalBlock(data, 0, data.Length);
            StringBuilder b = new StringBuilder();
            foreach (byte x in sha.Hash) b.Append(x.ToString("x2"));
            return b.ToString();
        }
    }

    private static string FindBundledWdi()
    {
        string p = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wdi-simple.exe");
        try
        {
            if (!File.Exists(p)) return null;
            FileInfo fi = new FileInfo(p);
            if (fi.Length != WdiSize) return null;
            if (!String.Equals(GitBlobSha1(p), WdiGitBlobSha1, StringComparison.OrdinalIgnoreCase)) return null;
            return p;
        }
        catch { return null; }
    }

    private static int Run(string file, string args, out string output)
    {
        ProcessStartInfo psi = new ProcessStartInfo();
        psi.FileName = file; psi.Arguments = args; psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
        using (Process p = Process.Start(psi))
        {
            string a = p.StandardOutput.ReadToEnd(); string b = p.StandardError.ReadToEnd(); p.WaitForExit(); output = (a + (String.IsNullOrWhiteSpace(b) ? "" : Environment.NewLine + b)).Trim(); return p.ExitCode;
        }
    }

    private static bool InstallWinUsb()
    {
        string wdi = FindBundledWdi();
        if (wdi == null)
        {
            Console.Error.WriteLine("[ERREUR] wdi-simple.exe absent ou invalide.");
            return false;
        }
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ActionReplayGBX", "DriverCache", "generated");
        Directory.CreateDirectory(dir);
        string args = "-n \"GBA Link\" -m \"Datel / Action Replay GBX\" -v 0x05fd -p 0xdaae -t 0 -d \"" + dir.Replace("\"", "\\\"") + "\" -o 120000 -l 1";
        Console.WriteLine("[INFO] Installation de Microsoft WinUSB pour VID_05FD&PID_DAAE...");
        Console.WriteLine("[INFO] Bootstrap libwdi embarqué et vérifié; aucun téléchargement réseau.");
        string output; int exit = Run(wdi, args, out output); if (!String.IsNullOrWhiteSpace(output)) Console.WriteLine(output);
        if (exit != 0) { Console.Error.WriteLine("[ERREUR] libwdi a échoué (code " + exit + ")."); return false; }
        Thread.Sleep(1800);
        Run("pnputil.exe", "/scan-devices", out output); if (!String.IsNullOrWhiteSpace(output)) Console.WriteLine(output);
        Thread.Sleep(1200);
        return true;
    }

    private static string DeviceParametersPath(DeviceInfo12311 d)
    {
        return @"SYSTEM\CurrentControlSet\Enum\" + d.InstanceId + @"\Device Parameters";
    }

    private static bool WriteGuid(DeviceInfo12311 d)
    {
        try
        {
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(DeviceParametersPath(d), true))
            {
                if (key == null) { Console.Error.WriteLine("[ERREUR] Clé Device Parameters inaccessible."); return false; }
                // WinUSB INF normally uses the plural REG_MULTI_SZ. The singular REG_SZ is also
                // written for robustness on single-interface devices/older generated packages.
                key.SetValue("DeviceInterfaceGUIDs", new string[] { InterfaceGuid }, RegistryValueKind.MultiString);
                key.SetValue("DeviceInterfaceGUID", InterfaceGuid, RegistryValueKind.String);
                key.Flush();
            }
            Console.WriteLine("[OK] GUID ActionReplayGBX enregistré: " + InterfaceGuid);
            return true;
        }
        catch (Exception ex) { Console.Error.WriteLine("[ERREUR] Écriture GUID impossible: " + ex.Message); return false; }
    }

    private static bool GuidPresent(DeviceInfo12311 d)
    {
        try
        {
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(DeviceParametersPath(d), false))
            {
                if (key == null) return false;
                object multi = key.GetValue("DeviceInterfaceGUIDs", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                string[] arr = multi as string[];
                if (arr != null) foreach (string s in arr) if (String.Equals(s, InterfaceGuid, StringComparison.OrdinalIgnoreCase)) return true;
                string single = Convert.ToString(key.GetValue("DeviceInterfaceGUID", ""));
                return String.Equals(single, InterfaceGuid, StringComparison.OrdinalIgnoreCase);
            }
        }
        catch { return false; }
    }

    private static bool Restart(DeviceInfo12311 d)
    {
        string output; int exit = Run("pnputil.exe", "/restart-device \"" + d.InstanceId.Replace("\"", "\\\"") + "\"", out output);
        if (!String.IsNullOrWhiteSpace(output)) Console.WriteLine(output);
        if (exit == 0)
        {
            Console.WriteLine("[OK] Redémarrage PnP demandé à Windows.");
            return true;
        }
        Console.WriteLine("[INFO] Redémarrage PnP impossible; débranche/rebranche l'USB après la réparation.");
        return false;
    }

    private static int RestartOnlyElevated()
    {
        DeviceInfo12311 d = FindDevice();
        if (d == null)
        {
            Console.Error.WriteLine("[INFO] Aucun Action Replay USB présent à redémarrer.");
            return 10;
        }
        Console.WriteLine("[INFO] Récupération USB : redémarrage PnP de " + d.InstanceId + " (service=" + (String.IsNullOrEmpty(d.Service) ? "(aucun)" : d.Service) + ")...");
        if (!Restart(d)) return 30;
        Thread.Sleep(1300);
        DeviceInfo12311 after = FindDevice();
        if (after == null)
        {
            Console.WriteLine("[INFO] Périphérique momentanément absent après redémarrage; la surveillance automatique poursuivra la reconnexion.");
            return 0;
        }
        Console.WriteLine("[INFO] Après redémarrage: service=" + (String.IsNullOrEmpty(after.Service) ? "(aucun)" : after.Service) + ", GUID=" + (GuidPresent(after) ? "présent" : "absent"));
        Console.WriteLine("[OK] Redémarrage PnP de récupération terminé.");
        return 0;
    }

    private static int ApplyElevated()
    {
        DeviceInfo12311 d = FindDevice();
        if (d == null)
        {
            Console.Error.WriteLine("[ERREUR] Aucun Action Replay USB présent. Branche l'appareil puis relance « Pilote ».");
            return 10;
        }
        Console.WriteLine("Action Replay USB: " + (String.IsNullOrEmpty(d.Name) ? "GBA Link" : d.Name));
        Console.WriteLine("Instance: " + d.InstanceId);
        Console.WriteLine("Service avant réparation: " + (String.IsNullOrEmpty(d.Service) ? "(aucun)" : d.Service));

        if (!String.Equals(d.Service, "WINUSB", StringComparison.OrdinalIgnoreCase))
        {
            if (!InstallWinUsb()) return 20;
            d = FindDevice();
            if (d == null) { Console.Error.WriteLine("[ERREUR] Le périphérique a disparu après l'installation."); return 21; }
            Console.WriteLine("Service après installation: " + (String.IsNullOrEmpty(d.Service) ? "(aucun)" : d.Service));
            if (!String.Equals(d.Service, "WINUSB", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine("[ERREUR] Windows n'a pas lié cette instance à WinUSB. Débranche/rebranche puis relance « Pilote ».");
                return 22;
            }
        }
        else Console.WriteLine("[OK] WinUSB est déjà actif sur cette instance.");

        if (!WriteGuid(d)) return 30;
        Restart(d);
        Thread.Sleep(1300);

        DeviceInfo12311 after = FindDevice();
        if (after == null)
        {
            Console.WriteLine("[INFO] Le périphérique est momentanément absent après redémarrage. Débranche/rebranche l'USB puis relance le logiciel.");
            return 0;
        }

        // Some generated WinUSB packages can rewrite Device Parameters during re-enumeration.
        // Re-assert the ActionReplayGBX GUID after the PnP restart and verify it persisted.
        if (!GuidPresent(after))
        {
            Console.WriteLine("[INFO] Le GUID a été perdu pendant la réénumération; réécriture...");
            if (!WriteGuid(after)) return 31;
            Restart(after);
            Thread.Sleep(900);
            after = FindDevice() ?? after;
        }

        if (!GuidPresent(after))
        {
            Console.Error.WriteLine("[ERREUR] DeviceInterfaceGUID(s) n'est toujours pas enregistré après réparation.");
            return 32;
        }

        Console.WriteLine("[OK] DeviceInterfaceGUID(s) vérifié dans le registre.");
        Console.WriteLine("[OK] Configuration WinUSB terminée pour ce port USB.");
        return 0;
    }

    private static int ShowStatus()
    {
        DeviceInfo12311 d = FindDevice();
        if (d == null) { Console.WriteLine("Action Replay USB: non détecté."); return 10; }
        Console.WriteLine("Action Replay USB: détecté"); Console.WriteLine("Instance: " + d.InstanceId); Console.WriteLine("Service: " + d.Service); Console.WriteLine("GUID ActionReplayGBX: " + (GuidPresent(d) ? "présent" : "ABSENT"));
        return String.Equals(d.Service, "WINUSB", StringComparison.OrdinalIgnoreCase) && GuidPresent(d) ? 0 : 20;
    }

    public static int Main(string[] args)
    {
        bool apply = Array.IndexOf(args, "--apply") >= 0;
        bool restartOnly = Array.IndexOf(args, "--restart-only") >= 0;
        bool elevated = Array.IndexOf(args, "--elevated") >= 0;
        if (elevated && restartOnly) return RestartOnlyElevated();
        if (!apply && !restartOnly) return ShowStatus();
        if (elevated) return ApplyElevated();
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = Assembly.GetExecutingAssembly().Location;
            psi.Arguments = restartOnly ? "--restart-only --elevated" : "--apply --elevated";
            psi.Verb = "runas";
            psi.UseShellExecute = true;
            using (Process p = Process.Start(psi)) { p.WaitForExit(); return p.ExitCode; }
        }
        catch (Win32Exception) { Console.Error.WriteLine("Élévation UAC annulée ou indisponible."); return 1223; }
    }
}