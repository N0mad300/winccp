# winccp

A minimalist CLI player based on Windows SMTC to control any media.

## Hotkeys

| Hotkey			| Action			|
| -----------------	| ----------------- |
| Space/P			| Pause/Play		|
| LeftArrow/B		| Previous track	|
| RightArrow/N		| Next track		|
| Escape/Q			| Quit				|
| R					| Refresh render	|

## Configuration

The player can be configured using an INI file, the app check for this 
file `$HOME\.config\winccp\config.ini`

The default config is equivalent to :
```ini
[Widgets]
# Order of the centered widgets in the terminal from top to bottom of the terminal
Order=AlbumCover,Infos,ProgressBar,Source

[AlbumCover]
Show=true

[Infos]
Show=true
Color=White

[ProgressBar]
Show=true
FilledSymbol=━
EmptySymbol=━
FilledColor=Green
EmptyColor=DarkGray
Time=true
TimeColor=Yellow

[Source]
Show=false
Color=DarkGray
```