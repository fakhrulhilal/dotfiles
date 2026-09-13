# Share agent skills from config/ai/skills with AI tools: ~/.claude/skills (Claude Code) and
# ~/.agents/skills (agentskills.io layout). Repo-local .agents is git-ignored, so link it here too.
skills_dir="$DOT_HOME/config/ai/skills"

link_skills() {
  target="$1"
  if [ -L "$target" ]; then
    [ "$(realpath "$target")" = "$(realpath "$skills_dir")" ] && return
    rm "$target"
  elif [ -e "$target" ]; then
    mv "$target" "${target}.bak"
  fi
  mkdir -p "$(dirname "$target")"
  ln -s "$skills_dir" "$target"
}

link_skills "$HOME/.claude/skills"
link_skills "$HOME/.agents/skills"
link_skills "$DOT_HOME/.agents/skills"

unset -f link_skills
unset skills_dir