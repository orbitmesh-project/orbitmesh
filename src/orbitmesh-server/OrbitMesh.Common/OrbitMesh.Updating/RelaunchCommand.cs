namespace OrbitMesh.Updating;

/// <summary>Captures how the current process was launched, so OrbitMesh.Updater can relaunch an
/// equivalent process in RestartMode.Standalone after a file swap - without either side needing to know
/// whether the install is framework-dependent (launched via "dotnet some.dll") or self-contained (a
/// single apphost exe launched directly).</summary>
public static class RelaunchCommand
{
    /// <summary>Captures the executable and arguments needed to relaunch the current process,
    /// handling framework-dependent ("dotnet some.dll") and self-contained (a single apphost exe)
    /// installs the same way.</summary>
    public static (string Executable, string[] Arguments) Capture()
    {
        var commandLineArgs = Environment.GetCommandLineArgs();
        var processPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Unable to determine the current process's executable path.");

        // Framework-dependent: argv[0] is the managed dll, ProcessPath is the "dotnet" host - dotnet
        // needs that dll path as its own first argument to know what to run. Self-contained: argv[0]
        // and ProcessPath are the same apphost exe, which needs no such argument.
        var isFrameworkDependent = !string.Equals(
            Path.GetFullPath(commandLineArgs[0]), Path.GetFullPath(processPath), StringComparison.OrdinalIgnoreCase);

        return isFrameworkDependent
            ? (processPath, commandLineArgs)
            : (processPath, commandLineArgs[1..]);
    }
}
