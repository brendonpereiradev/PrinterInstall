# Especificação de desenho — Validação Lexmark Universal v2 e v4 XL

**Data:** 2026-05-04  
**Estado:** Rascunho — aguarda revisão do utilizador antes do plano de implementação  

## 1. Contexto e objectivo

Em parte dos computadores da organização já existe o driver universal Lexmark, mas em versões diferentes da embalagem por defeito da ferramenta: **`Lexmark Universal v2 XL`** em vez de **`Lexmark Universal v4 XL`**. O v2 é aceitável para criar filas TCP/IP nas mesmas condições que o v4.

O código actual (`PrinterCatalog` + `DriverNameMatcher`) só reconhece o nome **v4**, pelo que alvos apenas com **v2** falham na validação e não chegam a configurar a impressora.

**Objectivo:** tratar **v2** e **v4** como drivers Lexmark **válidos** para validação e usar na criação da fila o nome **realmente instalado**, com **preferência pelo v4** quando ambos existirem.

## 2. Requisitos funcionais

### 2.1 Nomes aceites (Lexmark)

Considerar o driver Lexmark **presente** no alvo se existir na lista remota de drivers instalados uma entrada cujo nome, após `Trim`, corresponda **exactamente** (comparação sem distinção de maiúsculas/minúsculas) a **um** dos seguintes:

| Ordem de preferência para uso na fila | Nome exacto no Windows |
| ------------------------------------- | ----------------------- |
| 1 (preferido)                         | `Lexmark Universal v4 XL` |
| 2                                     | `Lexmark Universal v2 XL` |

**Epson** e **Gainscha** mantêm um único nome esperado cada um; regra de correspondência inalterada (igualdade normalizada como hoje).

### 2.2 Resolução do nome ao criar a impressora

Ao invocar a operação remota que adiciona a fila (`AddPrinterAsync` ou equivalente), o argumento **driver** deve ser:

1. **`Lexmark Universal v4 XL`** se esse nome existir na lista de drivers instalados do alvo nessa fase do fluxo;
2. caso contrário, **`Lexmark Universal v2 XL`** se existir na lista;
3. caso contrário, o fluxo já deve ter falhado na validação (não criar fila sem driver resolvido).

### 2.3 Instalação remota quando falta driver

Quando a funcionalidade de instalação automática do pacote local estiver activa para Lexmark:

- O pacote continua a ser o da linha **v4** (comportamento actual do repositório).
- Após instalar, a reconfirmação deve aceitar **`Lexmark Universal v4 XL`** como válido (como hoje), sem exigir o v2.

Mensagens de erro quando o driver “não corresponde” ao esperado devem referir explicitamente que, para Lexmark, são aceites **v2 ou v4** (ou listar ambos os nomes), para reduzir confusão em suporte.

### 2.4 Fora de âmbito

- Outras variantes de nome (por exemplo **v1**, regionalizações ou sufixos não confirmados) **não** são aceites até existir confirmação do nome exacto reportado pelo Windows nos alvos.
- **Não** se adopta correspondência por prefixo ou expressão regular genérica apenas com “Lexmark Universal”; mantém-se lista fechada para evitar falsos positivos.

## 3. Componentes e alterações previstas

| Área | Alteração |
| ---- | --------- |
| Catálogo (`PrinterCatalog` ou API equivalente) | Expor para Lexmark uma lista **ordenada** de nomes aceites e a ordem de preferência para resolução; marcas restantes continuam com um único nome. |
| `DriverNameMatcher` (ou API nova) | Função do tipo “existe algum dos nomes aceites na lista instalada?” mantendo igualdade exacta por nome (trim + ordinal ignore case). |
| `PrinterDeploymentOrchestrator` | Validar com o conjunto aceite; resolver `driverNameForQueue` conforme §2.2; passar esse valor a `AddPrinterAsync`; alinhar ramos de instalação/reconfirmação com múltiplos nomes aceites para Lexmark. |
| Testes unitários | Casos: só v2, só v4, ambos (deve escolher v4 para a fila), nenhum dos dois; regressão Epson/Gainscha. |

## 4. Compatibilidade com especificações anteriores

O documento `2026-04-16-printer-remote-config-design.md` §3.4 lista apenas **v4** para Lexmark. Após implementação deste desenho, a tabela Lexmark deve ser entendida como **dois nomes aceites** com a preferência definida em §2.2; o restante do fluxo (lista remota de drivers, igualdade por nome) mantém-se.

## 5. Critérios de verificação

- Em alvo com apenas `Lexmark Universal v2 XL` instalado, o deploy Lexmark **conclui** criação de porta/fila usando esse nome de driver.
- Em alvo com ambos v2 e v4, a fila nova usa **v4**.
- Em alvo sem v2 nem v4, o comportamento de falha permanece coerente com o produto actual (abortar com mensagem clara).
- Testes automatizados cobrem os cenários da §3.
