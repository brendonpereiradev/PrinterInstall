# WPF-UI second-wave UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Integrar **WPF-UI 4.2.0** no `PrinterInstall.App`, tema **claro**, **acento azul discreto** (#3D5A80), três janelas visualmente coerentes, **estado vazio** na grade principal, sem alterar `PrinterInstall.Core` nem regras de negócio.

**Architecture:** Manter **`System.Windows.Window`** (e `x:Class` existentes) para **menor atrito** com `ShowDialog`/`Owner`. Fundir `ThemesDictionary` + `ControlsDictionary` no `App.xaml` **antes** dos dicionários pt-BR. Aplicar **`ApplicationThemeManager`** e **`ApplicationAccentColorManager`** no arranque após cultura pt-BR. Substituir botões principais por **`ui:Button`** (`Appearance`). Secções com **`Border`** estilizado usando **brushes dinâmicos** do tema WPF-UI (evita dependência de API exacta de `Card`). Estado vazio via **`ShowStatusEmptyHint`** no `MainViewModel` + conversor de visibilidade.

**Tech stack:** .NET 8 WPF, **WPF-UI** (`WPF-UI` NuGet **4.2.0**), CommunityToolkit.Mvvm, xunit (teste opcional do conversor).

---

## Mapa de arquivos

| Arquivo | Responsabilidade |
|---------|------------------|
| `src/PrinterInstall.App/PrinterInstall.App.csproj` | `PackageReference` WPF-UI 4.2.0 |
| `src/PrinterInstall.App/App.xaml` | `xmlns:ui`, `ThemesDictionary` Light, `ControlsDictionary`, merges pt-BR e overrides |
| `src/PrinterInstall.App/App.xaml.cs` | `ApplicationThemeManager.Apply`, `ApplicationAccentColorManager.Apply` |
| `src/PrinterInstall.App/Themes/WpfUiLocalBrushes.xaml` | Brush fixo da faixa de cabeçalho (azul discreto) |
| `src/PrinterInstall.App/Themes/AdminToolTheme.xaml` | **Remover** estilos globais de `Button` que conflituam com WPF-UI (arquivo pode ficar vazio ou ser apagado) |
| `src/PrinterInstall.App/Converters/InverseBooleanToVisibilityConverter.cs` | Inverter visibilidade para esconder `DataGrid` quando vazio |
| `src/PrinterInstall.App/Strings/Main.pt-BR.xaml` | `Main_StatusEmptyHint`, chaves de cabeçalho opcionais |
| `src/PrinterInstall.App/ViewModels/MainViewModel.cs` | `ShowStatusEmptyHint`, subscrição `Targets.CollectionChanged` |
| `src/PrinterInstall.App/Views/MainWindow.xaml` | Faixa de acento, `ui:Button`, `Border` de secção, estado vazio |
| `src/PrinterInstall.App/Views/LoginWindow.xaml` | Faixa + `ui:Button` Entrar |
| `src/PrinterInstall.App/Views/RemovalWizardWindow.xaml` | Faixa + `ui:Button` Avançar / Executar |
| `tests/PrinterInstall.App.Tests/Converters/InverseBooleanToVisibilityConverterTests.cs` | Teste unitário do conversor |

---

### Task 1: Pacote WPF-UI

**Files:**
- Modify: `src/PrinterInstall.App/PrinterInstall.App.csproj`

- [ ] **Step 1: Adicionar referência fixa**

Dentro do primeiro `<ItemGroup>` de `PackageReference`, adicionar:

```xml
<PackageReference Include="WPF-UI" Version="4.2.0" />
```

- [ ] **Step 2: Restaurar**

Run: `dotnet restore "src/PrinterInstall.App/PrinterInstall.App.csproj"`  
Expected: restore **succeeded**.

- [ ] **Step 3: Commit** (omitir se política do repositório exigir aprovação explícita)

