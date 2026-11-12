using AudioLimits.Core.Services;
using Microsoft.Win32;

namespace AudioLimits.Core.Tests;

public sealed class ApoModeFallbackPlannerTests
{
    [Fact]
    public void CleanFailedSfxEfx_CanSafelySwitchToSfxMfxAndRestore()
    {
        using var fixture = new RegistryFixture();
        var backup = fixture.CreateOriginalRegistration();
        var plan = ApoModeFallbackPlanner.Build(backup, fixture.Fx, fixture.Child);

        Assert.True(plan.CanSwitch, plan.Reason);
        Assert.Equal(4, plan.Changes.Count);
        Assert.Contains(plan.Changes, x => x.Name == ApoEndpointRegistrationPlanner.SlotName(6) &&
            x.Text == ApoEndpointRegistrationPlanner.PostMixGuid);
        Assert.Contains(plan.Changes, x => x.Name == ApoEndpointRegistrationPlanner.SlotName(7) &&
            x.Delete);

        var prepared = backup with
        {
            Phase = "ModeSwitchPrepared",
            FallbackChangedUtc = DateTimeOffset.UtcNow,
            FallbackOriginalFxValues = ApoEndpointRegistrationTransaction.Capture(
                fixture.Fx, plan.Changes.Select(x => x.Name)),
            FallbackIntendedFxChanges = plan.Changes
        };

        // Model the four registry operations of the real helper.
        foreach (var change in plan.Changes)
            ApoEndpointRegistrationTransaction.Apply(fixture.Fx, change);
        foreach (var change in plan.Changes)
            ApoEndpointRegistrationTransaction.Verify(fixture.Fx, change);

        Assert.Equal(ApoEndpointRegistrationPlanner.PostMixGuid,
            fixture.Fx.GetValue(ApoEndpointRegistrationPlanner.SlotName(6)));
        Assert.Null(fixture.Fx.GetValue(ApoEndpointRegistrationPlanner.SlotName(7)));

        Assert.True(ApoModeFallbackPlanner.CanRestoreInterrupted(
            prepared, fixture.Fx, fixture.Child));
        ApoModeFallbackPlanner.RestoreCaptured(fixture.Fx, prepared);

        Assert.Equal(ApoEndpointRegistrationPlanner.PostMixGuid,
            fixture.Fx.GetValue(ApoEndpointRegistrationPlanner.SlotName(7)));
        Assert.Null(fixture.Fx.GetValue(ApoEndpointRegistrationPlanner.SlotName(6)));
        Assert.Null(fixture.Fx.GetValue(ApoEndpointRegistrationPlanner.ModeName(6)));
        Assert.NotNull(fixture.Fx.GetValue(ApoEndpointRegistrationPlanner.ModeName(7)));
        Assert.Equal("2", fixture.Child.GetValue("Version"));
    }

    [Fact]
    public void OriginalManufacturerApo_DisablesAutomaticSwitch()
    {
        using var fixture = new RegistryFixture();
        var backup = fixture.CreateOriginalRegistration() with
        {
            OriginalFxValues =
            [
                new RegistryValueBackup(
                    ApoEndpointRegistrationPlanner.SlotName(7), true,
                    RegistryValueKind.String,
                    "{11111111-2222-3333-4444-555555555555}",
                    null, null, null)
            ]
        };
        var plan = ApoModeFallbackPlanner.Build(backup, fixture.Fx, fixture.Child);
        Assert.False(plan.CanSwitch);
    }

    [Fact]
    public void UnexpectedExistingMfx_IsNeverOverwritten()
    {
        using var fixture = new RegistryFixture();
        var backup = fixture.CreateOriginalRegistration();
        fixture.Fx.SetValue(ApoEndpointRegistrationPlanner.SlotName(6),
            "{11111111-2222-3333-4444-555555555555}");
        Assert.False(ApoModeFallbackPlanner.Build(
            backup, fixture.Fx, fixture.Child).CanSwitch);
    }

    [Theory]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    public void AdditionalDriverEffectSlots_BlockAutomaticMigration(int slot)
    {
        using var fixture = new RegistryFixture();
        var backup = fixture.CreateOriginalRegistration();
        fixture.Fx.SetValue(ApoEndpointRegistrationPlanner.SlotName(slot),
            "{11111111-2222-3333-4444-555555555555}");
        Assert.False(ApoModeFallbackPlanner.Build(
            backup, fixture.Fx, fixture.Child).CanSwitch);
    }

    [Fact]
    public void ModifiedOriginalApoRegistration_IsNotOverwritten()
    {
        using var fixture = new RegistryFixture();
        var backup = fixture.CreateOriginalRegistration();
        fixture.Fx.SetValue(ApoEndpointRegistrationPlanner.SlotName(7),
            "{11111111-2222-3333-4444-555555555555}");
        Assert.False(ApoModeFallbackPlanner.Build(
            backup, fixture.Fx, fixture.Child).CanSwitch);
    }

    [Fact]
    public void ModifiedChildApoChain_IsNotOverwritten()
    {
        using var fixture = new RegistryFixture();
        var backup = fixture.CreateOriginalRegistration();
        fixture.Child.SetValue("PostMixChild", "{11111111-2222-3333-4444-555555555555}");
        Assert.False(ApoModeFallbackPlanner.Build(
            backup, fixture.Fx, fixture.Child).CanSwitch);
    }

