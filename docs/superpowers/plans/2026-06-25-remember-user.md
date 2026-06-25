# Remember User — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Persistir somente o nome de usuário de domínio (opt-in via checkbox "Lembrar-me") para pré-preencher o login na próxima abertura; senha nunca vai para disco.

**Architecture:** Novo `IRememberedUserStore` na camada App grava `{ domainName, userName }` em JSON em `%LocalAppData%\PrinterInstall\`. `LoginViewModel` carrega no startup e salva/limpa após LDAP bem-sucedido. UI: checkbox na `LoginWindow`. Core inalterado.

**Tech Stack:** .NET 8, WPF, CommunityToolkit.Mvvm, Microsoft.Extensions.Hosting (DI), System.Text.Json, xUnit

**Spec:** `docs/superpowers/specs/2026-06-25-remember-user-design.md`

---

## Mapa de arquivos

| Arquivo | Responsabilidade |
|---------|------------------|
| `src/PrinterInstall.App/Services/IRememberedUserStore.cs` | Contrato load/save/clear |
| `src/PrinterInstall.App/Services/RememberedUserStore.cs` | Persistência JSON em LocalAppData |
| `src/PrinterInstall.App/ViewModels/LoginViewModel.cs` | `RememberMe`, `LoadRememberedUser()`, save/clear pós-login |
| `src/PrinterInstall.App/Views/LoginWindow.xaml` | Checkbox "Lembrar-me" |
| `src/PrinterInstall.App/Views/LoginWindow.xaml.cs` | Loaded → load + foco na senha |
| `src/PrinterInstall.App/App.xaml.cs` | Registro DI do store |
| `src/PrinterInstall.App/Strings/Login.pt-BR.xaml` | String `Login_RememberMeLabel` |
| `tests/PrinterInstall.App.Tests/Services/RememberedUserStoreTests.cs` | Testes do store |
| `tests/PrinterInstall.App.Tests/ViewModels/LoginViewModelRememberUserTests.cs` | Testes do ViewModel |

---

### Task 1: Contrato e record `RememberedUser`

**Files:**
- Create: `src/PrinterInstall.App/Services/IRememberedUserStore.cs`

- [ ] **Step 1: Criar o arquivo com contrato completo**

```csharp
namespace PrinterInstall.App.Services;

public sealed record RememberedUser(string DomainName, string UserName);

public interface IRememberedUserStore
{
    RememberedUser? Load();
    void Save(RememberedUser user);
    void Clear();
}
```

- [ ] **Step 2: Verificar compilação**

Run: `dotnet build src/PrinterInstall.App/PrinterInstall.App.csproj -v q`

Expected: BUILD SUCCESS (store ainda não implementado — interface compila sozinha)

- [ ] **Step 3: Commit**

```bash
git add src/PrinterInstall.App/Services/IRememberedUserStore.cs
git commit -m "feat: add IRememberedUserStore contract for login remember-me"
```

---

### Task 2: `RememberedUserStore` com testes TDD

**Files:**
- Create: `src/PrinterInstall.App/Services/RememberedUserStore.cs`
- Create: `tests/PrinterInstall.App.Tests/Services/RememberedUserStoreTests.cs`

- [ ] **Step 1: Escrever testes que falham**

Criar `tests/PrinterInstall.App.Tests/Services/RememberedUserStoreTests.cs`:

```csharp
using PrinterInstall.App.Services;

namespace PrinterInstall.App.Tests.Services;

