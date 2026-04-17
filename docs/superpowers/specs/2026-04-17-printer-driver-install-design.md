# Especificação de desenho — Instalação remota de drivers de impressora

**Data:** 2026-04-17  
**Estado:** Para revisão  
**Domínio de referência:** `preventsenior.local`  
**Relaciona-se com:** `2026-04-16-printer-remote-config-design.md` (substitui a regra “sem instalação” naquela spec para as marcas suportadas)

## 1. Objectivo

Tornar o fluxo actual de configuração de impressoras **capaz de instalar o driver** no computador alvo quando este não estiver presente, usando **pacotes INF embutidos** na aplicação (um por marca). O comportamento existente continua íntegro quando o driver **já** existe; só quando falta é que entra o novo passo.

A regra anterior “**abortar** o alvo quando o driver não está instalado” mantém-se como **fallback** — quando **não houver módulo local** para a marca, ou quando o canal remoto não suportar instalação (fluxo em §4.3, casos `AbortedDriverMissing`).

## 2. Decisões de âmbito (resumo do brainstorming)

| # | Decisão | Valor |
|---|---|---|
| 1 | Fonte dos drivers | **Embutidos ao lado do executável** |
| 2 | Formato de distribuição | **Instalador modular por marca** |
| 3 | Comportamento quando módulo local está ausente | **Não bloquear** UI; tentar fluxo remoto e abortar apenas no alvo (igual ao actual) |
| 4 | Formato canónico do pacote | **Pasta com `.inf` + `.cat` + ficheiros associados** (extraídos do DriverStore via `Get-PrinterDriver` → `Split-Path` → cópia da pasta `FileRepository`) |
| 5 | Suporte em canal CIM/WMI puro | **Sim, via SMB + `Win32_Process.Create`** |
| 6 | Versionamento dos ficheiros | **Git normal** (sem LFS), pasta `drivers/` no topo do repositório |
| 7 | Empacotamento no build | **`csproj` + `Content`** (instalador WiX modular fica para segunda entrega) |

## 3. Fonte e estrutura dos pacotes

### 3.1 Árvore no repositório

```
drivers/
├─ Epson/     (E_JFB0DE.INF + E_JFB0DE.CAT + BIN/CFX/USX + WINX64\)    ~113 MB
├─ Lexmark/   (LMUX1l50.inf + LMUX1L50.cat + amd64\ + LMUX1\ loc.)      ~34 MB
└─ Gainscha/  (Gprinter.inf + Gprinter.cat + Common\ + x64\)            ~30 MB
```

**Total:** ≈ 178 MB versionados no git. Aceite conscientemente; reavaliar Git LFS se o repositório crescer ou se surgirem atualizações frequentes de pacotes.

### 3.2 Árvore no executável publicado

```
<pasta-do-exe>/
├─ PrinterInstall.App.exe
└─ Drivers/
   ├─ Epson/...
   ├─ Lexmark/...
   └─ Gainscha/...
```

### 3.3 Arquitetura dos alvos

Todos os pacotes incluídos são **x64**. Alvos Windows x86 não são suportados nesta entrega (consistente com o parque actual). Se no futuro for necessário, acrescentam-se sub-pastas por arquitetura dentro de cada marca e a descoberta (§4.1) passa a ler a sub-pasta adequada.

## 4. Arquitectura da funcionalidade

### 4.1 Descoberta local (`LocalDriverPackageCatalog`)

Novo tipo em `PrinterInstall.Core.Drivers`:

```csharp
public sealed record LocalDriverPackage(
    PrinterBrand Brand,
    string RootFolder,
    string InfFileName,
    string ExpectedDriverName);

public interface ILocalDriverPackageCatalog
{
    LocalDriverPackage? TryGet(PrinterBrand brand);
}
```

Implementação por defeito: `LocalDriverPackageCatalog` procura em `AppContext.BaseDirectory\Drivers\<Marca>\`, selecciona o **primeiro `.inf` no topo** da pasta (ignora subdiretorias como `amd64/`, `Common/`, `WINX64/`, `LMUX1/`). Se a pasta não existir ou não tiver `.inf`, devolve `null`.

O `ExpectedDriverName` vem do catálogo existente (`PrinterCatalog.GetExpectedDriverName(brand)`), para manter uma única fonte de verdade sobre o nome esperado.

Uma segunda implementação, `NullLocalDriverPackageCatalog`, devolve sempre `null` — usada em testes e para manter retro-compatibilidade de construção.

### 4.2 Instalação remota (`IRemotePrinterOperations`)

Novo método no contrato existente, com **default** que lança `NotImplementedException` para manter retro-compatibilidade:

```csharp
Task InstallPrinterDriverAsync(
    string computerName,
    NetworkCredential credential,
    LocalDriverPackage package,
    IProgress<string>? log,
    CancellationToken cancellationToken = default)
    => throw new NotImplementedException();
