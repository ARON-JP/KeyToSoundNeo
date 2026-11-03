# KeyToSound Neo

Windows desktop app that plays assigned sounds while you type in its keyboard view.

## Run

Open `bin\Release\net10.0-windows\win-x64\publish\KeyToSoundNeo.exe`. The published executable is self-contained and does not require a separate .NET installation.

## Use

1. Select a key in the keyboard view.
2. Choose a layout zone and add one or more press and release sound files.
3. Keep the keyboard view open and this window in the foreground to hear the assigned sounds. A held key plays once on press and once on release.
4. Open **プロファイル** to change the response, apply a preset, create profiles, or import/export `.ksn` files. Rapid repeated key presses reduce volume.
5. Open **詳細設定** to configure startup, process priority, and optional background playback.

The JIS and ANSI layouts can be selected in the keyboard view. **実際のキーを押して登録** maps the selected sound to the next physical key pressed. By default, key sounds are only processed while this app is active. The opt-in background setting uses Windows Raw Input without blocking keystrokes, plays sounds only for keys assigned in the current profile, and does not log or transmit input. Audio playback is asynchronous and continues independently of the UI once triggered.

Sound decoding uses NAudio and Windows Media Foundation. WAV and MP3 are supported directly; other formats depend on the codecs available on the Windows installation.

## Build

```powershell
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
```

`.ksn` files are ZIP-based packages containing the profile, relative sound references, and bundled audio files.