public class RememberedUserStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string _filePath;

    public RememberedUserStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "PrinterInstallTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _filePath = Path.Combine(_dir, "remembered-user.json");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // best effort cleanup
        }
    }

    private RememberedUserStore CreateSut() => new(_filePath);

    [Fact]
    public void Load_WhenFileMissing_ReturnsNull()
    {
        var sut = CreateSut();
        Assert.Null(sut.Load());
    }

    [Fact]
    public void Save_ThenLoad_ReturnsSameUser()
    {
        var sut = CreateSut();
        var expected = new RememberedUser("preventsenior.local", "admin.user");

        sut.Save(expected);
        var loaded = sut.Load();

        Assert.NotNull(loaded);
        Assert.Equal(expected.DomainName, loaded.DomainName);
        Assert.Equal(expected.UserName, loaded.UserName);
    }

    [Fact]
    public void Clear_AfterSave_LoadReturnsNull()
    {
        var sut = CreateSut();
        sut.Save(new RememberedUser("preventsenior.local", "admin.user"));

        sut.Clear();

        Assert.Null(sut.Load());
        Assert.False(File.Exists(_filePath));
    }

    [Fact]
    public void Save_OverwritesPrevious()
    {
        var sut = CreateSut();
        sut.Save(new RememberedUser("preventsenior.local", "user.a"));
        sut.Save(new RememberedUser("preventsenior.local", "user.b"));

        var loaded = sut.Load();

        Assert.NotNull(loaded);
        Assert.Equal("user.b", loaded.UserName);
    }

    [Fact]
    public void Load_WhenFileCorrupted_ReturnsNullAndDeletesFile()
    {
        File.WriteAllText(_filePath, "{ not valid json");
        var sut = CreateSut();

        var loaded = sut.Load();

        Assert.Null(loaded);
        Assert.False(File.Exists(_filePath));
    }

    [Fact]
    public void Load_WhenFieldsEmpty_ReturnsNullAndDeletesFile()
    {
        File.WriteAllText(_filePath, """{"domainName":"","userName":"x"}""");
        var sut = CreateSut();

        Assert.Null(sut.Load());
        Assert.False(File.Exists(_filePath));
    }
}
```

- [ ] **Step 2: Rodar testes e confirmar falha**

Run: `dotnet test tests/PrinterInstall.App.Tests/PrinterInstall.App.Tests.csproj --filter "FullyQualifiedName~RememberedUserStoreTests" -v n`

Expected: FAIL — `RememberedUserStore` não existe

- [ ] **Step 3: Implementação mínima**

Criar `src/PrinterInstall.App/Services/RememberedUserStore.cs`:

```csharp
using System.Text.Json;

namespace PrinterInstall.App.Services;

public sealed class RememberedUserStore : IRememberedUserStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly string _filePath;

    public RememberedUserStore()
        : this(DefaultFilePath())
    {
    }

    public RememberedUserStore(string filePath)
    {
        _filePath = filePath;
    }

    public RememberedUser? Load()
    {
        if (!File.Exists(_filePath))
            return null;

        try
        {
            var json = File.ReadAllText(_filePath);
            var dto = JsonSerializer.Deserialize<RememberedUserDto>(json, JsonOptions);
            if (dto is null ||
                string.IsNullOrWhiteSpace(dto.DomainName) ||
                string.IsNullOrWhiteSpace(dto.UserName))
            {
                TryDeleteFile();
                return null;
            }

            return new RememberedUser(dto.DomainName.Trim(), dto.UserName.Trim());
        }
        catch (JsonException)
        {
            TryDeleteFile();
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public void Save(RememberedUser user)
    {
        if (string.IsNullOrWhiteSpace(user.DomainName) || string.IsNullOrWhiteSpace(user.UserName))
            return;

        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var dto = new RememberedUserDto
            {
                DomainName = user.DomainName.Trim(),
                UserName = user.UserName.Trim()
            };
            var json = JsonSerializer.Serialize(dto, JsonOptions);
            File.WriteAllText(_filePath, json);
        }
        catch (IOException)
        {
            // persistência é conveniência; não bloqueia login
        }
    }

    public void Clear()
    {
        TryDeleteFile();
    }

    private void TryDeleteFile()
    {
        try
        {
            if (File.Exists(_filePath))
                File.Delete(_filePath);
        }
        catch (IOException)
        {
            // ignorar
        }
    }

    private static string DefaultFilePath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PrinterInstall");
        return Path.Combine(dir, "remembered-user.json");
    }

    private sealed class RememberedUserDto
    {
        public string DomainName { get; set; } = "";
        public string UserName { get; set; } = "";
    }
}
```

- [ ] **Step 4: Rodar testes e confirmar passagem**

Run: `dotnet test tests/PrinterInstall.App.Tests/PrinterInstall.App.Tests.csproj --filter "FullyQualifiedName~RememberedUserStoreTests" -v n`

Expected: PASS (6 testes)

- [ ] **Step 5: Commit**

```bash
git add src/PrinterInstall.App/Services/RememberedUserStore.cs tests/PrinterInstall.App.Tests/Services/RememberedUserStoreTests.cs
git commit -m "feat: persist remembered domain username to LocalAppData"
```

---

### Task 3: `LoginViewModel` — load e save/clear

**Files:**
- Modify: `src/PrinterInstall.App/ViewModels/LoginViewModel.cs`
- Create: `tests/PrinterInstall.App.Tests/ViewModels/LoginViewModelRememberUserTests.cs`

- [ ] **Step 1: Escrever testes que falham**

Criar `tests/PrinterInstall.App.Tests/ViewModels/LoginViewModelRememberUserTests.cs`:

```csharp
using System.Net;
using Microsoft.Extensions.Configuration;
using PrinterInstall.App.Services;
using PrinterInstall.App.ViewModels;
using PrinterInstall.Core.Auth;

