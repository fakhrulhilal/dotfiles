# Share agent skills from config/ai/skills with AI tools: ~/.claude/skills (Claude Code) and
# ~/.agents/skills (agentskills.io layout). Repo-local .agents is git-ignored, so link it here too.
skills_dir="$DOT_HOME/config/ai/skills"
agents_dir="$DOT_HOME/config/ai/agents"

link_folder() {
  source="$1"
  target="$2"
  if [ -L "$target" ]; then
    [ "$(realpath "$target")" = "$(realpath "$source")" ] && return
    rm "$target"
  elif [ -e "$target" ]; then
    mv "$target" "${target}.bak"
  fi
  mkdir -p "$(dirname "$target")"
  ln -s "$source" "$target"
}

link_folder "$skills_dir" "$HOME/.claude/skills"
link_folder "$skills_dir" "$HOME/.agents/skills"
link_folder "$skills_dir" "$DOT_HOME/.agents/skills"
link_folder "$agents_dir" "$HOME/.claude/agents"
link_folder "$agents_dir" "$HOME/.agents/agents"
link_folder "$agents_dir" "$DOT_HOME/.agents/agents"

unset -f link_folder
unset skills_dir agents_dir
