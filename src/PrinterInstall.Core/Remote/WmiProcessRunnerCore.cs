using System.Diagnostics;
using System.Globalization;
using System.Management;

namespace PrinterInstall.Core.Remote;

internal static class WmiProcessRunnerCore
{
    public static (uint CreateReturnValue, uint? ProcessId) TryStart(
        ManagementScope scope,
        string commandLine)
    {
        scope.Connect();

        using var processClass = new ManagementClass(scope, new ManagementPath("Win32_Process"), null);
        using var inParams = processClass.GetMethodParameters("Create");
        inParams["CommandLine"] = commandLine;

        using var outParams = processClass.InvokeMethod("Create", inParams, null);
        var returnValue = Convert.ToUInt32(outParams["ReturnValue"], CultureInfo.InvariantCulture);
        if (returnValue != 0)
            return (returnValue, null);

        var pid = Convert.ToUInt32(outParams["ProcessId"], CultureInfo.InvariantCulture);
        return (0, pid);
    }

    public static RemoteProcessResult Run(
        ManagementScope scope,
        string commandLine,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var (createReturn, pid) = TryStart(scope, commandLine);
        if (createReturn != 0)
            return new RemoteProcessResult(createReturn, null, TimedOut: false);

        if (pid is null)
            return new RemoteProcessResult(1, null, TimedOut: false);

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                TryTerminate(scope, pid.Value);
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (!ProcessExists(scope, pid.Value))
                return new RemoteProcessResult(0, pid, TimedOut: false);

            Thread.Sleep(500);
        }

        TryTerminate(scope, pid.Value);
        return new RemoteProcessResult(0, pid, TimedOut: true);
    }

    public static void TryTerminate(ManagementScope scope, uint pid)
    {
        try
        {
            var query = new ObjectQuery($"SELECT * FROM Win32_Process WHERE ProcessId = {pid}");
            using var searcher = new ManagementObjectSearcher(scope, query);
            foreach (ManagementObject mo in searcher.Get())
            {
                using (mo)
                    mo.InvokeMethod("Terminate", new object[] { 1u });
            }
        }
        catch
        {
            // Best effort.
        }
    }

    public static RemoteProcessResult WaitForLocalProcessExit(
        uint pid,
        ManagementScope scope,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var proc = Process.GetProcessById((int)pid);
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                    break;
                if (proc.WaitForExit((int)Math.Min(remaining.TotalMilliseconds, 500)))
                    return new RemoteProcessResult((uint)proc.ExitCode, pid, TimedOut: false);
            }
            catch (ArgumentException)
            {
                return new RemoteProcessResult(0, pid, TimedOut: false);
            }
        }

        TryTerminate(scope, pid);
        try
        {
            using var proc = Process.GetProcessById((int)pid);
            proc.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best effort.
        }

        return new RemoteProcessResult(0, pid, TimedOut: true);
    }

    private static bool ProcessExists(ManagementScope scope, uint pid)
    {
        var query = new ObjectQuery($"SELECT ProcessId FROM Win32_Process WHERE ProcessId = {pid}");
        using var searcher = new ManagementObjectSearcher(scope, query);
        foreach (ManagementObject mo in searcher.Get())
        {
            mo.Dispose();
            return true;
        }

        return false;
    }
}
