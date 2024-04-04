ZSH_WEB_SEARCH_ENGINES=(
    toped "https://www.tokopedia.com/search?st=&srp_component_id=02.01.00.00&srp_page_id=&srp_page_title=&navsource=&q="
)

COLOR_NONE='\e[0m'
TEXT_BOLD='\e[1m'
TEXT_DARK='\e[2m'
TEXT_ITALIC='\e[3m'
TEXT_UNDERLINED='\e[4m'
TEXT_BLINK='\e[5m'
TEXT_REVERSE='\e[7m'
function {
     _set='\e'
    local _fgDark=30
    local _fgBright=90
    local _bgDark=40
    local _bgBright=100
    for color in {BLACK=0,RED=1,GREEN=2,YELLOW=3,BLUE=4,MAGENTA=5,CYAN=6,WHITE=7}
    do
        local _color="${color%=*}"
        local _code="${color#*=}"
        local _fgDarkCode=_code+_fgDark
        local _fgBrightCode=_code+_fgBright
        local _bgDarkCode=_code+_bgDark
        local _bgBrightCode=_code+_bgBright
        "COLOR_FG_(${(P)_color})=${_set}[${_fgDarkCode}m"
        "COLOR_FG_${color%=*}=${_set}[${_fgDarkCode}m"
        declare "COLOR_FG_${color%=*}_LIGHT=${_set}[${_fgBrightCode}m"
        declare "COLOR_BG_${color%=*}=${_set}[${_bgDarkCode}m"
        declare "COLOR_BG_${color%=*}_LIGHT=${_set}[${_bgBrightCode}m"
    done
}
