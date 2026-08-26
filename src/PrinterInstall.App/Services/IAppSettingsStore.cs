using PrinterInstall.App.Models;

namespace PrinterInstall.App.Services;

/// <summary>
/// Contrato para persistência e carregamento das configurações da aplicação.
/// </summary>
public interface IAppSettingsStore
{
    /// <summary>
    /// Carrega as configurações persistidas ou retorna os valores padrão.
    /// </summary>
    AppSettings Load();

    /// <summary>
    /// Salva as novas configurações de domínio e rede.
    /// </summary>
    void Save(AppSettings settings);

    /// <summary>
    /// Restaura as configurações para os padrões de fábrica.
    /// </summary>
    void ResetToDefaults();
}
