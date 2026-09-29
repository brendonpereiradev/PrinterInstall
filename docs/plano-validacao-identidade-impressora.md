# Plano de implementação: identificação e compatibilidade de impressoras

Data: 29/09/2026  
Status: implementação de código concluída; homologação das demais marcas em equipamentos reais pendente.

O código atual contém consulta SNMP/IPP, comparação de marca/modelo e bloqueio antes das alterações remotas. As listas abaixo registram o plano original; a homologação completa de hardware continua pendente. Os detalhes de operação estão no `README.md`.

Verificação real em 29/09/2026: a consulta somente de leitura a `192.0.2.216` retornou `Lexmark CX532ADWE` por `SNMP sysDescr`. O utilitário de diagnóstico bloqueou a seleção Epson e aceitou Lexmark. Não foi executada instalação em máquina-alvo nessa verificação.

## Objetivo

Consultar a impressora no endereço informado e impedir a configuração nas máquinas-alvo quando o fabricante ou modelo identificado for incompatível com o driver selecionado. Aplicar a proteção a Epson, Lexmark, Brother e Gainscha, em ambos os sentidos de qualquer combinação de marcas.

O requisito é geral e vale para qualquer IP informado, em cada linha de configuração e em todas as execuções. A identidade deve ser obtida consultando o equipamento que responde naquele endereço e comparada com a seleção correspondente feita pelo usuário. Não criar regras especiais, listas de IPs conhecidos ou associações fixas entre endereço e fabricante/modelo.

Caso motivador, exclusivamente como exemplo de teste: o usuário informou que o IP `192.0.2.216` pertence a uma Lexmark e que a instalação prosseguiu com Epson selecionada. A consulta realizada durante a implementação confirmou Lexmark CX532ADWE. Esse IP não foi incorporado às regras de produção nem é necessário para a funcionalidade operar.

### Comparação por configuração

Para cada impressora configurada, usar dois dados independentes:

1. **Equipamento real:** fabricante e modelo retornados pela consulta ao IP informado.
2. **Seleção do usuário:** marca/perfil de impressora selecionado e os drivers correspondentes no catálogo.

Hoje o formulário seleciona a marca (`PrinterFormRowViewModel.Brand`), não um modelo físico separado. Portanto, a implementação deverá comparar a marca detectada com a selecionada e o modelo físico detectado com os modelos compatíveis com o driver daquele perfil. Não comparar literalmente o nome do modelo físico com o nome comercial de um driver universal. Se futuramente houver seleção explícita de modelo, esse modelo também deverá participar da validação.

Exemplos aplicáveis a qualquer endereço: Epson detectada com Lexmark selecionada bloqueia; Lexmark detectada com Epson selecionada bloqueia; Brother detectada com Gainscha selecionada bloqueia. Marca coincidente exige ainda a verificação da compatibilidade do modelo/driver.

## Situação atual

- `PrinterBrandHeuristicsValidator` analisa o nome da fila e apresenta avisos que permitem continuar; não consulta o equipamento.
- `PrinterPingService` verifica disponibilidade, sem identificar fabricante ou modelo.
- `PrinterDeploymentOrchestrator.RunAsync` inicia o processamento das máquinas e pode instalar drivers antes do teste de conectividade da impressora.
- `PrinterCatalog` relaciona as quatro marcas aos drivers aceitos, incluindo alternativas Epson e Lexmark.
- `DirectRawPrinterTestService` envia comandos de impressão conforme a marca escolhida e também precisa da proteção.

## Política proposta

Esta é a política de referência para a implementação posterior; o bloqueio de identificação inconclusiva e de todo o lote são decisões propostas, além do requisito original de bloquear marcas incompatíveis.

| Resultado | Ação |
| --- | --- |
| Fabricante diferente do selecionado | Bloquear |
| Modelo identificado incompatível com o driver | Bloquear |
| Identidade e compatibilidade confirmadas | Prosseguir |
| Marca reconhecida, mas modelo/compatibilidade insuficientes | Bloquear como validação inconclusiva |
| Sem resposta, acesso negado ou protocolos indisponíveis | Bloquear como validação inconclusiva |
| Respostas conflitantes ou equipamento não suportado | Bloquear e explicar o motivo |

