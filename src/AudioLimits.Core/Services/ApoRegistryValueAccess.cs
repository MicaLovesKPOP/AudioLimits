using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace AudioLimits.Core.Services;

/// <summary>
/// Opens an *existing* Windows audio FX key for value reads/writes, without
/// requesting KEY_CREATE_SUB_KEY or modifying its ACL. Audio endpoint keys
/// often allow SET_VALUE but reject the broad KEY_WRITE requested by
/// RegistryKey.OpenSubKey(writable: true), even for elevated processes.
/// </summary>
internal static class ApoRegistryValueAccess
{
    private const int KeyQueryValue = 0x0001;
    private const int KeySetValue = 0x0002;
    private const int KeyWow64_64Key = 0x0100;

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegOpenKeyExW")]
    private static extern int RegOpenKeyEx(
        SafeRegistryHandle root,
        string subKey,
        int options,
        int desiredAccess,
        out SafeRegistryHandle result);

    public static RegistryKey OpenExisting(RegistryKey root, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Registry path is required.", nameof(path));

        var result = RegOpenKeyEx(
            root.Handle,
            path,
            0,
            KeyQueryValue | KeySetValue | KeyWow64_64Key,
            out var handle);

        if (result != 0)
        {
            handle?.Dispose();
            throw new Win32Exception(result,
                $"The existing audio effect key could not be opened with value-only permissions (Windows error {result}).");
        }

        return RegistryKey.FromHandle(handle, RegistryView.Registry64);
    }
}
