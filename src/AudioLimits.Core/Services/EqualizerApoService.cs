using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AudioLimits.Core.Models;
using Microsoft.Win32;

namespace AudioLimits.Core.Services;

public enum ApoProcessingStage
{
    None,
    PreMix,
    PostMix
}

public sealed record ManagedApoEntry(
    string EndpointGuid,
    ApoProcessingStage Stage,
    double AttenuationDb);

public sealed class EqualizerApoService
{
    private const string ManagedFileName = "AudioLimits.txt";
    private const string StartMarker = "# >>> Audio Limits managed include >>>";
    private const string EndMarker = "# <<< Audio Limits managed include <<<";
    private const string LegacyStartMarker = "# >>> AudioLimits managed include >>>";
    private const string LegacyEndMarker = "# <<< AudioLimits managed include <<<";
    private const string ManagedHeader = "Managed by Audio Limits";
    private const string LegacyManagedHeader = "Managed by AudioLimits";

    // Verified against Equalizer APO 1.4.2/current SourceForge main on 2026-08-19.
    // DeviceAPOInfo.cpp treats these direct FxProperties slots as installed stages:
    // pre-mix: LFX (1), SFX (5); post-mix: GFX (2), MFX (6), EFX (7).
    // The multi-effect slots 13/14/15 participate in install-mode selection but are
    // not used by DeviceAPOInfo::load() as direct Equalizer APO registrations.
    private static readonly string[] PreMixEffectProperties =
    {
        "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},1",
        "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},5"
    };

    private static readonly string[] PostMixEffectProperties =
    {
        "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},2",
        "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},6",
        "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},7"
    };

    private const string DisableEnhancementsValue =
        "{1da5d803-d492-4edd-8c23-e0c0ffee7f0e},5";

    public const string DownloadUrl = "https://sourceforge.net/projects/equalizerapo/";

    public string? ConfigDirectory => FindConfigDirectory();

    public bool IsInstalled =>
        ConfigDirectory is { } dir && File.Exists(Path.Combine(dir, "config.txt"));

    public string? DeviceSelectorPath
    {
        get
        {
            var configDir = ConfigDirectory;
            if (configDir is null)
                return null;

            var installDir = Directory.GetParent(configDir)?.FullName;
            if (installDir is null)
                return null;

            foreach (var fileName in new[] { "DeviceSelector.exe", "Configurator.exe" })
            {
                var path = Path.Combine(installDir, fileName);
                if (File.Exists(path))
                    return path;
            }

            return null;
        }
    }