```

**Implementação em `WinRmRemotePrinterOperations`:**

1. Abre sessão PS Remoting ao alvo (reutiliza `IPowerShellInvoker`).
2. `Copy-Item -ToSession` copia a pasta `package.RootFolder` para `C:\Windows\Temp\PrinterInstall\<guid>\` no alvo.
3. `Invoke-Command`:
   - `pnputil.exe /add-driver "<inf>" /install`
   - `Add-PrinterDriver -Name "<ExpectedDriverName>"` (necessário para disponibilizar o driver a `Add-Printer`; o `pnputil` por si só regista-o no DriverStore).
4. Limpa a pasta temp em `finally`, mesmo em erro.

**Implementação em `CimRemotePrinterOperations`:**

1. **Copiar via SMB:** monta `\\<computer>\ADMIN$\Temp\PrinterInstall\<guid>\` com a `NetworkCredential` via `WNetUseConnection` (P/Invoke a `mpr.dll`). Copia a árvore com `robocopy /E /R:1 /W:1` ou `Directory.EnumerateFiles` + `File.Copy`. Desmonta com `WNetCancelConnection2(force=true)` em `finally`.
2. **Executar no alvo:** chama `Win32_Process.Create` via WMI:
   ```
   cmd.exe /c "pnputil.exe /add-driver C:\Windows\Temp\PrinterInstall\<guid>\<inf> /install > C:\Windows\Temp\PrinterInstall\<guid>\out.log 2>&1"
   ```
   O WMI devolve o **PID**; aguardamos terminação via `__InstanceDeletionEvent` (WQL) com timeout ou, em alternativa, polling de `SELECT * FROM Win32_Process WHERE ProcessId = <pid>`.
3. **Recolher resultado:** após o processo sair, lê `out.log` via SMB para extrair a última linha útil e decidir sucesso/erro com base na mensagem/exit implícito.
4. **Registar o driver** para o spooler: numa segunda chamada `Win32_Process.Create` no alvo, correr `rundll32.exe printui.dll,PrintUIEntry /ia /m "<ExpectedDriverName>" /f "<inf>"` (via o mesmo padrão do passo 2, com redirecção para um `out2.log`). Esta via é consistente com o canal CIM (sem PSRemoting) e evita depender de `Win32_PrinterDriver.AddPrinterDriver`, que exige caminhos locais ao alvo já conhecidos pelo spooler.
5. **Limpeza:** remove a pasta temp do alvo via SMB em `finally`.

**Composição:** `CompositeRemotePrinterOperations` continua a delegar no operador escolhido; ambos passam agora a implementar `InstallPrinterDriverAsync`.

### 4.3 Novo fluxo no `PrinterDeploymentOrchestrator`

Dois estados acrescentados a `TargetMachineState` (sem remover os existentes):

- `InstallingDriver`
- `DriverInstalledReconfirming`

Fluxo:

```
ContactingRemote → ValidatingDriver
  ├─ driver já presente → Configuring → CompletedSuccess
  └─ driver em falta
       ├─ ILocalDriverPackageCatalog.TryGet(brand) == null → AbortedDriverMissing (igual ao actual)
       └─ pacote local disponível
            ├─ InstallingDriver → IRemotePrinterOperations.InstallPrinterDriverAsync
            │    ├─ sucesso → DriverInstalledReconfirming → revalidação com DriverNameMatcher
            │    │    ├─ match → Configuring → CompletedSuccess
            │    │    └─ sem match → AbortedDriverMissing ("esperado X, encontrado [amostra]")
            │    ├─ NotImplementedException → AbortedDriverMissing ("install unsupported on this channel")
            │    └─ exceção genérica → Error (mensagem `pnputil`/WMI achatada)
