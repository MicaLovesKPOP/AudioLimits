using AudioLimits.Core.Services;
using Microsoft.Win32;
using System.Security.AccessControl;
using System.Security.Principal;

namespace AudioLimits.Core.Tests;

public sealed class ApoEndpointRegistrationTransactionTests
{
    private const string VendorPre = "{11111111-2222-3333-4444-555555555555}";
    private const string VendorPost = "{66666666-7777-8888-9999-AAAAAAAAAAAA}";

    [Fact]
    public void SimulatedInstallAndRollback_PreserveVendorEffectsAndUnrelatedValues()
    {
        using var sandbox = new TestRegistryKey();
        var root = sandbox.Key;
        using var endpoint = root.CreateSubKey("Endpoint", true)!;
        using var fx = endpoint.CreateSubKey("FxProperties", true)!;
        using var children = root.CreateSubKey("Child APOs", true)!;

        var pre = ApoEndpointRegistrationPlanner.SlotName(5);
        var post = ApoEndpointRegistrationPlanner.SlotName(7);
        var unrelated = "Unique-OEM-value";
        fx.SetValue(pre, VendorPre);
        fx.SetValue(post, VendorPost);
        fx.SetValue(unrelated, 12345, RegistryValueKind.DWord);
        var status = new ApoFxRegistrationSnapshot(
            true, true, false,
            [new ApoFxSlotSnapshot(5, RegistryValueKind.String, VendorPre),
             new ApoFxSlotSnapshot(7, RegistryValueKind.String, VendorPost)]);
        var plan = ApoEndpointRegistrationPlanner.Build(status, false);
        Assert.True(plan.CanInstall);

        var original = ApoEndpointRegistrationTransaction.Capture(
            fx, plan.FxChanges.Select(x => x.Name));
        var guid = Guid.NewGuid().ToString("B").ToUpperInvariant();
        var backup = new ApoRegistryBackup(
            guid, DateTimeOffset.UtcNow, "Prepared", true,
            original, plan.FxChanges, plan.ChildValues);

        using (var child = children.CreateSubKey(guid, true)!)
        {
            foreach (var operation in plan.ChildValues)
                ApoEndpointRegistrationTransaction.Apply(child, operation);
            Assert.Equal(VendorPre, child.GetValue("PreMixChild"));
            Assert.Equal(VendorPost, child.GetValue("PostMixChild"));
        }

        foreach (var operation in plan.FxChanges)
        {
            if (operation.Name.StartsWith(ApoEndpointRegistrationPlanner.ModeGuid,
                    StringComparison.OrdinalIgnoreCase) &&
                fx.GetValueNames().Contains(operation.Name,
                    StringComparer.OrdinalIgnoreCase))
                continue;
            ApoEndpointRegistrationTransaction.Apply(fx, operation);
        }
        Assert.Equal(ApoEndpointRegistrationPlanner.PreMixGuid, fx.GetValue(pre));
        Assert.Equal(ApoEndpointRegistrationPlanner.PostMixGuid, fx.GetValue(post));

        ApoEndpointRegistrationTransaction.RestoreOriginal(endpoint, children, backup);
        Assert.Equal(VendorPre, fx.GetValue(pre));
        Assert.Equal(VendorPost, fx.GetValue(post));
        Assert.Equal(12345, fx.GetValue(unrelated));
        Assert.DoesNotContain(ApoEndpointRegistrationPlanner.ModeName(5), fx.GetValueNames());
        Assert.DoesNotContain(ApoEndpointRegistrationPlanner.ModeName(7), fx.GetValueNames());
        using var removed = children.OpenSubKey(guid);
        Assert.Null(removed);
    }

