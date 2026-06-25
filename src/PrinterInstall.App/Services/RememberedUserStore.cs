using System.IO;
using System.Text.Json;

namespace PrinterInstall.App.Services;

public sealed class RememberedUserStore : IRememberedUserStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly string _filePath;

    public RememberedUserStore()
        : this(DefaultFilePath())
    {
    }

    public RememberedUserStore(string filePath)
    {
        _filePath = filePath;
    }

    public RememberedUser? Load()
    {
        if (!File.Exists(_filePath))
            return null;

        try
        {
            var json = File.ReadAllText(_filePath);
            var dto = JsonSerializer.Deserialize<RememberedUserDto>(json, JsonOptions);
            if (dto is null ||
                string.IsNullOrWhiteSpace(dto.DomainName) ||
                string.IsNullOrWhiteSpace(dto.UserName))
            {
                TryDeleteFile();
                return null;
            }

            return new RememberedUser(dto.DomainName.Trim(), dto.UserName.Trim());
        }
        catch (JsonException)
        {
            TryDeleteFile();
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public void Save(RememberedUser user)
    {
        if (string.IsNullOrWhiteSpace(user.DomainName) || string.IsNullOrWhiteSpace(user.UserName))
            return;

        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var dto = new RememberedUserDto
            {
                DomainName = user.DomainName.Trim(),
                UserName = user.UserName.Trim()
            };
            var json = JsonSerializer.Serialize(dto, JsonOptions);
            File.WriteAllText(_filePath, json);
        }
        catch (IOException)
        {
            // persistência é conveniência; não bloqueia login
        }
    }

    public void Clear()
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
            // ignorar
        }
    }

    private static string DefaultFilePath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PrinterInstall");
        return Path.Combine(dir, "remembered-user.json");
    }

    private sealed class RememberedUserDto
    {
        public string DomainName { get; set; } = "";
        public string UserName { get; set; } = "";
    }
}
