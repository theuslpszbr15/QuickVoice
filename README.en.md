<p align="center"><img src="docs/icon.png" width="128" alt="QuickVoice: a speech bubble, half said and half dashed"></p>

<h1 align="center">QuickVoice</h1>

<p align="center"><b>Control Windows with your voice. It acts before you finish the sentence.</b></p>

<p align="center">
  <a href="https://github.com/theuslpszbr15/QuickVoice/releases/latest"><img src="https://img.shields.io/github/v/release/theuslpszbr15/QuickVoice?label=download&amp;color=ffd60a" alt="Latest release"></a>
  <a href="https://github.com/theuslpszbr15/QuickVoice/actions/workflows/ci.yml"><img src="https://github.com/theuslpszbr15/QuickVoice/actions/workflows/ci.yml/badge.svg" alt="Build and tests"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-blue" alt="MIT license"></a>
  <br><a href="README.md">Português</a>
</p>

<p align="center"><a href="docs/demo.mp4"><img src="docs/demo.gif" width="800" alt="Demo: QuickVoice opens Notepad mid-sentence, types good morning and searches the dollar rate on Google"></a><br><sub><a href="docs/demo.mp4">Watch the full-resolution video (MP4)</a> · <a href="docs/demo-vertical.mp4">vertical version (Reels, TikTok, Shorts)</a></sub></p>

Say "open notepad and type good morning": Notepad is already opening while you are still saying
"type", and "good morning" is typed at the pause. Free, no account and no API key: by default every
decision is made on your PC.

