using System.Windows;
using System.Windows.Data;
using Wpf.Ui.Controls;
using GlyphTextBlock = System.Windows.Controls.TextBlock;

namespace PrinterInstall.App.Controls;

public sealed class ThemeToggleIcon : SymbolIcon
{
    protected override UIElement InitializeChildren()
    {
        var glyph = (GlyphTextBlock)base.InitializeChildren();
        // O glifo interno do WPF UI pode manter a cor herdada na primeira renderização.
        glyph.SetBinding(GlyphTextBlock.ForegroundProperty, new Binding(nameof(Foreground)) { Source = this });
        return glyph;
    }
}
