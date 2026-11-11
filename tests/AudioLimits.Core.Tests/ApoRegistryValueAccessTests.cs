using AudioLimits.Core.Services;
using Microsoft.Win32;

namespace AudioLimits.Core.Tests;

public sealed class ApoRegistryValueAccessTests
{
    [Fact]
    public void ExistingKey_CanReadAndWriteValuesUsingNarrowOpen()
    {
        var subPath = @"Software\AudioLimits.Core.Tests\" + Guid.NewGuid().ToString("N");
        using (var created = Registry.CurrentUser.CreateSubKey(subPath, true)!)
        {
            created.SetValue("Original", "Manufacturer");
        }

        try
        {
            using var narrow = ApoRegistryValueAccess.OpenExisting(
                Registry.CurrentUser, subPath);
            Assert.Equal("Manufacturer", narrow.GetValue("Original"));
            narrow.SetValue("Original", "Preserved", RegistryValueKind.String);
            narrow.SetValue("NewValue", "SetWithoutKeyWrite", RegistryValueKind.String);
            Assert.Contains("NewValue", narrow.GetValueNames());

            // The wrapper requests only QUERY_VALUE and SET_VALUE.
            // HKCU has broader ambient permissions than MMDevices, so its
            // ability to create child keys is not a useful ACL assertion.

            using var check = Registry.CurrentUser.OpenSubKey(subPath)!;
            Assert.Equal("Preserved", check.GetValue("Original"));
            Assert.Equal("SetWithoutKeyWrite", check.GetValue("NewValue"));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(subPath, false);
        }
    }

    [Fact]
    public void MissingKey_DoesNotGetCreated()
    {
        var subPath = @"Software\AudioLimits.Core.Tests\" + Guid.NewGuid().ToString("N");
        Assert.ThrowsAny<Exception>(() =>
            ApoRegistryValueAccess.OpenExisting(Registry.CurrentUser, subPath));
        Assert.Null(Registry.CurrentUser.OpenSubKey(subPath));
    }
}
