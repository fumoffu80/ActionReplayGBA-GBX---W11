using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

[assembly: AssemblyTitle("ActionReplayGBX Engine")]
[assembly: AssemblyProduct("ActionReplayGBX")]
[assembly: AssemblyCompany("ActionReplayGBX project")]
[assembly: AssemblyDescription("ActionReplayGBX v1.2.29 CSharp WinUSB engine")]
[assembly: AssemblyVersion("1.2.29.0")]
[assembly: AssemblyFileVersion("1.2.29.0")]
[assembly: AssemblyInformationalVersion("1.2.29-port")]

internal static class BinaryLE
{
    internal static uint ReadU32(byte[] b, int o)
    {
        return (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));
    }

    internal static void WriteU32(byte[] b, int o, uint v)
    {
        b[o] = (byte)v;
        b[o + 1] = (byte)(v >> 8);
        b[o + 2] = (byte)(v >> 16);
        b[o + 3] = (byte)(v >> 24);
    }
}

internal static class Checksums
{
    private static readonly uint[] CrcTable = BuildTable();

    private static uint[] BuildTable()
    {
        uint[] table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int j = 0; j < 8; j++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[i] = c;
        }
        return table;
    }

    internal static uint Crc32(byte[] data)
    {
        uint c = 0xFFFFFFFFu;
        for (int i = 0; i < data.Length; i++)
            c = CrcTable[(c ^ data[i]) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }

    internal static string Sha256(byte[] data)
    {
        using (SHA256 s = SHA256.Create())
            return BitConverter.ToString(s.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
    }
}

internal sealed class FirmwarePayload
{
    internal byte[] Payload;
    internal int InputSize;
    internal string Format;
}

internal sealed class Device : IDisposable
{
    private const uint DIGCF_PRESENT = 0x00000002;
    private const uint DIGCF_DEVICEINTERFACE = 0x00000010;
    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_OVERLAPPED = 0x40000000;
    private const uint PIPE_TRANSFER_TIMEOUT = 3;
    private const byte EP_OUT = 0x02;
    private const byte EP_IN = 0x81;
    private const int ERROR_NO_MORE_ITEMS = 259;
    private const int ERROR_INSUFFICIENT_BUFFER = 122;

    internal const int FirmwareTotalSize = 0x40000;
    internal const int FirmwareCodeSize = 0x20000;
    internal const int DatelGsuSize = 4 + FirmwareCodeSize + 4;

    private static readonly Guid DeviceInterfaceGuid = new Guid("325DDF96-938C-11D3-9E34-0080C82727F4");
    private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA
    {
        public uint cbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid ClassGuid, IntPtr Enumerator, IntPtr hwndParent, uint Flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr DeviceInfoSet, IntPtr DeviceInfoData, ref Guid InterfaceClassGuid, uint MemberIndex, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr DeviceInfoSet, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData, IntPtr DeviceInterfaceDetailData, uint DeviceInterfaceDetailDataSize, out uint RequiredSize, IntPtr DeviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_Initialize(SafeFileHandle DeviceHandle, out IntPtr InterfaceHandle);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_Free(IntPtr InterfaceHandle);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_ReadPipe(IntPtr InterfaceHandle, byte PipeID, byte[] Buffer, uint BufferLength, out uint LengthTransferred, IntPtr Overlapped);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_WritePipe(IntPtr InterfaceHandle, byte PipeID, byte[] Buffer, uint BufferLength, out uint LengthTransferred, IntPtr Overlapped);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_SetPipePolicy(IntPtr InterfaceHandle, byte PipeID, uint PolicyType, uint ValueLength, ref uint Value);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_AbortPipe(IntPtr InterfaceHandle, byte PipeID);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_ResetPipe(IntPtr InterfaceHandle, byte PipeID);

    private SafeFileHandle file;
    private IntPtr usb;

    private Device(SafeFileHandle f, IntPtr u)
    {
        file = f;
        usb = u;
    }

    internal static Device Open()
    {
        Guid guid = DeviceInterfaceGuid;
        IntPtr set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (set == IntPtr.Zero || set == INVALID_HANDLE_VALUE)
            throw new InvalidOperationException("SetupDiGetClassDevsW failed: " + Marshal.GetLastWin32Error());

        try
        {
            for (uint index = 0; ; index++)
            {
                SP_DEVICE_INTERFACE_DATA data = new SP_DEVICE_INTERFACE_DATA();
                data.cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, index, ref data))
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err == ERROR_NO_MORE_ITEMS) break;
                    break;
                }

                uint required;
                SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out required, IntPtr.Zero);
                int firstErr = Marshal.GetLastWin32Error();
                if (required < 16 || (firstErr != ERROR_INSUFFICIENT_BUFFER && firstErr != 0))
                    continue;

                IntPtr detail = Marshal.AllocHGlobal((int)required);
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(set, ref data, detail, required, out required, IntPtr.Zero))
                        continue;
                    string path = Marshal.PtrToStringUni(IntPtr.Add(detail, 4));
                    if (string.IsNullOrEmpty(path)) continue;

                    SafeFileHandle handle = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
                    if (handle == null || handle.IsInvalid)
                    {
                        if (handle != null) handle.Dispose();
                        continue;
                    }

                    IntPtr winUsb;
                    if (!WinUsb_Initialize(handle, out winUsb))
                    {
                        handle.Dispose();
                        continue;
                    }

                    Device d = new Device(handle, winUsb);
                    d.SetTimeout(5000);
                    return d;
                }
                finally
                {
                    Marshal.FreeHGlobal(detail);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }

        throw new InvalidOperationException("Action Replay not found through WinUSB GUID; bind WinUSB and register DeviceInterfaceGUIDs first.");
    }

    internal void SetTimeout(uint ms)
    {
        WinUsb_SetPipePolicy(usb, EP_OUT, PIPE_TRANSFER_TIMEOUT, 4, ref ms);
        WinUsb_SetPipePolicy(usb, EP_IN, PIPE_TRANSFER_TIMEOUT, 4, ref ms);
    }

    internal void RecoverPipes()
    {
        WinUsb_AbortPipe(usb, EP_OUT);
        WinUsb_AbortPipe(usb, EP_IN);
        WinUsb_ResetPipe(usb, EP_OUT);
        WinUsb_ResetPipe(usb, EP_IN);
    }

    private void WritePipe(byte[] data)
    {
        if (data == null || data.Length == 0) return;
        uint n;
        if (!WinUsb_WritePipe(usb, EP_OUT, data, (uint)data.Length, out n, IntPtr.Zero))
            throw new IOException("WinUsb_WritePipe failed: " + Marshal.GetLastWin32Error());
        if (n != data.Length)
            throw new IOException("USB short write " + n + "/" + data.Length);
    }

    private void ReadPipe(byte[] data)
    {
        if (data == null || data.Length == 0) return;
        uint n;
        if (!WinUsb_ReadPipe(usb, EP_IN, data, (uint)data.Length, out n, IntPtr.Zero))
            throw new IOException("WinUsb_ReadPipe failed: " + Marshal.GetLastWin32Error());
        if (n != data.Length)
            throw new IOException("USB short read " + n + "/" + data.Length);
    }

    private static bool AllByte(byte[] data, byte value)
    {
        if (data == null || data.Length == 0) return false;
        for (int i = 0; i < data.Length; i++) if (data[i] != value) return false;
        return true;
    }

    private byte[] Exchange8(byte[] packet)
    {
        if (packet == null || packet.Length != 8) throw new ArgumentException("exchange8 length");
        WritePipe(packet);
        byte[] ack = new byte[8];
        ReadPipe(ack);
        return ack;
    }

    private void Send8(byte[] packet)
    {
        byte[] ack = Exchange8(packet);
        if (!AllByte(ack, 0))
            throw new IOException("non-zero ACK: " + HexBytes(ack));
    }

    private byte[] Recv8()
    {
        WritePipe(new byte[8]);
        byte[] data = new byte[8];
        ReadPipe(data);
        return data;
    }

    private void SendBytes(byte[] data)
    {
        int off = 0;
        while (off < data.Length)
        {
            byte[] p = new byte[8];
            int n = Math.Min(8, data.Length - off);
            Buffer.BlockCopy(data, off, p, 0, n);
            Send8(p);
            off += n;
        }
    }

    private void RecvBytes(byte[] destination)
    {
        int off = 0;
        while (off < destination.Length)
        {
            byte[] p = Recv8();
            int n = Math.Min(8, destination.Length - off);
            Buffer.BlockCopy(p, 0, destination, off, n);
            off += n;
        }
    }

    private void Command(byte command)
    {
        Send8(new byte[] { (byte)'C', (byte)'B', (byte)'W', command, 0, 0, 0, 0 });
    }

    internal string Version()
    {
        Command(0x1C);
        byte[] b = new byte[2];
        RecvBytes(b);
        return b[1].ToString() + "." + b[0].ToString();
    }

    internal uint Storage()
    {
        Command(0x1B);
        byte[] b = new byte[4];
        RecvBytes(b);
        return BinaryLE.ReadU32(b, 0);
    }

    private static string TrimZeroAscii(byte[] b)
    {
        int n = 0;
        while (n < b.Length && b[n] != 0) n++;
        return Encoding.ASCII.GetString(b, 0, n);
    }

    internal string Game()
    {
        Command(0x15);
        byte[] name = new byte[16];
        byte[] id = new byte[4];
        RecvBytes(name);
        RecvBytes(id);
        return TrimZeroAscii(name) + " (" + TrimZeroAscii(id) + ")";
    }

    private byte[] ReadMemory(uint address, uint length)
    {
        Command(0x11);
        byte[] info = new byte[8];
        BinaryLE.WriteU32(info, 0, address);
        BinaryLE.WriteU32(info, 4, length);
        SendBytes(info);
        byte[] result = new byte[length];
        RecvBytes(result);
        return result;
    }

    private static void WriteFileAtomic(string path, byte[] data)
    {
        if (string.IsNullOrEmpty(path)) throw new ArgumentException("empty output path");
        string full = Path.GetFullPath(path);
        string dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        string tmp = full + ".part";
        if (File.Exists(tmp)) File.Delete(tmp);
        using (FileStream f = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            f.Write(data, 0, data.Length);
            f.Flush(true);
        }
        if (new FileInfo(tmp).Length != data.Length)
            throw new IOException("temporary firmware file has wrong size");
        if (File.Exists(full)) File.Delete(full);
        File.Move(tmp, full);
    }

    internal void DumpFirmware(string path)
    {
        byte[] image = new byte[FirmwareTotalSize];
        for (int off = 0; off < FirmwareTotalSize; off += 0x100)
        {
            byte[] block = ReadMemory(0x08000000u + (uint)off, 0x100);
            Buffer.BlockCopy(block, 0, image, off, block.Length);
            Console.Write("\rFirmware: {0,3}%", (off + 0x100) * 100 / FirmwareTotalSize);
        }
        Console.WriteLine();
        WriteFileAtomic(path, image);
        if (new FileInfo(path).Length != FirmwareTotalSize)
            throw new IOException("firmware dump verification failed");

        byte[] system = new byte[FirmwareCodeSize];
        byte[] codes = new byte[FirmwareCodeSize];
        Buffer.BlockCopy(image, 0, system, 0, system.Length);
        Buffer.BlockCopy(image, FirmwareCodeSize, codes, 0, codes.Length);
        Console.WriteLine("Firmware saved: " + path);
        Console.WriteLine("Firmware size: 262144 bytes (256 KiB)");
        Console.WriteLine("Full flash CRC32: {0:X8}", Checksums.Crc32(image));
        Console.WriteLine("Full flash SHA256: " + Checksums.Sha256(image));
        Console.WriteLine("System region CRC32: {0:X8}", Checksums.Crc32(system));
        Console.WriteLine("System region SHA256: " + Checksums.Sha256(system));
        Console.WriteLine("Code DB region CRC32: {0:X8}", Checksums.Crc32(codes));
        Console.WriteLine("Code DB region SHA256: " + Checksums.Sha256(codes));
    }

    internal static FirmwarePayload LoadFirmwarePayload(string path)
    {
        byte[] raw = File.ReadAllBytes(path);
        FirmwarePayload result = new FirmwarePayload();
        result.InputSize = raw.Length;
        byte[] payload;
        string format;

        if (raw.Length == DatelGsuSize)
        {
            if (raw[0] != (byte)'G' || raw[1] != (byte)'S' || raw[2] != (byte)'A' || raw[3] != (byte)'U')
                throw new InvalidDataException("invalid Datel GSU: 0x20008-byte file does not start with GSAU");
            uint seed = BinaryLE.ReadU32(raw, raw.Length - 4);
            byte[] encrypted = new byte[FirmwareCodeSize];
            Buffer.BlockCopy(raw, 4, encrypted, 0, encrypted.Length);
            payload = DecryptDatelGsu(encrypted, seed);
            uint got = Checksums.Crc32(payload);
            if (got != seed)
                throw new InvalidDataException(string.Format("invalid Datel GSU: decrypted CRC32 {0:X8} does not match trailer/seed {1:X8}", got, seed));
            format = string.Format("official Datel GSU (GSAU, TEA decrypted, CRC32 {0:X8} verified)", seed);
        }
        else if (raw.Length == FirmwareCodeSize)
        {
            payload = (byte[])raw.Clone();
            format = "raw 128 KiB firmware region";
        }
        else if (raw.Length == FirmwareTotalSize)
        {
            payload = new byte[FirmwareCodeSize];
            Buffer.BlockCopy(raw, 0, payload, 0, payload.Length);
            format = "full 256 KiB flash dump (first 128 KiB selected; code database preserved)";
        }
        else if (raw.Length == 0x80000)
        {
            throw new InvalidDataException("512 KiB firmware rejected: updater is restricted to classic 256 KiB/SST2M family");
        }
        else
        {
            throw new InvalidDataException("unsupported firmware file size " + raw.Length + " bytes");
        }

        ValidateClassicFirmware(payload);
        result.Payload = payload;
        result.Format = format + "; executable signature/USB markers verified";
        return result;
    }

    private static byte[] DecryptDatelGsu(byte[] encrypted, uint seed)
    {
        if (encrypted.Length != FirmwareCodeSize) throw new InvalidDataException("Datel GSU encrypted payload has wrong size");
        byte[] output = (byte[])encrypted.Clone();
        uint k0 = seed;
        uint k1 = seed ^ 0x10101010u;
        uint k2 = seed ^ 0x01010101u;
        uint k3 = seed ^ 0x11001100u;
        unchecked
        {
            for (int off = 0; off < output.Length; off += 8)
            {
                uint v0 = BinaryLE.ReadU32(output, off);
                uint v1 = BinaryLE.ReadU32(output, off + 4);
                uint sum = 0xC6EF3720u;
                for (int round = 0; round < 32; round++)
                {
                    v1 -= ((v0 << 4) + k2) ^ ((v0 >> 5) + k3) ^ (sum + v0);
                    uint e = ((v1 << 4) + k0) ^ ((v1 >> 5) + k1) ^ (sum + v1);
                    sum += 0x61C88647u;
                    v0 -= e;
                }
                BinaryLE.WriteU32(output, off, v0);
                BinaryLE.WriteU32(output, off + 4, v1);
            }
        }
        return output;
    }

    private static bool ContainsAscii(byte[] data, string text)
    {
        byte[] marker = Encoding.ASCII.GetBytes(text);
        for (int i = 0; i <= data.Length - marker.Length; i++)
        {
            int j = 0;
            while (j < marker.Length && data[i + j] == marker[j]) j++;
            if (j == marker.Length) return true;
        }
        return false;
    }

    private static void ValidateClassicFirmware(byte[] payload)
    {
        if (payload.Length != FirmwareCodeSize) throw new InvalidDataException("firmware executable region has wrong size");
        if (AllByte(payload, 0x00) || AllByte(payload, 0xFF)) throw new InvalidDataException("firmware payload is blank");
        uint entry = BinaryLE.ReadU32(payload, 0);
        if ((entry & 0xFF000000u) != 0xEA000000u)
            throw new InvalidDataException(string.Format("payload rejected: first ARM word {0:X8} is not the expected classic AR/GBX branch entry", entry));
        string[] markers = new string[] { "Waiting For USB Command", "USB Working", "USB ERROR" };
        for (int i = 0; i < markers.Length; i++)
            if (!ContainsAscii(payload, markers[i])) throw new InvalidDataException("payload rejected: missing classic AR/GBX firmware marker " + markers[i]);
    }

    internal void WriteFirmware(string path)
    {
        FirmwarePayload fw = LoadFirmwarePayload(path);
        string currentVersion = Version();
        if (!currentVersion.StartsWith("3.", StringComparison.Ordinal) && !currentVersion.StartsWith("4.", StringComparison.Ordinal))
            throw new InvalidOperationException("firmware write blocked on reported device version " + currentVersion);

        Console.WriteLine("Current device firmware: " + currentVersion);
        Console.WriteLine("Input: " + fw.Format + ".");
        uint crc = Checksums.Crc32(fw.Payload);
        Console.WriteLine("Firmware payload CRC32: {0:X8}", crc);
        Console.WriteLine("Firmware payload SHA256: " + Checksums.Sha256(fw.Payload));

        string backup = "ActionReplayGBX-firmware-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".bin";
        Console.WriteLine("Backup: " + backup);
        DumpFirmware(backup);
        byte[] backupBytes = File.ReadAllBytes(backup);
        if (backupBytes.Length != FirmwareTotalSize)
            throw new IOException("automatic firmware backup verification failed; flash aborted");

        Console.WriteLine("Backup complete and verified; starting device-native firmware update command 0x14.");
        Command(0x14);
        for (int off = 0; off < fw.Payload.Length; off += 8)
        {
            byte[] packet = new byte[8];
            Buffer.BlockCopy(fw.Payload, off, packet, 0, 8);
            try { Send8(packet); }
            catch (Exception ex) { throw new IOException(string.Format("firmware transfer failed at 0x{0:X5}: {1}", off, ex.Message), ex); }
            if ((off % 0x400) == 0 || off + 8 == fw.Payload.Length)
                Console.Write("\rFirmware update: {0,3}%", (off + 8) * 100 / fw.Payload.Length);
        }
        Console.WriteLine();
        byte[] trailer = new byte[8];
        BinaryLE.WriteU32(trailer, 0, crc);
        Send8(trailer);
        Console.WriteLine("Firmware CRC accepted by USB protocol; internal flash/reboot routine started.");
        Console.WriteLine("Do not disconnect USB or power until the Action Replay menu has returned.");
        Thread.Sleep(5000);
    }

    private static uint RecordLen(byte[] b, int offset)
    {
        return BinaryLE.ReadU32(b, offset) & 0x3FFFFFFFu;
    }

    internal void DumpCodes(string path)
    {
        Command(0x12);
        using (FileStream f = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            byte[] stats = new byte[8];
            RecvBytes(stats);
            uint games = BinaryLE.ReadU32(stats, 0);
            uint declared = BinaryLE.ReadU32(stats, 4);
            if (games > 100000 || declared > 10000000) throw new InvalidDataException("implausible code DB header");
            f.Write(stats, 0, stats.Length);
            ulong seen = 0;
            for (uint i = 0; i < games; i++)
            {
                byte[] g = new byte[24];
                RecvBytes(g);
                f.Write(g, 0, g.Length);
                uint gc = RecordLen(g, 0);
                if (gc > 1000000) throw new InvalidDataException("implausible cheats/game");
                Console.Write("\rGames: {0}/{1} {2}", i + 1, games, TrimZeroAscii(SubArray(g, 4, 20)));
                for (uint j = 0; j < gc; j++)
                {
                    byte[] c = new byte[24];
                    RecvBytes(c);
                    f.Write(c, 0, c.Length);
                    uint words = RecordLen(c, 0);
                    if ((words & 1) != 0 || words > 1000000) throw new InvalidDataException("invalid code word count");
                    for (uint k = 0; k < words / 2; k++)
                    {
                        byte[] r = new byte[8];
                        RecvBytes(r);
                        f.Write(r, 0, r.Length);
                    }
                    seen++;
                }
            }
            f.Flush(true);
            Console.WriteLine();
            Console.WriteLine("Header: {0} games, {1} cheats; parsed {2}.", games, declared, seen);
        }
    }

    internal static byte[] ValidateCodes(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        if (data.Length < 8) throw new InvalidDataException("missing DB header");
        int p = 8;
        uint games = BinaryLE.ReadU32(data, 0);
        uint declared = BinaryLE.ReadU32(data, 4);
        if (games > 100000 || declared > 10000000) throw new InvalidDataException("implausible DB counts");
        ulong actual = 0;
        for (uint i = 0; i < games; i++)
        {
            Need(data, p, 24, "truncated game record");
            uint gc = RecordLen(data, p);
            p += 24;
            if (gc > 1000000) throw new InvalidDataException("implausible cheats/game");
            for (uint j = 0; j < gc; j++)
            {
                Need(data, p, 24, "truncated cheat record");
                uint words = RecordLen(data, p);
                p += 24;
                if ((words & 1) != 0) throw new InvalidDataException("odd code word count");
                long n = (long)(words / 2) * 8L;
                if (n > int.MaxValue) throw new InvalidDataException("code data too large");
                Need(data, p, (int)n, "truncated code data");
                p += (int)n;
                actual++;
            }
        }
        if (p != data.Length) throw new InvalidDataException("trailing DB bytes");
        if (actual != declared) throw new InvalidDataException("cheat count mismatch header=" + declared + " records=" + actual);
        return data;
    }

    private static void Need(byte[] data, int offset, int count, string message)
    {
        if (offset < 0 || count < 0 || offset > data.Length - count) throw new InvalidDataException(message);
    }

    internal void WriteCodes(string path)
    {
        byte[] b = ValidateCodes(path);
        uint games = BinaryLE.ReadU32(b, 0);
        uint cheats = BinaryLE.ReadU32(b, 4);
        Command(0x13);
        int packets = (b.Length + 7) / 8;
        int nonZeroLater = 0;
        for (int i = 0, off = 0; off < b.Length; i++, off += 8)
        {
            byte[] pkt = new byte[8];
            int n = Math.Min(8, b.Length - off);
            Buffer.BlockCopy(b, off, pkt, 0, n);
            byte[] ack = Exchange8(pkt);
            if (i == 0)
            {
                byte expected = games == 1 ? (byte)0x33 : (byte)0x44;
                if (!AllByte(ack, expected))
                    throw new IOException("unexpected v3.x write-header status: got " + HexBytes(ack) + " expected " + expected.ToString("X2") + " x8");
                Console.WriteLine("Write handshake accepted: {0:X2} x8 ({1} games, {2} cheats).", expected, games, cheats);
            }
            else if (!AllByte(ack, 0))
            {
                nonZeroLater++;
                Console.WriteLine("WARNING: payload status packet {0}/{1} = {2} (Datel-compatible continue).", i + 1, packets, HexBytes(ack));
            }
            if (packets >= 10 && (((i + 1) % 10) == 0 || i + 1 == packets))
                Console.Write("\rWriting code DB: {0}/{1} packets", i + 1, packets);
        }
        if (packets >= 10) Console.WriteLine();
        if (nonZeroLater != 0) Console.WriteLine("WARNING: {0} non-zero payload status packet(s) were observed.", nonZeroLater);
        uint storage = Storage();
        Console.WriteLine("Post-write storage query OK: {0} bytes remaining.", storage);
    }

    internal void DumpSave(string path)
    {
        Command(0x17);
        byte[] lb = new byte[4];
        RecvBytes(lb);
        uint n = BinaryLE.ReadU32(lb, 0);
        if (n < 8 || (n & 0xF) != 8 || n > 0x1000000) throw new InvalidDataException("unusual save length " + n.ToString("X"));
        byte[] header = new byte[8];
        RecvBytes(header);
        n -= 8;
        uint total = n;
        uint done = 0;
        using (FileStream f = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            while (n > 0)
            {
                byte[] block = new byte[8];
                RecvBytes(block);
                int take = (int)Math.Min(8u, n);
                f.Write(block, 0, take);
                n -= (uint)take;
                done += (uint)take;
                if ((done % 0x400u) == 0 || done == total)
                    Console.Write("\rSAVE dump: {0}/{1} bytes", done, total);
            }
            f.Flush(true);
        }
        if (total > 0) Console.WriteLine();
    }

    internal void WriteSave(string path)
    {
        byte[] b = File.ReadAllBytes(path);
        if (b.Length != 0x10000) throw new InvalidDataException("save must be exactly 64 KiB");
        Command(0x18);
        byte[] header = new byte[8];
        BinaryLE.WriteU32(header, 0, (uint)b.Length + 8);
        SendBytes(header);
        header = new byte[8];
        header[0] = 1;
        SendBytes(header);
        int off = 0;
        while (off < b.Length)
        {
            byte[] packet = new byte[8];
            int take = Math.Min(8, b.Length - off);
            Buffer.BlockCopy(b, off, packet, 0, take);
            Send8(packet);
            off += take;
            if ((off % 0x400) == 0 || off == b.Length)
                Console.Write("\rSAVE write: {0}/{1} bytes", off, b.Length);
        }
        Console.WriteLine();
    }

    internal void Disconnect()
    {
        Command(0x20);
    }

    private static byte[] SubArray(byte[] data, int offset, int count)
    {
        byte[] r = new byte[count];
        Buffer.BlockCopy(data, offset, r, 0, count);
        return r;
    }

    private static string HexBytes(byte[] b)
    {
        return BitConverter.ToString(b).Replace("-", " ").ToLowerInvariant();
    }

    public void Dispose()
    {
        if (usb != IntPtr.Zero)
        {
            WinUsb_Free(usb);
            usb = IntPtr.Zero;
        }
        if (file != null)
        {
            file.Dispose();
            file = null;
        }
    }
}