```bash
git add src/PrinterInstall.App/PrinterInstall.App.csproj
git commit -m "chore(app): add WPF-UI 4.2.0"
```

---

### Task 2: Dicionários globais e brushes locais

**Files:**
- Create: `src/PrinterInstall.App/Themes/WpfUiLocalBrushes.xaml`
- Modify: `src/PrinterInstall.App/App.xaml`
- Modify: `src/PrinterInstall.App/Themes/AdminToolTheme.xaml`

- [ ] **Step 1: Criar `WpfUiLocalBrushes.xaml`**

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <!-- Faixa de cabeçalho: azul discreto (R=61 G=90 B=128) -->
  <SolidColorBrush x:Key="AppHeaderAccentBrush" Color="#FF3D5A80"/>
</ResourceDictionary>
```

- [ ] **Step 2: Substituir `App.xaml` completo**

```xml
<Application x:Class="PrinterInstall.App.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
             Startup="App_OnStartup"
             Exit="App_OnExit">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ui:ThemesDictionary Theme="Light" />
                <ui:ControlsDictionary />
                <ResourceDictionary Source="Themes/WpfUiLocalBrushes.xaml"/>
                <ResourceDictionary Source="Themes/AdminToolTheme.xaml"/>
                <ResourceDictionary Source="Strings/Main.pt-BR.xaml"/>
                <ResourceDictionary Source="Strings/Login.pt-BR.xaml"/>
                <ResourceDictionary Source="Strings/RemovalWizard.pt-BR.xaml"/>
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

- [ ] **Step 3: Esvaziar conflitos em `AdminToolTheme.xaml`**

Substituir o conteúdo por um dicionário vazio (remove estilos implícitos de `Button`):

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
</ResourceDictionary>
```

- [ ] **Step 4: Compilar**

Run: `dotnet build src/PrinterInstall.App/PrinterInstall.App.csproj`  
Expected: **Build succeeded** (pode falhar até Task 3 se faltar `ApplicationThemeManager` — repetir após Task 3).

- [ ] **Step 5: Commit**

```bash
git add src/PrinterInstall.App/App.xaml src/PrinterInstall.App/Themes/WpfUiLocalBrushes.xaml src/PrinterInstall.App/Themes/AdminToolTheme.xaml
git commit -m "feat(app): merge WPF-UI theme dictionaries and local accent brush"
```

---

### Task 3: Tema claro + acento no arranque

**Files:**
- Modify: `src/PrinterInstall.App/App.xaml.cs`

- [ ] **Step 1: Acrescentar usings**

```csharp
using System.Windows.Media;
using Wpf.Ui.Appearance;
```

- [ ] **Step 2: Depois de definir `pt-BR` e antes de `Host.CreateApplicationBuilder()`, aplicar tema e acento**

```csharp
ApplicationThemeManager.Apply(ApplicationTheme.Light, WindowBackdropType.None);

ApplicationAccentColorManager.Apply(
    Color.FromRgb(61, 90, 128),
    ApplicationTheme.Light);
```

- [ ] **Step 3: Compilar**

Run: `dotnet build src/PrinterInstall.App/PrinterInstall.App.csproj`  
Expected: **Build succeeded**.

- [ ] **Step 4: Commit**

```bash
git add src/PrinterInstall.App/App.xaml.cs
git commit -m "feat(app): apply WPF-UI light theme and discrete blue accent"
```

---

### Task 4: Conversor de visibilidade + teste

**Files:**
- Create: `src/PrinterInstall.App/Converters/InverseBooleanToVisibilityConverter.cs`
- Create: `tests/PrinterInstall.App.Tests/Converters/InverseBooleanToVisibilityConverterTests.cs`

- [ ] **Step 1: Implementar conversor**

```csharp
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PrinterInstall.App.Converters;

public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
```

- [ ] **Step 2: Teste**

```csharp
using System.Windows;
using PrinterInstall.App.Converters;

