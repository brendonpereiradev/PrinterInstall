# 📖 Manual de Utilização — PrinterInstall
### Guia Prático para Técnicos de Suporte e Helpdesk (Ambiente Hospitalar)

> **Versão da Aplicação:** 2.0  
> **Público-Alvo:** Suporte Nível 1 / Helpdesk Júnior / Operadores de TI  
> **Objetivo:** Instalar, configurar, testar e gerenciar impressoras de rede e etiquetadoras em múltiplos computadores de forma rápida, segura e padronizada.

---

## 📋 Sumário

1. [Visão Geral e Pré-Requisitos](#1-visão-geral-e-pré-requisitos)
2. [Acesso ao Sistema (Login)](#2-acesso-ao-sistema-login)
3. [Instalação de Impressoras em Lote (Deploy)](#3-instalação-de-impressoras-em-lote-deploy)
   - [3.1 Adicionando Computadores Alvos](#31-adicionando-computadores-alvos)
   - [3.2 Configurando as Filas de Impressão](#32-configurando-as-filas-de-impressão)
   - [3.3 Iniciando e Acompanhando a Instalação](#33-iniciando-e-acompanhando-a-instalação)
   - [3.4 Rollback Automático e Segurança](#34-rollback-automático-e-segurança)
4. [Assistente de Controle de Impressoras (Remoção e Renomeação)](#4-assistente-de-controle-de-impressoras-remoção-e-renomeação)
   - [4.1 Consultando Impressoras Instaladas](#41-consultando-impressoras-instaladas)
   - [4.2 Removendo Filas Antigas ou Órfãs](#42-removendo-filas-antigas-ou-órfãs)
   - [4.3 Renomeando Filas em Lote](#43-renomeando-filas-em-lote)
5. [Ferramenta de Teste de Rede Direto](#5-ferramenta-de-teste-de-rede-direto)
6. [Tabela de Referência de Etiquetas Gainscha](#6-tabela-de-referência-de-etiquetas-gainscha)
7. [Resolução de Problemas Mais Frequentes (FAQ)](#7-resolução-de-problemas-mais-frequentes-faq)

---

## 1. Visão Geral e Pré-Requisitos

O **PrinterInstall** automatiza todo o processo de instalação de impressoras de rede em estações de trabalho locais ou remotas. Ele cuida automaticamente de:
- Instalar os drivers corretos no Windows sem necessidade de CDs ou downloads manuais.
- Criar as portas TCP/IP de rede.
- Criar e nomear as filas de impressão no padrão hospitalar.
- Calibrar o tamanho correto das etiquetas térmicas (Gainscha).
- Desfazer alterações caso ocorra algum erro no meio do caminho (*Rollback*).

### 📝 O que você precisa ter em mãos antes de começar:
1. **Sua credencial de rede:** Usuário e senha do domínio com privilégio administrativo (ex: conta de suporte de TI).
2. **Nome ou IP dos computadores:** Identificação das máquinas onde as impressoras serão instaladas:
   - Por nome de rede: Padrões `NOTE-XXXXXX` (ex: `NOTE-001234`) ou `113-DESKXXXXXX` (ex: `113-DESK004567`).
   - Por endereço IP da máquina: Também é possível usar diretamente o IP da estação (ex: `192.168.10.120`).
3. **Endereço IP da impressora:** O IP fixo ou reservado da impressora na rede hospitalar (ex: `192.168.10.45`).

---

## 2. Acesso ao Sistema (Login)

Ao abrir o aplicativo, a tela de autenticação será exibida:

```
+-------------------------------------------------------------+
|                     PRINTERINSTALL - LOGIN                  |
+-------------------------------------------------------------+
|  Usuário: [ suporte.silva@hospital.local                 ]  |
|  Senha:   [ ******************                           ]  |
|                                                             |
|                      [  ENTRAR  ]                           |
+-------------------------------------------------------------+
```

### Como preencher o campo Usuário:
Você pode digitar seu usuário de rede em qualquer um dos dois formatos aceitos pelo Active Directory:
- **Formato UPN (Recomendado):** `seu_usuario@dominio.hospital` (Exemplo: `suporte.ti@saude.local`)
- **Formato NetBIOS:** `DOMINIO\seu_usuario` (Exemplo: `HOSPITAL\suporte.ti`)
- **Para testes em máquina local sem domínio:** Digite apenas o nome do usuário local (Exemplo: `Administrador`).

> [!TIP]
> Suas credenciais são usadas de forma segura para autenticar e executar as instalações nos computadores remotos da rede. Nenhuma senha fica salva no computador após fechar o programa.

---

## 3. Instalação de Impressoras em Lote (Deploy)

A tela principal do PrinterInstall divide o trabalho em três partes práticas: **Computadores Alvos**, **Filas de Impressão** e **Painel de Execução**.

### 3.1 Adicionando Computadores Alvos

No painel esquerdo, informe os computadores que receberão as impressoras:

1. **Instalar no próprio computador onde você está:**
   - Basta clicar no botão **"Adicionar Este PC"**. O nome da sua máquina será adicionado instantaneamente.
2. **Instalar em computadores remotos (por Nome ou por IP):**
   - **Por Nome de Máquina:** Digite o nome no padrão hospitalar (ex: `NOTE-001234` ou `113-DESK004567`) e clique em **"+"** (ou pressione `Enter`).
   - **Por Endereço IP:** Você também pode informar diretamente o IP do computador de destino na rede (ex: `192.168.10.120`) e clicar em **"+"**.
3. **Adicionar múltiplos computadores de uma vez:**
   - Se você tiver uma lista de computadores ou IPs em um bloco de notas ou planilha, basta copiar e colar no campo. O sistema adiciona todos automaticamente.

---

### 3.2 Configurando as Filas de Impressão

No painel central, monte a lista de impressoras que serão instaladas em cada computador alvo:

1. **Selecione a Marca:**
   - `Epson`: Para impressoras de folha A4 e multifuncionais (ex: Epson EcoTank, WorkForce). Utiliza o *EPSON Universal Print Driver*.
   - `Lexmark`: Para impressoras laser de receitas e prontuários (ex: séries MS/MX). Utiliza o *Lexmark Universal Driver*.
   - `Brother`: Para impressoras laser de postos de atendimento (ex: Brother HL-L5212DW).
   - `Gainscha`: Para impressoras térmicas de etiquetas de identificação (ex: Gainscha GA-2408T).

2. **Informe o Endereço IP da Impressora:**
   - Digite o IP da impressora na rede (Exemplo: `192.168.10.50`).

3. **Defina o Nome da Fila:**
   - Dê um nome claro para que os médicos e enfermeiros identifiquem a impressora no Windows.
   - *Exemplos recomendados:*
     - `POSTO-ENF-LASER-01`
     - `RECEPCAO-EPSON-01`
     - `TRIAGEM-ETIQ-PULSEIRA`
     - `LAB-ETIQ-TUBOS`

4. **Para impressoras Gainscha — Escolha do Preset de Etiqueta:**
   Ao selecionar a marca **Gainscha**, o campo *Preset* ficará ativo. Escolha o tamanho exato da etiqueta:
   - `Paciente`: Para identificação de ficha e prontuário (89 mm x 36 mm).
   - `Matrix`: Para tubos de coleta e frascos de exames (50 mm x 30 mm).
   - `Pulseira`: Para pulseiras de identificação hospitalar (25 mm x 270 mm).
   - `Lote`: Para identificação de medicamentos e farmácia (45 mm x 13 mm).

> [!NOTE]
> **Alerta Inteligente de Nomenclatura:** Se você selecionar a marca *Epson* e digitar um nome como `ETIQ-PULSEIRA`, o sistema exibirá um aviso amigável para lembrá-lo de conferir se a marca selecionada está correta, evitando erros de instalação!

---

### 3.3 Iniciando e Acompanhando a Instalação

1. Com os computadores e impressoras configurados, clique no botão **"Iniciar Instalação"** (ou *Deploy*).
2. O sistema iniciará a instalação simultânea em todos os computadores da lista.
3. Você verá o status de cada máquina atualizando em tempo real:
   - 🟡 **Pendente:** Aguardando início.
   - 🔵 **Instalando Driver:** O driver está sendo enviado e registrado no Windows.
   - 🔵 **Criando Porta / Fila:** A porta TCP/IP e a fila com nome correto estão sendo criadas.
   - 🟢 **Concluído:** Impressora instalada e pronta para uso!
   - 🔴 **Falha:** Houve algum impedimento na máquina (veja a mensagem de detalhe no painel de log).

4. **Exportar Relatório:** Ao final, clique em **"Exportar Logs"** para salvar um arquivo de texto com o comprovante de tudo o que foi instalado com sucesso.

---

### 3.4 Rollback Automático e Segurança

O PrinterInstall possui um sistema de segurança chamado **Rollback Automático**:
- Se ocorrer qualquer erro durante a instalação em um computador (por exemplo, a rede cair ou o computador travar no meio do processo), o sistema **desfaz automaticamente** as portas e filas incompletas criadas naquela máquina.
- Isso garante que a estação de trabalho nunca fique com arquivos corrompidos ou filas "fantasmas" que possam travar o Windows.

---

## 4. Assistente de Controle de Impressoras (Remoção e Renomeação)

Para acessar o assistente de manutenção, clique no menu ou botão **"Assistente de Remoção / Controle de Filas"**.

Esta ferramenta é ideal para quando o hospital troca impressoras antigas de lugar ou quando é necessário padronizar o nome de impressoras já existentes.

```
+-------------------------------------------------------------------------+
|                  ASSISTENTE DE CONTROLE DE IMPRESSORAS                  |
+-------------------------------------------------------------------------+
| [✓] 113-DESK004567 | [X] IMP_ANTIGA_HP   (Porta: 192.168.1.99) -> [Remover] |
| [✓] NOTE-001234    | [ ] EPSON-REC-01    (Porta: 192.168.1.50) -> [Manter]  |
|                                                                         |
| Ação em Lote: [ Excluir Selecionadas ]   [ Renomear Fila ]               |
+-------------------------------------------------------------------------+
```

### 4.1 Consultando Impressoras Instaladas
1. Adicione os computadores que deseja consultar (ou clique em "Adicionar Este PC").
2. Clique em **"Listar Impressoras"**.
3. O sistema fará uma varredura remota e exibirá todas as impressoras instaladas em cada estação.

### 4.2 Removendo Filas Antigas ou Órfãs
1. Marque a caixinha ao lado das impressoras que deseja remover (ex: impressoras antigas que foram substituídas).
2. Clique em **"Remover Impressoras Selecionadas"**.
3. O sistema remove com segurança as filas e limpa as portas de rede não utilizadas.

### 4.3 Renomeando Filas em Lote
1. Selecione a impressora na lista.
2. Digite o novo nome padronizado (ex: mudar de `EPSON_NOVA` para `POSTO-ENF-EPSON-01`).
3. Clique em **"Renomear"**. O sistema atualiza o nome imediatamente no Windows remoto sem precisar reinstalar o driver.

---

## 5. Ferramenta de Teste de Rede Direto

Antes de instalar uma impressora em múltiplos computadores, é uma boa prática testar se ela está ligada e respondendo na rede. O PrinterInstall possui uma ferramenta dedicada para isso:

1. Abra a janela **"Teste de Rede Direto"**.
2. Digite o **Endereço IP** da impressora (ex: `192.168.10.45`).
3. Selecione a **Marca** da impressora.
4. Se for Gainscha, escolha o **Preset da Etiqueta** inserida na impressora.
5. Clique em **"Testar Conexão"**:
   - O sistema valida a porta de rede raw 9100.
6. Clique em **"Imprimir Página de Teste"**:
   - O sistema envia um comando de teste direto à impressora:
     - Para impressoras de folha (Epson, Brother, Lexmark): Uma página de teste limpa informando a data, IP e sucesso da comunicação.
     - Para impressoras Gainscha: Uma etiqueta impressa no tamanho exato configurado, sem desalinhar o rolo!

---

## 6. Tabela de Referência de Etiquetas Gainscha

Ao instalar ou testar impressoras térmicas **Gainscha GA-2408T**, use a tabela abaixo como guia para escolher o preset correto de acordo com o setor do hospital:

| Preset no Sistema | Largura x Altura | Onde é Utilizada no Hospital? | Exemplo de Aplicação |
| :--- | :---: | :--- | :--- |
| **`Paciente`** | **89 mm x 36 mm** | Recepção, Triagem, Posto de Enfermagem | Identificação de pastas, fichas de prontuário e leitos. |
| **`Matrix`** | **50 mm x 30 mm** | Laboratório de Análises Clínicas | Identificação de tubos de sangue, frascos e lâminas. |
| **`Pulseira`** | **25 mm x 270 mm** | Triagem / Classificação de Risco | Pulseira plástica colocada no braço do paciente. |
| **`Lote`** | **45 mm x 13 mm** | Farmácia Hospitalar e Almoxarifado | Código de barras de medicamentos, dose unitária e validade. |

> [!IMPORTANT]
> **Dica de Ouro:** Sempre confirme qual rolo de etiqueta está fisicamente colocado dentro da impressora Gainscha antes de instalar a fila. Selecionar o preset incorreto fará a impressão sair fora da margem da etiqueta.

---

## 7. Resolução de Problemas Mais Frequentes (FAQ)

### ❓ 1. Erro: "Não foi possível conectar ao computador alvo (WMI / CIM / RPC)"
- **Causa 1:** O computador alvo está desligado ou desconectado da rede.
- **Causa 2:** O Firewall do Windows no computador alvo está bloqueando a comunicação remota.
- **Causa 3:** O usuário utilizado no login não possui permissão de Administrador no computador alvo.
- **Como resolver:** Verifique se o computador responde ao comando `ping <nome-ou-ip-do-pc>` (ex: `ping NOTE-001234` ou `ping 192.168.10.120`) e se sua conta de login possui perfil de suporte/administrador de domínio.

---

### ❓ 2. Erro: "A impressora não respondeu na porta 9100"
- **Causa 1:** O IP digitado está incorreto ou a impressora está desligada.
- **Causa 2:** Cabo de rede da impressora desconectado ou em VLAN sem comunicação com a estação.
- **Como resolver:** Use a ferramenta de **Teste de Rede Direto** do PrinterInstall para testar o IP. Verifique se a luz de rede (link) atrás da impressora está piscando.

---

### ❓ 3. A impressora Gainscha imprime uma etiqueta e pula 2 etiquetas em branco
- **Causa:** O preset selecionado no PrinterInstall é maior do que o tamanho real da etiqueta colocada na impressora.
- **Como resolver:** Abra o **Assistente de Remoção**, exclua a fila atual e reinstale a impressora selecionando o preset com as medidas corretas (consulte a [Tabela de Etiquetas](#6-tabela-de-referência-de-etiquetas-gainscha)).

---

### ❓ 4. Como cancelar uma instalação que está demorando?
- Basta clicar no botão **"Cancelar"** na tela principal. O PrinterInstall interrompe o processo com segurança e executa o Rollback nas máquinas que ainda não haviam concluído, deixando o ambiente limpo.

---

### ❓ 5. Onde ficam salvos os relatórios e logs?
- Os relatórios gerados ficam salvos na pasta de logs da aplicação e também podem ser exportados para a sua **Área de Trabalho** ou pasta de sua escolha clicando no botão **"Exportar Logs"**.

---

## 8. Configurações de Domínio e Rede

Para que o **PrinterInstall** funcione em qualquer ambiente de TI (diferentes domínios corporativos, filiais ou redes isoladas), você pode configurar os parâmetros de domínio e rede antes de fazer login:

1. Na tela de **Login**, clique no **ícone de engrenagem** (⚙️) no canto superior direito.
2. A janela **Configurações de Domínio e Rede** será aberta:
   - **Domínio Padrão:** Digite o nome do domínio Active Directory (ex: `hospital.local` ou `HOSPITAL`).
   - **Botão Detectar Domínio:** Clica para detectar automaticamente o domínio da máquina em que você está logado no momento.
   - **Servidor / Host LDAP Alternativo (Opcional):** Permite apontar diretamente para o IP ou FQDN de um Controlador de Domínio específico (caso o DNS local não resolva automaticamente).
3. Clique em **Salvar**. As preferências são salvas em `%LocalAppData%\PrinterInstall\settings.json` e persistirão mesmo ao atualizar ou mover o executável único.

---

## 9. Como Criar e Publicar Releases no GitHub

O repositório está equipado com uma esteira de automação **CI/CD via GitHub Actions** ([`.github/workflows/release.yml`](.github/workflows/release.yml)) para empacotar e disponibilizar o executável único `Printer Install.exe` automaticamente em cada versão.

### 🚀 Método 1: Automático via Git Tag (Recomendado)

1. Certifique-se de que todas as alterações foram commitadas na branch principal:
   ```bash
   git add .
   git commit -m "feat: novas melhorias e correções"
   git push origin main
   ```
2. Crie uma tag de versão no formato `vX.Y.Z` e envie para o GitHub:
   ```bash
   git tag v1.0.0
   git push origin v1.0.0
   ```
3. O GitHub Actions iniciará automaticamente:
   - Compilará o projeto em ambiente Windows limpo (`windows-latest`).
   - Executará toda a suíte de testes automatizados.
   - Gerará o executável único autocontido (`Printer Install.exe`) com todos os drivers embutidos.
   - Criará a **Release** no GitHub e anexará o executável e a lista de checksums SHA256.

---

### 💻 Método 2: Via Script Local e Interface Web do GitHub

1. No terminal PowerShell, execute o script de publicação do projeto:
   ```powershell
   powershell -ExecutionPolicy Bypass -File .\scripts\Publish-PrinterInstall.ps1 -Configuration Release
   ```
2. O executável standalone será gerado em:
   ```
   publish\PrinterInstall\Printer Install.exe
   ```
3. No GitHub:
   - Acesse o repositório `brendonpereiradev/PrinterInstall`.
   - Vá na seção **Releases** e clique em **"Draft a new release"** (ou **"Create a new release"**).
   - Escolha ou crie uma tag (ex: `v1.0.0`).
   - Digite o título da versão e as notas da release.
   - Arraste e solte o arquivo `Printer Install.exe` na área de anexos de binários (*Attach binaries by dropping them here*).
   - Clique em **"Publish release"**.

---

### ⚡ Método 3: Via GitHub CLI (`gh`)

Se você utiliza o GitHub CLI instalado na sua máquina:
```powershell
# 1. Gerar o executável
powershell -ExecutionPolicy Bypass -File .\scripts\Publish-PrinterInstall.ps1 -Configuration Release

# 2. Publicar a release diretamente
gh release create v1.0.0 "publish\PrinterInstall\Printer Install.exe" --title "Release v1.0.0" --generate-notes
```

---

*Manual elaborado pela equipe de Engenharia de Software e Infraestrutura de TI.*