    [Theory]
    [InlineData("RegisteredRestartRequired")]
    [InlineData("StageInitialized")]
    [InlineData("Prepared")]
    [InlineData("ModeSwitchRestartRequired")]
    public void UnprovenFailureOrAlreadySwitching_CannotStartFallback(string phase)
    {
        using var fixture = new RegistryFixture();
        var backup = fixture.CreateOriginalRegistration() with { Phase = phase };
        Assert.False(ApoModeFallbackPlanner.Build(
            backup, fixture.Fx, fixture.Child).CanSwitch);
    }

    [Fact]
    public void AlreadyAttemptedFallback_CannotLoop()
    {
        using var fixture = new RegistryFixture();
        var backup = fixture.CreateOriginalRegistration() with
        {
            FallbackOriginalFxValues = Array.Empty<RegistryValueBackup>()
        };
        Assert.False(ApoModeFallbackPlanner.Build(
            backup, fixture.Fx, fixture.Child).CanSwitch);
    }

    [Fact]
    public void InterruptedRollbackRefusesThirdPartyChange()
    {
        using var fixture = new RegistryFixture();
        var backup = fixture.CreateOriginalRegistration();
        var plan = ApoModeFallbackPlanner.Build(backup, fixture.Fx, fixture.Child);
        var prepared = backup with
        {
            Phase = "ModeSwitchPrepared",
            FallbackOriginalFxValues = ApoEndpointRegistrationTransaction.Capture(
                fixture.Fx, plan.Changes.Select(x => x.Name)),
            FallbackIntendedFxChanges = plan.Changes
        };
        foreach (var operation in plan.Changes)
            ApoEndpointRegistrationTransaction.Apply(fixture.Fx, operation);

        fixture.Fx.SetValue(ApoEndpointRegistrationPlanner.SlotName(6),
            "{11111111-2222-3333-4444-555555555555}");
        Assert.False(ApoModeFallbackPlanner.CanRestoreInterrupted(
            prepared, fixture.Fx, fixture.Child));
    }

    [Fact]
    public void RecoveryRejectsForgedOrDuplicateRegistryJournalEntries()
    {
        using var fixture = new RegistryFixture();
        var backup = fixture.CreateOriginalRegistration();
        var plan = ApoModeFallbackPlanner.Build(backup, fixture.Fx, fixture.Child);
        Assert.True(plan.CanSwitch);
        var captured = ApoEndpointRegistrationTransaction.Capture(
            fixture.Fx, plan.Changes.Select(x => x.Name));
        var prepared = backup with
        {
            Phase = "ModeSwitchPrepared",
            FallbackOriginalFxValues = captured,
            FallbackIntendedFxChanges = plan.Changes
        };

        var unexpectedMutation = plan.Changes
            .Select((x, i) => i == 0
                ? new ApoRegistryMutation(ApoEndpointRegistrationPlanner.SlotName(3),
                    ApoEndpointRegistrationPlanner.PostMixGuid)
                : x).ToArray();
        Assert.False(ApoModeFallbackPlanner.CanRestoreInterrupted(
            prepared with { FallbackIntendedFxChanges = unexpectedMutation },
            fixture.Fx, fixture.Child));

        var duplicates = captured
            .Select((x, i) => i == 0 ? x with { Name = captured[1].Name } : x)
            .ToArray();
        Assert.False(ApoModeFallbackPlanner.CanRestoreInterrupted(
            prepared with { FallbackOriginalFxValues = duplicates },
            fixture.Fx, fixture.Child));
    }

    private sealed class RegistryFixture : IDisposable
    {
        private readonly string _path = @"Software\AudioLimits.Core.Tests\" +
            Guid.NewGuid().ToString("N");
        private readonly RegistryKey _root;
        public RegistryKey Fx { get; }
        public RegistryKey Child { get; }

        public RegistryFixture()
        {
            _root = Registry.CurrentUser.CreateSubKey(_path, true)!;
            Fx = _root.CreateSubKey("Fx", true)!;
            Child = _root.CreateSubKey("Child", true)!;
        }

        public ApoRegistryBackup CreateOriginalRegistration()
        {
            var plan = ApoEndpointRegistrationPlanner.Build(
                new ApoFxRegistrationSnapshot(
                    true, true, false, Array.Empty<ApoFxSlotSnapshot>()),
                false);
            Assert.True(plan.CanInstall);
            var original = ApoEndpointRegistrationTransaction.Capture(
                Fx, plan.FxChanges.Select(x => x.Name));
            foreach (var change in plan.FxChanges)
                ApoEndpointRegistrationTransaction.Apply(Fx, change);
            foreach (var change in plan.ChildValues)
                ApoEndpointRegistrationTransaction.Apply(Child, change);

            return new ApoRegistryBackup(
                Guid.NewGuid().ToString("B").ToUpperInvariant(),
                DateTimeOffset.UtcNow, "StageNotInitialized", true,
                original, plan.FxChanges, plan.ChildValues);
        }

        public void Dispose()
        {
            Fx.Dispose();
            Child.Dispose();
            _root.Dispose();
            Registry.CurrentUser.DeleteSubKeyTree(_path, false);
        }
    }
}
