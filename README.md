# Central de Apps Alimentos Wilson

Aplicativo Windows para instalar e atualizar o Excel AW e o Word AW. Desenvolvido por Caio Victor Rodrigues para uso interno na Alimentos Wilson, com o logotipo do [site oficial da empresa](https://www.alimentoswilson.com.br/).

## Instalação

1. Baixe o ZIP em [Releases](https://github.com/CaioRods/Launcher-Wilson/releases) e extraia todo o conteúdo.
2. Abra **Alimentos Wilson.exe**.
3. Escolha **Instalar Excel AW**, **Instalar Word AW** ou **Instalar os dois**.

Compatível com Windows 10 e 11 x64. A opção **Instalar para todos os usuários** solicita permissão de administrador. Com ela desligada, a instalação ocorre no perfil atual. Os dois MSIs ficam na pasta `instaladores` e a instalação inicial pode ser feita offline.

## Atualizações

Use **Buscar atualizações** para consultar as Releases do [Excel AW](https://github.com/CaioRods/Excel-AW/releases) e do [Word AW](https://github.com/CaioRods/Word-AW/releases). Para oferecer uma nova versão, publique uma tag no formato `vMAJOR.MINOR.PATCH` no repositório do app, com o MSI x64 como asset da Release. Use o nome `Excel-AW-x64.msi` ou `Word-AW-x64.msi` conforme o app. A central verifica a soma SHA-256 informada pelo GitHub antes da instalação.

## Compilação

A central usa C# e Windows Forms. O workflow **Central Alimentos Wilson** compila a central, gera os MSIs das revisões fixadas do Excel AW e do Word AW usando Rust e WiX, e reúne os três executáveis de instalação em um ZIP.
