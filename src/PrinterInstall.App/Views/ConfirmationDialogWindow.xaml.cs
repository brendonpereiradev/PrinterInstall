using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace PrinterInstall.App.Views;

public partial class ConfirmationDialogWindow : FluentWindow
{
    public ConfirmationDialogWindow()
    {
        InitializeComponent();
    }

    public void ConfigureForDeployWarning(
        string title,
        string header,
        IEnumerable<string> warnings,
        string question,
        string primaryButtonText,
        string secondaryButtonText)
    {
        Title = title;
        HeaderTextBlock.Text = header;
        DetailsItemsControl.ItemsSource = warnings.ToList();
        QuestionTextBlock.Text = question;
        QuestionTextBlock.Visibility = Visibility.Visible;
        PrimaryButton.Content = primaryButtonText;
        SecondaryButton.Content = secondaryButtonText;
        SecondaryButton.Visibility = Visibility.Visible;

        // Ícone de aviso (laranja/âmbar)
        IconGlyphText.Text = "\uE7BA"; // Warning icon
        ApplyIconPalette("Warning");
    }

    public void ConfigureForPrinterIdentityBlock(
        string title,
        string header,
        IEnumerable<string> reasons,
        string buttonText)
    {
        ConfigureForDeployWarning(title, header, reasons, string.Empty, buttonText, string.Empty);
        QuestionTextBlock.Visibility = Visibility.Collapsed;
        SecondaryButton.Visibility = Visibility.Collapsed;
    }

    public void ConfigureForInversionWarning(
        string title,
        string header,
        IEnumerable<string> inversions,
        string question,
        string primaryButtonText,
        string secondaryButtonText)
    {
        Title = title;
        HeaderTextBlock.Text = header;
        DetailsItemsControl.ItemsSource = inversions.ToList();
        QuestionTextBlock.Text = question;
        QuestionTextBlock.Visibility = Visibility.Visible;
        PrimaryButton.Content = primaryButtonText;
        SecondaryButton.Content = secondaryButtonText;
        SecondaryButton.Visibility = Visibility.Visible;

        // Ícone de troca/inversão e aviso (âmbar/laranja)
        IconGlyphText.Text = "\uE8AB"; // Switch / Swap icon
        ApplyIconPalette("Warning");
    }

    public void ConfigureForNetworkTest(
        string title,
        string header,
        IEnumerable<string> details,
        string primaryButtonText,
        string secondaryButtonText)
    {
        Title = title;
        HeaderTextBlock.Text = header;
        DetailsItemsControl.ItemsSource = details.ToList();
        QuestionTextBlock.Text = "";
        QuestionTextBlock.Visibility = Visibility.Collapsed;
        PrimaryButton.Content = primaryButtonText;
        SecondaryButton.Content = secondaryButtonText;
        SecondaryButton.Visibility = Visibility.Visible;

        // Ícone de envio/informação (azul de destaque)
        IconGlyphText.Text = "\uE749"; // Send/Device icon or Info
        ApplyIconPalette("Info");
    }

    public void ConfigureForSpoolerReset(
        string title,
        string header,
        IEnumerable<string> details,
        string question,
        string primaryButtonText,
        string secondaryButtonText)
    {
        Title = title;
        HeaderTextBlock.Text = header;
        DetailsItemsControl.ItemsSource = details.ToList();
        QuestionTextBlock.Text = question;
        QuestionTextBlock.Visibility = Visibility.Visible;
        PrimaryButton.Content = primaryButtonText;
        SecondaryButton.Content = secondaryButtonText;
        SecondaryButton.Visibility = Visibility.Visible;

        // Ícone de manutenção (azul/ferramentas)
        IconGlyphText.Text = "\uE777";
        ApplyIconPalette("Info");
    }

    public void ConfigureForNoComputersAlert(
        string title,
        string header,
        IEnumerable<string> details,
        string buttonText)
    {
        Title = title;
        HeaderTextBlock.Text = header;
        DetailsItemsControl.ItemsSource = details.ToList();
        QuestionTextBlock.Text = "";
        QuestionTextBlock.Visibility = Visibility.Collapsed;
        SecondaryButton.Visibility = Visibility.Collapsed;
        PrimaryButton.Content = buttonText;

        // Ícone de aviso (laranja/âmbar)
        IconGlyphText.Text = "\uE7BA"; // Warning icon
        ApplyIconPalette("Warning");
    }

    private void ApplyIconPalette(string palette)
    {
        IconGlyphText.SetResourceReference(TextBlock.ForegroundProperty, $"AppBadge{palette}ForegroundBrush");
        IconBadgeBorder.SetResourceReference(Border.BackgroundProperty, $"AppBadge{palette}BackgroundBrush");
        IconBadgeBorder.SetResourceReference(Border.BorderBrushProperty, $"AppBadge{palette}BorderBrush");
    }

    protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == System.Windows.Input.Key.Escape && SecondaryButton.Visibility != Visibility.Visible)
        {
            DialogResult = true;
            Close();
        }
    }


    private void OnPrimaryButtonClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnSecondaryButtonClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
