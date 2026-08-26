using System.Net.NetworkInformation;

namespace PrinterInstall.App.Services;

/// <summary>
/// Detecta automaticamente o domínio da máquina local utilizando propriedades de rede do Windows.
/// </summary>
public sealed class DomainDetector : IDomainDetector
{
    public string? DetectCurrentDomain()
    {
        try
        {
            // 1. Tenta obter o sufixo DNS de domínio primário da máquina
            var ipProps = IPGlobalProperties.GetIPGlobalProperties();
            var dnsDomain = ipProps.DomainName;
            if (!string.IsNullOrWhiteSpace(dnsDomain))
                return dnsDomain.Trim();
        }
        catch
        {
            // Ignora e tenta método alternativo
        }

        try
        {
            // 2. Tenta verificar se o UserDomainName difere do nome da máquina e de nomes genéricos
            var userDomain = Environment.UserDomainName;
            var machineName = Environment.MachineName;

            if (!string.IsNullOrWhiteSpace(userDomain) &&
                !userDomain.Equals(".", StringComparison.OrdinalIgnoreCase) &&
                !userDomain.Equals(machineName, StringComparison.OrdinalIgnoreCase) &&
                !userDomain.Equals("WORKGROUP", StringComparison.OrdinalIgnoreCase) &&
                !userDomain.Equals("BUILTIN", StringComparison.OrdinalIgnoreCase))
            {
                return userDomain.Trim();
            }
        }
        catch
        {
            // Ignora falha de consulta de ambiente
        }

        return null;
    }
}
