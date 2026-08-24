using System;
using System.Collections.Generic;
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
[assembly: AssemblyDescription("ActionReplayGBX v1.2.31 WinUSB install and repair helper")]
[assembly: AssemblyVersion("1.2.31.0")]
[assembly: AssemblyFileVersion("1.2.31.0")]
[assembly: AssemblyInformationalVersion("1.2.31-driver-install")]

internal sealed class DeviceInfo
{
    internal string InstanceId;
    internal string Service;
    internal string Name;
}

internal sealed class ReusableDriver
{
    internal string InstanceKey;
    internal string InfPath;
    internal string DriverKey;
}

internal static class Program
{
    private const string DevicePrefix = "USB\\VID_05FD&PID_DAAE";
    private const string EnumDeviceKey = @"SYSTEM\CurrentControlSet\Enum\USB\VID_05FD&PID_DAAE";
    private const string ClassRoot = @"SYSTEM\CurrentControlSet\Control\Class";
    private const string InterfaceGuid = "{325DDF96-938C-11D3-9E34-0080C82727F4}";
    private const long WdiSize = 6404608L;
    private const string WdiGitBlobSha1 = "44e91f82ede2fec7262b774e608160ee402e8a2d";

    private static DeviceInfo FindPresentDevice()
    {
        using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT PNPDeviceID, Service, Name FROM Win32_PnPEntity"))
        using (ManagementObjectCollection results = searcher.Get())
        {
            foreach (ManagementObject item in results)
            {
                string id = Convert.ToString(item["PNPDeviceID"]);
                if (String.IsNullOrEmpty(id) || !id.StartsWith(DevicePrefix, StringComparison.OrdinalIgnoreCase)) continue;
                DeviceInfo d = new DeviceInfo();
                d.InstanceId = id;
                d.Service = Convert.ToString(item["Service"]);
                d.Name = Convert.ToString(item["Name"]);
                return d;
            }
        }
        return null;
    }

    private static ReusableDriver FindReusableWinUsbDriver()
    {
        using (RegistryKey root = Registry.LocalMachine.OpenSubKey(EnumDeviceKey, false))
        {
            if (root == null) return null;
            foreach (string instanceName in root.GetSubKeyNames())
            {
                using (RegistryKey instance = root.OpenSubKey(instanceName, false))
                {
                    if (instance == null) continue;
                    string service = Convert.ToString(instance.GetValue("Service", ""));
                    if (!String.Equals(service, "WINUSB", StringComparison.OrdinalIgnoreCase)) continue;
                    string driverKey = Convert.ToString(instance.GetValue("Driver", ""));
                    if (String.IsNullOrEmpty(driverKey)) continue;
                    using (RegistryKey driver = Registry.LocalMachine.OpenSubKey(ClassRoot + "\\" + driverKey, false))
                    {
                        if (driver == null) continue;
                        string inf = Convert.ToString(driver.GetValue("InfPath", ""));
                        if (String.IsNullOrEmpty(inf)) continue;
                        ReusableDriver r = new ReusableDriver();
                        r.InstanceKey = instanceName;
                        r.InfPath = inf.Trim();
                        r.DriverKey = driverKey;
                        return r;
                    }
                }
            }
        }
        return null;
    }

    private static int Run(string file, string arguments, out string output)
    {
        ProcessStartInfo psi = new ProcessStartInfo();
        psi.FileName = file;
        psi.Arguments = arguments;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        using (Process p = Process.Start(psi))
        {
            string a = p.StandardOutput.ReadToEnd();
            string b = p.StandardError.ReadToEnd();
            p.WaitForExit();
            output = (a + (String.IsNullOrWhiteSpace(b) ? "" : Environment.NewLine + b)).Trim();
            return p.ExitCode;
        }
    }

    private static void PrintDevice(DeviceInfo d)
    {
        if (d == null)
        {
            Console.WriteLine("Action Replay USB: non détecté.");
            return;
        }
        Console.WriteLine("Action Replay USB: détecté");
        Console.WriteLine("Nom: " + (String.IsNullOrEmpty(d.Name) ? "(inconnu)" : d.Name));
        Console.WriteLine("Instance: " + d.InstanceId);
        Console.WriteLine("Service: " + (String.IsNullOrEmpty(d.Service) ? "(aucun)" : d.Service));
    }