Não oferecer opção de ignorar uma incompatibilidade. Validar todas as impressoras antes de alterar qualquer máquina-alvo. Se uma configuração falhar, interromper o lote inteiro; distinguir a impressora que causou o bloqueio das demais canceladas por causa dele.

Marca correta não comprova compatibilidade de modelo. O termo “Universal” no nome do driver não autoriza aceitar automaticamente todos os modelos da marca.

## Etapa 1 — Levantar as respostas dos equipamentos

- [x] Consultar, somente para leitura, a Lexmark informada em `192.0.2.216`.
- [ ] Coletar amostras de um equipamento de cada marca e dos modelos homologados disponíveis.
- [ ] Confirmar protocolos habilitados, acesso a partir do computador que executa o aplicativo e parâmetros de autenticação necessários.
- [ ] Confirmar especialmente os recursos disponíveis na Gainscha GA-2408T; não presumir suporte a SNMP ou IPP.
- [ ] Salvar amostras sanitizadas como fixtures de testes, removendo credenciais e informações de rede desnecessárias.
- [ ] Registrar fabricante, modelo e variantes reais de nomenclatura retornadas.

A detecção inicial partirá do computador que executa o aplicativo. Uma falha desse caminho não deve ser apresentada como prova de indisponibilidade a partir das máquinas-alvo. Consulta executada remotamente nas máquinas-alvo fica fora da primeira versão.

## Etapa 2 — Implementar o serviço de identificação

Componentes propostos em `PrinterInstall.Core`:

- `Network/IPrinterIdentityService.cs`: contrato assíncrono de consulta, com cancelamento.
- `Models/PrinterIdentityResult.cs`: endereço informado/resolvido, fabricante, modelo, evidências, fonte, horário e motivo de falha/inconclusão.
- Consultores SNMP e IPP separados, coordenados pelo serviço de identificação.
- Opções de consulta com tempo limite, tentativas, concorrência e parâmetros de acesso configuráveis.

Sequência de consulta:

1. Resolver e normalizar o endereço informado.
2. Consultar SNMP, quando disponível: `sysDescr`, `sysObjectID` e a descrição dos dispositivos de impressão em `hrDeviceDescr`. Descobrir o índice do dispositivo; não fixar um índice arbitrário.
3. Se insuficiente, consultar IPP usando `Get-Printer-Attributes` e `printer-make-and-model`.
4. Normalizar os dados e classificar o resultado, preservando a origem da evidência.

Restrições e cuidados de implementação:

- [ ] Selecionar biblioteca de protocolo mantida e compatível com .NET 8, verificando licença e requisitos antes de adicionar dependências.
- [ ] Usar apenas operações de leitura; não enviar comandos de impressão para descobrir o modelo.
- [ ] Não usar nome da fila, DNS, ping, porta 9100 aberta ou fabricante da interface de rede como prova suficiente de identidade.
- [ ] Tratar descrições genéricas de servidores de impressão como inconclusivas.
- [ ] Definir descoberta dos endpoints IPP suportados sem varredura indiscriminada.
- [ ] Definir autenticação SNMP conforme o ambiente; não assumir comunidade padrão nem reutilizar credenciais de domínio.
- [ ] Proteger segredos de consulta e nunca registrá-los nos logs.
- [ ] Aplicar limites de tamanho de resposta, tempo total e tentativas; respeitar cancelamento.
- [ ] Não condicionar a identificação a sucesso de ping: a consulta de identificação pode funcionar mesmo sem resposta ICMP.
- [ ] Consultar cada endereço normalizado uma vez por execução, evitando consultas duplicadas sob concorrência. Não manter resultado entre execuções.
- [ ] Manter coerência entre endereço validado e endereço usado para configurar a porta, especialmente para nomes DNS com múltiplas respostas.

Se um modelo não disponibilizar identidade por SNMP/IPP, registrar a limitação e avaliar um adaptador de leitura específico, documentado e testado. Não liberar silenciosamente o equipamento.

## Etapa 3 — Implementar o catálogo de compatibilidade

