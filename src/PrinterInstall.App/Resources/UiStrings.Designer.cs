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
    public static string Main_Validation_GainschaLabelPresetRequired => ResourceManager.GetString(nameof(Main_Validation_GainschaLabelPresetRequired), ResourceCulture)!;
    public static string Main_InvalidComputerNameFormat => ResourceManager.GetString(nameof(Main_InvalidComputerNameFormat), ResourceCulture)!;
    public static string Main_LocalComputerSuffix => ResourceManager.GetString(nameof(Main_LocalComputerSuffix), ResourceCulture)!;
    public static string Main_SummaryLineFormat => ResourceManager.GetString(nameof(Main_SummaryLineFormat), ResourceCulture)!;
    public static string Main_SummaryOtherFormat => ResourceManager.GetString(nameof(Main_SummaryOtherFormat), ResourceCulture)!;
    public static string Main_SummaryFailureLineFormat => ResourceManager.GetString(nameof(Main_SummaryFailureLineFormat), ResourceCulture)!;
    public static string Main_SummaryDialogTitle => ResourceManager.GetString(nameof(Main_SummaryDialogTitle), ResourceCulture)!;
    public static string Main_DeployCancelRequested => ResourceManager.GetString(nameof(Main_DeployCancelRequested), ResourceCulture)!;
    public static string Main_DeployRollbackStarting => ResourceManager.GetString(nameof(Main_DeployRollbackStarting), ResourceCulture)!;
    public static string Main_DeployRollbackFinished => ResourceManager.GetString(nameof(Main_DeployRollbackFinished), ResourceCulture)!;
    public static string Main_DeployRollbackErrorFormat => ResourceManager.GetString(nameof(Main_DeployRollbackErrorFormat), ResourceCulture)!;
    public static string Main_DeployCooperativeCancelHint => ResourceManager.GetString(nameof(Main_DeployCooperativeCancelHint), ResourceCulture)!;
    public static string Main_DeployCancelledRowMessage => ResourceManager.GetString(nameof(Main_DeployCancelledRowMessage), ResourceCulture)!;
    public static string Main_RollbackPreparingOnHost => ResourceManager.GetString(nameof(Main_RollbackPreparingOnHost), ResourceCulture)!;
    public static string Main_SummaryRolledBackFormat => ResourceManager.GetString(nameof(Main_SummaryRolledBackFormat), ResourceCulture)!;
    public static string Main_SummaryDeployCancelledFormat => ResourceManager.GetString(nameof(Main_SummaryDeployCancelledFormat), ResourceCulture)!;
    public static string Login_Validation_DomainUserRequired => ResourceManager.GetString(nameof(Login_Validation_DomainUserRequired), ResourceCulture)!;
    public static string Removal_NotAuthenticated => ResourceManager.GetString(nameof(Removal_NotAuthenticated), ResourceCulture)!;
    public static string Removal_Validation_ComputersRequired => ResourceManager.GetString(nameof(Removal_Validation_ComputersRequired), ResourceCulture)!;
    public static string Removal_NoPrintersSelected => ResourceManager.GetString(nameof(Removal_NoPrintersSelected), ResourceCulture)!;
    public static string Removal_Finished => ResourceManager.GetString(nameof(Removal_Finished), ResourceCulture)!;
    public static string Removal_StepLabelFormat => ResourceManager.GetString(nameof(Removal_StepLabelFormat), ResourceCulture)!;
    public static string Removal_ReviewNothingFormat => ResourceManager.GetString(nameof(Removal_ReviewNothingFormat), ResourceCulture)!;
    public static string Removal_ReviewRemoveFormat => ResourceManager.GetString(nameof(Removal_ReviewRemoveFormat), ResourceCulture)!;
    public static string Removal_ReviewRenameFormat => ResourceManager.GetString(nameof(Removal_ReviewRenameFormat), ResourceCulture)!;
    public static string Removal_LogListPrintersFailedFormat => ResourceManager.GetString(nameof(Removal_LogListPrintersFailedFormat), ResourceCulture)!;
    public static string Removal_LogErrorFormat => ResourceManager.GetString(nameof(Removal_LogErrorFormat), ResourceCulture)!;
    public static string NetworkTest_Validation_HostRequired => ResourceManager.GetString(nameof(NetworkTest_Validation_HostRequired), ResourceCulture)!;
    public static string NetworkTest_Progress_Connectivity => ResourceManager.GetString(nameof(NetworkTest_Progress_Connectivity), ResourceCulture)!;
    public static string NetworkTest_Progress_Sending => ResourceManager.GetString(nameof(NetworkTest_Progress_Sending), ResourceCulture)!;
    public static string NetworkTest_Cancelled => ResourceManager.GetString(nameof(NetworkTest_Cancelled), ResourceCulture)!;
    public static string Main_Validation_InvalidHostAddressFormat => ResourceManager.GetString(nameof(Main_Validation_InvalidHostAddressFormat), ResourceCulture)!;
    public static string Main_Validation_InversionDetectedFormat => ResourceManager.GetString(nameof(Main_Validation_InversionDetectedFormat), ResourceCulture)!;
    public static string Main_LogExportSuccessFormat => ResourceManager.GetString(nameof(Main_LogExportSuccessFormat), ResourceCulture)!;
    public static string Main_LogExportErrorFormat => ResourceManager.GetString(nameof(Main_LogExportErrorFormat), ResourceCulture)!;
    public static string Removal_LogExportSuccessFormat => ResourceManager.GetString(nameof(Removal_LogExportSuccessFormat), ResourceCulture)!;
    public static string Removal_LogExportErrorFormat => ResourceManager.GetString(nameof(Removal_LogExportErrorFormat), ResourceCulture)!;
    public static string Removal_SummaryDialogTitle => ResourceManager.GetString(nameof(Removal_SummaryDialogTitle), ResourceCulture)!;
    public static string Removal_SummaryLineFormat => ResourceManager.GetString(nameof(Removal_SummaryLineFormat), ResourceCulture)!;
    public static string Removal_SummaryFailuresHeader => ResourceManager.GetString(nameof(Removal_SummaryFailuresHeader), ResourceCulture)!;
    public static string Removal_Cancelled => ResourceManager.GetString(nameof(Removal_Cancelled), ResourceCulture)!;
    public static string Main_DeployWarningDialogTitle => ResourceManager.GetString(nameof(Main_DeployWarningDialogTitle), ResourceCulture)!;
    public static string Main_DeployWarningHeader => ResourceManager.GetString(nameof(Main_DeployWarningHeader), ResourceCulture)!;
    public static string Main_DeployWarningQuestion => ResourceManager.GetString(nameof(Main_DeployWarningQuestion), ResourceCulture)!;
    public static string Main_DeployWarningProceedButton => ResourceManager.GetString(nameof(Main_DeployWarningProceedButton), ResourceCulture)!;
    public static string Main_DeployWarningCancelButton => ResourceManager.GetString(nameof(Main_DeployWarningCancelButton), ResourceCulture)!;
    public static string Main_DeployCancelledByMismatchWarning => ResourceManager.GetString(nameof(Main_DeployCancelledByMismatchWarning), ResourceCulture)!;
    public static string NetworkTest_ConfirmDialogTitle => ResourceManager.GetString(nameof(NetworkTest_ConfirmDialogTitle), ResourceCulture)!;
    public static string NetworkTest_ConfirmHeader => ResourceManager.GetString(nameof(NetworkTest_ConfirmHeader), ResourceCulture)!;
    public static string NetworkTest_ConfirmHostFormat => ResourceManager.GetString(nameof(NetworkTest_ConfirmHostFormat), ResourceCulture)!;
    public static string NetworkTest_ConfirmBrandFormat => ResourceManager.GetString(nameof(NetworkTest_ConfirmBrandFormat), ResourceCulture)!;
    public static string NetworkTest_ConfirmPresetFormat => ResourceManager.GetString(nameof(NetworkTest_ConfirmPresetFormat), ResourceCulture)!;
    public static string NetworkTest_ConfirmProceedButton => ResourceManager.GetString(nameof(NetworkTest_ConfirmProceedButton), ResourceCulture)!;
    public static string NetworkTest_ConfirmCancelButton => ResourceManager.GetString(nameof(NetworkTest_ConfirmCancelButton), ResourceCulture)!;
}