namespace PrinterInstall.App.Tests.Converters;

public class InverseBooleanToVisibilityConverterTests
{
    private readonly InverseBooleanToVisibilityConverter _sut = new();

    [Fact]
    public void Convert_true_returns_Collapsed()
    {
        Assert.Equal(Visibility.Collapsed, (Visibility)_sut.Convert(true, typeof(Visibility), null, CultureInfo.InvariantCulture)!);
    }

    [Fact]
    public void Convert_false_returns_Visible()
    {
        Assert.Equal(Visibility.Visible, (Visibility)_sut.Convert(false, typeof(Visibility), null, CultureInfo.InvariantCulture)!);
    }
}
```

- [ ] **Step 3: Correr testes**

Run: `dotnet test tests/PrinterInstall.App.Tests/PrinterInstall.App.Tests.csproj --filter "FullyQualifiedName~InverseBooleanToVisibilityConverterTests"`  
Expected: **Passed**.

- [ ] **Step 4: Commit**

```bash
git add src/PrinterInstall.App/Converters/InverseBooleanToVisibilityConverter.cs tests/PrinterInstall.App.Tests/Converters/InverseBooleanToVisibilityConverterTests.cs
git commit -m "feat(app): add InverseBooleanToVisibilityConverter with tests"
```

---

### Task 5: MainViewModel — estado vazio

**Files:**
- Modify: `src/PrinterInstall.App/ViewModels/MainViewModel.cs`

- [ ] **Step 1: Adicionar propriedade e subscrição no construtor**

Após o bloco do construtor que define `_selectedBrand`, adicionar:

```csharp
Targets.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ShowStatusEmptyHint));
```

Adicionar propriedade pública (não `[ObservableProperty]` — calculada):

```csharp
public bool ShowStatusEmptyHint => Targets.Count == 0;
```

- [ ] **Step 2: Garantir notificação após `Targets.Clear()` / adições**

A subscrição `CollectionChanged` cobre `Clear`, `Add`, etc.

- [ ] **Step 3: Compilar**

Run: `dotnet build src/PrinterInstall.App/PrinterInstall.App.csproj`  
Expected: **succeeded**.

- [ ] **Step 4: Commit**

```bash
git add src/PrinterInstall.App/ViewModels/MainViewModel.cs
git commit -m "feat(app): expose ShowStatusEmptyHint for main status grid"
```

---

### Task 6: Strings pt-BR — dica de estado vazio

**Files:**
- Modify: `src/PrinterInstall.App/Strings/Main.pt-BR.xaml`

- [ ] **Step 1: Adicionar entradas**

Dentro do `ResourceDictionary`, adicionar:

```xml
  <sys:String x:Key="Main_StatusEmptyHint">Nenhum computador na lista. Adicione nomes à esquerda e clique em Implantar para ver o progresso aqui.</sys:String>
  <sys:String x:Key="Main_PageSubtitle">Implantação remota de impressoras</sys:String>
