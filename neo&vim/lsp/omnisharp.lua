local lspconfig = require('lspconfig')
local pid = vim.fn.getpid()
local omnisharp_bin = '~/AppData/Local/omnisharp-vim/omnisharp-roslyn/OmniSharp.exe'
lspconfig.omnisharp.setup({ cmd = { omnisharp_bin, "--languageserver" , "--hostPID", tostring(pid) } })