- [ ] Expandir `Catalog/PrinterCatalog.cs` com modelos/famílias homologados, aliases explícitos e drivers compatíveis.
- [ ] Criar `Validation/PrinterCompatibilityValidator.cs`, sem dependência de rede, para comparar identidade e seleção.
- [ ] Separar marca incompatível, modelo incompatível e compatibilidade desconhecida.
- [ ] Normalizar capitalização, espaços e aliases comprovados; evitar correspondências vagas por substring.
- [ ] Validar também o driver efetivamente resolvido quando houver alternativas, antes de qualquer alteração remota.

Base inicial extraída de `MODELOS_TESTADOS.txt`:

| Marca | Modelos registrados como testados no projeto |
| --- | --- |
| Epson | M1180, WF-M5899, WF-M5799, WF-C5890, WF-C5790 |
| Lexmark | CX532adwe |
| Brother | HL-L5212DW |
| Gainscha | GA-2408T |

Essa lista serve de ponto de partida para compatibilidade. Ela não comprova suporte aos protocolos de identificação nem documenta separadamente a compatibilidade de cada versão alternativa do driver. Confirmar esses pontos na homologação; não presumir que a Lexmark do IP informado seja CX532adwe.

## Etapa 4 — Integrar antes de qualquer alteração

- [ ] Inserir a validação no início de `PrinterDeploymentOrchestrator.RunAsync`, antes do processamento que instala drivers ou altera filas.
- [ ] Preparar a escolha dos drivers aplicáveis, permitindo consultas remotas somente de leitura quando necessárias. Validar as alternativas selecionadas antes de iniciar as alterações do lote.
- [ ] Em caso de falha, emitir os resultados das filas/máquinas afetadas e encerrar sem instalar drivers, criar portas/filas, configurar etiquetas ou imprimir.
- [ ] Cobrir o caminho de fila existente, incluindo aplicação de preferências Gainscha.
- [ ] Registrar os serviços em `PrinterInstall.App/App.xaml.cs`.
- [ ] Atualizar construtores e testes; não introduzir um serviço nulo que aprove silenciosamente a instalação em produção.
- [ ] Aplicar a mesma validação ao teste direto de impressão antes do envio de qualquer conteúdo.
- [ ] Preservar cancelamento e rollback. Bloqueio prévio deve deixar o diário de rollback sem alterações novas.

Fluxo previsto:

```text
Validar preenchimento
  -> Identificar todas as impressoras
  -> Validar compatibilidade e drivers aplicáveis
  -> Se houver falha: encerrar o lote e apresentar os motivos
  -> Se tudo estiver válido: iniciar alterações nas máquinas-alvo
```

## Etapa 5 — Interface, configurações e diagnóstico

- [ ] Adicionar estados de identificação, validação, incompatibilidade e identificação inconclusiva.
- [ ] Atualizar `TargetMachineState`, textos localizados, cores/ícones e tratamento de conclusão das operações.
- [ ] Exibir IP, fabricante/modelo detectados quando disponíveis e driver selecionado.
- [ ] Mostrar explicitamente quais filas falharam e quais não foram iniciadas por interrupção do lote.
- [ ] Registrar fonte da identificação, duração, motivo da decisão e falhas de consulta, sem segredos.
- [ ] Integrar opções de consulta às configurações e à persistência existentes, mantendo compatibilidade com configurações antigas.
- [ ] Garantir que o resultado não seja exibido como sucesso quando o lote for bloqueado.

Exemplo de mensagem para divergência confirmada:

> Configuração interrompida. O equipamento em 192.0.2.216 foi identificado como Lexmark, mas o driver selecionado é EPSON Universal Print Driver. Corrija o IP ou a seleção do driver. Nenhuma máquina-alvo foi alterada.

Exemplo de identificação inconclusiva:

> Não foi possível identificar com segurança o equipamento em 192.0.2.216. Verifique o endereço e o acesso aos serviços de identificação. A configuração não foi iniciada.

## Etapa 6 — Testes e homologação

### Testes automatizados