namespace PrinterInstall.App.Tests.ViewModels;

public class LoginViewModelRememberUserTests
{
    private sealed class FakeLdapValidator : ILdapCredentialValidator
    {
        public bool Succeed { get; set; } = true;

        public Task<LdapValidationResult> ValidateAsync(
            string domainName,
            NetworkCredential credential,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Succeed
                ? LdapValidationResult.Success()
                : LdapValidationResult.Failure("ldap fail"));
    }

    private sealed class FakeRememberedUserStore : IRememberedUserStore
    {
        public RememberedUser? Stored { get; private set; }
        public int SaveCount { get; private set; }
        public int ClearCount { get; private set; }

        public RememberedUser? Load() => Stored;

        public void Save(RememberedUser user)
        {
            SaveCount++;
            Stored = user;
        }

        public void Clear()
        {
            ClearCount++;
            Stored = null;
        }
    }

    private static LoginViewModel CreateSut(
        FakeRememberedUserStore store,
        FakeLdapValidator? ldap = null,
        SessionContext? session = null)
    {
        ldap ??= new FakeLdapValidator();
        session ??= new SessionContext();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DomainName"] = "preventsenior.local"
            })
            .Build();

        return new LoginViewModel(ldap, session, config, store);
    }

    [Fact]
    public void LoadRememberedUser_SetsUserNameAndRememberMe()
    {
        var store = new FakeRememberedUserStore
        {
            Stored = new RememberedUser("preventsenior.local", "saved.user")
        };
        var sut = CreateSut(store);

        sut.LoadRememberedUser();

        Assert.Equal("saved.user", sut.UserName);
        Assert.True(sut.RememberMe);
    }

    [Fact]
    public void LoadRememberedUser_WhenNothingSaved_LeavesDefaults()
    {
        var store = new FakeRememberedUserStore();
        var sut = CreateSut(store);

        sut.LoadRememberedUser();

        Assert.Equal("", sut.UserName);
        Assert.False(sut.RememberMe);
    }

    [Fact]
    public async Task TryLoginAsync_WithRememberMe_CallsSave()
    {
        var store = new FakeRememberedUserStore();
        var session = new SessionContext();
        var sut = CreateSut(store, session: session);
        sut.UserName = "admin";
        sut.Password = "secret";
        sut.RememberMe = true;

        var result = await sut.TryLoginAsync();

        Assert.True(result.Success);
        Assert.Equal(1, store.SaveCount);
        Assert.Equal(0, store.ClearCount);
        Assert.NotNull(store.Stored);
        Assert.Equal("admin", store.Stored!.UserName);
        Assert.Equal("preventsenior.local", store.Stored.DomainName);
        Assert.NotNull(session.Credential);
    }

    [Fact]
    public async Task TryLoginAsync_WithoutRememberMe_CallsClear()
    {
        var store = new FakeRememberedUserStore
        {
            Stored = new RememberedUser("preventsenior.local", "old.user")
        };
        var sut = CreateSut(store);
        sut.UserName = "admin";
        sut.Password = "secret";
        sut.RememberMe = false;

        var result = await sut.TryLoginAsync();

        Assert.True(result.Success);
        Assert.Equal(0, store.SaveCount);
        Assert.Equal(1, store.ClearCount);
        Assert.Null(store.Stored);
    }

    [Fact]
    public async Task TryLoginAsync_WhenLdapFails_DoesNotTouchStore()
    {
        var store = new FakeRememberedUserStore
        {
            Stored = new RememberedUser("preventsenior.local", "old.user")
        };
        var ldap = new FakeLdapValidator { Succeed = false };
        var sut = CreateSut(store, ldap);
        sut.UserName = "admin";
        sut.Password = "wrong";
        sut.RememberMe = true;

        var result = await sut.TryLoginAsync();

        Assert.False(result.Success);
        Assert.Equal(0, store.SaveCount);
        Assert.Equal(0, store.ClearCount);
        Assert.NotNull(store.Stored);
    }
}
```

- [ ] **Step 2: Rodar testes e confirmar falha**

Run: `dotnet test tests/PrinterInstall.App.Tests/PrinterInstall.App.Tests.csproj --filter "FullyQualifiedName~LoginViewModelRememberUserTests" -v n`

Expected: FAIL — construtor de `LoginViewModel` não aceita `IRememberedUserStore`; propriedades/métodos ausentes

- [ ] **Step 3: Implementação mínima em `LoginViewModel.cs`**

Substituir o conteúdo da classe por:

```csharp
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Configuration;
using PrinterInstall.App.Resources;
using PrinterInstall.Core.Auth;
using PrinterInstall.App.Services;

