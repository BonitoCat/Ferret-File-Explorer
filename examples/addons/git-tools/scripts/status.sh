#!/usr/bin/env bash

declare -A decorated
declare -A watched
declare -A rank=([ignored]=1 [staged]=2 [modified]=3 [untracked]=4 [conflict]=5)
declare -A color=([untracked]=#E5534B [modified]=#E0A23B [staged]=#57AB5A [conflict]=#E5534B [ignored]=#768390)
declare -A badge_color=([untracked]=#C93C37 [modified]=#B7791F [staged]=#347D39 [conflict]=#8E1519)
declare -A letter=([untracked]=U [modified]=M [staged]=A [conflict]=!)
declare -A tooltip=([untracked]=\$GitUntracked [modified]=\$GitModified [staged]=\$GitStaged [conflict]=\$GitConflict [ignored]=\$GitIgnored)

declare -A setting=(
    [colors]="${FE_SETTING_COLORS:-true}"
    [badges]="${FE_SETTING_BADGES:-true}"
    [badge_letters]="${FE_SETTING_BADGE_LETTERS:-true}"
    [badge_corner]="${FE_SETTING_BADGE_CORNER:-top-right}"
    [tint]="${FE_SETTING_TINT:-false}"
    [folders]="${FE_SETTING_FOLDERS:-true}"
    [untracked]="${FE_SETTING_UNTRACKED:-true}"
    [ignored]="${FE_SETTING_IGNORED:-true}"
)

shown=()
pending=false

classify() {
    case "$1" in
        "??") state=untracked ;;
        "!!") state=ignored ;;
        DD|AU|UD|UA|DU|AA|UU) state=conflict ;;
        ?" ") state=staged ;;
        *) state=modified ;;
    esac
}

style() {
    local state="$1"
    local parts="tooltip=${tooltip[$state]} | tag=$state"
    if [ "$state" = ignored ]; then
        [ "${setting[colors]}" = true ] && parts+=" | color=${color[$state]}"
        echo "$parts"
        return
    fi

    [ "${setting[colors]}" = true ] && parts+=" | color=${color[$state]}"
    [ "${setting[tint]}" = true ] && parts+=" | background=${color[$state]}"
    if [ "${setting[badges]}" = true ]; then
        parts+=" | badge-color=${badge_color[$state]} | badge-corner=${setting[badge_corner]}"
        [ "${setting[badge_letters]}" = true ] && parts+=" | badge-text=${letter[$state]}"
    fi
    echo "$parts"
}

remember() {
    local dir="$1" kept=("$1") entry
    for entry in "${shown[@]}"; do
        [ "$entry" != "$dir" ] && kept+=("$entry")
    done
    shown=("${kept[@]:0:4}")
}

refresh() {
    local dir="${1%/}"
    remember "$dir"
    if [ -n "${decorated[$dir]}" ]; then
        echo "fe:undecorate ${decorated[$dir]}"
        unset "decorated[$dir]"
    fi

    local root git_dir
    root=$(git -C "$dir" rev-parse --show-toplevel 2>/dev/null) || return
    git_dir=$(git -C "$dir" rev-parse --absolute-git-dir 2>/dev/null)
    if [ -n "$git_dir" ] && [ -z "${watched[$git_dir]}" ]; then
        watched[$git_dir]=1
        echo "fe:watch $git_dir"
    fi
    local prefix="${dir#"$root"}"
    prefix="${prefix#/}"
    [ -n "$prefix" ] && prefix="$prefix/"

    local ignored_mode=no
    [ "${setting[ignored]}" = true ] && ignored_mode=matching
    local untracked_mode=normal
    [ "${setting[untracked]}" = true ] || untracked_mode=no

    local -A states=()
    local entry code name state
    while IFS= read -r -d '' entry; do
        code="${entry:0:2}"
        name="${entry:3}"
        [[ "$code" == R* || "$code" == C* ]] && IFS= read -r -d '' _
        [[ "$name" == "$prefix"* ]] || continue
        name="${name#"$prefix"}"
        [ -z "$name" ] && continue
        if [[ "$name" == */?* ]]; then
            [ "${setting[folders]}" = true ] || continue
        fi
        name="${name%%/*}"
        classify "$code"
        if [ "${rank[$state]}" -gt "${rank[${states[$name]:-none}]:-0}" ]; then
            states[$name]="$state"
        fi
    done < <(git --no-optional-locks -C "$dir" status --porcelain=v1 -z --ignored=$ignored_mode --untracked-files=$untracked_mode -- . 2>/dev/null)

    local paths=()
    for name in "${!states[@]}"; do
        echo "fe:decorate $dir/$name | $(style "${states[$name]}")"
        paths+=("$dir/$name")
    done

    [ ${#paths[@]} -gt 0 ] && decorated[$dir]=$(IFS='|'; echo "${paths[*]}")
}

refresh_shown() {
    pending=false
    local dir
    for dir in "${shown[@]}"; do
        refresh "$dir"
    done
}

while true; do
    wait_for=()
    [ "$pending" = true ] && wait_for=(-t 0.3)
    if ! IFS= read -r "${wait_for[@]}" line; then
        [ $? -gt 128 ] || exit 0
        [ "$pending" = true ] && refresh_shown
        continue
    fi

    case "$line" in
        "event:refresh "*)
            refresh "${line#event:refresh }"
            ;;
        "event:watch "*)
            pending=true
            ;;
        "event:setting "*)
            entry="${line#event:setting }"
            setting[${entry%% *}]="${entry#* }"
            pending=true
            ;;
        "event:close")
            exit 0
            ;;
    esac
done
