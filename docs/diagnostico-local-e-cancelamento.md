# Diagnóstico local e cancelamento do deploy

## Coleta no computador alvo

1. Copie o executável atualizado para uma pasta local do computador com problema.
2. Abra com **Executar como administrador** e autentique-se no PrinterInstall.
3. Clique em **Adicionar Este PC**, mantenha somente esse computador como alvo e
   preencha as impressoras.
4. Execute o deploy. Em caso de cancelamento, aguarde também o término da reversão.
5. Antes de fechar o aplicativo, use **Exportar Logs** e leve o `.txt` ao computador
   em que a análise será feita.

O `.txt` contém o resumo dos alvos, o histórico mostrado na interface e o log técnico
da sessão atual do aplicativo. Inclui um identificador da execução e da sessão,
horários e duração, versão e caminho do executável, conta informada, usuário efetivo
do processo, elevação e grupos do token, além de indicar execução local ou remota.

A coleta do Windows consulta domínio, versão do sistema, rede e DNS, spooler,
membros diretos do grupo Administradores local, compartilhamento `ADMIN$`, drivers
e filas existentes. Os membros de grupos de domínio não são expandidos. Consultas
indisponíveis ou que excedam o tempo limite são registradas sem impedir o deploy.
A coleta não modifica esses componentes.

O relatório também reúne códigos de erro, cadeia de exceções e saída dos
instaladores/tarefas temporárias capturada antes da limpeza. A senha informada é
ocultada tanto no relatório quanto nos registros produzidos após sua identificação.
Não é necessário transferir o arquivo de log diário separadamente. A exportação
fica disponível durante a execução para registrar um estado parcial; para analisar
o resultado completo, exporte novamente depois do resumo final.

## Comportamento de Cancelar

O primeiro clique registra o pedido, muda o botão para **Cancelando…** e desabilita
novos cliques. A execução ocorre fora da interface, e alvos ainda aguardando também
são marcados como cancelados. O token é verificado entre as etapas e após chamadas
que podem retornar um erro nativo depois do clique.

As esperas de consultas WMI e de negociação SMB podem ser interrompidas. Caso uma
conexão SMB só termine depois do cancelamento, ela é liberada. Processos locais
iniciados pelo programa recebem uma solicitação de encerramento da árvore do
processo. Para execução remota por tarefa agendada, o programa solicita `schtasks
/End` antes de remover a tarefa e seus arquivos, registrando falhas nessa tentativa.

Chamadas nativas que já estão alterando o Windows podem precisar terminar antes
da reversão. O programa aguarda essas operações para registrar as filas e portas
criadas e desfazê-las pelo diário de reversão. O cancelamento não desinstala drivers
e não garante reversão de comandos físicos já enviados à impressora.

## Validação

Testes cobrem a interface disponível durante uma chamada nativa bloqueada,
cancelamento durante a coleta, bloqueio de novas mutações, cancelamento tardio
seguido de erro de acesso, encerramento de tarefas pelas rotas WMI e RPC, captura
de logs com um token independente antes da limpeza, saída preservada após timeout,
exportação de diagnósticos e ocultação da senha. A coleta real do Windows local foi
exercitada sem modificar impressoras. A instalação real no computador alvo depende
da execução nesse equipamento.
