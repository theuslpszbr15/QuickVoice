<p align="center"><img src="docs/icon.png" width="128" alt="QuickVoice: um balão de fala meio dito, meio tracejado"></p>

<h1 align="center">QuickVoice</h1>

<p align="center"><b>Controle o Windows pela voz. Ele age antes de você terminar a frase.</b></p>

Diga "abre o bloco de notas e digita bom dia": o Bloco de Notas já abre enquanto você
ainda está dizendo "digita", e "bom dia" é digitado na pausa. Grátis, sem conta e sem
chave de API: por padrão, as decisões são tomadas no seu PC.

Inspirado no [partway](https://github.com/tostechbr/partway) (macOS, Swift), portado para
Windows (.NET 10 + WPF). Opcionalmente usa o [Jev](https://typesafe.ai/blog/introducing-system-one-models-and-jev),
o System One da TypeSafe, para entender frases mais soltas.

## O que dá pra dizer

| Frase | O que acontece |
|---|---|
| "abre o bloco de notas", "open spotify" | abre o app, muitas vezes antes de você terminar a frase |
| "cria uma nota nova", "new tab" | Ctrl+N no app em primeiro plano |
| "abre o linkedin no google", "open x dot com" | abre o site |
| "pesquisa receita de pão de queijo" | pesquisa no navegador em primeiro plano (ou no padrão) |
| "digita bom dia" | digita no app em primeiro plano |

Encadeie numa frase só: "abre o terminal e digita dir".

**Modo de escrever:** aperte **Alt+Shift+Espaço** (ou o botão ⌨ na pílula, ou o menu da bandeja),
escreva o comando, por exemplo "Pesquise no google sobre o dólar", e aperte **Enter**. Esc cancela.
O comando passa pelo mesmo motor da voz, e o foco volta para o app em que você estava, então
"digita…" escreve lá.

## Instalar e usar

Requisitos: Windows 10 2004+ ou Windows 11 e [.NET 10 SDK](https://dotnet.microsoft.com/download).

**Sem pagar nada:** sem chave, as decisões vêm de regras locais (`LocalDecider`): grátis,
offline e instantâneas, para frases como "abre X", "pesquisa X", "digita X", "entra no X",
"cria uma nota nova". Com uma chave paga do Jev ([console.typesafe.ai](https://console.typesafe.ai/keys)),
ele passa a decidir e entende frases mais soltas: ícone na bandeja → "Usar chave do Jev".

```powershell
dotnet run --project src/QuickVoice
```

1. Uma pílula flutua no topo da tela. Aperte **Alt+Espaço** (ou **Ctrl+Alt+Espaço**, se outro
   app já usa Alt+Espaço, como o PowerToys Run) ou o botão ▶ e fale.
2. Na primeira vez, o Windows pede duas permissões. O próprio app abre a página certa:
   - **Configurações → Privacidade e segurança → Fala → Reconhecimento de fala online: Ativado**
     (o ditado contínuo do Windows precisa disso).
   - **Configurações → Privacidade e segurança → Microfone → Permitir que apps da área de
     trabalho acessem o microfone**.
3. O idioma é o da fala do Windows (pt-BR e en-US testados neste PC). Para outro:
   `--locale en-US`. Se faltar, instale em **Configurações → Hora e idioma → Fala**.

O ícone na bandeja (perto do relógio) começa/pausa com um clique; o botão direito tem
"Escrever um comando", "Usar chave do Jev (opcional, pago)…" e "Sair". Arraste a pílula para qualquer lugar.

Para gerar um executável único:

```powershell
dotnet publish src/QuickVoice -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

O ícone (balão de fala meio dito, meio tracejado, como o do original) sai de `scripts/make-icon.ps1`,
que gera `src/QuickVoice/app.ico`.

## Como ele decide

A lógica em `src/QuickVoice.Core` é um port 1:1 do `PartwayCore`, com os mesmos testes.
Quem responde "o que fazer" é um `IDecider`: as regras locais (`LocalDecider`, padrão) ou o Jev.

- Cada transcrição parcial vira uma pergunta: qual ação, qual app, quais
  palavras são o argumento, e um sim/não "pede para abrir um app?".
- Abrir app dispara no meio da frase quando duas parciais seguidas concordam e o app é
  nomeado sem dúvida (≥ 0,95). Novo item precisa de 0,85.
- Pesquisa, site e digitação esperam a pausa (600 ms): "pesquisa norbert" não é
  "pesquisa norbert wiener" até você parar.
- Na pausa ele age pelo **resultado**, não pelo rótulo: ir ao LinkedIn, pesquisar por ele ou
  abrir o navegador citado dão no mesmo lugar, então as probabilidades somam.
- Nada escreve texto por conta própria: o código corta os trechos do que você disse, o decisor
  escolhe um, e ele é copiado literalmente.

## Diferenças para o original (macOS → Windows)

| partway (macOS) | QuickVoice (Windows) |
|---|---|
| Apple Speech (`SFSpeechRecognizer`) | `Windows.Media.SpeechRecognition` (ditado contínuo, com hipóteses parciais) |
| `/Applications/*.app` | pasta `shell:AppsFolder` (Menu Iniciar: apps Win32 e da Store, com nomes já no idioma do Windows) |
| `NSWorkspace.openApplication` | `explorer.exe shell:AppsFolder\<id>` |
| `CGEvent` (precisa de Acessibilidade) | `SendInput` Unicode (sem permissão extra; não digita em janelas de administrador) |
| ⌘N | Ctrl+N |
| ⌥Space | Alt+Espaço, ou Ctrl+Alt+Espaço |
| barra de menus | ícone na bandeja |
| Jev (pago) decide tudo | regras locais grátis por padrão; Jev opcional |
| chave em `~/.config/partway/api-key` (0600) | `%APPDATA%\QuickVoice\api-key.bin`, criptografada com DPAPI |
| `~/Library/Logs/partway` | `%LOCALAPPDATA%\QuickVoice\Logs` |
| — | modo de escrever (Alt+Shift+Espaço) |

## Testar sem microfone

```powershell
dotnet run --project src/QuickVoice -- --text "abre o chrome e pesquisa receita de pão de queijo" --dry-run --log
dotnet test
```

`--text` manda a frase palavra por palavra no ritmo da fala (`--wpm 160`), ou de uma vez com
`--write`; `--dry-run`
mostra os comandos sem executá-los; `--log` grava um `.jsonl` local com tudo o que foi
ouvido (eventos: start, heard, ask, answer, fire, run, pause, end, error).

## Privacidade

A fala vira texto pelo reconhecedor do Windows (o ditado contínuo usa o serviço online da
Microsoft). Com as regras locais, nada mais sai do PC; só com uma chave do Jev as palavras
vão para a TypeSafe. `--log` fica desligado por padrão e, quando
ligado, guarda tudo o que o microfone ouvir.

## Limitações

- Abrir um app que já está aberto pode abrir outra janela (depende do app), em vez de só
  trazê-lo para frente.
- Não clica em botões de outros apps: ainda não lê a tela.
- Texto que soa como tarefa ("escreve lista de compras") pode não fazer nada, e a onda
  sonora é decorativa (como no original).
- Se o Windows reescrever palavras no resultado final (ex.: "dois" → "2"), os deslocamentos
  de palavras mudam; o app compara as palavras normalizadas para minimizar isso.

## Licença

[MIT](LICENSE), mantendo o aviso do projeto original ([partway](https://github.com/tostechbr/partway), de Tiago Oliveira).
