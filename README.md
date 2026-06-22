# dotfiles manager

My personal shell experience

## Feature

1. Git shell
2. Git compare with delta by default
3. Pre-config for git compare: 
   1. [code compare](https://www.devart.com/codecompare/visual-studio-integration.html)
   2. Built-in Visual Studio code compare
   3. [Semantic merge](https://semanticmerge.com/documentation/how-to-configure/semanticmerge-configuration-guide)
4. Pre-configure nvim, keeping vim untouched
5. Pre-configure bash and zsh shell
6. Use CascaydiaCove from [nerdfonts](https://www.nerdfonts.com/font-downloads) as default font
7. Install necessary package for linux, especially for development stuff
8. Independent apps
   1. [.NET](https://dotnet.microsoft.com/)
   2. [Mise](https://mise.jdx.dev), as developer tool manager
   3. [ohmyposh](https://ohmyposh.dev)
9. Developer apps (through [mise](config/mise.toml))
   1. Language support: Rust, Go, Deno, Nodejs, Bun, Python 3.x
   2. .NET apps: aspire CLI, C# REPL, EF core CLI, Test reporting generator
   3. AI: opencode, copilot, gemini, claude
   4. Neovim
10. CLI apps (through [mise](config/mise.toml))
    1. lsd (alternative for `ls`)
    2. ripgrep (alternative for `grep`)
    3. yq, jq with yaml support
    4. delta, comparison tool for Git/Lazygit
    5. bat, alternative for `cat`
    6. fd, alternative for `find`
    7. fzf, fuzzy finder
    8. viddy, alternative for `watch`
    9. rgx-cli, regex tester toolkit
    10. darya, disk usage analysis
    11. csvlens, CSV viewer
    12. lazygit, git TUI
    13. Azure CLI
11. [Mac apps](mac_app.txt) (skip by exporting `SKIP_INSTALL_MAC_APPS` variable):
    1. REST toolkit: Bruno, Postman
    2. Ghostty for terminal
    3. IDE: Rider, dotMemory, dotTrace, Zed, VS code
    4. Orbstack for docker alternative
    5. Wakatime for productivity tracker
    6. Meeting/communication apps: MS Teams, Zoom, Discord
    7. Office: Adobe Acrobat, MS 365
    8. MS Edge
    9. AI apps: Claude GUI, Microsoft Copilot
    10. GPG suite
    11. VPN: Tunnelblick, OpenVPN
    12. OneDrive
12. Using [fnox](https://fnox.jdx.dev/) as secret manager

## Installation

> ### TLDR
> 
> Configure on fresh MacOS: `zsh shell/bootstrap.sh`<br />
> Configure on fresh linux: `bash shell/bootstrap.sh`

To skip install Mac GUI apps, export variable `SKIP_INSTALL_MAC_APPS` (regardless the value).

1. Download the latest version of [git](https://git-scm.com/download). Suggested installation option:
   - Use git from the command line and also from 3rd-party software: this is needed if we want additional feature, such as creating ssh key, signing commit, etc
   - Use bundled OpenSSH
   - Use the native windows secure channel library: so we can rely on existing certificate store
   - Checkout as is, commit as is
   - Use Git Credential Manager Core
   - Check for symbolic links
2. Clone this repo somewhere, f.e. ~/sources/shell
3. Try configure your name globally so the global git config will be created (`git config --global user.name "your name"`)
4. Run [`zsh shell/bootstrap.sh`](shell/bootstrap.sh) for zshell or [`bash shell/bootstrap.sh`](shell/bootstrap.sh) for bash.
5. Run PowerShell core with administrative privilege, replace profile with this command: `New-Item -ItemType SymbolicLink -Path $PROFILE -Value C:\path\to\shell\powershell\_profile.ps1 -Force`
6. Run PowerShell core with user privilege for the first time, it will install required module (ohmposh, posh-git)
7. Right click on top title of your PowerShell core app, and choose Properties. Under Font tab, change font to CaskaydiaCove NF. You should see nice font now.

## Usage

1. Set your env variable in [shell/variable.sh](shell/variables.sh) across posix shells (zsh, bash) and contain no secret
2. Set your env variable in `$HOME/.zshenv` (zsh) or `$HOME/.env` (bash) if you don't want to share to repo, especially when containing secret
3. Define your function in
   1. [shell/functions.sh](shell/functions.sh) across posix shells
   2. [zsh/functions.zsh](zsh/functions.zsh) for zsh only
   3. [bash/functions.sh](bash/functions.sh) for bash only
4. Define your script bootstrapper in
   1. [zsh/rc.zsh](zsh/rc.zsh) or [zsh/profile.zsh](zsh/profile.zsh) for zsh
   1. [bash/profile.sh](bash/profile.sh) which also loads [bash/rc.sh](bash/rc.sh) for bash
5. Define your shell alias in
   1. [shell/aliases.sh](shell/aliases.sh) across posix shells
   2. [zsh/aliases.zsh](zsh/aliases.zsh) for zsh only
   3. [bash/aliases.sh](bash/aliases.sh) for bash only
6. It will automatically create secret manager folder on `$HOME/.secret`. Push it on the same git repo user under name _secret_, such as git@github.com:my_user/secret.git. Then it should be easy for next machine setup.