alias watch=viddy
alias ll="lsd -lh"
alias tree="lsd --tree"
alias cls=clear
alias compare='delta --pager "less -RF"'
alias gitc='git -P diff | delta --pager "less -RF"'
alias gitcs='git -P diff --staged | delta --pager "less -RF"'
alias gitcl='git -P show $(git rev-parse HEAD) | delta'
if ! command -v vim &>/dev/null; then
  alias vim=nvim
  export EDITOR=nvim
fi
if ! command -v vi &>/dev/null; then
  alias vi=nvim
fi