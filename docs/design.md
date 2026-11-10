# Especificação visual

Referência: captura enviada pelo usuário (janela compacta "Display Dimmer").

## Janela principal

| Item | Valor |
|---|---|
| Largura | 420 px (fixa) |
| Altura | automática (conteúdo), ~382 px |
| Cantos | raio 12 px |
| Fundo | `#202020` (preto mais escuro permitido) |
| Borda | 1 px `#383838` |
| Sombra | desfoque 18, deslocamento 3, opacidade 0.55 |
| Margem interna | 16 px |
| Fonte | Segoe UI |

### Estrutura

1. **Barra de título** (altura 28): ícone 18×18, título 13 px semibold, botão fechar 26×26 à direita.
2. **Seletor de monitores**: altura 36, fundo `#2C2C2C`, raio 8, seta à direita.
3. **Brilho**: rótulo 13 px semibold à esquerda, valor 13 px secundário à direita, slider azul.
4. **Contraste**: mesmo padrão; valor no formato `Neutral · 50%`.
5. **Temperatura**: rótulo + selo "Pro" (fundo `#1E3A5F`, texto `#8FB8FF`, 9.5 px); valor no formato `Neutral · 6.500 K`; trilho com gradiente quente→neutro→frio.
6. **Botões**: "Configurações" e "Restaurar níveis", altura 40, fundo `#2C2C2C`, raio 8, largura igual.
7. **Níveis** (linha clicável): rótulo + resumo `Preto #202020 · Branco #FFFFFF` + chevron que gira 180° ao abrir.
8. **Painel de níveis** (recolhido por padrão): sliders "Preto máximo" e "Branco máximo" com trilho em gradiente preto→branco e, à direita do rótulo, um campo de texto de 96×26 em Consolas com o código da cor (`#202020`), editável.
9. **Rodapé do painel**: dica 11.5 px sobre digitar o código e pressionar Enter.

### Sliders de nível

- Trilho em gradiente `#000000 → #FFFFFF`, sem preenchimento colorido.
- Polegar branco com contorno `#202020` de 1 px, para continuar visível no extremo branco.

### Sliders

- Trilho: 6 px de altura, raio 3.
- Preenchimento: `#3B82F6` até o polegar.
- Trilho vazio: `#3E3E3E`.
- Polegar: círculo 15 px branco com sombra suave.
- Clique em qualquer ponto do trilho move o polegar (`IsMoveToPointEnabled`).
- Temperatura: trilho com gradiente `#F0A03C → #FFD3A0 → #FFFFFF → #CFE0FF → #4C8DF6`, sem preenchimento azul.

## Janela de configurações

| Item | Valor |
|---|---|
| Tamanho | 760 × 580 (mínimo 680 × 480) |
| Navegação | coluna fixa de 186 px com itens de 12 px de padding |
| Conteúdo | área rolável com cartões de raio 10 e borda `#363636` |
| Abas | Geral, Presets, Automações, Monitores e grupos, Atalhos |

### Cartões de automação

- Nome 13 px semibold + selo de estado (`Ativo` verde, `Pausado` âmbar).
- Linha secundária com horário/preset ou processo/preset.
- Ações à direita: `Pausar`/`Ativar` e `Excluir`, altura 28.

## Cores

### Tema escuro

| Token | Valor |
|---|---|
| WindowBg | `#202020` |
| PanelBg | `#262626` |
| Surface | `#2C2C2C` |
| SurfaceHover | `#343434` |
| Border | `#3C3C3C` |
| TextPrimary | `#F3F3F3` |
| TextSecondary | `#A6A6A6` |
| TextMuted | `#7A7A7A` |
| Accent | `#3B82F6` |
| TrackBg | `#3E3E3E` |
| BadgeActive | fundo `#1B3A2A`, texto `#6EE7A8` |
| BadgePaused | fundo `#3A3220`, texto `#E5C07B` |

### Tema claro

| Token | Valor |
|---|---|
| WindowBg | `#F7F7F8` |
| PanelBg | `#FFFFFF` |
| Surface | `#EFEFF2` |
| Border | `#DCDCE1` |
| TextPrimary | `#1B1B1F` |
| TextSecondary | `#5F5F68` |
| Accent | `#2563EB` |
| TrackBg | `#D6D6DC` |

O tema do sistema segue `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme`, reavaliado a cada 30 segundos.

## Acessibilidade

- Todos os sliders têm `AutomationProperties.Name`.
- Contraste de texto principal acima de 7:1 nos dois temas.
- Alvos de clique com no mínimo 28 px.
- Nenhuma informação depende apenas de cor: os selos têm texto (`Ativo`/`Pausado`).