    [Fact]
    public void InterruptedRecovery_RefusesUnexpectedEffectChanges()
    {
        using var sandbox = new TestRegistryKey();
        var root = sandbox.Key;
        using var fx = root.CreateSubKey("FxProperties", true)!;
        using var child = root.CreateSubKey("Child", true)!;
        var pre = ApoEndpointRegistrationPlanner.SlotName(5);
        var backup = new ApoRegistryBackup(
            Guid.NewGuid().ToString("B"), DateTimeOffset.UtcNow, "Prepared",
            true,
            [new RegistryValueBackup(pre, true, RegistryValueKind.String,
                VendorPre, null, null, null)],
            [new ApoRegistryMutation(pre, ApoEndpointRegistrationPlanner.PreMixGuid)],
            [new ApoRegistryMutation("Version", "2")]);

        fx.SetValue(pre, ApoEndpointRegistrationPlanner.PreMixGuid);
        child.SetValue("Version", "2");
        Assert.True(ApoEndpointRegistrationTransaction.CanRestoreOwnValues(
            fx, child, backup));

        fx.SetValue(pre, VendorPost);
        Assert.False(ApoEndpointRegistrationTransaction.CanRestoreOwnValues(
            fx, child, backup));

        fx.SetValue(pre, VendorPre);
        child.SetValue("Version", "newer-driver-change");
        Assert.False(ApoEndpointRegistrationTransaction.CanRestoreOwnValues(
            fx, child, backup));
    }

    [Fact]
    public void Rollback_RemovesNewFxKeyOnlyWhenItWasOriginallyAbsent()
    {
        using var sandbox = new TestRegistryKey();
        var root = sandbox.Key;
        using var endpoint = root.CreateSubKey("Endpoint", true)!;
        using var children = root.CreateSubKey("Child APOs", true)!;
        var guid = Guid.NewGuid().ToString("B").ToUpperInvariant();
        var plan = ApoEndpointRegistrationPlanner.Build(
            new ApoFxRegistrationSnapshot(
                true, false, false, Array.Empty<ApoFxSlotSnapshot>()), false);

        using (var fx = endpoint.CreateSubKey("FxProperties", true)!)
        {
            foreach (var change in plan.FxChanges)
                ApoEndpointRegistrationTransaction.Apply(fx, change);
        }
        using (var child = children.CreateSubKey(guid, true)!)
            foreach (var change in plan.ChildValues)
                ApoEndpointRegistrationTransaction.Apply(child, change);

        var backup = new ApoRegistryBackup(guid, DateTimeOffset.UtcNow,
            "Prepared", false,
            plan.FxChanges.Select(x =>
                new RegistryValueBackup(x.Name, false, RegistryValueKind.Unknown,
                    null, null, null, null)).ToList(),
            plan.FxChanges, plan.ChildValues);

        ApoEndpointRegistrationTransaction.RestoreOriginal(endpoint, children, backup);
        using var after = endpoint.OpenSubKey("FxProperties");
        Assert.Null(after);
        using var afterChild = children.OpenSubKey(guid);
        Assert.Null(afterChild);
    }

    [Theory]
    [InlineData("Prepared", -30, true)]
    [InlineData("RollbackIncomplete", -30, true)]
    [InlineData("RegisteredRestartRequired", -30, true)]
    [InlineData("RegisteredRestartRequired", 30, false)]
    [InlineData("RolledBack", -30, false)]
    [InlineData("UnrecognizedStatus", 30, true)]
    public void RestartGate_FailsClosedUntilPostRegistrationBoot(
        string status, int bootOffsetMinutes, bool expected)
    {
        var registered = new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
        var backup = new ApoRegistryBackup(Guid.NewGuid().ToString("B"),
            registered, status, true,
            Array.Empty<RegistryValueBackup>(),
            Array.Empty<ApoRegistryMutation>(),
            Array.Empty<ApoRegistryMutation>());

        Assert.Equal(expected, ApoEndpointRegistrationTransaction.RequiresRestart(
            backup, registered.AddMinutes(bootOffsetMinutes)));
    }

