using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DiscordTtsMic;

/// <summary>
/// User-mode bridge to the v3 virtual microphone driver.
/// The driver exposes \\.\DiscordTtsVirtualAudio and accepts 48 kHz mono PCM16 frames.
/// </summary>
public sealed class DriverBridgeClient : IDisposable
{
    private const string DevicePath = @"\\.\DiscordTtsVirtualAudio";
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;

    private SafeFileHandle? _handle;
    public bool IsConnected => _handle is { IsInvalid: false, IsClosed: false };

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes,
        uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(
        SafeFileHandle hFile, byte[] lpBuffer, uint nNumberOfBytesToWrite,
        out uint lpNumberOfBytesWritten, IntPtr lpOverlapped);

    public bool TryConnect(out string status)
    {
        DisposeHandle();
        _handle = CreateFileW(DevicePath, GENERIC_WRITE, 0, IntPtr.Zero, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);
        if (_handle.IsInvalid)
        {
            status = $"Virtual microphone driver not available (Win32 {Marshal.GetLastWin32Error()}).";
            DisposeHandle();
            return false;
        }
        status = "Virtual microphone driver connected.";
        return true;
    }

    public void WritePcm16(byte[] pcm)
    {
        if (!IsConnected) throw new InvalidOperationException("Virtual microphone driver is not connected.");
        if (!WriteFile(_handle!, pcm, (uint)pcm.Length, out var written, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to write PCM to virtual microphone driver.");
        if (written != pcm.Length)
            throw new IOException($"Short driver write: {written}/{pcm.Length} bytes.");
    }

    private void DisposeHandle()
    {
        _handle?.Dispose();
        _handle = null;
    }

    public void Dispose() => DisposeHandle();
}
