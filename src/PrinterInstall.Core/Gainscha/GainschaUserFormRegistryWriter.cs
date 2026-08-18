using Microsoft.Win32;

namespace PrinterInstall.Core.Gainscha;

internal static class GainschaUserFormRegistryWriter
{
    private const string PrintersKeyPath =
        @"SYSTEM\CurrentControlSet\Control\Print\Printers";

    internal static void ApplyUserFormEntries(
        string printerQueueName,
        IReadOnlyList<GainschaUserFormDriverDataEntry> entries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerQueueName);

        if (entries.Count == 0)
        {
            throw new InvalidOperationException(
                "Nenhuma entrada User Form encontrada para gravar em PrinterDriverData.");
        }

        var keyPath = $@"{PrintersKeyPath}\{printerQueueName}\PrinterDriverData";
        using var key = Registry.LocalMachine.CreateSubKey(keyPath, writable: true)
            ?? throw new InvalidOperationException(
                $"Nao foi possivel abrir PrinterDriverData para '{printerQueueName}'.");

        foreach (var entry in entries)
        {
            key.SetValue(entry.Name, ToRegistryValue(entry), ToRegistryValueKind(entry.RegistryType));
        }
    }

    internal static bool TryReadBinaryValue(string printerQueueName, string valueName, out byte[] data)
    {
        data = Array.Empty<byte>();
        ArgumentException.ThrowIfNullOrWhiteSpace(printerQueueName);
        ArgumentException.ThrowIfNullOrWhiteSpace(valueName);

        var keyPath = $@"{PrintersKeyPath}\{printerQueueName}\PrinterDriverData";
        using var key = Registry.LocalMachine.OpenSubKey(keyPath, writable: false);
        if (key?.GetValue(valueName) is not byte[] bytes || bytes.Length == 0)
            return false;

        data = bytes;
        return true;
    }

    private static object ToRegistryValue(GainschaUserFormDriverDataEntry entry) =>
        entry.RegistryType switch
        {
            GainschaUserFormSdsOptionParser.RegBinary => entry.Data,
            GainschaUserFormSdsOptionParser.RegDword => entry.Data.Length >= 4
                ? BitConverter.ToUInt32(entry.Data, 0)
                : throw new InvalidOperationException($"DWORD invalido para '{entry.Name}'."),
            GainschaUserFormSdsOptionParser.RegSz => DecodeNullTerminatedUnicode(entry.Data),
            _ => throw new InvalidOperationException(
                $"Tipo de registry nao suportado para '{entry.Name}': {entry.RegistryType}."),
        };

    private static string DecodeNullTerminatedUnicode(byte[] data)
    {
        var length = data.Length;
        if (length >= 2 && data[^1] == 0 && data[^2] == 0)
            length -= 2;

        return System.Text.Encoding.Unicode.GetString(data, 0, length);
    }

    private static RegistryValueKind ToRegistryValueKind(uint registryType) =>
        registryType switch
        {
            GainschaUserFormSdsOptionParser.RegBinary => RegistryValueKind.Binary,
            GainschaUserFormSdsOptionParser.RegDword => RegistryValueKind.DWord,
            GainschaUserFormSdsOptionParser.RegSz => RegistryValueKind.String,
            _ => throw new InvalidOperationException($"Tipo de registry nao suportado: {registryType}."),
        };
}