```

(`xmlns:sys` já deve existir no arquivo.)

- [ ] **Step 2: Commit**

```bash
git add src/PrinterInstall.App/Strings/Main.pt-BR.xaml
git commit -m "feat(app): add main window empty-state and subtitle strings"
```

---

### Task 7: MainWindow — layout WPF-UI

**Files:**
- Modify: `src/PrinterInstall.App/Views/MainWindow.xaml`

- [ ] **Step 1: Substituir o XAML pelo seguinte** (ajustar espaçamentos finos se necessário após smoke visual)

```xml
<Window x:Class="PrinterInstall.App.Views.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
        xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
        xmlns:conv="clr-namespace:PrinterInstall.App.Converters"
        xmlns:controls="clr-namespace:System.Windows.Controls;assembly=PresentationFramework"
        mc:Ignorable="d"
        Title="{DynamicResource Main_WindowTitle}"
        Height="720" Width="1024"
        WindowStartupLocation="CenterScreen">
    <Window.Resources>
        <conv:InverseBooleanToVisibilityConverter x:Key="InverseBoolToVis"/>
        <controls:BooleanToVisibilityConverter x:Key="BoolToVis"/>
    </Window.Resources>
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
        </Grid.RowDefinitions>

        <DockPanel Grid.Row="0" LastChildFill="True">
            <Border DockPanel.Dock="Top" Height="4" Background="{DynamicResource AppHeaderAccentBrush}"/>
            <StackPanel Margin="16,12,16,8">
                <TextBlock Text="{DynamicResource Main_WindowTitle}" FontSize="18" FontWeight="SemiBold"/>
                <TextBlock Text="{DynamicResource Main_PageSubtitle}" FontSize="12"
                           Foreground="{DynamicResource TextFillColorSecondaryBrush}" Margin="0,4,0,0"/>
            </StackPanel>
        </DockPanel>

        <Grid Grid.Row="1" Margin="16,0,16,16">
            <Grid.RowDefinitions>
                <RowDefinition Height="*"/>
                <RowDefinition Height="220" MinHeight="160"/>
            </Grid.RowDefinitions>

            <Grid Grid.Row="0">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="2*"/>
                    <ColumnDefinition Width="16"/>
                    <ColumnDefinition Width="280"/>
                </Grid.ColumnDefinitions>

                <Grid Grid.Column="0">
                    <Grid.RowDefinitions>
                        <RowDefinition Height="*"/>
                        <RowDefinition Height="8"/>
                        <RowDefinition Height="200"/>
                    </Grid.RowDefinitions>

                    <Border Grid.Row="0" Background="{DynamicResource ControlFillColorSecondaryBrush}"
                            BorderBrush="{DynamicResource ControlStrokeColorDefaultBrush}"
                            BorderThickness="1" CornerRadius="4" Padding="8" Margin="0,0,0,8">
                        <DockPanel LastChildFill="True">
                            <TextBlock DockPanel.Dock="Top" Text="{DynamicResource Main_ComputersLabel}"
                                       FontWeight="SemiBold" Margin="0,0,0,8"/>
                            <TextBox Text="{Binding ComputersText, UpdateSourceTrigger=PropertyChanged}"
                                     AcceptsReturn="True" TextWrapping="Wrap" VerticalScrollBarVisibility="Auto"/>
                        </DockPanel>
                    </Border>

                    <Border Grid.Row="2" Background="{DynamicResource ControlFillColorSecondaryBrush}"
                            BorderBrush="{DynamicResource ControlStrokeColorDefaultBrush}"
                            BorderThickness="1" CornerRadius="4" Padding="8">
                        <Grid>
                            <TextBlock Text="{DynamicResource Main_StatusGroupHeader}" FontWeight="SemiBold" Margin="0,0,0,8"
                                       VerticalAlignment="Top"/>
                            <DataGrid ItemsSource="{Binding Targets}"
                                      Margin="0,28,0,0"
                                      AutoGenerateColumns="False"
                                      IsReadOnly="True"
                                      HeadersVisibility="Column"
                                      CanUserAddRows="False"
                                      GridLinesVisibility="Horizontal"
                                      Visibility="{Binding ShowStatusEmptyHint, Converter={StaticResource InverseBoolToVis}}">
                                <DataGrid.Columns>
                                    <DataGridTextColumn Header="{DynamicResource Main_ColumnComputer}" Binding="{Binding ComputerName}" Width="*"/>
                                    <DataGridTextColumn Header="{DynamicResource Main_ColumnState}" Binding="{Binding StateDisplay}" Width="160"/>
                                    <DataGridTextColumn Header="{DynamicResource Main_ColumnMessage}" Binding="{Binding Message}" Width="2*"/>
                                </DataGrid.Columns>
                            </DataGrid>
                            <TextBlock Text="{DynamicResource Main_StatusEmptyHint}"
                                       TextWrapping="Wrap" TextAlignment="Center" VerticalAlignment="Center" Margin="16"
                                       Foreground="{DynamicResource TextFillColorSecondaryBrush}"
                                       Visibility="{Binding ShowStatusEmptyHint, Converter={StaticResource BoolToVis}}"/>
                        </Grid>
                    </Border>
                </Grid>

                <StackPanel Grid.Column="2">
                    <TextBlock Text="{DynamicResource Main_BrandLabel}" FontWeight="SemiBold"/>
                    <ComboBox ItemsSource="{Binding Brands}" SelectedItem="{Binding SelectedBrand}" Margin="0,4,0,8"/>

                    <TextBlock Text="{DynamicResource Main_DisplayNameLabel}" FontWeight="SemiBold"/>
                    <TextBox Text="{Binding DisplayName, UpdateSourceTrigger=PropertyChanged}" Margin="0,4,0,8"/>

                    <TextBlock Text="{DynamicResource Main_PrinterHostLabel}" FontWeight="SemiBold"/>
                    <TextBox Text="{Binding PrinterHostAddress, UpdateSourceTrigger=PropertyChanged}" Margin="0,4,0,8"/>

                    <ui:Button Content="{DynamicResource Main_DeployButton}" Command="{Binding DeployCommand}"
                               Appearance="Primary" Margin="0,16,0,0"/>
                    <ui:Button Content="{DynamicResource Main_RemovePrintersButton}" Command="{Binding OpenRemovalWizardCommand}"
                               Appearance="Secondary" Margin="0,8,0,0"/>
                </StackPanel>
            </Grid>

            <Border Grid.Row="1" Background="{DynamicResource ControlFillColorSecondaryBrush}"
                    BorderBrush="{DynamicResource ControlStrokeColorDefaultBrush}"
                    BorderThickness="1" CornerRadius="4" Padding="8" Margin="0,8,0,0">
                <DockPanel LastChildFill="True">
                    <TextBlock DockPanel.Dock="Top" Text="{DynamicResource Main_LogGroupHeader}" FontWeight="SemiBold" Margin="0,0,0,8"/>
                    <TextBox Text="{Binding LogText}" IsReadOnly="True" VerticalScrollBarVisibility="Auto"
                             TextWrapping="Wrap" FontFamily="Consolas"/>
                </DockPanel>
            </Border>
        </Grid>
    </Grid>
