call plug#begin('~/.vim_plugins')

    Plug 'junegunn/fzf', { 'do': { -> fzf#install() } }
    Plug 'junegunn/fzf.vim'
	Plug 'tpope/vim-fugitive'
	Plug 'puremourning/vimspector'
	Plug 'vim-airline/vim-airline'
	Plug 'vim-airline/vim-airline-themes'
	Plug 'ryanoasis/vim-devicons'
	Plug 'prabirshrestha/asyncomplete.vim'

	if has('nvim')
        Plug 'onsails/lspkind-nvim'
		Plug 'neovim/nvim-lspconfig'
		Plug 'nvim-lua/completion-nvim'
        Plug 'hrsh7th/nvim-compe'
	endif
	
	Plug 'OmniSharp/omnisharp-vim', { 'do': { -> OmniSharp#Install }}
    Plug 'rust-lang/rust.vim'
    Plug 'cespare/vim-toml'
    Plug 'pprovost/vim-ps1'
    
    Plug 'mhinz/vim-signify'
    Plug 'Quramy/vison'

    Plug 'rakr/vim-one'

call plug#end()

" Plugin: ALE --------------------------------------------------
let g:ale_linters = { 'cs': ['OmniSharp'] }
let g:ale_fixer = {
    \ '*': ['remove_trailing_lines', 'trim_whitespaces'],
    \ 'rust': ['rustfmt'],
\}
let g:ale_completion_autoimport = 1
let g:ale_echo_msg_format  = '[%linter% - %severity%] %code: %%s'
let g:ale_completion_enabled = 1
let g:ale_sign_column_always = 1
let g:ale_sign_warning = ''
let g:ale_sign_error = '✗'

" Plugin: Vimspector ------------------------------------------------
let g:vimspector_enable_mappings = 'VISUAL_STUDIO'
"packadd! vimspector

" Plugin: FZF -------------------------------------------------------
nmap <Leader><Tab> <Plug>(fzf-maps-n)
map <C-g> :GFiles<CR>
map <C-f> :Files<CR>

" Plugin: Airline ---------------------------------------------------
let g:airline_powerline_fonts=1
let g:airline_theme='dark'
let g:airline#extensions#tabline#enabled = 1
if has('nvim')
	let g:airline#extensions#tabline#tabs_label = 'NeoVIM'
else
	let g:airline#extensions#tabline#tabs_label = 'VIM'
endif
let g:airline#extensions#tabline#formatter = 'unique_tail'
let g:airline#extensions#tabline#show_splits = 0
let g:airline#extensions#whitespace#enabled = 0
let g:airline#extensions#tabline#show_buffers = 0
let g:airline#extensions#tabline#show_close_button = 0
let g:airline#extensions#ale#enabled = 1
set noshowmode

" Plugin: PS1 -------------------------------------------------------
let g:ps1_nofold_blocks = 1

let g:python3_host_prog='D:\Aplikasi\python\python.exe'

" Plugin: Signify ---------------------------------------------------
let g:signify_vcs_list = ['git']
let g:signify_sign_add = ''
let g:signify_sign_delete = ''
let g:signify_sign_change = ''
let g:signify_sign_change_delete = ''
let g:signify_sign_delete_first_line = '裸'
let g:signify_line_highlight = 0
nmap <Leader>sh :SignifyToggleHighlight<CR>
nmap <Leader>st :SignifyToggle<CR>
nmap <Leader>sd :SignifyHunkDiff<CR>

if has('nvim')
	runtime plugins.neo.vim
endif