internal static class Program
{
    private static void Usage()
    {
        Console.WriteLine("Action Replay GBX WinUSB Engine v1.2.29");
        Console.WriteLine("Usage:");
        Console.WriteLine("  argbx-engine.exe probe");
        Console.WriteLine("  argbx-engine.exe info [--recover]");
        Console.WriteLine("  argbx-engine.exe dump-firmware <file>");
        Console.WriteLine("  argbx-engine.exe validate-firmware <file>");
        Console.WriteLine("  argbx-engine.exe write-firmware <file> --enable-firmware-write");
        Console.WriteLine("  argbx-engine.exe dump-codes <file>");
        Console.WriteLine("  argbx-engine.exe validate-codes <file>");
        Console.WriteLine("  argbx-engine.exe write-codes <file> --enable-write");
        Console.WriteLine("  argbx-engine.exe dump-save <file>");
        Console.WriteLine("  argbx-engine.exe write-save <file> --enable-write");
        Console.WriteLine("  argbx-engine.exe disconnect");
    }

    private static bool HasArg(string[] args, string wanted)
    {
        for (int i = 0; i < args.Length; i++) if (args[i] == wanted) return true;
        return false;
    }

    private static int Fail(Exception ex)
    {
        Console.Error.WriteLine("ERROR: " + ex.Message);
        return 2;
    }

