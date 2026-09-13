function PLUGIN:BackendInstall(ctx)
    local cmd = require("cmd")
    local file = require("file")

    -- plugin dir is a symlink into the repo; its real parent is the csharp folder
    local source_dir = cmd.exec('cd "$(dirname "$(realpath "$PLUGIN_DIR")")" && pwd', {
        env = { PLUGIN_DIR = RUNTIME.pluginDirPath },
    }):gsub("%s+$", "")

    local script = ctx.tool:gsub("%.cs$", "") .. ".cs"
    if not file.exists(file.join_path(source_dir, script)) then
        error("C# script not found: " .. file.join_path(source_dir, script))
    end

    cmd.exec('dotnet publish -c Release -o "$OUT_DIR" "$SCRIPT"', {
        cwd = source_dir,
        env = { OUT_DIR = file.join_path(ctx.install_path, "bin"), SCRIPT = script },
    })
    return {}
end