    [Fact]
    public void RegistryBackup_RoundTripsEverySupportedValueKind()
    {
        using var sandbox = new TestRegistryKey();
        var root = sandbox.Key;
        root.SetValue("str", "value", RegistryValueKind.String);
        root.SetValue("multi", new[] { "alpha", "beta" }, RegistryValueKind.MultiString);
        root.SetValue("dword", 12345, RegistryValueKind.DWord);
        root.SetValue("qword", 12345678900L, RegistryValueKind.QWord);
        root.SetValue("binary", new byte[] { 1, 2, 3, 4 }, RegistryValueKind.Binary);
        var originals = ApoEndpointRegistrationTransaction.Capture(root,
            ["str", "multi", "dword", "qword", "binary", "missing"]);
        var backup = new ApoRegistryBackup(Guid.NewGuid().ToString("B"),
            DateTimeOffset.UtcNow, "Prepared", true,
            originals, Array.Empty<ApoRegistryMutation>(), Array.Empty<ApoRegistryMutation>());

        var path = Path.Combine(Path.GetTempPath(),
            "AudioLimits-APO-BackupTest-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            ApoEndpointRegistrationTransaction.WriteBackup(path, backup);
            var restored = ApoEndpointRegistrationTransaction.ReadBackup(path);
            Assert.Equal(6, restored.OriginalFxValues.Count);
            Assert.Equal("value", restored.OriginalFxValues[0].Text);
            Assert.Equal(new[] { "alpha", "beta" }, restored.OriginalFxValues[1].MultiText);
            Assert.Equal(12345L, restored.OriginalFxValues[2].Numeric);
            Assert.Equal(12345678900L, restored.OriginalFxValues[3].Numeric);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, restored.OriginalFxValues[4].Binary);
            Assert.False(restored.OriginalFxValues[5].IsPresent);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ProtectedAdminOwnedBackupAcl_IsAccepted()
    {
        var acl = ApoEndpointRegistrationTransaction.CreateBackupAcl();
        Assert.True(ApoEndpointRegistrationTransaction.IsSecureBackupAcl(acl));
        Assert.True(acl.AreAccessRulesProtected);
    }

    [Fact]
    public void UntrustedUsersCannotWriteRecoveryBackups()
    {
        var acl = ApoEndpointRegistrationTransaction.CreateBackupAcl();
        acl.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.Write, InheritanceFlags.ContainerInherit |
            InheritanceFlags.ObjectInherit, PropagationFlags.None,
            AccessControlType.Allow));

        Assert.False(ApoEndpointRegistrationTransaction.IsSecureBackupAcl(acl));
    }

    [Fact]
    public void UntrustedBackupDirectoryOwner_IsRejected()
    {
        var acl = ApoEndpointRegistrationTransaction.CreateBackupAcl();
        acl.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null));
        Assert.False(ApoEndpointRegistrationTransaction.IsSecureBackupAcl(acl));
    }

    [Fact]
    public void MutationVerification_DetectsTampering()
    {
        using var sandbox = new TestRegistryKey();
        var root = sandbox.Key;
        var mutation = new ApoRegistryMutation("test", "expected");
        ApoEndpointRegistrationTransaction.Apply(root, mutation);
        ApoEndpointRegistrationTransaction.Verify(root, mutation);
        root.SetValue("test", "different");
        Assert.Throws<InvalidOperationException>(
            () => ApoEndpointRegistrationTransaction.Verify(root, mutation));
    }

    private sealed class TestRegistryKey : IDisposable
    {
        private readonly string _name = @"Software\AudioLimits.Core.Tests\" + Guid.NewGuid().ToString("N");
        public RegistryKey Key { get; }

        public TestRegistryKey() => Key = Registry.CurrentUser.CreateSubKey(_name, true)!;

        public void Dispose()
        {
            Key.Dispose();
            Registry.CurrentUser.DeleteSubKeyTree(_name, throwOnMissingSubKey: false);
        }
    }
}