Inspired by [partway](https://github.com/tostechbr/partway) (macOS, Swift), ported to Windows
(.NET 10 + WPF). It can optionally use [Jev](https://typesafe.ai/blog/introducing-system-one-models-and-jev),
TypeSafe's System One, to understand looser phrasing.

## What you can say

The local rules understand Portuguese and English.

| Say | What happens |
|---|---|
| "open notepad", "abre o spotify" | opens the app, often before you finish the sentence |
| "new tab", "cria uma nota nova" | Ctrl+N in the app in front |
| "open x dot com", "entra no linkedin" | opens the site |
| "search norbert wiener", "pesquisa receita de bolo" | searches in the browser in front (or the default one) |
| "type see you tomorrow" | types into the app in front |
| "type hi comma how are you question mark" | types "hi, how are you?" (spoken punctuation) |
| "close the window", "close tab", "minimize", "maximize", "switch window", "show desktop" | controls the window in front |
| "volume up", "volume 30", "mute" | sound |
| "next track", "pause the music", "previous track" | media keys (Spotify, YouTube…) |
| "take a screenshot", "lock the computer" | screenshot saved to Pictures → Screenshots; lock screen |
| "click save", "click on the send button" | clicks the button, link or menu item with that name in the window in front |
| "start dictation" … "stop dictation" | everything said in between is typed, at each pause |
| "close chrome", "switch to teams" | closes every window of the app / brings the app forward |
| "chrome on the left", "coloca o bloco de notas na direita", "send teams to the other monitor" | snaps windows side by side or moves them to the other monitor |
| "open the downloads", "abre a pasta projetos", "open the latest pdf" | opens folders (your own too, by name) and the newest file of a kind |
| "what is 15 percent of 320", "what time is it in Tokyo", "que dia é hoje" | answers on the bar (math is copied to the clipboard) |
| "undo" | closes the window QuickVoice just opened, else Ctrl+Z |
| "do it again", "repete" | runs the last sentence's commands again |
| your phrases | your own shortcuts and routines (see below) |

Chain them in one breath: "open terminal and type dir", "open spotify and volume up". If the app is
already open, "open…" brings its window forward instead of starting another one.

**Write mode:** press **Alt+Shift+Space** (or the ⌨ button on the pill, or the tray menu), type a
command such as "search the weather in Lisbon" and press **Enter**. Esc cancels. The command goes
through the same engine as speech, and focus returns to the app you were in, so "type…" writes there.

## Install and use

**Download the installer** from [Releases](https://github.com/theuslpszbr15/QuickVoice/releases/latest)
(`QuickVoice-Setup-x.y.z.exe`, no admin rights needed) or the portable build (`.zip`: extract and run
`QuickVoice.exe`). Requires Windows 10 2004+ or Windows 11 (64-bit). No .NET install needed.

**Free:** without a key, decisions come from local rules (`LocalDecider`): free, offline and instant.
With a paid Jev key ([console.typesafe.ai](https://console.typesafe.ai/keys)) Jev decides and understands
looser phrasing: tray icon → "Usar chave do Jev". System controls, clicks and shortcuts stay on the local rules.

1. A pill floats at the top of the screen. Press **Alt+Space** (or **Ctrl+Alt+Space** if another app
   such as PowerToys Run owns Alt+Space) or the ▶ button, and speak.
2. The first time, Windows asks for two permissions; the app opens the right page:
   - **Settings → Privacy & security → Speech → Online speech recognition: On** (Windows continuous
     dictation needs it; Whisper does not).
   - **Settings → Privacy & security → Microphone → Let desktop apps access your microphone**.
3. The language is the Windows speech language (pt-BR and en-US tested). Change it in QuickVoice's
   **Settings** or with `--locale en-US`.

The tray icon (next to the clock) starts/pauses with a click; right-click for "Escrever um comando" (write
mode), "Configurações…" (settings), "Editar meus atalhos…" (shortcuts), "Histórico…" (history: double-click a
line to repeat it; kept only on the PC), "Procurar atualizações" (updates), the Jev key and "Sair" (quit).
Drag the pill anywhere.

### Settings

Tray → **Configurações…** (saved to `%APPDATA%\QuickVoice\config.json`):

- **Speech language.**
- **Recognition:** Windows (online, fastest) or **Whisper (offline)**: runs
  [whisper.cpp](https://github.com/ggml-org/whisper.cpp) on your PC and nothing leaves it. The model (tiny
  75 MB, base 140 MB or small 470 MB) is downloaded once. With Whisper, partial results arrive about every
  second, so apps open mid-sentence a little later.
- **Listen shortcut:** Alt+Space, Ctrl+Alt+Space or Ctrl+Shift+Space.
- **Wake word:** always listening, acting only on sentences that start with "QuickVoice"
  ("QuickVoice, open chrome"). You choose the name and its variations.
- **Bar look:** dark or light theme, accent color (yellow, blue, green, pink) and size.
- **Updates:** checks GitHub for a newer release at startup; tray → **Atualizar** downloads the release
  installer, installs it silently and reopens the app (the portable build opens the release page instead).

### Your own shortcuts

Tray → **Editar meus atalhos…** opens `%APPDATA%\QuickVoice\atalhos.json` in Notepad. Each shortcut has
phrases and what to do, in order: open apps (by name), folders, files, programs or sites; type a text; press
keys. A list in "open" makes a **routine**. Save and it applies. English keys work too (`say`, `open`, `type`, `keys`).

```jsonc
[
  { "say": ["work mode"], "open": ["Outlook", "Microsoft Teams", "https://github.com"] },
  { "say": ["open my project", "my project"], "open": "%USERPROFILE%\\Projects\\site" },
  { "say": ["email signature"], "type": "Best regards,\nMatheus" },
  { "say": ["save all"], "keys": "ctrl+shift+s" }
]
```

### Run from source

With the [.NET 10 SDK](https://dotnet.microsoft.com/download):

```powershell
dotnet run --project src/QuickVoice
```

A release is built by pushing a `v*` tag (`git tag v1.2.0; git push --tags`): the
[release workflow](.github/workflows/release.yml) runs the tests, builds the installer (Inno Setup,
[`installer/QuickVoice.iss`](installer/QuickVoice.iss)) and the `.zip`, and publishes them to Releases. Every
push runs build and tests in [CI](.github/workflows/ci.yml).

## How it decides

The logic in `src/QuickVoice.Core` is a 1:1 port of `PartwayCore`, with the same tests. What to do comes
from an `IDecider`: the local rules (`LocalDecider`, default) or Jev.

- Every partial transcript becomes a question: which action, which app, which words are the argument,
  and a yes/no "does it ask to open an app?".
- Opening an app fires mid-sentence when two partials in a row agree and the app is named beyond doubt
  (≥ 0.95). A new item needs 0.85.
- Search, sites and typing wait for the pause (600 ms): "search norbert" is not "search norbert wiener"
  until you stop.
- At the pause it acts on the **outcome**, not the label: going to LinkedIn, searching for it or opening the
  browser named all end up in the same place, so their probabilities add up.
- Nothing writes text on its own: code cuts spans out of what you said, the decider picks one, and it is
  copied verbatim.
- System controls, clicks and shortcuts come only from the local rules (`Controls`, `LocalDecider`) and act
  at the pause. "click…" finds the name through Windows UI Automation in the window in front.

## Testing without a microphone

```powershell
dotnet run --project src/QuickVoice -- --text "open chrome and search cake recipes" --dry-run --log
dotnet test
```

`--text` feeds the sentence word by word at speaking pace (`--wpm 160`), or all at once with `--write`;
`--dry-run` prints the commands instead of running them; `--log` writes a local `.jsonl` with everything
heard (events: start, heard, ask, answer, fire, run, pause, end, error).

## Privacy

With Windows recognition, speech becomes text through Microsoft's online service; with **Whisper**, it stays
on the PC. With the local rules nothing else leaves the PC; only with a Jev key are the words sent to
TypeSafe. With the wake word on, the microphone is always open (anything that does not start with the name is
discarded). `--log` is off by default and, when on, keeps everything the microphone hears.

## Limitations

- "click…" needs the app to expose its controls through UI Automation (most do; games and some
  Electron/Java apps do not).
- It cannot type into or click administrator windows (Windows blocks it).
- Text that sounds like a task ("write a shopping list") may do nothing, and the waveform is decorative
  (as in the original).
- If Windows rewrites words in the final result ("two" → "2"), word offsets shift; the app compares
  normalized words to keep that rare.

## License

[MIT](LICENSE), keeping the notice of the original project ([partway](https://github.com/tostechbr/partway),
by Tiago Oliveira).
