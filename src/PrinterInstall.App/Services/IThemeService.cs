using Wpf.Ui.Appearance;

namespace PrinterInstall.App.Services;

/// <summary>
/// Contrato para gerenciamento, alternância e persistência de temas da interface (Claro / Escuro).
/// </summary>
public interface IThemeService
{
    /// <summary>
    /// Tema atualmente ativo na aplicação.
    /// </summary>
    ApplicationTheme CurrentTheme { get; }

    /// <summary>
    /// Indica se o modo escuro está ativo.
    /// </summary>
    bool IsDarkMode { get; }

    /// <summary>
    /// Aplica explicitamente um tema (Light ou Dark).
    /// </summary>
    /// <param name=theme>O tema desejado.</param>
    /// <param name=persist>Indica se a preferência deve ser salva nas configurações.</param>
    void SetTheme(ApplicationTheme theme, bool persist = true);

    /// <summary>
    /// Alterna entre os modos Claro e Escuro.
    /// </summary>
    void ToggleTheme();

    /// <summary>
    /// Notificação disparada quando o tema visual da aplicação é alterado.
    /// </summary>
    event EventHandler<ApplicationTheme>? ThemeChanged;
}
