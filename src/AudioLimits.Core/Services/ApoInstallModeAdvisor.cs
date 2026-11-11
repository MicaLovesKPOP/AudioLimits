namespace AudioLimits.Core.Services;

/// <summary>
/// Reproduces only the official Equalizer APO 1.4.2 install-mode selection
/// decision for a new endpoint. Does not install anything, authorize registry
/// writes, override driver APOs, or prove that the audio engine is processing.
/// Source: DeviceAPOInfo.cpp load() default-install-mode branch.
/// </summary>
public enum ApoInstallModeHint
{
    LegacyLfxGfx,
    StreamSfxEfx,
    StreamSfxMfx
}

public static class ApoInstallModeAdvisor
{
    public static ApoInstallModeHint? Suggest(ApoFxRegistrationSnapshot state)
    {
        if (!state.InspectionSucceeded)
            return null;

        var slots = state.EffectSlots.Select(x => x.Slot).ToHashSet();
        var legacyOnly = (slots.Contains(1) || slots.Contains(2)) &&
            !slots.Contains(5) && !slots.Contains(6) &&
            !slots.Contains(7) && !slots.Contains(13) &&
            !slots.Contains(14) && !slots.Contains(15);

        if (legacyOnly)
            return ApoInstallModeHint.LegacyLfxGfx;

        return state.IsCombinedBluetoothEndpoint
            ? ApoInstallModeHint.StreamSfxMfx
            : ApoInstallModeHint.StreamSfxEfx;
    }
}
