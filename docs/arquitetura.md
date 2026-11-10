# Arquitetura

## Visão geral

Aplicativo WPF em .NET 8, sem dependências externas. Tudo que fala com o hardware usa P/Invoke das DLLs do próprio Windows (`dxva2.dll`, `gdi32.dll`, `user32.dll`, `shcore.dll`, `shell32.dll`).

```
App (orquestra)
 ├── SettingsService        JSON em %APPDATA%\DisplayDimmer
 ├── ThemeService           troca Dark.xaml / Light.xaml
 ├── DisplayController      regra de negócio de exibição
 │    ├── MonitorEnumerator   EnumDisplayMonitors + DisplayConfig + DDC/CI
 │    ├── ColorMath           rampa de gamma (brilho, contraste, temperatura)
 │    ├── GammaRamp           SetDeviceGammaRamp por dispositivo
 │    ├── DdcCi               brilho/contraste no painel (VCP 0x10 e 0x12)
 │    └── OverlayService      camada preta por monitor (escurecimento alternativo)
 ├── ScheduleService        agendamentos (DispatcherTimer, 20 s)
 ├── AppRuleService         processo em primeiro plano / tela cheia (1 s)
 ├── HotkeyService          RegisterHotKey
 ├── BrightnessKeyService   Raw Input HID Consumer 0x006F / 0x0070
 ├── TrayService            Shell_NotifyIcon + hook de roda do mouse
 └── IpcService             named pipe para a linha de comando
```

## Fluxo de aplicação de valores

1. A UI altera o estado de um alvo (`all`, `group:<id>` ou id do monitor).
2. `DisplayController` resolve os monitores do alvo e atualiza `AppSettings.Displays[id]`.
3. Para cada monitor, `ApplyDisplay` decide:
   - **DDC/CI** quando o monitor suporta e `UseHardware` está ligado → brilho e contraste no painel.
   - **Gamma** para o que não foi para o hardware: contraste e temperatura sempre; brilho apenas se o hardware não foi usado.
   - **Sobreposição** opcional quando `UseOverlayForSoftwareDimming` está ligado.
4. `SettingsService.SaveSoon` grava com atraso de 600 ms para não escrever a cada movimento de slider.

## Decisões

### Brilho relativo ao ajustar todos

`SetBrightness(..., relative: true)` calcula a média dos monitores do alvo e aplica o **delta** em cada um. Assim, se um monitor está em 80% e outro em 40%, mover o slider de 60% para 70% leva a 90% e 50%.

### Preto e branco máximos

`ColorMath.BuildRamp` recebe o preto e o branco em 0-255 e mapeia `saida = preto + (branco - preto) * curva`. Com preto `0x20` e branco `0xFF` o resultado é a curva com piso `#202020`; com preto `0x00` o preto puro volta a ser possível; baixar o branco comprime o topo da faixa sem tocar no preto. O campo de texto usa `ColorMath.TryParseHexLevel`, que aceita `#RRGGBB`, `#RGB` e o nível em hexadecimal.

### Verificação do DDC/CI e fallback

Alguns drivers devolvem handles inválidos (`0x0`) ou aceitam `SetMonitorBrightness` sem alterar o painel. Por isso o controlador:

1. descarta handles nulos na enumeração;
2. na primeira escrita de cada monitor, lê o valor de volta e compara (tolerância de 3 pontos);
3. ao detectar falha, marca o monitor como sem DDC/CI e passa a usar gamma, sem repetir a tentativa a cada movimento de slider;
4. permite forçar `DDC/CI` ou `Software` por monitor em Configurações → Monitores.

### Temperatura de cor

Aproximação de corpo negro de Tanner Helland, normalizada para 6500 K = (1, 1, 1). O ajuste é multiplicativo por canal, aplicado antes do piso de preto.

### Gamma x sobreposição

O gamma escurece sem criar janelas e não depende do driver, mas em níveis muito baixos perde faixas de cor. A sobreposição chega a níveis menores, porém cria uma janela por monitor e escurece inclusive a interface do aplicativo. Por isso o padrão é gamma e a sobreposição é opcional.

### Identificação de monitores

O id persistido é o `DeviceID` do monitor (`MONITOR\...\{GUID}\0001`), estável entre reinícios. O nome GDI (`\\.\DISPLAY1`) é usado apenas para aplicar a rampa de gamma. O nome amigável e a tecnologia de saída vêm de `QueryDisplayConfig` + `DisplayConfigGetDeviceInfo`; a tela interna é identificada por `outputTechnology == 0x80000000`.

### Primeira execução com um monitor novo

Ao encontrar um monitor sem estado salvo, o aplicativo lê o brilho e o contraste atuais via DDC/CI em vez de assumir 100%. Isso evita "sequestrar" o brilho que o usuário já tinha ajustado no painel.

### Instância única e linha de comando

Um mutex nomeado garante instância única. A segunda instância envia os argumentos para a primeira por named pipe (protocolo de bytes UTF-8 crus, terminado em `\n`) e imprime a resposta. Sem instância aberta, os comandos rodam em processo, aplicam o estado e encerram.

### Reaplicação após mudanças de tela

Jogos e alterações de resolução reiniciam a rampa de gamma. Um `DispatcherTimer` de 8 s compara a assinatura do ambiente (`SM_CMONITORS`, tela virtual) e, quando muda, reenumera os monitores e reaplica os valores.

### Encerramento

Ao sair, as rampas de gamma são devolvidas ao neutro para não deixar a tela escurecida após o processo terminar.

## Persistência

`%APPDATA%\DisplayDimmer\settings.json` com `System.Text.Json` (enums como texto, propriedades ignoradas para campos derivados). Arquivo corrompido é renomeado com carimbo de data e um novo é criado.

## Testes e diagnóstico

- `--debug` imprime monitores, área, escala, DDC/CI, tecnologia de saída e estado salvo.
- `DISPLAYDIMMER_LOG=1` grava eventos de bandeja e IPC em `%TEMP%\displaydimmer-ipc.log`.
- `--shot <arquivo.png>` e `--shot-tabs <pasta>` geram imagens das janelas para revisão visual.

## Limitações conhecidas

- DDC/CI depende do monitor e do driver; alguns modelos recusam leitura e só aceitam escrita.
- HDR ativo pode limitar o efeito da rampa de gamma.
- Teclas nativas de brilho dependem de o driver expor HID Consumer.
- A sobreposição cobre todas as janelas, inclusive a do próprio aplicativo.