    public void OpenDeviceSelector()
    {
        var path = DeviceSelectorPath
                   ?? throw new FileNotFoundException(
                       "Equalizer APO's Device Selector was not found. Reinstall or repair Equalizer APO.");

        var installDir = Path.GetDirectoryName(path)
                         ?? throw new InvalidOperationException(
                             "Could not determine Equalizer APO's installation folder.");

        // Launch the same way Explorer/Start-menu shortcuts launch a normal GUI app.
        // v0.3 started Device Selector as a child process from Audio Limits' working
        // context and reproduced a Qt platform-plugin initialization failure. Using
        // ShellExecute plus Equalizer APO's own working directory avoids imposing any
        // Audio Limits-specific Qt environment on Device Selector.
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            WorkingDirectory = installDir,
            UseShellExecute = true
        });
    }

    public void OpenDownloadPage() =>
        Process.Start(new ProcessStartInfo(DownloadUrl) { UseShellExecute = true });

    public ApoProcessingStage GetPreferredProcessingStage(string endpointGuid)
    {
        var availability = GetStageAvailability(endpointGuid);
        // Prefer post-mix when available: Equalizer APO's documentation recommends
        // it for output processing. Pre-mix remains a valid fallback when Device
        // Selector troubleshooting installs only that stage.
        if (availability.PostMix)
            return ApoProcessingStage.PostMix;
        if (availability.PreMix)
            return ApoProcessingStage.PreMix;
        return ApoProcessingStage.None;
    }

    public bool IsStageActiveForEndpoint(string endpointGuid, ApoProcessingStage stage)
    {
        var availability = GetStageAvailability(endpointGuid);
        return stage switch
        {
            ApoProcessingStage.PreMix => availability.PreMix,
            ApoProcessingStage.PostMix => availability.PostMix,
            _ => false
        };
    }

    public bool IsActiveForEndpoint(string endpointGuid)
    {
        var availability = GetStageAvailability(endpointGuid);
        return availability.PreMix || availability.PostMix;
    }

    private (bool PreMix, bool PostMix) GetStageAvailability(string endpointGuid)
    {
        endpointGuid = NormalizeGuid(endpointGuid);
        var preMix = false;
        var postMix = false;

        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var fx = hklm.OpenSubKey(
                    $@"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render\{endpointGuid}\FxProperties");

                if (fx is null)
                    continue;

                if (fx.GetValue(DisableEnhancementsValue) is int disabled && disabled != 0)
                    return (false, false);

                postMix |= PostMixEffectProperties.Any(
                    name => PropertyContainsEqualizerApo(fx, name, view));
                preMix |= PreMixEffectProperties.Any(
                    name => PropertyContainsEqualizerApo(fx, name, view));
            }
            catch (Exception ex)
            {
                AppLog.Error($"Could not inspect Equalizer APO registration for {endpointGuid}", ex);
            }
        }

        return (preMix, postMix);
    }

    public bool HasManagedConfiguration
    {
        get
        {
            var configDir = ConfigDirectory;
            if (configDir is null)
                return false;

            var mainConfig = Path.Combine(configDir, "config.txt");
            if (!File.Exists(mainConfig))
                return false;

            try
            {
                var main = File.ReadAllText(mainConfig);
                // Presence means ownership must be validated, not that the referenced
                // managed file is already trustworthy or even present. An orphaned
                // include therefore flows into strict validation and fails closed.
                return HasManagedInclude(main);
            }
            catch
            {
                return false;
            }
        }
    }


    public bool TryReadManagedEntries(
        out IReadOnlyList<ManagedApoEntry> entries,
        out string? error)
    {
        entries = Array.Empty<ManagedApoEntry>();
        error = null;

        if (!HasManagedConfiguration)
            return true;

        var configDir = ConfigDirectory;
        if (configDir is null)
            return true;

        var mainConfig = Path.Combine(configDir, "config.txt");
        var managedConfig = Path.Combine(configDir, ManagedFileName);
        try
        {
            var main = File.ReadAllText(mainConfig);
            ValidateManagedReferenceStructure(main);

            if (!File.Exists(managedConfig))
                throw new FileNotFoundException(
                    "Audio Limits' managed Equalizer APO file is missing even though config.txt still includes it.",
                    managedConfig);

            entries = ParseManagedEntries(File.ReadAllText(managedConfig));
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            AppLog.Error("Could not validate Audio Limits' managed Equalizer APO file", ex);
            return false;
        }
    }

    public ManagedApoEntry? TryGetManagedEntry(string endpointGuid)
    {
        var wanted = NormalizeGuid(endpointGuid);
        return TryReadManagedEntries(out var entries, out _)
            ? entries.FirstOrDefault(x =>
                string.Equals(
                    x.EndpointGuid,
                    wanted,
                    StringComparison.OrdinalIgnoreCase))
            : null;
    }

    public bool IsManagedLimitConfigured(DeviceLimit limit)
    {
        if (!IsInstalled)
            return false;

        var preferred = GetPreferredProcessingStage(limit.EndpointGuid);
        if (preferred == ApoProcessingStage.None)
            return false;

        var entry = TryGetManagedEntry(limit.EndpointGuid);
        return entry is not null &&
               IsStageActiveForEndpoint(limit.EndpointGuid, entry.Stage) &&
               Math.Abs(entry.AttenuationDb - limit.AttenuationDb) <= 0.002;
    }

    public void ValidateCanChangeManagedConfiguration()
    {
        var configDir = ConfigDirectory
                        ?? throw new InvalidOperationException(
                            "Equalizer APO is not installed. Install it before changing an audio limit.");

        var mainConfig = Path.Combine(configDir, "config.txt");
        if (!File.Exists(mainConfig))
            throw new FileNotFoundException("Equalizer APO's config.txt was not found.", mainConfig);

        var main = File.ReadAllText(mainConfig);
        if (HasManagedInclude(main))
        {
            ValidateManagedReferenceStructure(main);

            var managedConfig = Path.Combine(configDir, ManagedFileName);
            if (!File.Exists(managedConfig))
                throw new FileNotFoundException(
                    "Audio Limits' managed Equalizer APO file is missing even though config.txt still includes it.",
                    managedConfig);

            _ = ParseManagedEntries(File.ReadAllText(managedConfig));
            return;
        }

        // Before a first managed include is created, prove that it will not land
        // inside an open Equalizer APO If block. Otherwise the Include could be
        // skipped while Audio Limits assumes attenuation was loaded.
        ValidateConditionalStructureAtAppendPoint(
            RemoveManagedReferences(main).TrimEnd());
    }

    public void ApplyLimits(IEnumerable<DeviceLimit> limits)
    {
        var configDir = ConfigDirectory
                        ?? throw new InvalidOperationException(
                            "Equalizer APO is not installed. Install it before applying an audio limit.");

        var mainConfig = Path.Combine(configDir, "config.txt");
        if (!File.Exists(mainConfig))
            throw new FileNotFoundException("Equalizer APO's config.txt was not found.", mainConfig);

        var limitList = limits
            .Where(x => x.AttenuationDb < -0.001)
            .OrderBy(x => x.FriendlyName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        // Validate any currently active managed include before changing it. This is
        // especially important for removal: duplicate includes can multiply the old
        // attenuation, so normalizing/removing them without first accounting for that
        // state could make the endpoint suddenly louder.
        IReadOnlyList<ManagedApoEntry> existingEntries = Array.Empty<ManagedApoEntry>();
        if (HasManagedConfiguration &&
            !TryReadManagedEntries(out existingEntries, out var managedError))
            throw new InvalidOperationException(
                $"Audio Limits could not safely change its existing Equalizer APO configuration: {managedError}");

        if (limitList.Count > 0 && !HasManagedConfiguration)
            ValidateConditionalStructureAtAppendPoint(
                RemoveManagedReferences(File.ReadAllText(mainConfig)).TrimEnd());

        if (limitList.Count == 0)
        {
            RemoveManagedConfiguration(mainConfig, Path.Combine(configDir, ManagedFileName));
            return;
        }

        Directory.CreateDirectory(configDir);
        var managedConfig = Path.Combine(configDir, ManagedFileName);
        WriteManagedConfig(managedConfig, limitList, existingEntries);
        EnsureManagedIncludeAtEnd(mainConfig);
        VerifyManagedConfiguration(mainConfig, managedConfig);
    }

    private void WriteManagedConfig(
        string path,
        IReadOnlyList<DeviceLimit> limits,
        IReadOnlyList<ManagedApoEntry> existingEntries)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Managed by Audio Limits. Manual edits will be overwritten.");
        sb.AppendLine("# Only fixed per-device attenuation is defined here.");
        sb.AppendLine();

        foreach (var limit in limits)
        {
            // Once a managed attenuation is active on a valid stage, keep it on that
            // stage. Moving the same gain from pre-mix to post-mix while both APOs are
            // running would require cross-APO synchronization that Equalizer APO does
            // not expose. Preserving the active stage avoids a possible reload gap.
            var existingStage = existingEntries
                .FirstOrDefault(x =>
                    string.Equals(
                        x.EndpointGuid,
                        NormalizeGuid(limit.EndpointGuid),
                        StringComparison.OrdinalIgnoreCase) &&
                    IsStageActiveForEndpoint(limit.EndpointGuid, x.Stage))
                ?.Stage ?? ApoProcessingStage.None;

            var stage = existingStage != ApoProcessingStage.None
                ? existingStage
                : GetPreferredProcessingStage(limit.EndpointGuid);

            if (stage == ApoProcessingStage.None)
            {
                // Keep saved intent deterministic while the endpoint is not configured.
                // On the next startup/repair after Device Selector enables the endpoint,
                // Audio Limits will rewrite this to the actually available stage.
                stage = ApoProcessingStage.PostMix;
            }

            sb.AppendLine($"# {SanitizeComment(limit.FriendlyName)} — limit {limit.LimitPercent}%");
            sb.AppendLine($"Device: {NormalizeGuid(limit.EndpointGuid)}");
            sb.AppendLine($"Stage: {StageText(stage)}");
            sb.AppendLine("Channel: all");
            sb.AppendLine($"Preamp: {limit.AttenuationDb.ToString("0.000000", CultureInfo.InvariantCulture)} dB");
            sb.AppendLine();
        }

        // Do not leak Audio Limits' last device/stage selector into configuration that
        // a user may append later.
        sb.AppendLine("Device: all");
        sb.AppendLine("Channel: all");
        sb.AppendLine("Stage: post-mix");

        AtomicWrite(path, sb.ToString());
    }

    internal static IReadOnlyList<ManagedApoEntry> ParseManagedEntriesForTest(string text) =>
        ParseManagedEntries(text);


    private static IReadOnlyList<ManagedApoEntry> ParseManagedEntries(string text)
    {
        if (!text.Contains(ManagedHeader, StringComparison.Ordinal) &&
            !text.Contains(LegacyManagedHeader, StringComparison.Ordinal))
            throw new InvalidDataException(
                "The managed file does not contain a recognized Audio Limits ownership header.");

        var result = new List<ManagedApoEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? currentDevice = null;
        var currentStage = ApoProcessingStage.PostMix;
        var currentHasPreamp = false;
        var currentHasExplicitStage = false;

        void FinishCurrentDevice()
        {
            if (currentDevice is not null && !currentHasPreamp)
                throw new InvalidDataException(
                    $"Managed Equalizer APO entry for {currentDevice} has no valid Preamp line.");
        }

        foreach (var rawLine in text.Split(
                     new[] { "\r\n", "\n" },
                     StringSplitOptions.None))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            if (line.StartsWith("Device:", StringComparison.OrdinalIgnoreCase))
            {
                FinishCurrentDevice();

                var value = line["Device:".Length..].Trim();
                if (string.Equals(value, "all", StringComparison.OrdinalIgnoreCase))
                {
                    currentDevice = null;
                    currentHasPreamp = false;
                    currentHasExplicitStage = false;
                    continue;
                }

                if (!Guid.TryParse(value, out var guid))
                    throw new InvalidDataException(
                        $"Managed Equalizer APO Device value is invalid: {value}");

                currentDevice = guid.ToString("B").ToUpperInvariant();
                currentHasPreamp = false;
                currentHasExplicitStage = false;
                continue;
            }

            if (line.StartsWith("Stage:", StringComparison.OrdinalIgnoreCase))
            {
                currentStage = ParseStageStrict(line["Stage:".Length..].Trim());
                if (currentDevice is not null)
                    currentHasExplicitStage = true;
                continue;
            }

            if (line.StartsWith("Preamp:", StringComparison.OrdinalIgnoreCase))
            {
                if (currentDevice is null)
                    throw new InvalidDataException(
                        "Managed Equalizer APO file contains a Preamp without a device.");
                if (!currentHasExplicitStage)
                    throw new InvalidDataException(
                        $"Managed Equalizer APO entry for {currentDevice} has no explicit Stage line.");

                var value = line["Preamp:".Length..].Trim();
                var match = Regex.Match(
                    value,
                    @"^([+-]?(?:\d+(?:\.\d*)?|\.\d+))\s*dB$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

                if (!match.Success ||
                    !double.TryParse(
                        match.Groups[1].Value,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var attenuation) ||
                    !double.IsFinite(attenuation) ||
                    attenuation > 0.0001 ||
                    attenuation < -200.0)
                {
                    throw new InvalidDataException(
                        $"Managed Equalizer APO Preamp value is invalid: {value}");
                }

                if (!seen.Add(currentDevice))
                    throw new InvalidDataException(
                        $"Managed Equalizer APO file contains duplicate device {currentDevice}.");

                result.Add(new ManagedApoEntry(
                    currentDevice,
                    currentStage,
                    attenuation));
                currentHasPreamp = true;
                continue;
            }

            if (line.StartsWith("Channel:", StringComparison.OrdinalIgnoreCase))
            {
                var value = line["Channel:".Length..].Trim();
                if (!string.Equals(value, "all", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(
                        $"Managed Equalizer APO Channel value is invalid: {value}");
                continue;
            }

            throw new InvalidDataException(
                $"Managed Equalizer APO file contains an unexpected command: {line}");
        }

        FinishCurrentDevice();
        return result;
    }

    private static ApoProcessingStage ParseStageStrict(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "pre-mix" => ApoProcessingStage.PreMix,
            "post-mix" => ApoProcessingStage.PostMix,
            _ => throw new InvalidDataException(
                $"Managed Equalizer APO Stage value is invalid: {value}")
        };

    private static string StageText(ApoProcessingStage stage) =>
        stage == ApoProcessingStage.PreMix ? "pre-mix" : "post-mix";

    private static void EnsureManagedIncludeAtEnd(string mainConfig)
    {
        var original = File.ReadAllText(mainConfig);
        var backup = mainConfig + ".audiolimits.bak";
        if (!File.Exists(backup))
            File.Copy(mainConfig, backup, overwrite: false);

        var cleaned = RemoveManagedReferences(original).TrimEnd();
        ValidateConditionalStructureAtAppendPoint(cleaned);

        var block =
            $"{StartMarker}{Environment.NewLine}" +
            $"Device: all{Environment.NewLine}" +
            $"Channel: all{Environment.NewLine}" +
            // Make the owned include block stage-neutral for render processing.
            // Equalizer APO 1.4.2 accepts multiple stage names on one Stage command.
            // Entries inside AudioLimits.txt then select their own exact stage.
            $"Stage: pre-mix post-mix{Environment.NewLine}" +
            $"Include: {ManagedFileName}{Environment.NewLine}" +
            $"{EndMarker}";

        var updated = cleaned + Environment.NewLine + Environment.NewLine + block + Environment.NewLine;
        if (!string.Equals(original, updated, StringComparison.Ordinal))
            AtomicWrite(mainConfig, updated);
    }

    private static void RemoveManagedConfiguration(string mainConfig, string managedConfig)
    {
        var original = File.ReadAllText(mainConfig);
        var updated = RemoveManagedReferences(original).TrimEnd() + Environment.NewLine;
        if (!string.Equals(original, updated, StringComparison.Ordinal))
            AtomicWrite(mainConfig, updated);

        try
        {
            if (File.Exists(managedConfig))
                File.Delete(managedConfig);
        }
        catch (Exception ex)
        {
            AppLog.Error("Could not delete the Audio Limits Equalizer APO file", ex);
        }
    }

    private static string RemoveManagedReferences(string text)
    {
        foreach (var markers in new[]
        {
            (StartMarker, EndMarker),
            (LegacyStartMarker, LegacyEndMarker)
        })
        {
            var pattern = Regex.Escape(markers.Item1) + ".*?" +
                          Regex.Escape(markers.Item2) + @"\s*";
            text = Regex.Replace(text, pattern, "", RegexOptions.Singleline);
        }

        // Also remove orphaned/duplicated standalone includes from older or manually
        // edited configs. AudioLimits.txt is an application-owned filename, and a
        // second include would otherwise sum the attenuation twice. Commented lines
        // are intentionally not matched.
        text = Regex.Replace(
            text,
            @"^[ \t]*Include:[ \t]*AudioLimits\.txt[ \t]*(?:\r?\n|$)",
            "",
            RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant);

        return text;
    }

    private static MatchCollection ManagedIncludeMatches(string main) =>
        Regex.Matches(
            main,
            // In .NET multiline mode, $ matches before \n but not before the \r in
            // a CRLF line ending. Accept an optional carriage return explicitly so
            // active Include lines are detected identically for CRLF and LF files.
            @"^[ \t]*Include:[ \t]*AudioLimits\.txt[ \t]*\r?$",
            RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static bool HasManagedInclude(string main) =>
        ManagedIncludeMatches(main).Count > 0;

    private static void ValidateManagedReferenceStructure(string main)
    {
        var includes = ManagedIncludeMatches(main);
        if (includes.Count != 1)
            throw new InvalidDataException(
                includes.Count == 0
                    ? "Equalizer APO config.txt no longer contains Audio Limits' managed include."
                    : "Equalizer APO config.txt contains Audio Limits.txt more than once. Audio Limits left processing unchanged because duplicate includes can multiply attenuation.");

        var markerStartCount = 0;
        var markerEndCount = 0;
        var containingBlockCount = 0;
        string? containingBlock = null;
        var containingBlockEnd = -1;

        foreach (var markers in new[]
        {
            (StartMarker, EndMarker),
            (LegacyStartMarker, LegacyEndMarker)
        })
        {
            markerStartCount += CountOccurrences(main, markers.Item1);
            markerEndCount += CountOccurrences(main, markers.Item2);

            var searchFrom = 0;
            while (true)
            {
                var start = main.IndexOf(markers.Item1, searchFrom, StringComparison.Ordinal);
                if (start < 0)
                    break;

                var end = main.IndexOf(
                    markers.Item2,
                    start + markers.Item1.Length,
                    StringComparison.Ordinal);
                if (end < 0)
                    break;

                var blockEnd = end + markers.Item2.Length;
                var block = main[start..blockEnd];
                if (HasManagedInclude(block))
                {
                    containingBlockCount++;
                    containingBlock = block;
                    containingBlockEnd = blockEnd;
                }

                searchFrom = blockEnd;
            }
        }

        if (markerStartCount != 1 || markerEndCount != 1 || containingBlockCount != 1 ||
            containingBlock is null || containingBlockEnd < 0)
            throw new InvalidDataException(
                "Equalizer APO config.txt contains an orphaned or damaged Audio Limits include. Audio Limits left processing unchanged because its effective attenuation cannot be inferred safely.");

        var managedBlockStart = main.IndexOf(containingBlock, StringComparison.Ordinal);
        if (managedBlockStart < 0)
            throw new InvalidDataException(
                "Equalizer APO config.txt contains an Audio Limits block that could not be located safely.");

        ValidateConditionalStructureAtAppendPoint(main[..managedBlockStart]);
        ValidateManagedBlockCommands(containingBlock);

        // The managed attenuation is intended to be the final Equalizer APO gain in
        // config.txt. If another active command follows it, that command could raise
        // the signal again (or otherwise invalidate the meaning of the displayed cap).
        // Comments and whitespace are harmless; everything else is treated as unknown.
        var tailCommands = main[containingBlockEnd..]
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToArray();

        if (tailCommands.Length != 0)
            throw new InvalidDataException(
                "Equalizer APO config.txt contains active commands after Audio Limits' managed include. Move the Audio Limits block back to the end of config.txt before changing limits.");
    }

    private static void ValidateConditionalStructureAtAppendPoint(string text)
    {
        // Equalizer APO evaluates If/Else/EndIf before Include. An unclosed If before
        // Audio Limits' block can therefore suppress the managed Include on some or all
        // endpoints. Raising the Windows endpoint while assuming attenuation loaded would
        // be unsafe, so refuse to append/change limits when the outer conditional
        // structure is not balanced. This check is deliberately conservative: malformed
        // conditionals hidden behind another selector are rejected rather than guessed.
        var depth = 0;
        foreach (var rawLine in text.Split(
                     new[] { "\r\n", "\n" },
                     StringSplitOptions.None))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            var colon = line.IndexOf(':');
            if (colon < 0)
                continue;

            var command = line[..colon].Trim();
            if (string.Equals(command, "If", StringComparison.OrdinalIgnoreCase))
            {
                depth++;
            }
            else if (string.Equals(command, "EndIf", StringComparison.OrdinalIgnoreCase))
            {
                if (depth == 0)
                    throw new InvalidDataException(
                        "Equalizer APO config.txt contains an EndIf without a matching If before Audio Limits' managed block. Fix the Equalizer APO configuration before changing limits.");
                depth--;
            }
        }

        if (depth != 0)
            throw new InvalidDataException(
                "Equalizer APO config.txt contains an If block that is still open where Audio Limits must add its managed include. Close the If block with EndIf before changing limits.");
    }

    internal static void ValidateConditionalStructureForTest(string text) =>
        ValidateConditionalStructureAtAppendPoint(text);

    private static void ValidateManagedBlockCommands(string block)
    {
        var commands = block
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToArray();

        var legacy = new[]
        {
            "Include: AudioLimits.txt"
        };
        var current = new[]
        {
            "Device: all",
            "Channel: all",
            "Stage: pre-mix post-mix",
            "Include: AudioLimits.txt"
        };
        // An earlier pre-release candidate briefly emitted a post-mix-only outer
        // Stage selector. Accept it so startup can migrate the block safely; the
        // next ApplyLimits call always rewrites it to the current all-render-stages form.
        var legacyPreReleasePostMixBlock = new[]
        {
            "Device: all",
            "Channel: all",
            "Stage: post-mix",
            "Include: AudioLimits.txt"
        };

        static bool SequenceEquals(string[] actual, string[] expected) =>
            actual.Length == expected.Length &&
            actual.Zip(expected).All(pair =>
                string.Equals(pair.First, pair.Second, StringComparison.OrdinalIgnoreCase));

        if (!SequenceEquals(commands, legacy) &&
            !SequenceEquals(commands, current) &&
            !SequenceEquals(commands, legacyPreReleasePostMixBlock))
            throw new InvalidDataException(
                "Equalizer APO's Audio Limits include block contains unexpected commands. Audio Limits left processing unchanged rather than guessing their effect.");
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }

    internal static bool HasManagedIncludeForTest(string text) =>
        HasManagedInclude(text);

    internal static void ValidateManagedReferenceStructureForTest(string text) =>
        ValidateManagedReferenceStructure(text);

    internal static string RemoveManagedReferencesForTest(string text) =>
        RemoveManagedReferences(text);

    private static void VerifyManagedConfiguration(string mainConfig, string managedConfig)
    {
        var main = File.ReadAllText(mainConfig);
        ValidateManagedReferenceStructure(main);

        if (!File.Exists(managedConfig))
            throw new IOException(
                "Audio Limits could not verify its Equalizer APO configuration file.");

        // Parse the file we just wrote rather than trusting only a header string.
        // This keeps the verification path identical to startup reconciliation.
        _ = ParseManagedEntries(File.ReadAllText(managedConfig));
    }

    private static void AtomicWrite(string path, string content)
    {
        var directory = Path.GetDirectoryName(path)
                        ?? throw new InvalidOperationException(
                            $"Could not determine the directory for {path}.");
        Directory.CreateDirectory(directory);

        var temp = Path.Combine(
            directory,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
        {
            writer.Write(content);
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }

        try
        {
            RetryTransientFileOperation(
                () => File.Move(temp, path, true),
                delay => Thread.Sleep(delay),
                onRetry: attempt => AppLog.Warn(
                    $"Equalizer APO configuration was temporarily busy; retrying atomic write ({attempt})."));
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }

    private static int RetryTransientFileOperation(
        Action operation,
        Action<int> delay,
        Action<int>? onRetry = null)
    {
        // Equalizer APO watches and reloads its configuration files. On Windows,
        // there can be a very short interval where the existing destination file
        // is open without delete sharing. An otherwise valid atomic replacement
        // then surfaces as UnauthorizedAccessException (or occasionally IOException).
        // Keep atomic replacement semantics, but tolerate that brief contention.
        // Persistent ACL/permission failures still escape after a bounded retry window.
        const int maxAttempts = 6;
        var delayMs = 20;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                operation();
                return attempt;
            }
            catch (Exception ex) when (
                IsTransientAtomicWriteException(ex) &&
                attempt < maxAttempts)
            {
                onRetry?.Invoke(attempt);
                delay(delayMs);
                delayMs = Math.Min(delayMs * 2, 160);
            }
        }
    }

    private static bool IsTransientAtomicWriteException(Exception ex) =>
        ex is UnauthorizedAccessException or IOException;

    internal static int RetryTransientFileOperationForTest(
        Action operation,
        Action<int>? delay = null) =>
        RetryTransientFileOperation(operation, delay ?? (_ => { }));

    private static bool PropertyContainsEqualizerApo(
        RegistryKey fx,
        string valueName,
        RegistryView view)
    {
        var value = fx.GetValue(valueName);
        return value switch
        {
            string clsid => IsEqualizerApoClass(clsid, view),
            string[] clsids => clsids.Any(x => IsEqualizerApoClass(x, view)),
            _ => false
        };
    }

    private static bool IsEqualizerApoClass(string? clsid, RegistryView view)
    {
        if (!Guid.TryParse(clsid, out var parsed))
            return false;

        var normalized = parsed.ToString("B").ToUpperInvariant();
        try
        {
            using var hkcr = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, view);
            using var clsidKey = hkcr.OpenSubKey($@"CLSID\{normalized}");

            var className = clsidKey?.GetValue(null) as string;
            if (className?.Contains(
                    "EqualizerAPO",
                    StringComparison.OrdinalIgnoreCase) == true ||
                className?.Contains(
                    "Equalizer APO",
                    StringComparison.OrdinalIgnoreCase) == true)
                return true;

            using var server = clsidKey?.OpenSubKey("InprocServer32");
            var dll = server?.GetValue(null) as string;
            return dll is not null &&
                   string.Equals(
                       Path.GetFileName(dll.Trim('"')),
                       "EqualizerAPO.dll",
                       StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string? FindConfigDirectory()
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = hklm.OpenSubKey(@"SOFTWARE\EqualizerAPO");
                if (key?.GetValue("ConfigPath") is string configPath &&
                    Directory.Exists(configPath))
                    return configPath;
            }
            catch
            {
                // Try the other registry view and then the conventional path.
            }
        }

        var fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "EqualizerAPO",
            "config");

        return Directory.Exists(fallback) ? fallback : null;
    }

    private static string NormalizeGuid(string guid)
    {
        if (!Guid.TryParse(guid, out var parsed))
            throw new InvalidOperationException($"Invalid audio endpoint GUID: {guid}");

        return parsed.ToString("B").ToUpperInvariant();
    }

    private static string SanitizeComment(string text) =>
        text.Replace("\r", " ").Replace("\n", " ");
}