</Window>
```

- [ ] **Step 2: Se o build falhar com recurso dinâmico em falta**, substituir o nome do brush pelo sugerido no IntelliSense da versão 4.2.0 (ex. `TextFillColorTertiaryBrush`). **Não** introduzir `TBD` — corrigir com nome válido verificado no tema.

- [ ] **Step 3: Compilar**

Run: `dotnet build src/PrinterInstall.App/PrinterInstall.App.csproj`

- [ ] **Step 4: Commit**

```bash
git add src/PrinterInstall.App/Views/MainWindow.xaml
git commit -m "feat(app): restyle MainWindow with WPF-UI and empty state"
```

---

### Task 8: LoginWindow

**Files:**
- Modify: `src/PrinterInstall.App/Views/LoginWindow.xaml`

- [ ] **Step 1: Envolver conteúdo com cabeçalho e substituir botão**

Estrutura alvo (manter `PasswordBox` e `Click` existentes):

- `xmlns:ui` e faixa `AppHeaderAccentBrush` iguais à `MainWindow`.
- `StackPanel` interior com `Margin="20"`.
- Substituir `<Button ... Sign in` por:

```xml
<ui:Button Content="{DynamicResource Login_SignInButton}" HorizontalAlignment="Right" Width="120" IsDefault="True" Click="SignIn_OnClick" Appearance="Primary"/>
```

- [ ] **Step 2: Compilar e smoke manual** (abrir login).

- [ ] **Step 3: Commit**

```bash
git add src/PrinterInstall.App/Views/LoginWindow.xaml
git commit -m "feat(app): restyle LoginWindow with WPF-UI header and primary button"
```

---

### Task 9: RemovalWizardWindow

**Files:**
- Modify: `src/PrinterInstall.App/Views/RemovalWizardWindow.xaml`

- [ ] **Step 1: Adicionar `xmlns:ui`, faixa de acento no topo** (dentro do `Grid` principal, primeira linha `Auto` com `DockPanel` ou `Grid.Row` extra — manter `TabControl` abaixo).

Esboço mínimo da hierarquia:

```xml
<Grid>
  <Grid.RowDefinitions>
    <RowDefinition Height="Auto"/>
    <RowDefinition Height="*"/>
  </Grid.RowDefinitions>
  <DockPanel Grid.Row="0" LastChildFill="True">
    <Border DockPanel.Dock="Top" Height="4" Background="{DynamicResource AppHeaderAccentBrush}"/>
    <TextBlock Text="{DynamicResource Removal_WindowTitle}" FontSize="16" FontWeight="SemiBold" Margin="12,8,12,4"/>
  </DockPanel>
  <Grid Grid.Row="1" Margin="12,0,12,12">
    <!-- TabControl existente (SelectedIndex binding) -->
  </Grid>
