function ToggleBackground()
  if &background == "dark"
    set background=light
  else
    set background=dark
  endif
endfunction

"custom function
"this one for reload vim configuration
command! Reload :so ~/.vimrc

"this one for toggle background
"useful when I work at noon/night
command! ToggleBg call ToggleBackground()

"using terminal mode in vim? this is for you!
"back to normal mode just by using double esc
tnoremap <ESC><ESC> <C-\><C-N>

nmap <C-s> :w<CR>
imap <C-s> <ESC>:w<CR>
