using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using PrinterInstall.App.Models;

namespace PrinterInstall.App.Services;

/// <summary>
/// Persistência de configurações do usuário em arquivo JSON local no %LocalAppData%.
/// </summary>
public sealed class AppSettingsStore : IAppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _filePath;
    private readonly string _defaultDomainName;

    public AppSettingsStore(IConfiguration? configuration = null)
        : this(DefaultFilePath(), configuration?["DomainName"] ?? "preventsenior.local")
    {
    }

    public AppSettingsStore(string filePath, string defaultDomainName = "preventsenior.local")
    {
        _filePath = filePath;
        _defaultDomainName = string.IsNullOrWhiteSpace(defaultDomainName) ? "preventsenior.local" : defaultDomainName.Trim();
    }

    public AppSettings Load()
    {
        if (!File.Exists(_filePath))
            return new AppSettings(_defaultDomainName);

        try
        {
            var json = File.ReadAllText(_filePath);
            var dto = JsonSerializer.Deserialize<AppSettingsDto>(json, JsonOptions);
            if (dto is null || string.IsNullOrWhiteSpace(dto.DomainName))
            {
                TryDeleteFile();
                return new AppSettings(_defaultDomainName);
            }

            var domain = dto.DomainName.Trim();
            var ldapHost = string.IsNullOrWhiteSpace(dto.LdapHost) ? null : dto.LdapHost.Trim();
            return new AppSettings(domain, ldapHost);
        }
        catch (JsonException)
        {
            TryDeleteFile();
            return new AppSettings(_defaultDomainName);
        }
        catch (IOException)
        {
            return new AppSettings(_defaultDomainName);
        }
    }

    public void Save(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.DomainName))
            return;

        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var dto = new AppSettingsDto
            {
                DomainName = settings.DomainName.Trim(),
                LdapHost = string.IsNullOrWhiteSpace(settings.LdapHost) ? null : settings.LdapHost.Trim()
            };

            var json = JsonSerializer.Serialize(dto, JsonOptions);
            File.WriteAllText(_filePath, json);
        }
        catch (IOException)
        {
            // Persistência não bloqueia a execução da aplicação
        }
    }

    public void ResetToDefaults()
    {
        TryDeleteFile();
    }

    private void TryDeleteFile()
    {
        try
        {
            if (File.Exists(_filePath))
                File.Delete(_filePath);
        }
        catch (IOException)
        {
            // Ignora falha de exclusão
        }
    }

    private static string DefaultFilePath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PrinterInstall");
        return Path.Combine(dir, "settings.json");
    }

    private sealed class AppSettingsDto
    {
        public string DomainName { get; set; } = "";
        public string? LdapHost { get; set; }
    }
}