    public static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Usage();
            return 1;
        }

        try
        {
            string cmd = args[0];
            if (cmd == "validate-codes")
            {
                if (args.Length < 2) { Usage(); return 1; }
                Device.ValidateCodes(args[1]);
                Console.WriteLine("Code database is structurally valid.");
                return 0;
            }
            if (cmd == "validate-firmware")
            {
                if (args.Length < 2) { Usage(); return 1; }
                FirmwarePayload fw = Device.LoadFirmwarePayload(args[1]);
                Console.WriteLine("Firmware file is structurally valid for the classic AR/GBX family.");
                Console.WriteLine("Input: " + fw.Format);
                Console.WriteLine("Payload CRC32: {0:X8}", Checksums.Crc32(fw.Payload));
                Console.WriteLine("Payload SHA256: " + Checksums.Sha256(fw.Payload));
                return 0;
            }

            using (Device d = Device.Open())
            {
                if (cmd == "probe")
                {
                    Console.WriteLine("WinUSB interface: OK");
                }
                else if (cmd == "info")
                {
                    if (HasArg(args, "--recover")) d.RecoverPipes();
                    Console.WriteLine("Version: " + d.Version());
                    Console.WriteLine("Remaining storage: " + d.Storage() + " bytes");
                    Console.WriteLine("Game: " + d.Game());
                }
                else if (cmd == "dump-firmware")
                {
                    if (args.Length < 2) { Usage(); return 1; }
                    d.DumpFirmware(args[1]);
                }
                else if (cmd == "write-firmware")
                {
                    if (args.Length < 2) { Usage(); return 1; }
                    if (!HasArg(args, "--enable-firmware-write")) throw new InvalidOperationException("FIRMWARE WRITE BLOCKED: add --enable-firmware-write only after confirming classic 256 KiB USB hardware");
                    d.WriteFirmware(args[1]);
                }
                else if (cmd == "dump-codes")
                {
                    if (args.Length < 2) { Usage(); return 1; }
                    d.DumpCodes(args[1]);
                }
                else if (cmd == "write-codes")
                {
                    if (args.Length < 2) { Usage(); return 1; }
                    if (!HasArg(args, "--enable-write")) throw new InvalidOperationException("WRITE BLOCKED: add --enable-write only after verified backups");
                    byte[] wanted = Device.ValidateCodes(args[1]);
                    string backup = "argbx-backup-codes-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".bin";
                    Console.WriteLine("Backup: " + backup);
                    d.DumpCodes(backup);
                    Console.WriteLine("Backup complete; starting Datel-compatible v3.x write.");
                    d.WriteCodes(args[1]);
                    string verify = "argbx-verify-codes-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".bin";
                    Console.WriteLine("Write transfer completed; reading database back immediately: " + verify);
                    d.DumpCodes(verify);
                    byte[] got = File.ReadAllBytes(verify);
                    if (!ByteEqual(wanted, got)) throw new IOException("POST-WRITE VERIFY FAILED: read-back differs from input; keep backup and do not write again");
                    Console.WriteLine("POST-WRITE VERIFY OK: read-back is byte-for-byte identical to input.");
                }
                else if (cmd == "dump-save")
                {
                    if (args.Length < 2) { Usage(); return 1; }
                    d.DumpSave(args[1]);
                }
                else if (cmd == "write-save")
                {
                    if (args.Length < 2) { Usage(); return 1; }
                    if (!HasArg(args, "--enable-write")) throw new InvalidOperationException("WRITE BLOCKED: add --enable-write only after backup");
                    d.WriteSave(args[1]);
                }
                else if (cmd == "disconnect")
                {
                    d.Disconnect();
                }
                else
                {
                    Usage();
                    return 1;
                }
            }
            return 0;
        }
        catch (Exception ex)
        {
            return Fail(ex);
        }
    }

    private static bool ByteEqual(byte[] a, byte[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }
}
