# Lembrar usuário no login

**Data:** 2026-06-25  
**Status:** Aprovado para plano de implementação  
**Relacionado:** `2026-04-16-printer-remote-config-design.md` (autenticação LDAP e `ISessionContext`)

## Objetivo

Permitir que o operador **não precise redigitar o nome de usuário de domínio** a cada abertura do Printer Install, mediante opt-in explícito via checkbox **"Lembrar-me"**. A **senha nunca é persistida** — continua obrigatória em todo login.

## Contexto

Hoje o fluxo é:

1. `App.xaml.cs` abre sempre `LoginWindow` na inicialização.
2. `LoginViewModel.TryLoginAsync` valida LDAP e grava `NetworkCredential` em `SessionContext` (singleton em memória).
3. Ao fechar e reabrir a aplicação, `SessionContext` é recriado vazio e o usuário digita tudo de novo.

A spec original (`2026-04-16`) exigia credenciais **apenas em memória** e proibia persistir senhas. Esta feature **mantém a proibição de senha em disco** e adiciona persistência **somente do username**, com consentimento do usuário.

## Decisões registradas

| Decisão | Escolha |
|---------|---------|
| Controle | Checkbox **"Lembrar-me"** opt-in a cada login bem-sucedido |
| Na reabertura | Pré-preenche **somente o usuário**; senha vazia; foco no campo senha |
| Auto-login | **Não** — sempre exige senha + clique em "Entrar" |
| Login sem "Lembrar-me" | **Apaga** o usuário salvo |
| Login com outro usuário + "Lembrar-me" | **Sobrescreve** o usuário salvo |
| Botão "Esquecer" | **Não** |
| O que persiste | `{ domainName, userName }` — **sem senha** |
| Criptografia | **Não necessária** (username de domínio não é segredo) |
| Armazenamento | JSON em `%LocalAppData%\PrinterInstall\remembered-user.json` |

## Fora de âmbito

- Persistência de senha (DPAPI, Credential Manager, etc.)
- Login automático sem interação
- TTL / expiração automática do usuário salvo
- Botão ou link "Esquecer credenciais" na tela de login
- Logout ou gestão de sessão na `MainWindow`
- Múltiplos usuários salvos simultaneamente
- Alterações em `PrinterInstall.Core` (LDAP permanece inalterado)

## Arquitetura

```text
App_OnStartup
  └── DI: IRememberedUserStore (Singleton)

LoginWindow (Loaded)
  └── LoginViewModel.LoadRememberedUser()
        └── IRememberedUserStore.Load() → preenche UserName + RememberMe=true

LoginViewModel.TryLoginAsync()
  ├── LDAP validate (inalterado)
  ├── se RememberMe → Save({ domain, userName })
  ├── se !RememberMe → Clear()
  └── SessionContext.Credential = NetworkCredential (em memória, como hoje)

SessionContext
  └── continua in-memory; sem persistência
```

Backend de operações remotas (**WMI, SMB, orquestradores**) permanece inalterado.

## Componentes

### `IRememberedUserStore` + `RememberedUserStore`

Local: `PrinterInstall.App/Services/`

```csharp
public sealed record RememberedUser(string DomainName, string UserName);

public interface IRememberedUserStore
{
    RememberedUser? Load();
    void Save(RememberedUser user);
    void Clear();
}
```

**Implementação:**

- Diretório: `%LocalAppData%\PrinterInstall\` (criar se não existir).
- Arquivo: `remembered-user.json`.
- Conteúdo JSON: `{ "domainName": "...", "userName": "..." }`.
- `Load()`: se arquivo ausente → `null`. Se JSON inválido ou campos vazios → apagar arquivo e retornar `null`.
- `Save()`: sobrescreve atomicamente (escrever em temp + move, ou write direto — escolha do implementador).
- `Clear()`: apagar arquivo se existir; no-op se ausente.
- Construtor aceita `filePath` opcional para testes unitários (injetar caminho temporário).

### `LoginViewModel`

Alterações:

- Injetar `IRememberedUserStore` e manter `IConfiguration` para `DomainName`.
- Propriedade `RememberMe` (`bool`, default `false`).
- Método `LoadRememberedUser()`: chama `Load()`; se retornar valor, define `UserName` e `RememberMe = true`.
- `TryLoginAsync` após LDAP bem-sucedido:
  - `RememberMe == true` → `Save(new RememberedUser(_domainName, UserName))`
  - `RememberMe == false` → `Clear()`
- Demais lógica inalterada.

### `LoginWindow.xaml`

- Adicionar `CheckBox` **"Lembrar-me"** abaixo do campo de senha, com binding `IsChecked="{Binding RememberMe}"`.
- Ajustar altura da janela se necessário para acomodar o checkbox (~24–32 px extras).

### `LoginWindow.xaml.cs`

- Handler `Loaded` (ou após `InitializeComponent` no construtor): chamar `_viewModel.LoadRememberedUser()` e posicionar foco no `PasswordBox`.

### `App.xaml.cs`

- Registrar `builder.Services.AddSingleton<IRememberedUserStore, RememberedUserStore>();`

### Strings de UI

Adicionar em recursos pt-BR (`UiStrings` / `.resx`):

- `Login_RememberMeLabel` → `"Lembrar-me"`

## Tratamento de erros

| Cenário | Comportamento |
|---------|---------------|
| Arquivo corrompido ou JSON inválido | Apagar arquivo silenciosamente; abrir login vazio |
| `Save()` falha (disco, permissão) | Login **continua** (LDAP já passou); falha ignorada |
| LDAP falha com usuário pré-preenchido | Exibir `ErrorMessage`; **não** apagar usuário salvo |
| `Clear()` falha | Ignorar; risco aceitável de usuário antigo na próxima abertura |

**Princípio:** falhas de persistência nunca bloqueiam o login.

## Testes

Projeto existente: `tests/PrinterInstall.App.Tests/`

### `RememberedUserStoreTests`

| Teste | Valida |
|-------|--------|
| `Load_WhenFileMissing_ReturnsNull` | Primeira execução |
| `Save_ThenLoad_ReturnsSameUser` | Round-trip |
| `Clear_AfterSave_LoadReturnsNull` | Limpeza |
| `Save_OverwritesPrevious` | Substituição por novo usuário |
| `Load_WhenFileCorrupted_ReturnsNullAndDeletesFile` | Recuperação de arquivo inválido |

Usar `Path.GetTempFileName()` ou diretório temporário via construtor com `filePath` customizado.

### `LoginViewModelRememberUserTests` (mock do store)

| Teste | Valida |
|-------|--------|
| `LoadRememberedUser_SetsUserNameAndRememberMe` | Pré-preenchimento no startup |
| `TryLoginAsync_WithRememberMe_CallsSave` | Persistência após login |
| `TryLoginAsync_WithoutRememberMe_CallsClear` | Limpeza após login |

Mock de `ILdapCredentialValidator` retornando sucesso; mock de `IRememberedUserStore` verificando chamadas.

Sem testes E2E de UI WPF ou LDAP real nesta feature.

## Critérios de aceite

1. Primeira abertura: campos vazios, checkbox desmarcado.
2. Login com "Lembrar-me" marcado → reabrir app → usuário pré-preenchido, senha vazia, checkbox marcado, foco na senha.
3. Login **sem** "Lembrar-me" → reabrir app → campos vazios.
4. Senha nunca aparece em disco (inspecionar `%LocalAppData%\PrinterInstall\remembered-user.json`).
5. Operações remotas após login funcionam como antes (`SessionContext` populado igual).
