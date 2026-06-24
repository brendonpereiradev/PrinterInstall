using System.Management;

namespace PrinterInstall.Core.Remote;

public static class AccessDeniedDetector
{
    private const int HResultAccessDenied = unchecked((int)0x80070005);

    public static bool IsAccessDenied(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is UnauthorizedAccessException)
                return true;

            if (current is ManagementException mgmt &&
                mgmt.ErrorCode == ManagementStatus.AccessDenied)
                return true;

            if (current.HResult == HResultAccessDenied)
                return true;

            var message = current.Message;
            if (message.Contains("Access is denied", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("Access Denied", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("Acesso negado", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static bool IsWmiAccessDeniedReturnValue(uint returnValue) => returnValue == 5;
}
