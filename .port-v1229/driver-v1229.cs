using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Management;
using System.Reflection;
using Microsoft.Win32;

[assembly: AssemblyTitle("ActionReplayGBX Driver Helper")]
[assembly: AssemblyProduct("ActionReplayGBX")]
[assembly: AssemblyCompany("ActionReplayGBX project")]
[assembly: AssemblyDescription("ActionReplayGBX v1.2.29 WinUSB post-configuration helper")]
[assembly: AssemblyVersion("1.2.29.0")]
[assembly: AssemblyFileVersion("1.2.29.0")]
[assembly: AssemblyInformationalVersion("1.2.29-port")]

internal sealed class DeviceInfo
{
    internal string InstanceId;
    internal string Service;
    internal string Name;
}

internal static class Program
{
    private const string DevicePrefix = "USB\\VID_05FD&PID_DAAE";
    private const string InterfaceGuid = "{325DDF96-938C-11D3-9E34-0080C82727F4}";

    private static DeviceInfo FindDevice()
    {
        using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT PNPDeviceID, Service, Name FROM Win32_PnPEntity"))
        using (ManagementObjectCollection results = searcher.Get())
        {
            foreach (ManagementObject item in results)
            {
                string id = Convert.ToString(item["PNPDeviceID"]);
                if (string.IsNullOrEmpty(id) || !id.StartsWith(DevicePrefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                DeviceInfo d = new DeviceInfo();
                d.InstanceId = id;
                d.Service = Convert.ToString(item["Service"]);
                d.Name = Convert.ToString(item["Name"]);
                return d;
            }
        }
        return null;
    }

    private static int ShowStatus()
    {
        DeviceInfo d = FindDevice();
        if (d == null)
        {
            Console.WriteLine("Action Replay USB: not detected.");
            return 10;
        }
        Console.WriteLine("Action Replay USB: detected");
        Console.WriteLine("Name: " + (string.IsNullOrEmpty(d.Name) ? "(unknown)" : d.Name));
        Console.WriteLine("Instance: " + d.InstanceId);
        Console.WriteLine("Service: " + (string.IsNullOrEmpty(d.Service) ? "(unknown)" : d.Service));
        if (!string.Equals(d.Service, "WINUSB", StringComparison.OrdinalIgnoreCase))
            Console.WriteLine("WinUSB is not currently active. This helper does not download or install a driver.");
        return 0;
    }

    private static int ApplyElevated()
    {
        DeviceInfo d = FindDevice();
        if (d == null)
        {
            Console.Error.WriteLine("Action Replay USB is not present.");
            return 10;
        }
        if (!string.Equals(d.Service, "WINUSB", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("WinUSB is not already active. No driver installation or download is performed.");
            return 20;
        }

        string subKey = @"SYSTEM\CurrentControlSet\Enum\" + d.InstanceId + @"\Device Parameters";
        using (RegistryKey key = Registry.LocalMachine.OpenSubKey(subKey, true))
        {
            if (key == null)
            {
                Console.Error.WriteLine("Unable to open Device Parameters registry key.");
                return 22;
            }
            key.SetValue("DeviceInterfaceGUIDs", new string[] { InterfaceGuid }, RegistryValueKind.MultiString);
        }

        ProcessStartInfo psi = new ProcessStartInfo();
        psi.FileName = "pnputil.exe";
        psi.Arguments = "/restart-device \"" + d.InstanceId.Replace("\"", "\\\"") + "\"";
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        using (Process p = Process.Start(psi))
        {
            string output = p.StandardOutput.ReadToEnd();
            string error = p.StandardError.ReadToEnd();
            p.WaitForExit();
            if (!string.IsNullOrWhiteSpace(output)) Console.WriteLine(output);
            if (!string.IsNullOrWhiteSpace(error)) Console.Error.WriteLine(error);
            if (p.ExitCode != 0) return 30;
        }

        Console.WriteLine("Existing WinUSB binding configured for ActionReplayGBX interface GUID and device restarted.");
        Console.WriteLine("No network access, download, or third-party executable was used.");
        return 0;
    }

    public static int Main(string[] args)
    {
        bool apply = Array.IndexOf(args, "--apply") >= 0;
        bool elevated = Array.IndexOf(args, "--elevated") >= 0;

        if (!apply)
            return ShowStatus();
        if (elevated)
            return ApplyElevated();

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
            Console.Error.WriteLine("Elevation was cancelled or unavailable.");
            return 1223;
        }
    }
}