namespace PrinterInstall.App.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly ILdapCredentialValidator _ldap;
    private readonly ISessionContext _session;
    private readonly IRememberedUserStore _rememberedUserStore;
    private readonly string _domainName;

    public LoginViewModel(
        ILdapCredentialValidator ldap,
        ISessionContext session,
        IConfiguration configuration,
        IRememberedUserStore rememberedUserStore)
    {
        _ldap = ldap;
        _session = session;
        _rememberedUserStore = rememberedUserStore;
        _domainName = (configuration["DomainName"] ?? "preventsenior.local").Trim();
    }

    [ObservableProperty]
    private string _userName = "";

    public string Password { get; set; } = "";

    [ObservableProperty]
    private bool _rememberMe;

    [ObservableProperty]
    private string? _errorMessage;

    public void LoadRememberedUser()
    {
        var remembered = _rememberedUserStore.Load();
        if (remembered is null)
            return;

        UserName = remembered.UserName;
        RememberMe = true;
    }

    public async Task<(bool Success, string? Error)> TryLoginAsync(CancellationToken cancellationToken = default)
    {
        ErrorMessage = null;
        if (string.IsNullOrWhiteSpace(UserName))
        {
            ErrorMessage = UiStrings.Login_Validation_DomainUserRequired;
            return (false, ErrorMessage);
        }

        var cred = new NetworkCredential(UserName, Password, _domainName);
        var result = await _ldap.ValidateAsync(_domainName, cred, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            ErrorMessage = result.ErrorMessage;
            return (false, result.ErrorMessage);
        }

        if (RememberMe)
            _rememberedUserStore.Save(new RememberedUser(_domainName, UserName));
        else
            _rememberedUserStore.Clear();

        _session.Credential = cred;
        _session.DomainName = _domainName;
        return (true, null);
    }
}
```

- [ ] **Step 4: Rodar testes e confirmar passagem**

Run: `dotnet test tests/PrinterInstall.App.Tests/PrinterInstall.App.Tests.csproj --filter "FullyQualifiedName~LoginViewModelRememberUserTests" -v n`

Expected: PASS (5 testes)

- [ ] **Step 5: Commit**

```bash
git add src/PrinterInstall.App/ViewModels/LoginViewModel.cs tests/PrinterInstall.App.Tests/ViewModels/LoginViewModelRememberUserTests.cs
git commit -m "feat: remember domain username on successful login"
```

---

### Task 4: DI, UI e strings

**Files:**
- Modify: `src/PrinterInstall.App/App.xaml.cs`
- Modify: `src/PrinterInstall.App/Views/LoginWindow.xaml`
- Modify: `src/PrinterInstall.App/Views/LoginWindow.xaml.cs`
- Modify: `src/PrinterInstall.App/Strings/Login.pt-BR.xaml`

- [ ] **Step 1: Registrar DI em `App.xaml.cs`**

Após a linha `builder.Services.AddSingleton<ISessionContext, SessionContext>();`, adicionar:

```csharp
builder.Services.AddSingleton<IRememberedUserStore, RememberedUserStore>();
```

- [ ] **Step 2: Adicionar string em `Login.pt-BR.xaml`**

Antes do fechamento `</ResourceDictionary>`, adicionar:

```xml
  <sys:String x:Key="Login_RememberMeLabel">Lembrar-me</sys:String>