</Grid>
```

- [ ] **Step 2: Substituir `Button` por `ui:Button`:** `StartCommand` → `Appearance="Primary"`; `NextQueueStepCommand` → `Primary`; `ExecuteCommand` → `Primary`; usar `Appearance="Secondary"` apenas se existir botão “Cancelar” no futuro (não há hoje).

- [ ] **Step 3: Revalidar passos do assistente** (tabs colapsadas ainda ocultas).

- [ ] **Step 4: Commit**

```bash
git add src/PrinterInstall.App/Views/RemovalWizardWindow.xaml
git commit -m "feat(app): restyle RemovalWizardWindow with WPF-UI"
```

---

### Task 10: Verificação final

- [ ] **Step 1: Testes**

Run: `dotnet test`  
Expected: **todos passam**.

- [ ] **Step 2: Smoke manual (checklist)**

1. Login: campos + Entrar + erro de validação pt-BR.  
2. Principal: faixa azul, subtítulo, secções com borda; **grade vazia** mostra dica; após “Implantar” com nomes válidos, dica desaparece e linhas aparecem.  
3. Assistente: cabeçalho, Avançar, grelha, Executar, log.

- [ ] **Step 3: Commit final** (se apenas ajustes de brush)

---

## Self-review

**1. Cobertura da spec (`2026-04-21-ui-second-wave-wpf-ui-design.md`):**

| Requisito | Tarefa |
|-----------|--------|
| WPF-UI pinado | Task 1 |
| Tema claro + acento | Tasks 2–3 |
| Três janelas | Tasks 7–9 |
| Admin denso / sem Mica chamativo | Task 3 `WindowBackdropType.None` |
| Estado vazio principal | Tasks 4–7 |
| Core sem UI | Nenhuma alteração na Core |
| pt-BR preservado | Tasks 6, merges finais em Task 2 |

**2. Placeholders:** nenhum `TBD` no plano; se um `DynamicResource` de brush não existir em 4.2.0, o implementador substitui por chave válida verificada no pacote (passo explícito na Task 7).

**3. Consistência:** `ShowStatusEmptyHint` e `InverseBooleanToVisibilityConverter` alinhados com o XAML da Task 7.

---

**Plan complete and saved to `docs/superpowers/plans/2026-04-21-ui-second-wave-wpf-ui.md`. Duas opções de execução:**

**1. Subagent-Driven (recomendado)** — Um subagente por tarefa, revisão entre tarefas, iteração rápida.

**2. Inline execution** — Executar tarefas nesta sessão com executing-plans, em lote com checkpoints.

**Qual abordagem prefere?**