    private static string GitBlobSha1(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        byte[] header = Encoding.ASCII.GetBytes("blob " + data.Length + "\0");
        byte[] all = new byte[header.Length + data.Length];
        Buffer.BlockCopy(header, 0, all, 0, header.Length);
        Buffer.BlockCopy(data, 0, all, header.Length, data.Length);
        using (SHA1 sha = SHA1.Create())
        {
            byte[] hash = sha.ComputeHash(all);
            StringBuilder sb = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }

    private static string FindBundledWdi()
    {
        string[] candidates = new string[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wdi-simple.exe"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DriverBootstrap", "wdi-simple.exe")
        };
        foreach (string path in candidates)
        {
            try
            {
                if (!File.Exists(path)) continue;
                FileInfo fi = new FileInfo(path);
                if (fi.Length != WdiSize) continue;
                if (!String.Equals(GitBlobSha1(path), WdiGitBlobSha1, StringComparison.OrdinalIgnoreCase)) continue;
                return path;
            }
            catch { }
        }
        return null;
    }

    private static int ShowStatus()
    {
        DeviceInfo d = FindPresentDevice();
        PrintDevice(d);
        string wdi = FindBundledWdi();
        Console.WriteLine("Bootstrap WinUSB embarqué: " + (wdi == null ? "ABSENT/INVALIDE" : "présent et vérifié"));
        if (d == null) return 10;
        if (String.Equals(d.Service, "WINUSB", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("WinUSB est actif sur ce port USB.");
            return 0;
        }
        ReusableDriver old = FindReusableWinUsbDriver();
        if (old != null)
        {
            Console.WriteLine("WinUSB n'est pas actif sur ce port USB.");
            Console.WriteLine("Ancien package WinUSB réutilisable trouvé: " + old.InfPath);
            return 20;
        }
        if (wdi != null)
        {
            Console.WriteLine("WinUSB n'a jamais été installé pour cette instance; l'installation initiale est disponible via --apply.");
            return 21;
        }
        Console.WriteLine("WinUSB n'est pas actif et aucun moyen d'installation valide n'est disponible.");
        return 22;
    }

    private static bool SetGuidAndRestart(DeviceInfo d)
    {
        string subKey = @"SYSTEM\CurrentControlSet\Enum\" + d.InstanceId + @"\Device Parameters";
        using (RegistryKey key = Registry.LocalMachine.OpenSubKey(subKey, true))
        {
            if (key == null)
            {
                Console.Error.WriteLine("Impossible d'ouvrir la clé Device Parameters.");
                return false;
            }
            key.SetValue("DeviceInterfaceGUIDs", new string[] { InterfaceGuid }, RegistryValueKind.MultiString);
        }
        Console.WriteLine("[OK] GUID ActionReplayGBX enregistré: " + InterfaceGuid);

        string output;
        int exit = Run("pnputil.exe", "/restart-device \"" + d.InstanceId.Replace("\"", "\\\"") + "\"", out output);
        if (!String.IsNullOrWhiteSpace(output)) Console.WriteLine(output);
        if (exit != 0) Console.WriteLine("[INFO] Redémarrage automatique impossible; débranche/rebranche l'USB.");
        else Console.WriteLine("[OK] Périphérique redémarré par Windows.");
        return true;
    }

    private static string FindExportedInf(string directory)
    {
        string[] infs = Directory.GetFiles(directory, "*.inf", SearchOption.AllDirectories);
        if (infs.Length == 0) return null;
        Array.Sort(infs, StringComparer.OrdinalIgnoreCase);
        return infs[0];
    }

    private static bool TryReuseExistingOemPackage(ReusableDriver old)
    {
        string infName = Path.GetFileName(old.InfPath);
        if (String.IsNullOrEmpty(infName)) return false;
        if (!infName.StartsWith("oem", StringComparison.OrdinalIgnoreCase) || !infName.EndsWith(".inf", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("[INFO] Ancien INF " + infName + " non exportable comme package OEM; passage à l'installation initiale libwdi.");
            return false;
        }

        string exportDir = Path.Combine(Path.GetTempPath(), "ActionReplayGBX-DriverReuse-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(exportDir);
        Console.WriteLine("[INFO] Ancienne instance WinUSB trouvée: " + old.InstanceKey);
        Console.WriteLine("[INFO] Package installé trouvé: " + infName);
        Console.WriteLine("[INFO] Export temporaire du package depuis le Driver Store Windows...");

        string output;
        int exit = Run("pnputil.exe", "/export-driver \"" + infName + "\" \"" + exportDir + "\"", out output);
        if (!String.IsNullOrWhiteSpace(output)) Console.WriteLine(output);
        if (exit != 0)
        {
            Console.WriteLine("[INFO] Réutilisation du package impossible; tentative libwdi ensuite.");
            return false;
        }

        string inf = FindExportedInf(exportDir);
        if (inf == null) return false;
        Console.WriteLine("[INFO] Réapplication du même package WinUSB à l'instance actuellement branchée...");
        exit = Run("pnputil.exe", "/add-driver \"" + inf + "\" /install", out output);
        if (!String.IsNullOrWhiteSpace(output)) Console.WriteLine(output);
        if (exit != 0)
        {
            Console.WriteLine("[INFO] Réapplication OEM impossible; tentative libwdi ensuite.");
            return false;
        }
        Run("pnputil.exe", "/scan-devices", out output);
        if (!String.IsNullOrWhiteSpace(output)) Console.WriteLine(output);
        Thread.Sleep(1800);
        DeviceInfo now = FindPresentDevice();
        return now != null && String.Equals(now.Service, "WINUSB", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryFirstInstallWithBundledLibwdi()
    {
        string wdi = FindBundledWdi();
        if (wdi == null)
        {
            Console.Error.WriteLine("[ERREUR] wdi-simple.exe embarqué absent ou empreinte invalide.");
            Console.Error.WriteLine("Attendu: taille " + WdiSize + " octets, Git blob SHA1 " + WdiGitBlobSha1 + ".");
            return false;
        }

        string driverDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ActionReplayGBX", "DriverCache", "generated");
        Directory.CreateDirectory(driverDir);
        string args = "-n \"GBA Link\" -m \"Datel / Action Replay GBX\" -v 0x05fd -p 0xdaae -t 0 -d \"" + driverDir.Replace("\"", "\\\"") + "\" -l 1";
        Console.WriteLine("[INFO] Installation initiale de Microsoft WinUSB pour VID_05FD&PID_DAAE...");
        Console.WriteLine("[INFO] Bootstrap libwdi local vérifié; aucun téléchargement réseau n'est effectué.");
        string output;
        int exit = Run(wdi, args, out output);
        if (!String.IsNullOrWhiteSpace(output)) Console.WriteLine(output);
        if (exit != 0)
        {
            Console.Error.WriteLine("[ERREUR] libwdi/wdi-simple a échoué (code " + exit + ").");
            return false;
        }
        Console.WriteLine("[OK] Installation WinUSB libwdi terminée.");
        Thread.Sleep(1800);
        Run("pnputil.exe", "/scan-devices", out output);
        if (!String.IsNullOrWhiteSpace(output)) Console.WriteLine(output);
        Thread.Sleep(1200);
        return true;
    }

    private static int ApplyElevated()
    {
        DeviceInfo d = FindPresentDevice();
        if (d != null) PrintDevice(d);
        else Console.WriteLine("[INFO] Action Replay non branché; préparation WinUSB possible, mais le GUID devra être enregistré après branchement.");

        if (d == null || !String.Equals(d.Service, "WINUSB", StringComparison.OrdinalIgnoreCase))
        {
            bool installed = false;
            ReusableDriver old = FindReusableWinUsbDriver();
            if (d != null && old != null) installed = TryReuseExistingOemPackage(old);
            if (!installed)
            {
                if (!TryFirstInstallWithBundledLibwdi()) return 20;
            }
            d = FindPresentDevice();
            if (d == null)
            {
                Console.WriteLine("[INFO] Package WinUSB préparé. Branche l'Action Replay puis relance « Installer / réparer WinUSB » pour terminer le GUID.");
                return 10;
            }
            Console.WriteLine("[INFO] Après installation: service=" + (String.IsNullOrEmpty(d.Service) ? "(aucun)" : d.Service));
            if (!String.Equals(d.Service, "WINUSB", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine("[ERREUR] Windows n'a pas lié cette instance à WinUSB après installation.");
                Console.Error.WriteLine("Débranche/rebranche l'USB puis relance la réparation; si le problème persiste, vérifie le Gestionnaire de périphériques.");
                return 23;
            }
        }
        else Console.WriteLine("[OK] WinUSB est déjà actif sur cette instance.");

        if (!SetGuidAndRestart(d)) return 30;
        Thread.Sleep(700);
        DeviceInfo check = FindPresentDevice();
        if (check != null && String.Equals(check.Service, "WINUSB", StringComparison.OrdinalIgnoreCase)) Console.WriteLine("[OK] Configuration WinUSB terminée pour ce port USB.");
        else Console.WriteLine("[INFO] Configuration enregistrée; si l'application ne détecte pas l'appareil, débranche/rebranche l'USB.");
        return 0;
    }

    public static int Main(string[] args)
    {
        bool apply = Array.IndexOf(args, "--apply") >= 0;
        bool elevated = Array.IndexOf(args, "--elevated") >= 0;
        if (!apply) return ShowStatus();
        if (elevated) return ApplyElevated();

        try
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = Assembly.GetExecutingAssembly().Location;
            psi.Arguments = "--apply --elevated";
            psi.Verb = "runas";
            psi.UseShellExecute = true;
            using (Process p = Process.Start(psi))
            {
                p.WaitForExit();
                return p.ExitCode;
            }
        }
        catch (Win32Exception)
        {
            Console.Error.WriteLine("Élévation UAC annulée ou indisponible.");
            return 1223;
        }
    }
}
