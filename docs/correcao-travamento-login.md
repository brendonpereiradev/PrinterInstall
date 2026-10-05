# Correção do travamento ao entrar no domínio

## Causa

O clique em **Entrar** chama `LoginViewModel.TryLoginAsync`, que aguarda
`LdapCredentialValidator.ValidateAsync`. Apesar do nome e do retorno `Task`,
o validador executava `LdapConnection.Bind`, a conexão SMB a `IPC$` e
`LogonUser` sincronamente, antes de retornar `Task.FromResult`.

Essas chamadas rodavam no Dispatcher do WPF, a mesma thread que processa
cliques, desenho da janela e a animação de **Entrando...**. O `await` no
chamador não muda a thread de execução de uma chamada síncrona. A interface
ficava bloqueada durante as tentativas, podendo receber a indicação
**Não está respondendo** do Windows, e voltava a funcionar ao terminar.

A implementação de [`LdapConnection.Bind` no .NET 8](https://github.com/dotnet/runtime/blob/v8.0.0/src/libraries/System.DirectoryServices.Protocols/src/System/DirectoryServices/Protocols/ldap/LdapConnection.cs)
também confirma que a conexão e o bind são feitos de forma síncrona.

Isso explica a ocorrência em computadores diferentes: o bloqueio está no
fluxo comum da aplicação. DNS, acesso ao controlador de domínio, VPN,
firewall e negociação de autenticação podem alterar a duração da espera.
O código tenta Negotiate, NTLM, SMB e LogonUser em sequência; para CPF com
pontuação, pode repetir as tentativas com o CPF sem formatação. Não foi
diagnosticada uma falha específica na infraestrutura dos computadores
reportados.

A construção do painel foi revisada: os construtores de suas dependências
não fazem essas consultas de autenticação. A extração antecipada dos
drivers já é iniciada em segundo plano.

## Alterações

- A sequência completa de autenticação passa a rodar em segundo plano com
  `Task.Run`, usando uma cópia das credenciais informadas.
- O ViewModel mantém o contexto do Dispatcher ao retomar depois do `await`,
  para atualizar mensagens, indicador de autenticação e sessão na thread da
  interface. O `ConfigureAwait(false)` anterior seria inadequado para essas
  atualizações depois de tornar o validador efetivamente assíncrono.
- O fechamento da janela cancela a tentativa. O cancelamento é verificado
  antes e depois de cada chamada nativa e antes de salvar o usuário ou
  criar a sessão. Uma chamada nativa já iniciada termina conforme o Windows;
  as próximas tentativas não são executadas depois do cancelamento.
- A ordem dos métodos, a tentativa com CPF sem formatação e a política
  existente de contingência entre domínios foram preservadas.

A correção elimina o bloqueio da interface durante a autenticação. O tempo
de resposta dos serviços de rede continua dependendo do ambiente.

## Validação

Os testes usam um Dispatcher WPF real em uma thread STA e operações de
autenticação controladas, sem enviar credenciais a servidores reais.

- Uma tentativa lenta foi simulada em cada etapa: LDAP Negotiate, LDAP NTLM,
  SMB e LogonUser. Em todos os casos, o Dispatcher processa outra mensagem
  enquanto a autenticação ainda aguarda. O indicador permanece ativo, e a
  sessão só é criada após a conclusão.
- Mensagens e notificações do ViewModel, inclusive em falhas e exceções,
  foram verificadas na thread da interface.
- Cancelamento antes da execução, durante o bind e antes de concluir o
  login não inicia contingências nem salva usuário ou sessão.
- Entradas inválidas e a sequência de tentativas para CPF formatado foram
  verificadas sem chamadas nativas reais.
- Ao restaurar temporariamente a execução síncrona, os quatro cenários de
  responsividade falharam. Ao restaurar o `ConfigureAwait(false)`, os dois
  cenários de atualização da interface após falha também falharam. Os
  arquivos corrigidos foram restaurados após cada verificação.
- A suíte completa em Release passou: **536 testes de Core e 277 de App,
  totalizando 813**, sem falhas ou testes ignorados.

Não foi realizado login em um Active Directory real nem execução nos
computadores reportados. Os testes reproduzem o defeito de concorrência e
verificam a correção independentemente da disponibilidade de um domínio.

## Executável

O executável corrigido está disponível na
[release v1.4.1](https://github.com/brendonpereiradev/PrinterInstall/releases/tag/v1.4.1),
com runtime e drivers embutidos, para copiar aos computadores de destino.