```

- [ ] **Step 3: Checkbox em `LoginWindow.xaml`**

Após o `Border` do campo de senha (antes do `TextBlock` de `ErrorMessage`), inserir:

```xml
            <CheckBox
                Content="{DynamicResource Login_RememberMeLabel}"
                IsChecked="{Binding RememberMe}"
                Margin="0,12,0,0"
                FontSize="13"
                Foreground="#FF444444"/>
```

Ajustar altura da janela de `408` para `440` em `Height`, `MinHeight` e `MaxHeight`.

- [ ] **Step 4: Loaded handler em `LoginWindow.xaml.cs`**

Adicionar no construtor, após `InitializeComponent();`:

```csharp
        Loaded += OnLoaded;
```

Adicionar método:

```csharp
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _viewModel.LoadRememberedUser();
        PasswordBox.Focus();
    }
```

- [ ] **Step 5: Build completo**

Run: `dotnet build src/PrinterInstall.App/PrinterInstall.App.csproj -v q`

Expected: BUILD SUCCESS

- [ ] **Step 6: Rodar todos os testes App**

Run: `dotnet test tests/PrinterInstall.App.Tests/PrinterInstall.App.Tests.csproj -v n`

Expected: PASS (todos)

- [ ] **Step 7: Commit**

```bash
git add src/PrinterInstall.App/App.xaml.cs src/PrinterInstall.App/Views/LoginWindow.xaml src/PrinterInstall.App/Views/LoginWindow.xaml.cs src/PrinterInstall.App/Strings/Login.pt-BR.xaml
git commit -m "feat: add remember-me checkbox to login screen"
```

---

### Task 5: Verificação manual (critérios de aceite)

- [ ] **Step 1: Primeira abertura**

Abrir app → usuário vazio, checkbox desmarcado, senha vazia.

- [ ] **Step 2: Login com Lembrar-me**

Marcar checkbox, login OK → fechar app → reabrir → usuário preenchido, senha vazia, checkbox marcado, foco na senha.

- [ ] **Step 3: Login sem Lembrar-me**

Desmarcar checkbox, login OK → fechar app → reabrir → campos vazios.

- [ ] **Step 4: Inspecionar disco**

Verificar `%LocalAppData%\PrinterInstall\remembered-user.json` contém apenas `domainName` e `userName` — **sem senha**.

- [ ] **Step 5: Commit final (se houver ajustes)**

```bash
git add -A
git commit -m "fix: address remember-user manual verification findings"
```

(Só se Step 1–4 revelarem correções.)
