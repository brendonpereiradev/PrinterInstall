using System.Windows;
using System.Windows.Media;

namespace PrinterInstall.App.Views;

public partial class ConfirmationDialogWindow : Window
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
        PrimaryButton.Content = primaryButtonText;
        SecondaryButton.Content = secondaryButtonText;

        // Ícone de aviso (laranja/âmbar)
        IconGlyphText.Text = "\uE7BA"; // Warning icon
        IconGlyphText.Foreground = new SolidColorBrush(Color.FromRgb(0xD3, 0x54, 0x00));
        IconBadgeBorder.Background = new SolidColorBrush(Color.FromRgb(0xFD, 0xF3, 0xE8));
        IconBadgeBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0xF3, 0x9C, 0x12));
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

        // Ícone de envio/informação (azul de destaque)
        IconGlyphText.Text = "\uE749"; // Send/Device icon or Info
        IconGlyphText.Foreground = new SolidColorBrush(Color.FromRgb(0x1B, 0x3A, 0x5C));
        IconBadgeBorder.Background = new SolidColorBrush(Color.FromRgb(0xEB, 0xF5, 0xFB));
        IconBadgeBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3D, 0x5A, 0x80));
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
