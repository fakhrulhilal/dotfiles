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

1. Get the latest release of [PowerShell core](https://github.com/PowerShell/PowerShell/releases). Be sure to register path when installing.
2. Download the latest version of [git](https://git-scm.com/download). Suggested installation option:
   - Use git from the command line and also from 3rd-party software: this is needed if we want additional feature, such as creating ssh key, signing commit, etc
   - Use bundled OpenSSH
   - Use the native windows secure channel library: so we can rely on existing certificate store
   - Checkout as is, commit as is
   - Use Git Credential Manager Core
   - Check for symbolic links
4. Clone this repo somewhere, f.e. ~/sources/shell
5. Try configure your name globally so the global git config will be created (`git config --global user.name "your name"`)
6. Edit your global git config (%UserProfile%\.gitconfig), add this line:

   ```
  [include]
		path = C:\\path\\to\\shell\\config\\git.txt
   ```

6. Run PowerShell core with administrative privilege, replace profile with this command: `New-Item -ItemType SymbolicLink -Path $PROFILE -Value C:\path\to\shell\powershell\_profile.ps1 -Force`
7. Run PowerShell core with user privilege for the first time, it will install required module (ohmposh, posh-git)
8. Get [nerdfonts](https://www.nerdfonts.com/), suggested to use [_CaskaydiaCove_](https://github.com/ryanoasis/nerd-fonts/releases/download/v2.1.0/CascadiaCode.zip). Extract and install as regular font.
9. Right click on top title of your PowerShell core app, and choose Properties. Under Font tab, change font to CaskaydiaCove NF. You should see nice font now.
10. Download the latest version of [delta](https://github.com/dandavison/delta/releases). Put it in any folder which is registered in PATH env variable. 