- [ ] As 12 combinações entre marcas diferentes bloqueiam, em ambos os sentidos.
- [ ] Modelos homologados e drivers compatíveis são aceitos.
- [ ] Modelos incompatíveis ou desconhecidos da mesma marca não são aprovados automaticamente.
- [ ] Aliases conhecidos funcionam sem falsos positivos por nomes semelhantes.
- [ ] Respostas SNMP/IPP válidas, truncadas, genéricas, malformadas e conflitantes são tratadas.
- [ ] O fallback IPP é executado quando SNMP é insuficiente.
- [ ] Timeout, falha de DNS, autenticação e cancelamento retornam estados corretos.
- [ ] IP repetido no lote compartilha a consulta; nova execução consulta novamente.
- [ ] A lógica funciona com endereços arbitrários e não contém exceções ou dependência do IP usado como exemplo.
- [ ] Alterar o IP ou a marca selecionada exige validar a nova combinação antes de configurar; não reutilizar uma aprovação de entradas anteriores.
- [ ] Se outro equipamento passar a responder no mesmo IP entre execuções, a nova resposta determina a identidade e a decisão.
- [ ] Mesmo IP com seleções diferentes é validado contra cada seleção.
- [ ] Em lote com uma impressora inválida, nenhuma máquina sofre alteração, inclusive sob concorrência.
- [ ] Nenhuma instalação de driver, criação de porta/fila, aplicação de etiqueta ou impressão ocorre antes de aprovação.
- [ ] Caminhos de filas existentes e teste direto também respeitam o bloqueio.
- [ ] Alternativas Lexmark v4/v2 são verificadas individualmente conforme o catálogo.
- [ ] Estados, logs, cancelamento, configurações antigas e relatório final continuam coerentes.

Usar fakes para provar ausência de chamadas de alteração e fixtures de respostas reais para verificar a interpretação. Testes automatizados não devem depender de equipamentos da rede corporativa.

### Homologação real

1. Consultar equipamentos de teste por seus respectivos IPs e registrar as identidades reais. A Lexmark `192.0.2.216` pode ser usada como um desses exemplos, se disponível; sua disponibilidade não é requisito para homologar a lógica geral.
2. Selecionar Epson e confirmar bloqueio antes de alterações.
3. Selecionar Lexmark e, após confirmar a compatibilidade do modelo/driver, validar o fluxo correto em máquina de teste.
4. Repetir com Epson, Brother e Gainscha.
5. Validar um lote misto com pelo menos uma incompatibilidade.
6. Simular indisponibilidade dos protocolos e confirmar mensagem de identificação inconclusiva.

## Critérios de aceite

- A reprodução Lexmark/IP + Epson selecionada é bloqueada com motivo correto.
- Qualquer IP informado é consultado dinamicamente e comparado com a seleção da respectiva configuração, sem mapeamento fixo IP/marca/modelo.
- A proteção cobre todas as marcas e todos os caminhos que configuram ou imprimem.
- Nenhuma operação de alteração começa antes da aprovação de todas as validações do lote.
- Falha de consulta nunca é confundida com compatibilidade nem com fabricante divergente.
- Modelos listados como homologados têm evidências de identificação e regras de driver verificadas.
- Testes relevantes de Core e App passam, incluindo regressões de instalação, etiquetas, cancelamento e rollback.
- Documentação do usuário explica requisitos de rede, modelos homologados e mensagens de bloqueio.

## Ordem de execução e limites

Executar as etapas 1 a 6 em ordem. A coleta inicial condiciona a escolha dos protocolos e as regras de normalização, especialmente para Gainscha. Entregar primeiro o serviço e o validador com testes; depois integrar o bloqueio, interface e homologação.

Fora do escopo inicial: descoberta de impressoras por varredura de sub-rede, troca automática da seleção do usuário, instalação automática de outros modelos, detecção remota a partir de cada máquina-alvo e alterações nas configurações das impressoras.

Este arquivo preserva o plano e seus critérios de aceite. A presença de código e testes automatizados não substitui a homologação de rede e equipamentos reais.

## Referências

- Código: `src/PrinterInstall.Core/Orchestration/PrinterDeploymentOrchestrator.cs`.
- Catálogo: `src/PrinterInstall.Core/Catalog/PrinterCatalog.cs`.
- Modelos testados: `MODELOS_TESTADOS.txt`.
- [RFC 3418 — SNMP: sysDescr e sysObjectID](https://www.rfc-editor.org/info/rfc3418/).
- [RFC 2790 — Host Resources: hrDeviceDescr](https://www.rfc-editor.org/rfc/rfc2790.html).
- [RFC 8011 — IPP: Get-Printer-Attributes e printer-make-and-model](https://www.rfc-editor.org/info/rfc8011/).
