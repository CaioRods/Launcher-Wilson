# AWcontrol

Aplicativo Windows para trabalhar com planilhas do Excel AW e documentos do Word AW em abas na mesma janela, além de instalar e atualizar os dois editores. Créditos: @CaioRods. A identidade visual utiliza o logotipo da [Alimentos Wilson](https://www.alimentoswilson.com.br/); as origens dos recursos estão em [ATTRIBUTION.md](launcher/assets/ATTRIBUTION.md).

A Home mostra as ações de criar/abrir e uma lista curta de arquivos recentes, com busca. Favoritos, modelos e gerenciamento dos aplicativos ficam no menu lateral. As abas podem ser reorganizadas por arraste e fechadas individualmente, com confirmação de alterações. O perfil é compartilhado entre os editores. Com a central aberta, arquivos abertos pelo Explorer entram nela; com a central fechada, os editores continuam funcionando individualmente.

## Instalação

1. Baixe o ZIP em [Releases](https://github.com/CaioRods/Launcher-Wilson/releases) e extraia todo o conteúdo.
2. Abra **AWcontrol.exe**.
3. Acesse **Aplicativos** ou **Gerenciar aplicativos** para instalar as versões incluídas. Após a instalação, os botões passam a **Abrir**. Uma atualização encontrada oferece **Atualizar**. Instalações existentes são reconhecidas também ao reabrir a central.
4. Na Home, escolha **Nova planilha**, **Novo documento** ou abra um arquivo existente.

Para atualizar um pacote de testes, feche a central e os editores, extraia o novo pacote completo e atualize ambos em **Gerenciar aplicativos**. Uma alteração no código do GitHub só chega aos outros PCs depois que uma nova versão é distribuída e instalada.

Compatível com Windows 10 e 11 x64. A opção **Instalar para todos os usuários** solicita permissão de administrador. Com ela desligada, a instalação ocorre no perfil atual. Os dois MSIs ficam na pasta `instaladores` e a instalação inicial pode ser feita offline.

## Atualizações

Use **Buscar atualizações** para consultar as Releases do [Excel AW](https://github.com/CaioRods/Excel-AW/releases) e do [Word AW](https://github.com/CaioRods/Word-AW/releases). Para oferecer uma nova versão, publique uma tag no formato `vMAJOR.MINOR.PATCH` no repositório do app, com o MSI x64 como asset da Release. Use o nome `Excel-AW-x64.msi` ou `Word-AW-x64.msi` conforme o app. A central verifica a soma SHA-256 informada pelo GitHub antes da instalação.

## Compilação

A central usa C# e Windows Forms. O workflow **Central Alimentos Wilson** compila a central, gera os MSIs das revisões fixadas do Excel AW e do Word AW usando Rust e WiX, e reúne os três executáveis de instalação em um ZIP.

A central redimensiona os editores somente quando a área disponível muda. As animações suaves ficam nas abas e nos cartões, sem movimentar a janela inteira do documento. Menus permanecem vivos durante o processamento do clique e são liberados com o controle proprietário.

O nome do executável é `AWcontrol.exe`. Aplicativos abre uma página na central. Clique com o botão direito em uma aba para salvar, salvar como, renomear, destacar ou fechar. Arrastar a aba para fora mostra a indicação de janela separada; soltar destaca o mesmo editor sem recriar o documento. O cabeçalho interno dos editores é fixo enquanto hospedado. Somente a central fica na barra de tarefas nesse modo; uma janela destacada recupera o ícone do editor.


A galeria **Modelos** separa Excel AW e Word AW e permite filtrar por aplicativo. Os modelos
originais em pt-BR incluem orçamento, vendas, estoque, tarefas e fluxo de caixa; relatório,
carta, ata de reunião, proposta, currículo e plano de projeto. Cada escolha cria um novo
arquivo editável pelo motor do respectivo app.

Para devolver uma janela separada, arraste-a sobre o AWcontrol e solte quando aparecer a
indicação de retorno. Também é possível puxar uma aba nativa para mover a janela. A transferência
mantém o processo, o conteúdo não salvo e o histórico. Se houver várias abas na janela nativa,
o conjunto é transferido e as abas internas continuam acessíveis. As bordas permitem
redimensionar as janelas; o ícone da central permanece sendo o do AWcontrol.

As responsabilidades da central, das sessões e dos editores estão descritas na
[arquitetura do AWcontrol](docs/architecture-awcontrol.md), incluindo o limite previsto para
uma futura área de comunicação.
