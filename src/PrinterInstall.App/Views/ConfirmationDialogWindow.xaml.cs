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
        QuestionTextBlock.Visibility = Visibility.Visible;
        PrimaryButton.Content = primaryButtonText;
        SecondaryButton.Content = secondaryButtonText;
        SecondaryButton.Visibility = Visibility.Visible;

        // Ícone de aviso (laranja/âmbar)
        IconGlyphText.Text = "\uE7BA"; // Warning icon
        IconGlyphText.Foreground = new SolidColorBrush(Color.FromRgb(0xD3, 0x54, 0x00));
        IconBadgeBorder.Background = new SolidColorBrush(Color.FromRgb(0xFD, 0xF3, 0xE8));
        IconBadgeBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0xF3, 0x9C, 0x12));
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
        SecondaryButton.Visibility = Visibility.Visible;

        // Ícone de envio/informação (azul de destaque)
        IconGlyphText.Text = "\uE749"; // Send/Device icon or Info
        IconGlyphText.Foreground = new SolidColorBrush(Color.FromRgb(0x1B, 0x3A, 0x5C));
        IconBadgeBorder.Background = new SolidColorBrush(Color.FromRgb(0xEB, 0xF5, 0xFB));
        IconBadgeBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3D, 0x5A, 0x80));
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
        IconGlyphText.Foreground = new SolidColorBrush(Color.FromRgb(0x0C, 0x4A, 0x8A));
        IconBadgeBorder.Background = new SolidColorBrush(Color.FromRgb(0xE5, 0xF2, 0xFF));
        IconBadgeBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x7A, 0xB6, 0xF0));
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
        IconGlyphText.Foreground = new SolidColorBrush(Color.FromRgb(0xD3, 0x54, 0x00));
        IconBadgeBorder.Background = new SolidColorBrush(Color.FromRgb(0xFD, 0xF3, 0xE8));
        IconBadgeBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0xF3, 0x9C, 0x12));
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
