using AudioLimits.Core.Services;

namespace AudioLimits.Core.Tests;

public sealed class ManagedApoConfigTests
{
    [Fact]
    public void Parser_PreservesPerDeviceProcessingStageAndAttenuation()
    {
        var first = Guid.NewGuid().ToString("B").ToUpperInvariant();
        var second = Guid.NewGuid().ToString("B").ToUpperInvariant();

        var text = $"""
        # Managed by Audio Limits
        Device: {first}
        Stage: post-mix
        Channel: all
        Preamp: -12.500000 dB

        Device: {second}
        Stage: pre-mix
        Channel: all
        Preamp: -6 dB

        Device: all
        Channel: all
        Stage: post-mix
        """;

        var entries = EqualizerApoService.ParseManagedEntriesForTest(text);

        Assert.Equal(2, entries.Count);
        Assert.Equal(first, entries[0].EndpointGuid);
        Assert.Equal(ApoProcessingStage.PostMix, entries[0].Stage);
        Assert.Equal(-12.5, entries[0].AttenuationDb, 6);

        Assert.Equal(second, entries[1].EndpointGuid);
        Assert.Equal(ApoProcessingStage.PreMix, entries[1].Stage);
        Assert.Equal(-6, entries[1].AttenuationDb, 6);
    }


    [Fact]
    public void Parser_RejectsDeviceEntryWithoutExplicitStage()
    {
        var endpoint = Guid.NewGuid().ToString("B").ToUpperInvariant();
        var text = $"""
        # Managed by Audio Limits
        Device: {endpoint}
        Preamp: -10 dB
        """;

        Assert.Throws<InvalidDataException>(
            () => EqualizerApoService.ParseManagedEntriesForTest(text));
    }

    [Fact]
    public void Parser_AcceptsV03LegacyManagedFile()
    {
        var endpoint = Guid.NewGuid().ToString("B").ToUpperInvariant();
        var text = $"""
        # Managed by AudioLimits. Manual edits will be overwritten.
        # This file only attenuates selected output endpoints.

        Device: {endpoint}
        Channel: all
        Stage: post-mix
        Preamp: -23.456 dB

        Device: all
        Channel: all
        Stage: post-mix
        """;

        var entry = Assert.Single(
            EqualizerApoService.ParseManagedEntriesForTest(text));

        Assert.Equal(endpoint, entry.EndpointGuid);
        Assert.Equal(ApoProcessingStage.PostMix, entry.Stage);
        Assert.Equal(-23.456, entry.AttenuationDb, 6);
    }

    [Fact]
    public void Parser_RejectsUnexpectedFilteringCommand()
    {
        var endpoint = Guid.NewGuid().ToString("B").ToUpperInvariant();
        var text = $"""
        # Managed by Audio Limits
        Device: {endpoint}
        Stage: post-mix
        Channel: all
        Preamp: -10 dB
        Filter: ON PK Fc 1000 Hz Gain 6 dB Q 1
        """;

        Assert.Throws<InvalidDataException>(
            () => EqualizerApoService.ParseManagedEntriesForTest(text));
    }

    [Fact]
    public void ManagedIncludeDetection_FindsStandaloneLegacyOrOrphanedInclude_WithCrLf()
    {
        var text = "Preamp: -1 dB\r\nInclude: AudioLimits.txt\r\n";
        Assert.True(EqualizerApoService.HasManagedIncludeForTest(text));
    }

    [Fact]
    public void ManagedIncludeDetection_FindsStandaloneLegacyOrOrphanedInclude_WithLf()
    {
        var text = "Preamp: -1 dB\nInclude: AudioLimits.txt\n";
        Assert.True(EqualizerApoService.HasManagedIncludeForTest(text));
    }

    [Fact]
    public void ManagedReferenceCleanup_RemovesBlockAndDuplicateStandaloneInclude()
    {
        var text = """
        Preamp: -1 dB
        # >>> AudioLimits managed include >>>
        Include: AudioLimits.txt
        # <<< AudioLimits managed include <<<
        Include: AudioLimits.txt
        # Include: AudioLimits.txt
        Filter: ON HP Fc 30 Hz
        """;

        var cleaned = EqualizerApoService.RemoveManagedReferencesForTest(text);

        Assert.False(EqualizerApoService.HasManagedIncludeForTest(cleaned));
        Assert.Contains("# Include: AudioLimits.txt", cleaned);
        Assert.Contains("Filter: ON HP Fc 30 Hz", cleaned);
    }

    [Fact]
    public void ManagedReferenceStructure_AcceptsCurrentDelimitedBlock()
    {
        var text = """
        Preamp: -1 dB
        # >>> Audio Limits managed include >>>
        Device: all
        Channel: all
        Stage: pre-mix post-mix
        Include: AudioLimits.txt
        # <<< Audio Limits managed include <<<
        """;

        EqualizerApoService.ValidateManagedReferenceStructureForTest(text);
    }

    [Fact]
    public void ManagedReferenceStructure_AcceptsRc4PostMixOnlyBlockForMigration()
    {
        var text = """
        # >>> Audio Limits managed include >>>
        Device: all
        Channel: all
        Stage: post-mix
        Include: AudioLimits.txt
        # <<< Audio Limits managed include <<<
        """;

        EqualizerApoService.ValidateManagedReferenceStructureForTest(text);
    }

    [Fact]
    public void ManagedReferenceStructure_AcceptsLegacyDelimitedBlock()
    {
        var text = """
        # >>> AudioLimits managed include >>>
        Include: AudioLimits.txt
        # <<< AudioLimits managed include <<<
        """;

        EqualizerApoService.ValidateManagedReferenceStructureForTest(text);
    }

