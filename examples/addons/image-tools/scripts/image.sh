#!/usr/bin/env bash

t() {
    local file="$FE_ADDON_DIR/translations/$FE_LANGUAGE.json"
    [ -f "$file" ] || file="$FE_ADDON_DIR/translations/en.json"
    local text
    text=$(sed -n "s/^ *\"$1\": *\"\(.*\)\",\{0,1\}$/\1/p" "$file")
    printf '%s' "${text//\{0\}/$2}"
}

im() {
    if command -v magick >/dev/null; then
        magick "$@"
    else
        convert "$@"
    fi
}

unique_path() {
    local path="$1" base="${1%.*}" ext="${1##*.}" n=2
    while [ -e "$path" ]; do
        path="$base ($n).$ext"
        n=$((n + 1))
    done
    printf '%s' "$path"
}

choices() {
    local default="$1" result="" option
    shift
    for option in "$@"; do
        [ "$option" = "$default" ] && option="*$option"
        result="$result,$option"
    done
    printf '%s' "${result#,}"
}

files=("$@")
done_count=0
failed=0

finish() {
    echo "fe:progress done"
    echo "fe:refresh"
    if [ "$failed" -gt 0 ]; then
        echo "fe:status $(t Failed "$failed")"
        exit 1
    fi
    echo "fe:status $(t Done "$done_count")"
}

trap 'echo "fe:progress done"; echo "fe:refresh"; echo "fe:status $(t Cancelled "$done_count")"; exit 0' TERM

start_progress() {
    echo "fe:progress-title $(t "$1")"
    echo "fe:progress 0/${#files[@]}"
}

step() {
    echo "fe:progress $1/${#files[@]}"
    echo "fe:progress-item $(basename "$2")"
}

fail() {
    echo "$(basename "$1"): $2" >&2
    failed=$((failed + 1))
}

case "$FE_ACTION_ID" in
    convert)
        echo "fe:form $(t FormConvert "${#files[@]}")"
        echo "fe:field format | choice | $(t FieldFormat) | $(choices "$FE_SETTING_FORMAT" webp png jpg gif)"
        echo "fe:field quality | number | $(t FieldQuality) | $FE_SETTING_QUALITY"
        echo "fe:field keep | bool | $(t FieldKeep) | true"
        echo "fe:form-show"
        IFS= read -r reply || exit 0
        readarray -t -d $'\t' values < <(printf '%s' "$reply")
        format="${values[0]}" quality="${values[1]:-$FE_SETTING_QUALITY}" keep="${values[2]}"

        start_progress ProgressConvert
        for i in "${!files[@]}"; do
            file="${files[$i]}"
            step "$i" "$file"
            extension="${file##*.}"
            extension="${extension,,}"
            [ "$extension" = jpeg ] && extension=jpg
            in_place=false
            if [ "$extension" = "$format" ] && [ "$keep" = false ]; then
                in_place=true
                target="$(dirname "$file")/.fe-convert-$$-$(basename "$file")"
            else
                target=$(unique_path "${file%.*}.$format")
            fi

            options=()
            [[ "$format" =~ ^(jpg|webp)$ ]] && options=(-quality "$quality")
            if ! error=$(im "$file" -auto-orient "${options[@]}" "$format:$target" 2>&1); then
                rm -f -- "$target"
                fail "$file" "$error"
                continue
            fi

            if [ "$in_place" = true ]; then
                mv -f -- "$target" "$file"
            elif [ "$keep" = false ]; then
                rm -f -- "$file"
            fi
            done_count=$((done_count + 1))
        done
        finish
        ;;

    resize)
        echo "fe:form $(t FormResize "${#files[@]}")"
        echo "fe:field size | number | $(t FieldMaxSize) | $FE_SETTING_MAX_SIZE"
        echo "fe:field shrink | bool | $(t FieldOnlyShrink) | true"
        echo "fe:field keep | bool | $(t FieldKeep) | true"
        echo "fe:form-show"
        IFS= read -r reply || exit 0
        readarray -t -d $'\t' values < <(printf '%s' "$reply")
        size="${values[0]:-$FE_SETTING_MAX_SIZE}" shrink="${values[1]}" keep="${values[2]}"

        geometry="${size}x${size}"
        [ "$shrink" = true ] && geometry="$geometry>"
        start_progress ProgressResize
        for i in "${!files[@]}"; do
            file="${files[$i]}"
            step "$i" "$file"
            if [ "$keep" = true ]; then
                target=$(unique_path "${file%.*}-$size.${file##*.}")
            else
                target="$(dirname "$file")/.fe-resize-$$-$(basename "$file")"
            fi

            if ! error=$(im "$file" -auto-orient -resize "$geometry" "$target" 2>&1); then
                rm -f -- "$target"
                fail "$file" "$error"
                continue
            fi

            [ "$keep" = true ] || mv -f -- "$target" "$file"
            done_count=$((done_count + 1))
        done
        finish
        ;;

    strip)
        start_progress ProgressStrip
        for i in "${!files[@]}"; do
            file="${files[$i]}"
            step "$i" "$file"
            temp="$(dirname "$file")/.fe-strip-$$-$(basename "$file")"
            if ! error=$(im "$file" -auto-orient -strip "$temp" 2>&1); then
                rm -f -- "$temp"
                fail "$file" "$error"
                continue
            fi

            mv -f -- "$temp" "$file"
            done_count=$((done_count + 1))
        done
        finish
        ;;

    info)
        file="${files[0]}"
        if command -v magick >/dev/null; then
            info=$(magick identify -format '%wx%h px\n%m · %[colorspace] · %z-bit' "$file[0]" 2>&1)
        else
            info=$(identify -format '%wx%h px\n%m · %[colorspace] · %z-bit' "$file[0]" 2>&1)
        fi || { echo "$info" >&2; exit 1; }
        size=$(numfmt --to=iec --suffix=B "$(stat -c %s "$file")")
        echo "fe:message $(basename "$file")\n\n${info//$'\n'/\\n}\n$size"
        ;;
esac
