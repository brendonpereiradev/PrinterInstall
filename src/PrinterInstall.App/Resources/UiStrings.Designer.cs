#nullable enable
namespace PrinterInstall.App.Resources;

[System.CodeDom.Compiler.GeneratedCode("System.Resources.Tools.StronglyTypedResourceBuilder", "17.0.0.0")]
[System.Diagnostics.DebuggerNonUserCode]
[System.Runtime.CompilerServices.CompilerGenerated]
public class UiStrings
{
    private static System.Resources.ResourceManager? s_resourceManager;
    private static readonly System.Globalization.CultureInfo ResourceCulture = new("pt-BR");

    internal UiStrings() { }

    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Advanced)]
    public static System.Resources.ResourceManager ResourceManager =>
        s_resourceManager ??= new System.Resources.ResourceManager(
            "PrinterInstall.App.Resources.UiStrings",
            typeof(UiStrings).Assembly);

    public static string Main_NotAuthenticated => ResourceManager.GetString(nameof(Main_NotAuthenticated), ResourceCulture)!;
    public static string Main_Validation_ComputersRequired => ResourceManager.GetString(nameof(Main_Validation_ComputersRequired), ResourceCulture)!;
    public static string Main_Validation_DisplayNameRequired => ResourceManager.GetString(nameof(Main_Validation_DisplayNameRequired), ResourceCulture)!;
    public static string Main_Validation_PrinterHostRequired => ResourceManager.GetString(nameof(Main_Validation_PrinterHostRequired), ResourceCulture)!;
    public static string Main_InvalidComputerNameFormat => ResourceManager.GetString(nameof(Main_InvalidComputerNameFormat), ResourceCulture)!;
    public static string Login_Validation_DomainUserRequired => ResourceManager.GetString(nameof(Login_Validation_DomainUserRequired), ResourceCulture)!;
    public static string Removal_NotAuthenticated => ResourceManager.GetString(nameof(Removal_NotAuthenticated), ResourceCulture)!;
    public static string Removal_Validation_ComputersRequired => ResourceManager.GetString(nameof(Removal_Validation_ComputersRequired), ResourceCulture)!;
    public static string Removal_NoPrintersSelected => ResourceManager.GetString(nameof(Removal_NoPrintersSelected), ResourceCulture)!;
    public static string Removal_Finished => ResourceManager.GetString(nameof(Removal_Finished), ResourceCulture)!;
    public static string Removal_StepLabelFormat => ResourceManager.GetString(nameof(Removal_StepLabelFormat), ResourceCulture)!;
    public static string Removal_ReviewNothingFormat => ResourceManager.GetString(nameof(Removal_ReviewNothingFormat), ResourceCulture)!;
    public static string Removal_ReviewRemoveFormat => ResourceManager.GetString(nameof(Removal_ReviewRemoveFormat), ResourceCulture)!;
    public static string Removal_LogListPrintersFailedFormat => ResourceManager.GetString(nameof(Removal_LogListPrintersFailedFormat), ResourceCulture)!;
    public static string Removal_LogErrorFormat => ResourceManager.GetString(nameof(Removal_LogErrorFormat), ResourceCulture)!;
}
