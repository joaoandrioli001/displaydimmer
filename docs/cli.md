# Linha de comando

O mesmo executável controla os monitores sem abrir a janela. Se o aplicativo já estiver aberto, o comando é enviado para a instância em execução (via named pipe) e responde na hora; se não estiver, o comando é aplicado e o processo encerra.

```powershell
.\dist\DisplayDimmer.exe --list
```

## Comandos

| Comando | O que faz |
|---|---|
| `--list` / `-l` | Lista os monitores, resolução, suporte a DDC/CI e valores atuais |
| `--get` / `-g` | Mostra brilho, contraste e temperatura do alvo |
| `--set` / `-s` | Altera valores (`brightness=`, `contrast=`, `temp=`, `black=`, `white=`) |
| `--preset "Nome"` / `-p` | Aplica um preset pelo nome ou id |
| `--reset` | Restaura 100% / 50% / 6500 K no alvo |
| `--status` | Mostra automações, tema, preto máximo e valores |
| `--automation on\|off` | Pausa ou ativa agendamentos e regras |
| `--theme dark\|light\|system` | Troca o tema |
| `--show` | Abre a janela principal |
| `--exit` | Encerra o aplicativo e restaura o gamma |
| `--debug` | Diagnóstico: DDC/CI, área, escala, estado, última aplicação e rampa de gamma ativa |
| `--ddc` | Testa leitura e escrita DDC/CI regravando o valor atual (não altera o brilho) |
| `--help` / `-h` | Ajuda |

## Opções

| Opção | Efeito |
|---|---|
| `--display all\|1\|nome` / `-d` | Define o alvo: todos, índice na lista, nome ou id do monitor |
| `--json` / `-j` | Saída em JSON |
| `--minimized` | Inicia direto na bandeja (usado pela inicialização com o Windows) |

Atalhos de valor: `-b` brilho, `-c` contraste, `-t` temperatura.

`black=` e `white=` aceitam número de 0 a 255 ou código de cor (`#RRGGBB`, `#RGB`).

## Exemplos

```powershell
# Listar monitores
.\DisplayDimmer.exe -l

# Brilho em todos os monitores
.\DisplayDimmer.exe -b 45

# Só no segundo monitor, com contraste e temperatura
.\DisplayDimmer.exe --set brightness=70 contrast=55 temp=5000 --display 2

# Preto máximo #303030 e branco máximo #DCDCDC no monitor principal
.\DisplayDimmer.exe --set black=#303030 white=#DCDCDC --display 1

# Preto puro (#000000) liberado
.\DisplayDimmer.exe --set black=#000000

# Aplicar preset no monitor principal
.\DisplayDimmer.exe --preset "Noite" --display 1

# Pausar automações antes de uma apresentação
.\DisplayDimmer.exe --automation off

# Ler o estado em JSON (útil em scripts)
$estado = .\DisplayDimmer.exe --get --json | ConvertFrom-Json
if ($estado.brilho -gt 80) { .\DisplayDimmer.exe -b 60 }
```

## Scripts

Exemplo de rotina noturna no PowerShell:

```powershell
# diminui o brilho aos poucos entre 20h e 23h
$hora = (Get-Date).Hour
$alvo = if ($hora -ge 23) { 25 } elseif ($hora -ge 20) { 55 } else { 100 }
& "$PSScriptRoot\DisplayDimmer.exe" --set brightness=$alvo temp=3400
```

## Códigos de saída

- `0`: comando executado (mesmo quando a resposta é uma mensagem de erro de domínio, como preset inexistente).
- `1`: falha ao iniciar o aplicativo.

## Observações

- Consultas (`--list`, `--get`, `--status`, `--debug`) não alteram brilho, contraste nem gamma.
- `--exit` restaura as rampas de gamma para o neutro, para não deixar a tela escurecida.
- Se a instância aberta não responder em 1,5 s, o comando informa que o aplicativo não respondeu em vez de ficar travado.
