alias ll="lsd -lh"
alias tree="lsd --tree"
alias cls=clear
#alias omp="eval $(oh-my-posh init zsh --config ~/proyek/dotfiles/config/posh-theme.omp.json)"
alias compare=delta --pager "less -RF"
alias gitc='git -P diff | delta --pager "less -RF"'
alias gitcs='git -P diff --staged | delta --pager "less -RF"'