    [Fact]
    public void ManagedReferenceStructure_RejectsDuplicateIncludes()
    {
        var text = """
        # >>> Audio Limits managed include >>>
        Include: AudioLimits.txt
        # <<< Audio Limits managed include <<<
        Include: AudioLimits.txt
        """;

        Assert.Throws<InvalidDataException>(
            () => EqualizerApoService.ValidateManagedReferenceStructureForTest(text));
    }

    [Fact]
    public void ManagedReferenceStructure_RejectsOrphanedInclude()
    {
        const string text = "Include: AudioLimits.txt\r\n";

        Assert.Throws<InvalidDataException>(
            () => EqualizerApoService.ValidateManagedReferenceStructureForTest(text));
    }

    [Fact]
    public void ManagedReferenceStructure_RejectsUnexpectedCommandInsideOwnedBlock()
    {
        var text = """
        # >>> Audio Limits managed include >>>
        Device: all
        Channel: all
        Stage: post-mix
        Preamp: 12 dB
        Include: AudioLimits.txt
        # <<< Audio Limits managed include <<<
        """;

        Assert.Throws<InvalidDataException>(
            () => EqualizerApoService.ValidateManagedReferenceStructureForTest(text));
    }

    [Fact]
    public void ManagedReferenceStructure_AllowsOnlyCommentsAfterManagedBlock()
    {
        var text = """
        # >>> Audio Limits managed include >>>
        Device: all
        Channel: all
        Stage: pre-mix post-mix
        Include: AudioLimits.txt
        # <<< Audio Limits managed include <<<

        # A harmless user comment after the block.
        """;

        EqualizerApoService.ValidateManagedReferenceStructureForTest(text);
    }

    [Fact]
    public void ManagedReferenceStructure_RejectsOpenIfBeforeManagedBlock()
    {
        var text = """
        If: 0
        # >>> Audio Limits managed include >>>
        Device: all
        Channel: all
        Stage: pre-mix post-mix
        Include: AudioLimits.txt
        # <<< Audio Limits managed include <<<
        """;

        Assert.Throws<InvalidDataException>(
            () => EqualizerApoService.ValidateManagedReferenceStructureForTest(text));
    }

    [Fact]
    public void ManagedReferenceStructure_RejectsActiveCommandAfterManagedBlock()
    {
        var text = """
        # >>> Audio Limits managed include >>>
        Device: all
        Channel: all
        Stage: pre-mix post-mix
        Include: AudioLimits.txt
        # <<< Audio Limits managed include <<<
        Preamp: 6 dB
        """;

        Assert.Throws<InvalidDataException>(
            () => EqualizerApoService.ValidateManagedReferenceStructureForTest(text));
    }

    [Fact]
    public void ConditionalStructure_RejectsOpenIfAtManagedAppendPoint()
    {
        var text = """
        Preamp: -2 dB
        If: 0
        Preamp: -3 dB
        """;

        Assert.Throws<InvalidDataException>(() =>
            EqualizerApoService.ValidateConditionalStructureForTest(text));
    }

    [Fact]
    public void ConditionalStructure_AcceptsBalancedNestedIfs()
    {
        var text = """
        If: 1
        If: 0
        Preamp: -3 dB
        EndIf:
        EndIf:
        """;

        EqualizerApoService.ValidateConditionalStructureForTest(text);
    }

    [Fact]
    public void ConditionalStructure_RejectsUnmatchedEndIf()
    {
        var text = """
        Preamp: -2 dB
        EndIf:
        """;

        Assert.Throws<InvalidDataException>(() =>
            EqualizerApoService.ValidateConditionalStructureForTest(text));
    }
    [Fact]
    public void AtomicWriteRetry_RetriesTransientAccessDenied()
    {
        var calls = 0;
        var delays = new List<int>();

        var attempts = EqualizerApoService.RetryTransientFileOperationForTest(
            () =>
            {
                calls++;
                if (calls < 3)
                    throw new UnauthorizedAccessException("temporarily busy");
            },
            delays.Add);

        Assert.Equal(3, attempts);
        Assert.Equal(3, calls);
        Assert.Equal(new[] { 20, 40 }, delays);
    }

    [Fact]
    public void AtomicWriteRetry_RetriesTransientIoFailure()
    {
        var calls = 0;

        var attempts = EqualizerApoService.RetryTransientFileOperationForTest(
            () =>
            {
                calls++;
                if (calls == 1)
                    throw new IOException("temporarily busy");
            });

        Assert.Equal(2, attempts);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void AtomicWriteRetry_DoesNotRetryNonTransientFailure()
    {
        var calls = 0;

        Assert.Throws<InvalidDataException>(() =>
            EqualizerApoService.RetryTransientFileOperationForTest(() =>
            {
                calls++;
                throw new InvalidDataException("bad data");
            }));

        Assert.Equal(1, calls);
    }

    [Fact]
    public void AtomicWriteRetry_StopsAfterBoundedAttempts()
    {
        var calls = 0;
        var delays = new List<int>();

        Assert.Throws<UnauthorizedAccessException>(() =>
            EqualizerApoService.RetryTransientFileOperationForTest(
                () =>
                {
                    calls++;
                    throw new UnauthorizedAccessException("still denied");
                },
                delays.Add));

        Assert.Equal(6, calls);
        Assert.Equal(new[] { 20, 40, 80, 160, 160 }, delays);
    }

}
