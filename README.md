# PrinterInstall

Aplicativo desktop para Windows que instala impressoras de rede e etiquetadoras térmicas em várias estações de trabalho de uma vez. Foi construído para ambientes hospitalares, onde o suporte de TI precisa padronizar filas de impressão em muitos computadores sem tocar em cada máquina manualmente.

> **Idiomas:** Português (este arquivo) · [English](README.en.md)

## O que ele faz

Você informa os computadores alvo e as filas que quer criar. O PrinterInstall cuida do resto em cada máquina:

- Instala o driver correto no Windows a partir de um pacote embutido, sem CD nem download manual.
- Cria a porta TCP/IP da impressora.
- Cria e nomeia a fila no padrão que você definir.
- Calibra o tamanho da etiqueta em impressoras térmicas Gainscha.
- Desfaz portas e filas incompletas quando algo falha no meio do processo.

Marcas testadas: **Epson**, **Lexmark**, **Brother** e **Gainscha**. Outros modelos do mesmo fabricante costumam funcionar. Consulte [`MODELOS_TESTADOS.txt`](MODELOS_TESTADOS.txt) para a lista validada.

## Recursos

| Recurso | Descrição |
| --- | --- |
| Deploy em lote | Instala uma ou várias filas em vários computadores ao mesmo tempo, com status por máquina em tempo real. |
| Rollback automático | Reverte as portas e filas criadas quando uma máquina falha, deixando a estação limpa. |
| Assistente de controle | Lista, remove e renomeia filas em máquinas remotas sem reinstalar o driver. |
| Teste de rede direto | Valida a porta raw 9100 e imprime uma página ou etiqueta de teste antes do deploy. |
| Presets Gainscha | Quatro tamanhos de etiqueta prontos: Paciente, Matrix, Pulseira e Lote. |
| Login por domínio | Autentica pelo Active Directory (UPN ou NetBIOS) via LDAP. |
| Exportação de logs | Salva um relatório de tudo que foi instalado. |
| Escalação UAC | Executa operações remotas elevadas via scheduled task quando necessário. |

## Arquitetura

A solução tem duas camadas e testes para cada uma.

```
src/
  PrinterInstall.Core/   Lógica de domínio: drivers, remoto, orquestração, rollback, rede, auth
  PrinterInstall.App/    Interface WPF (MVVM): views, view models, serviços de UI
tests/
  PrinterInstall.Core.Tests/
  PrinterInstall.App.Tests/
```

**Stack:** .NET 8 (`net8.0-windows`), WPF com [WPF-UI](https://github.com/lepoco/wpfui), [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet), injeção de dependência via `Microsoft.Extensions.Hosting`, `System.Management` para WMI/CIM e `System.DirectoryServices.Protocols` para LDAP.

**Padrões:** MVVM, Strategy, Orchestrator, Saga/Rollback, Router/Proxy, Result Pattern. Operações remotas passam pelo `RoutingRemotePrinterOperations`, que decide entre execução local e remota. Toda operação que cria recursos registra no `DeploymentRollbackJournal` para permitir a reversão.

## Como compilar e executar

Você precisa do [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) e do Windows.

```powershell
git clone https://github.com/brendonpereiradev/PrinterInstall
cd PrinterInstall
dotnet build PrinterInstall.sln
dotnet run --project src/PrinterInstall.App
```

Rodar os testes:

```powershell
dotnet test PrinterInstall.sln
```

Gerar um executável único e autocontido (empacota o runtime .NET, os drivers e a configuração):

```powershell
pwsh scripts/Publish-PrinterInstall.ps1
```

O resultado sai em `publish/PrinterInstall`.

## Uso

1. **Login.** Entre com sua conta de domínio no formato `usuario@dominio` ou `DOMINIO\usuario`. As credenciais não ficam salvas depois que você fecha o programa.
2. **Alvos.** Adicione os computadores por nome de rede ou IP. O botão "Adicionar Este PC" inclui a máquina local. Colar uma lista adiciona todas de uma vez.
3. **Filas.** Escolha a marca, informe o IP da impressora e o nome da fila. Para Gainscha, selecione o preset de etiqueta.
4. **Deploy.** Inicie a instalação e acompanhe o status de cada máquina. Ao final, exporte o relatório.

O [Manual do Usuário](MANUAL_DO_USUARIO.md) traz o passo a passo completo, a tabela de presets Gainscha e a resolução dos erros mais comuns.

## Referência de etiquetas Gainscha

| Preset | Tamanho | Uso no hospital |
| --- | :---: | --- |
| Paciente | 89 × 36 mm | Fichas, prontuários e leitos |
| Matrix | 50 × 30 mm | Tubos de coleta e frascos de exame |
| Pulseira | 25 × 270 mm | Pulseira de identificação do paciente |
| Lote | 45 × 13 mm | Medicamentos e almoxarifado |

Confirme qual rolo está na impressora antes de instalar a fila. Um preset maior que a etiqueta física faz a impressão sair da margem.

## Documentação

- [Manual do Usuário](MANUAL_DO_USUARIO.md) — guia para técnicos de suporte
- [Modelos testados](MODELOS_TESTADOS.txt) — impressoras validadas por marca
- [GEMINI.md](GEMINI.md) — regras e diretrizes de desenvolvimento
