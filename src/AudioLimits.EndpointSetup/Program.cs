using AudioLimits.Core.Services;

namespace AudioLimits.EndpointSetup;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        AppLog.Initialize();
        // No arbitrary registry paths, scripts, or command execution accepted.
        // The only writable target is a valid active playback endpoint GUID.
        if (args.Length != 2 || !Guid.TryParse(args[1], out var guid))
            return 10;

        try
        {
            var endpoint = guid.ToString("B").ToUpperInvariant();
            var apo = new EqualizerApoService();

            if (args[0] == "--inspect")
            {
                var state = ApoEndpointSetupInspector.ReadRegistration(endpoint);
                var plan = ApoEndpointRegistrationPlanner.Build(
                    state, apo.IsActiveForEndpoint(endpoint));
                return (int)plan.Readiness + 20;
            }

            if (args[0] != "--install" && args[0] != "--restore")
                return 10;

            var transaction = new ApoEndpointRegistrationTransaction(apo);
            var result = args[0] == "--restore"
                ? transaction.RestoreInterrupted(endpoint)
                : transaction.Install(endpoint);
            AppLog.Info($"Endpoint setup {endpoint}: {result}");
            return result switch
            {
                ApoEndpointInstallResult.RegisteredRestartRequired => 0,
                ApoEndpointInstallResult.RolledBack => 6,
                ApoEndpointInstallResult.AlreadyInstalled => 1,
                ApoEndpointInstallResult.UnsupportedEndpoint => 2,
                ApoEndpointInstallResult.NoAdministratorPrivileges => 3,
                ApoEndpointInstallResult.FailedAndRolledBack => 4,
                ApoEndpointInstallResult.RollbackIncomplete => 5,
                _ => 12
            };
        }
        catch (Exception ex)
        {
            AppLog.Error("Endpoint setup failed before registration", ex);
            return 12;
        }
    }
}
