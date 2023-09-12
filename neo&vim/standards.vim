" standard: file settings ----------------------------------------------------
set nocompatible

syntax on
filetype plugin on
filetype indent on

" standard: training wheels for new vim users. Use the system clipboard on all
" registers. Take these weels of when you get more confident in Vim.
set clipboard=unnamed

" utf-8/unicode support
" requires Vim to be compiled with Multibyte support, you can check that by
" running `vim --version` and checking for +multi_byte.
if has('multi_byte')
  scriptencoding utf-8
  set encoding=utf-8
end

" highlight spell errors
hi SpellErrors guibg=red guifg=black ctermbg=red ctermfg=black

" behavior
                        " ignore these files when completing names and in
                        " explorer
set wildignore=.svn,CVS,.git,.hg,*.o,*.a,*.class,*.mo,*.la,*.so,*.obj,*.swp,*.jpg,*.png,*.xpm,*.gif
set autowriteall        " Automatically save before commands like :next and :make
set hidden              " enable multiple modified buffers
set history=10000
set autoread            " automatically read file that has been changed on disk and doesn't have changes in vim
set backspace=indent,eol,start
set guioptions-=T       " disable toolbar"
let bash_is_sh=1        " syntax shell files as bash scripts
set cinoptions=:0,(s,u0,U1,g0,t0 " some indentation options ':h cinoptions' for details
set modelines=5         " number of lines to check for vim: directives at the start/end of file
"set fixdel                 " fix terminal code for delete (if delete is broken but backspace works)
set autoindent          " automatically indent new line
set et                  " expand tabs into spaces
set ts=4                " number of spaces in a tab
set sw=4                " number of spaces for indent
set ttimeoutlen=50      " fast Esc to normal mode
set ruler               " line and column number of the cursor position
set completeopt=menuone,noinsert,noselect
set list
set laststatus=2        " always show the status line
set wildmenu            " enhanced command completion
set showmatch           " Show matching brackets.
set nowrap              " Do not wrap words (view)
set textwidth=0         " Do not wrap words (insert)
set numberwidth=3       " number of culumns for line numbers

" mouse settings
if has("mouse")
  set mouse=a
endif
set mousehide                           " Hide mouse pointer on insert mode."


"
" omni completion settings
set ofu=syntaxcomplete#Complete

" directory settings
call system('mkdir -vp ~/.backup/undo/ > /dev/null 2>&1')
set backupdir=~/.backup,.       " list of directories for the backup file
set directory=~/.backup,~/tmp,. " list of directory names for the swap file
set nobackup            " do not write backup files
set backupskip+=~/tmp/*,/private/tmp/* " skip backups on OSX temp dir, for crontab -e to properly work
set noswapfile          " do not write .swp files
set undofile
set undodir=~/.backup/undo/,~/tmp,.

" folding
set foldcolumn=1        " columns for folding
set foldmethod=syntax
set foldlevel=9
set nofoldenable        " dont fold by default "