```

O construtor do orquestrador aceita agora `ILocalDriverPackageCatalog`. É criado um construtor sobrecarregado sem o catálogo, que injecta `NullLocalDriverPackageCatalog` (retro-compatibilidade com testes existentes).

A revalidação pós-instalação usa **o mesmo** `DriverNameMatcher` já existente, o que naturalmente cobre as variações de nome reportadas pelo Windows (ex.: Gainscha pode surgir como *“Gainscha GA-2408T”* mas também com sufixos dependentes do pacote).

## 5. Erros, timeouts e limpeza

### 5.1 Mensagens por causa

| Causa | Mensagem mostrada ao utilizador |
|---|---|
| Módulo local ausente | *“Driver not installed on `<host>`: `<expected>`”* (actual) |
| Falha `Copy-Item -ToSession` (WinRM) | *“Falha a copiar drivers para `<host>`: `<detalhe>`”* → aborta alvo |
| Falha `WNetUseConnection` (SMB) | *“Partilha ADMIN$ inacessível em `<host>` (erro `<n>`)”* → aborta alvo |
| Falha `Win32_Process.Create` | *“Não foi possível iniciar pnputil em `<host>` (WMI return `<n>`)”* → aborta alvo |
| `pnputil` com código ≠ 0 | *“pnputil falhou (exit `<n>`): `<última linha do log>`”* → aborta alvo |
| Revalidação falha após instalação | *“Driver instalado não corresponde ao esperado: esperado `<A>`, encontrado [`<amostra>`]”* → aborta alvo |

Nenhum erro interrompe a corrida — apenas o alvo corrente, coerente com o comportamento actual do orquestrador.

### 5.2 Timeouts

| Etapa | Limite | Nota |
|---|---|---|
| Cópia (WinRM ou SMB) | 5 min | Epson ~113 MB é o pior caso. Respeita `CancellationToken`. |
| `pnputil` no alvo | 10 min | Em esgotamento, `Win32_Process.Terminate(pid)` + aborto. |
| Revalidação | igual ao actual | Reutiliza `GetInstalledDriverNamesAsync`. |

Valores definidos como constantes `TimeSpan` no orquestrador nesta primeira entrega; expostos como configuração se futuramente for necessário.

### 5.3 Limpeza

- Pasta temp `C:\Windows\Temp\PrinterInstall\<guid>\` no alvo é sempre removida em `finally`; falha de limpeza gera *warning* no log, não aborta o alvo se o driver ficou instalado.
- Montagem SMB desfeita com `WNetCancelConnection2(force=true)` em `finally`.
- Driver **permanece** instalado mesmo que a criação de porta/fila a seguir falhe — propositado (cache no DriverStore acelera a próxima tentativa).

## 6. Pré-requisitos de rede

### 6.1 Canal WinRM (preferido)

Os já documentados em `2026-04-16-printer-remote-config-design.md` (portas 5985/5986, PSRemoting habilitado, etc.). Sem mudanças.

### 6.2 Canal CIM/WMI (novo suporte à instalação)

- **SMB (445/TCP)** aberto no alvo; `File and Printer Sharing` habilitado; partilha administrativa `ADMIN$` disponível.
- **WMI** habilitado (já requisito existente do canal CIM).
- Em alvos **standalone** (não domínio): `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System\LocalAccountTokenFilterPolicy = 1` (mesma restrição do canal CIM actual).
- Credencial de domínio com permissão de escrita em `ADMIN$` e de criar processos via WMI (equivalente a “administrador local” no alvo, como já exigido hoje).

Se qualquer pré-condição falhar, o comportamento é **abortar este alvo** com a mensagem específica da §5.1, sem parar a corrida.

## 7. Build e empacotamento

### 7.1 `PrinterInstall.App.csproj`

Acrescentar:

```xml
<ItemGroup>
  <Content Include="..\..\drivers\**\*">
    <Link>Drivers\%(RecursiveDir)%(Filename)%(Extension)</Link>
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </Content>
</ItemGroup>
```

Resultado: cada build coloca os pacotes em `bin\...\Drivers\<Marca>\`.

### 7.2 Instalador modular

Fora do âmbito desta entrega. Quando for criado (WiX/MSI), cada marca é uma *feature* que copia `drivers\<Marca>\` para `<InstallDir>\Drivers\<Marca>\`. A lógica em runtime não muda — continua a procurar em `AppContext.BaseDirectory\Drivers\…`.

## 8. Testes

### 8.1 `LocalDriverPackageCatalogTests`

- fixture `tests/PrinterInstall.Core.Tests/TestDrivers/Fake/fake.inf` → `TryGet(brand)` devolve pacote.
- pasta inexistente → `null`.
- pasta existe mas sem `.inf` no topo → `null` (mesmo que haja `.inf` em sub-pastas).
- `.pnf`/`.cat` não são confundidos com `.inf`.

### 8.2 `PrinterDeploymentOrchestratorDriverInstallTests`

Com um `FakeRemotePrinterOperations` injectável:

- **A)** driver ausente + pacote local + instalação OK + revalidação OK → `CompletedSuccess`; transições observadas incluem `InstallingDriver` e `DriverInstalledReconfirming`.
- **B)** driver ausente + instalação OK + revalidação falha → `AbortedDriverMissing` com mensagem “esperado X, encontrado Y”.
- **C)** driver ausente + pacote local ausente → `AbortedDriverMissing` (caminho legado intacto).
- **D)** `InstallPrinterDriverAsync` lança `NotImplementedException` → `AbortedDriverMissing` com sufixo *“install unsupported on this channel”*.
- **E)** instalação lança exceção genérica → `Error`; corrida continua nos próximos alvos.
- **F)** cancelamento a meio da instalação → `OperationCanceledException`; idêntico ao actual.

### 8.3 Unit tests utilitários

- Parser da última linha útil de `out.log` para compor a mensagem *“pnputil falhou (exit N): …”*.

### 8.4 Integração real

Validação em máquina de referência com os três pacotes, em ambos os canais (WinRM e CIM/SMB), executada manualmente. O *checklist* de validação (passos exactos, comandos `pnputil`/`Get-PrinterDriver` para confirmação) é escrito como anexo operacional no plano de implementação, não nesta spec. Não entra em CI.

## 9. Fora de âmbito

- Instalador silencioso de fabricante (`.exe`/MSI). A app só consome **pacotes INF locais**.
- Drivers vindos de partilhas de rede ou URLs externas (pode ser uma entrega futura, cf. opção C/D descartadas no brainstorming).
- Arquitetura x86/ARM64 (cf. §3.3).
- Instalador WiX modular da app (cf. §7.2).
- Rollback automático do driver em caso de erro posterior — o driver permanece registado no DriverStore por design.
