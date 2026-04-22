# shell

My personal shell experience

## Feature

1. Git shell
2. Git compare with delta by default
3. Pre-config for git compare: 
   1. [code compare](https://www.devart.com/codecompare/visual-studio-integration.html)
   2. Built-in Visual Studio code compare
   3. [Semantic merge](https://semanticmerge.com/documentation/how-to-configure/semanticmerge-configuration-guide)

## Installation

1. Download the latest version of [git](https://git-scm.com/download). Suggested installation option:
   - Use git from the command line and also from 3rd-party software: this is needed if we want additional feature, such as creating ssh key, signing commit, etc
   - Use bundled OpenSSH
   - Use the native windows secure channel library: so we can rely on existing certificate store
   - Checkout as is, commit as is
   - Use Git Credential Manager Core
   - Check for symbolic links
2. Clone this repo somewhere, f.e. ~/sources/shell
3. Try configure your name globally so the global git config will be created (`git config --global user.name "your name"`)
4. Run [`zsh/bootstrap.sh`](zsh/bootstrap.sh) for zshell.
5. Run PowerShell core with administrative privilege, replace profile with this command: `New-Item -ItemType SymbolicLink -Path $PROFILE -Value C:\path\to\shell\powershell\_profile.ps1 -Force`
6. Run PowerShell core with user privilege for the first time, it will install required module (ohmposh, posh-git)
7. Right click on top title of your PowerShell core app, and choose Properties. Under Font tab, change font to CaskaydiaCove NF. You should see nice font now.
