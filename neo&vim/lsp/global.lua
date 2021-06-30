require'snippets'.use_suggested_mappings()

local capabilities = vim.lsp.protocol.make_client_capabilities()
capabilities.textDocument.completion.completionItem.snippetSupport = true;
local lspconfig = require'lspconfig'
lspconfig.util.default_config = vim.tbl_extend(
	"force",
	lspconfig.util.default_config,
	{ log_level = lsp.protocol.MessageType.Warning.Error }
)