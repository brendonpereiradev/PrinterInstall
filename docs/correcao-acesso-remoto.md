# Acesso negado na primeira consulta ao computador

Quando todas as impressoras falham logo após “Conectando”, a implantação ainda
está consultando `Win32_PrinterDriver` no destino. Nenhum driver, porta ou fila
foi criado nessa etapa. Um relatório contendo somente “Access is denied” não
identifica qual política ou permissão do Windows causou a recusa.

## Defeito corrigido

As consultas de drivers, filas, existência de fila e utilização de porta eram
exclusivamente WMI/DCOM, sem a alternativa elevada usada nas mutações. Além
disso, o preflight abortava em uma recusa WMI, e o executor de tarefas só tentava
RPC quando `Win32_Process` retornava um código de erro, não quando a conexão
lançava uma exceção de acesso negado.

Agora as consultas com acesso negado tentam uma tarefa temporária como SYSTEM,
criada com as credenciais explícitas do operador. O resultado é transportado
como JSON por ADMIN$, lido antes da limpeza e validado antes de continuar.
Depois de uma consulta bem-sucedida, as próximas operações do mesmo destino
usam a execução elevada. A conexão WMI negada na criação da tarefa também
aciona a alternativa RPC do Agendador. Cancelamento não aciona essa alternativa.

Se o acesso administrativo também for recusado, o erro informa o destino,
a falha WMI original e os detalhes da alternativa ADMIN$/Agendador, sem senha.
As tarefas e os arquivos temporários continuam sendo removidos ao concluir.

## Permissões necessárias

Não é necessário entrar interativamente no notebook para criar um perfil do
operador. A conta informada deve possuir direitos administrativos no destino
e autorização de acesso pela rede. Elevar o aplicativo na origem ou validar
a senha no LDAP não concede esses direitos no computador remoto.

A alternativa depende de ADMIN$ (SMB) e do Agendador de Tarefas remoto (RPC).
Ela não modifica UAC, firewall, grupos locais ou políticas do domínio. Se a
conta não tiver os direitos exigidos, a administração do ambiente precisará
corrigir a associação aos Administradores do destino ou a política aplicável.

Referências Microsoft:

- [Configurar uma conexão WMI remota](https://learn.microsoft.com/en-us/windows/win32/wmisdk/connecting-to-wmi-remotely-starting-with-vista)
- [Segurança da conexão WMI](https://learn.microsoft.com/en-us/windows/win32/wmisdk/securing-a-remote-wmi-connection)
- [Perfis de usuário](https://learn.microsoft.com/en-us/windows/win32/shell/about-user-profiles)

## Validação

Os testes simulam recusa DCOM na consulta e na criação do processo, sucesso
do Agendador, consultas subsequentes, leitura antes da limpeza, dados inválidos,
recusa SMB e cancelamento. A implantação real exige nova execução no ambiente
com as credenciais do operador; os testes não comprovam as permissões do notebook.

## Continuação: erro 1311 na autenticação SMB

`Win32 1311 / ERROR_NO_LOGON_SERVERS` significa que não há servidor de logon
disponível para a autenticação solicitada. Não é evidência de falta de associação
aos Administradores. É preciso verificar domínio da conta, DNS e comunicação
com controladores tanto na origem quanto no destino. Estar na rede e ter o
computador ingressado no domínio não comprova que essa comunicação está funcional.

Foram corrigidos também os seguintes defeitos:

- Domínios DNS agora geram UPN (`usuario@laboratorio.test`) nas chamadas Windows;
  o formato `LABORATORIO\usuario` permanece para nomes NetBIOS. UPN explícito é preservado.
- LDAP recebe UPN sem repetir o domínio em um campo separado. `LogonUser` recebe
  domínio nulo para UPN, conforme o contrato da API, e não substitui o domínio
  da conta pelo endereço do servidor LDAP.
- Falha em todos os validadores agora impede o login, em vez de retornar sucesso
  silenciosamente. A identidade efetivamente validada é usada na implantação.
- Conflito SMB com outra conta não é tratado como autenticação bem-sucedida.
- O diagnóstico distingue indisponibilidade do domínio de permissão administrativa.

Para o próximo teste, prefira o nome DNS completo do destino, confirmado no DNS
como pertencente ao IP esperado. O acesso por IP normalmente não usa Kerberos.
Essas correções não reparam DNS, conectividade ou o vínculo do notebook com o
domínio; uma implantação bem-sucedida no ambiente ainda precisa ser confirmada.

Referências adicionais:

- [Erro 1311 e descoberta de controladores](https://learn.microsoft.com/en-us/troubleshoot/windows-server/windows-security/troubleshoot-kerberos-domain-not-found-event-id-5719)
- [Formatos de nome de usuário](https://learn.microsoft.com/en-us/windows/win32/secauthn/user-name-formats)
- [Contrato de LogonUserW](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-logonuserw)
- [Kerberos e endereços IP](https://learn.microsoft.com/en-us/windows-server/security/kerberos/configuring-kerberos-over-ip)
