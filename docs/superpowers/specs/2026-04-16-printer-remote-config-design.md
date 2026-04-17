# Especificação de desenho — Configuração remota de impressoras (domínio AD)

**Data:** 2026-04-16  
**Estado:** Aprovado para plano de implementação  
**Domínio de referência:** `preventsenior.local`

## 1. Objectivo

Aplicação **desktop Windows** para automatizar a configuração de impressoras em **múltiplos computadores** do Active Directory, usando **credenciais de administrador de domínio** em todas as operações remotas. O produto **não instala drivers**; apenas valida se o driver esperado já existe no alvo e, em caso afirmativo, configura porta TCP/IP e fila de impressão.

## 2. Abordagem arquitectónica (seleccionada)

**Abordagem C:** cliente **WPF** (.NET LTS) com **biblioteca .NET separada** que concentra autenticação, catálogo, remoting e orquestração. Motivos: fronteiras claras, testabilidade e manutenção sem dispersar lógica na UI.

## 3. Requisitos funcionais

### 3.1 Autenticação

- Ecrã de **login na inicialização** com credenciais de administrador do domínio.
- **Validação explícita** ao domínio antes de aceder ao ecrã principal: tentativa de **ligação LDAP** ao serviço de directório do domínio (nome DNS do domínio configurável; valor por defeito `preventsenior.local`).
- Credenciais mantidas **apenas em memória** durante a sessão; não persistir palavras-passe; libertar dados sensíveis ao terminar.

### 3.2 Selecção de alvos

- O utilizador indica **um ou mais computadores** através de **lista manual** (digitar ou colar), suportando **FQDN** ou **nome NetBIOS**.
- **Validação simples** de formato/não vazio no cliente; erros de existência ou alcance tratados na fase remota.

### 3.3 Configuração da impressora

- Selecção de **marca:** Epson, Lexmark ou Gainscha.
- Selecção de **modelo** a partir de **catálogo fixo** embutido na aplicação (lista curada por marca).
- Definição do **nome de exibição** da fila de impressora nos computadores alvo.
- **Ligação TCP/IP** ao dispositivo de impressão: **um endereço (IP ou DNS)** comum a **todos** os alvos na operação corrente.
- **Protocolo e porta configuráveis** pelo utilizador; **predefinição:** porta **9100**, modo **RAW** (JetDirect).

### 3.4 Validação de driver (sem instalação)

Antes de criar porta/fila num alvo:

1. Obter remotamente a lista de **drivers de impressão já instalados** no Windows (via WinRM ou CIM/WMI, conforme secção 4).
2. Determinar o **nome de driver esperado** consoante a **marca** seleccionada:


| Marca    | Nome do driver instalado (referência) |
| -------- | ------------------------------------- |
| Epson    | `EPSON Universal Print Driver`        |
| Gainscha | `Gainscha GA-2408T`                   |
| Lexmark  | `Lexmark Universal v4 XL`             |


1. **Regra de correspondência:** considerar o driver **presente** se existir uma entrada cujo **nome** corresponda ao esperado, com comparação **sem distinção de maiúsculas/minúsculas**. Se, em ambiente real, surgirem variações (sufixos de versão, etc.), a implementação pode ajustar para **igualdade normalizada** ou **prefixo/contém** documentado — a regra base permanece **igualdade da cadeia de nome** após normalização de capitalização.
2. Se o driver **não** estiver instalado: **abortar apenas esse alvo** com motivo explícito; **não** tentar instalar o driver.

**Nota:** Com drivers universais por marca, o dropdown de **modelo** serve sobretudo à selecção e consistência operacional; a **validação de driver** baseia-se na **marca → nome de driver** da tabela acima. Se no futuro um modelo exigir um pacote com nome distinto, o catálogo deverá mapear **modelo → nome de driver** para essa entrada.

### 3.5 Processamento e UI

- Execução **sequencial:** uma máquina de cada vez, pela **ordem** da lista.
- Interface com **estado em tempo real** por máquina e **log** textual das operações (com timestamp).
- Canal remoto: tentar **WinRM / PowerShell Remoting** primeiro; se falhar, **CIM/WMI** com as **mesmas** credenciais. Comportamento documentado quando ambos falham.

## 4. Requisitos técnicos remotos

- **Primário:** operações via **WinRM** (portas 5985/5986 típicas quando HTTPS) onde disponível.
- **Secundário:** **CIM/WMI remoto** quando WinRM não estiver disponível ou falhar de forma recuperável.
- A aplicação deve comunicar **pré-requisitos** claros (firewall, serviços, permissões) quando a falha for por canal indisponível.

## 5. Componentes lógicos


| Componente              | Responsabilidade                                                                  |
| ----------------------- | --------------------------------------------------------------------------------- |
| UI WPF (MVVM)           | Login, formulário de alvos e parâmetros, grelha de estado, log                    |
| Serviço de autenticação | LDAP bind / validação de credenciais ao domínio                                   |
| Serviço de remoting     | Abstracção WinRM com recurso a CIM/WMI; execução de comandos ou APIs equivalentes |
| Catálogo                | Marcas, modelos e nomes de driver esperados (recurso embutido ou código)          |
| Orquestrador            | Pipeline sequencial por alvo: validar driver → criar porta TCP → criar impressora |
| Modelos de domínio      | Entradas de log, estado por alvo, resultado da operação                           |


## 6. Fluxo de dados (resumo)

1. Login → validação LDAP → ecrã principal.
2. Utilizador preenche lista de computadores, marca, modelo, nome de exibição, endereço TCP comum, protocolo/porta.
3. Disparo → para cada alvo em sequência: remoting → listar drivers → comparar nome → se OK criar porta e fila; senão abortar alvo.
4. Actualização da UI e do log a partir de eventos da camada de orquestração (thread de fundo com marshalling para a UI).

## 7. Estados e erros

Estados mínimos por máquina: Pendente → Contacto remoto → Validação de driver → Configuração → **Concluído (sucesso)** | **Abortado (driver em falta)** | **Erro** (rede, credenciais, remoting, criação de porta/fila).

Classificar mensagens para: falha de autenticação/autorização, alvo inacessível, ambos os canais remotos indisponíveis, driver ausente (aborto intencional), falha ao criar porta ou fila.

Log: linhas com timestamp, identificador do alvo e mensagem; **nunca** registar segredos.

## 8. Testes (âmbito)

- Testes unitários na biblioteca: validação de nomes de máquina, mapeamento marca → driver esperado, lógica de comparação de nomes de driver.
- Testes de integração com **dobras** (mocks) para remoting, simulando presença/ausência de driver e falhas de canal.
- Testes manuais em ambiente de domínio com pelo menos um alvo por marca, confirmando correspondência exacta dos nomes devolvidos pelo Windows.

## 9. Fora de âmbito

- Instalação ou actualização de drivers nos alvos.
- Descoberta de computadores por OU/LDAP (para esta versão; apenas lista manual).
- Processamento paralelo de alvos.

## 10. Próximo passo

Criar **plano de implementação** detalhado (estrutura de solução, pacotes, ordem de entrega) com base nesta especificação, usando o fluxo *writing-plans* quando for altura de implementar.