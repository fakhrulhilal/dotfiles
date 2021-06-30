# Description

Data files compatible with VIm & NeoVim

## Installation

```powershell
If (-not(Test-Path -PathType Container -Path "$Env:LOCALAPPDATA\nvim")) {
	New-Item -ItemType Directory -Path "$Env:LOCALAPPDATA\nvim"
}
New-Item -ItemType File -Path "$Env:LOCALAPPDATA\nvim\init.vim" -Force
Set-Content -Path "$Env:LOCALAPPDATA\nvim\init.vim" -Value '
set runtimepath^=~/.vim runtimepath+=~/.vim/after
let &packpath=&runtimepath
source ~/.vimrc
'
New-Item -ItemType Junction -Path ~/.vim -Value 'D:\Cloud\OneDrive\Pribadi\shell\neo&vim'
New-Item -ItemType SymbolicLink -Path ~/.vimrc -Value 'D:\Cloud\OneDrive\Pribadi\shell\neo&vim\rc.txt' -Force
```

