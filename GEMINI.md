# Regras do Projeto — PrinterInstall

## Documentação Técnica de Referência

Antes de realizar qualquer ação neste projeto (implementação, correção, refatoração, análise ou resposta a perguntas), você **deve** consultar a documentação técnica de referência localizada em:

```
C:\Users\Admin\.gemini\antigravity\brain\c8367978-fd29-4a86-af0e-61c944a93537\technical_documentation.md
```

Essa documentação contém:
- Arquitetura completa da solução (Core + App + Tests)
- Módulos, interfaces, classes e seus propósitos
- Design patterns utilizados
- Fluxo de deploy, rollback e remoção
- Modelos de domínio e enums de estado
- Estrutura de ViewModels, Views, Converters e Services
- Dependências NuGet e configurações
- Drivers suportados (Epson, Gainscha, Lexmark)
- Testes existentes e padrões de teste
- Scripts auxiliares e processo de publicação

## Diretrizes Gerais

- **Linguagem do código:** Nomeie componentes (classes, métodos, propriedades, variáveis) em inglês. Comentários e mensagens de UI devem ser em português brasileiro (pt-BR).
- **Framework:** .NET 8 (`net8.0-windows`), WPF com WPF-UI, CommunityToolkit.Mvvm.
- **Padrões:** Siga os design patterns já estabelecidos no projeto — MVVM, Strategy, Orchestrator, Saga/Rollback, Router/Proxy, Result Pattern, DI via `Microsoft.Extensions.Hosting`.
- **Testes:** Use xUnit + Moq. Mantenha o padrão Arrange-Act-Assert. Novos módulos devem ter testes correspondentes.
- **Consistência:** Ao criar novas classes de resultado, siga o padrão `sealed class` com factory methods `Success()` / `Failure(message)`. Ao criar novos models, use `sealed record` quando imutável.
- **Gainscha:** Respeite o protocolo SSDAL/SDS. Arquivos SDS devem ser UTF-8 sem BOM. Scripts headless não devem conter chamadas Win32 bloqueantes.
- **Remote:** Novas operações remotas devem passar pelo `RoutingRemotePrinterOperations` para roteamento automático local/remoto. Considere escalação UAC via scheduled task.
- **Rollback:** Operações que criam recursos (portas, filas) devem registrar no `DeploymentRollbackJournal` para permitir reversão automática.
