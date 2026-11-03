#!/usr/bin/env bash

files=("$@")
started=$SECONDS
done_pages=0
total_pages=0

trap 'echo "fe:status \$Cancelled"; exit 0' TERM

page_count() {
    local escaped="${1//\\/\\\\}" count
    escaped="${escaped//(/\\(}"
    escaped="${escaped//)/\\)}"
    count=$(gs -q -dNODISPLAY -dSAFER --permit-file-read="$1" -c "($escaped) (r) file runpdfbegin pdfpagecount = quit" 2>/dev/null | tail -n 1)
    [[ "$count" =~ ^[0-9]+$ ]] && [ "$count" -gt 0 ] || return 1
    printf '%s' "$count"
}

gs_path() {
    printf '%s' "${1//%/%%}"
}

run_gs() {
    local line
    : > "$FE_TEMP_DIR/gs.log"
    { gs -dSAFER -dBATCH -dNOPAUSE -sDEVICE=pdfwrite "$@" 2>&1; echo "fe-exit $?"; } | while IFS= read -r line; do
        case "$line" in
            "fe-exit "*)
                exit "${line#fe-exit }"
                ;;
            "Page "*)
                done_pages=$((done_pages + 1))
                echo "fe:progress $done_pages/$total_pages"
                ;;
            *"****"*)
                echo "fe:log warning $line"
                printf '%s\n' "$line" >> "$FE_TEMP_DIR/gs.log"
                ;;
            *)
                printf '%s\n' "$line" >> "$FE_TEMP_DIR/gs.log"
                ;;
        esac
    done
}

gs_failed() {
    cat "$FE_TEMP_DIR/gs.log" >&2
    echo "\$Failed $(basename "$1")" >&2
    exit 1
}

unreadable() {
    echo "\$Unreadable $(basename "$1")" >&2
    exit 1
}

read_form() {
    echo "fe:form-show"
    IFS= read -r reply || return 1
    readarray -t -d $'\t' values < <(printf '%s' "$reply")
}

file_name() {
    local name="${1//\//-}"
    name="${name%.[pP][dD][fF]}"
    name="${name#"${name%%[![:space:]]*}"}"
    name="${name%"${name##*[![:space:]]}"}"
    printf '%s.pdf' "${name:-$2}"
}

free_name() {
    local folder="$1" name="$2" base extension="" n=2
    base="$name"
    if [[ "$name" == *.pdf ]]; then
        base="${name%.pdf}"
        extension=".pdf"
    fi

    while [ -e "$folder/$name" ]; do
        name="$base ($n)$extension"
        n=$((n + 1))
    done
    printf '%s' "$name"
}

deliver() {
    local folder="$1" list="" path free reply
    local -a paths=()
    delivered=()
    shift
    for path in "$@"; do
        free=$(free_name "$folder" "$(basename "$path")")
        if [ "$free" != "$(basename "$path")" ]; then
            mv -- "$path" "$(dirname "$path")/$free"
            path="$(dirname "$path")/$free"
        fi
        paths+=("$path")
        delivered+=("$free")
        list="$list | $path"
    done
    set -- "${paths[@]}"

    echo "fe:progress done"
    echo "fe:move-to $folder$list"
    IFS= read -r reply
    [ "$reply" = yes ] || exit 0

    list=""
    for path in "$@"; do
        list="$list | $folder/$(basename "$path")"
    done
    echo "fe:reveal ${list# | }"
}

finish() {
    if [ $((SECONDS - started)) -ge 10 ]; then
        echo "fe:notify $1"
        return
    fi

    echo "fe:status $1"
}

wait_until_gone() {
    local attempt path
    for attempt in {1..50}; do
        for path in "$@"; do
            [ -e "$path" ] && sleep 0.1 && continue 2
        done
        return 0
    done
    return 1
}

merge() {
    local folder_name name output count
    if [ "${#files[@]}" -lt 2 ]; then
        echo 'fe:message $NeedTwo'
        exit 0
    fi

    folder_name=$(basename "$FE_CURRENT_DIR")
    echo 'fe:form $FormMerge'
    echo "fe:field name | text | \$FieldName | $folder_name"
    echo 'fe:field order | choice | $FieldOrder | *A → Z,Z → A'
    read_form || exit 0
    name=$(file_name "${values[0]}" "$folder_name")

    sort_flags=(-zV)
    [ "${values[1]}" = "Z → A" ] && sort_flags+=(-r)
    readarray -t -d '' files < <(printf '%s\0' "${files[@]}" | sort "${sort_flags[@]}")

    for file in "${files[@]}"; do
        count=$(page_count "$file") || unreadable "$file"
        total_pages=$((total_pages + count))
    done

    echo 'fe:progress-title $ProgressMerge'
    echo "fe:progress-item $name"
    output="$FE_TEMP_DIR/$name"
    run_gs -o "$(gs_path "$output")" "${files[@]}" || gs_failed "${files[0]}"

    deliver "$FE_CURRENT_DIR" "$output"
    finish "\$Merged ${delivered[0]}"
}

compress() {
    local resolution preset trash folder file base count output size_before=0 size_after=0 before after
    local -a counts=() sources=() outputs=() skipped=()
    folder=$(dirname "${files[0]}")
    echo 'fe:form $FormCompress'
    echo "fe:field resolution | choice | \$FieldResolution | $(resolution_choices)"
    echo 'fe:field trash | bool | $FieldTrash | false'
    read_form || exit 0
    resolution="${values[0]%% *}"
    trash="${values[1]}"
    case "$resolution" in
        72) preset=/screen ;;
        300) preset=/printer ;;
        *) resolution=150 preset=/ebook ;;
    esac

    for file in "${files[@]}"; do
        count=$(page_count "$file") || count=0
        counts+=("$count")
        total_pages=$((total_pages + count))
    done

    echo 'fe:progress-title $ProgressCompress'
    echo "fe:progress 0/$total_pages"
    for i in "${!files[@]}"; do
        file="${files[$i]}"
        if [ "${counts[$i]}" -eq 0 ]; then
            echo "fe:log warning \$Unreadable $(basename "$file")"
            skipped+=("$(basename "$file")")
            continue
        fi

        base=$(basename "${file%.*}")
        output="$FE_TEMP_DIR/$base ($resolution dpi).pdf"
        echo "fe:progress-item $(basename "$file")"
        run_gs -dPDFSETTINGS="$preset" -dCompatibilityLevel=1.5 -o "$(gs_path "$output")" "$file" || gs_failed "$file"
        done_pages=$((done_pages + counts[i]))

        before=$(stat -c %s "$file")
        after=$(stat -c %s "$output")
        if [ "$after" -ge $((before * 95 / 100)) ]; then
            echo "fe:log info \$NotSmaller $(basename "$file")"
            skipped+=("$(basename "$file")")
            rm -f -- "$output"
            continue
        fi

        size_before=$((size_before + before))
        size_after=$((size_after + after))
        sources+=("$file")
        outputs+=("$output")
    done

    if [ "${#outputs[@]}" -eq 0 ]; then
        echo "fe:progress done"
        finish "\$Compressed 0 · \$Skipped ${#skipped[@]}"
        exit 0
    fi

    if [ "$trash" = true ]; then
        trash_originals
    fi

    deliver "$folder" "${outputs[@]}"
    summary="\$Compressed $(numfmt --to=iec "$size_before")B → $(numfmt --to=iec "$size_after")B (−$(((size_before - size_after) * 100 / size_before)) %)"
    [ "${#skipped[@]}" -gt 0 ] && summary="$summary · \$Skipped ${#skipped[@]}"
    finish "$summary"
}

trash_originals() {
    local list="" i renamed
    for i in "${!sources[@]}"; do
        list="$list | ${sources[$i]}"
    done
    echo "fe:trash ${list# | }"
    wait_until_gone "${sources[@]}"

    for i in "${!sources[@]}"; do
        if [ -e "${sources[$i]}" ]; then
            echo "fe:log warning \$TrashFailed $(basename "${sources[$i]}")"
            continue
        fi

        renamed="$FE_TEMP_DIR/$(basename "${sources[$i]}")"
        mv -- "${outputs[$i]}" "$renamed"
        outputs[i]="$renamed"
    done
}

resolution_choices() {
    local result="" option
    for option in "72 dpi" "150 dpi" "300 dpi"; do
        [ "${option%% *}" = "${FE_SETTING_RESOLUTION:-150}" ] && option="*$option"
        result="$result,$option"
    done
    printf '%s' "${result#,}"
}

parse_pages() {
    local list="${1// /}" part first last
    list="${list//;/,}"
    selected=0
    [[ "$list" =~ ^[0-9]+(-[0-9]*)?(,[0-9]+(-[0-9]*)?)*$ ]] || return 1
    IFS=, read -ra parts <<< "$list"
    for part in "${parts[@]}"; do
        first="${part%%-*}"
        last="$first"
        if [[ "$part" == *-* ]]; then
            last="${part#*-}"
            last="${last:-$count}"
        fi
        [ "$first" -ge 1 ] && [ "$first" -le "$last" ] && [ "$last" -le "$count" ] || return 1
        selected=$((selected + last - first + 1))
    done
    pages="$list"
}

extract() {
    local file="${files[0]}" count base input output title
    count=$(page_count "$file") || unreadable "$file"
    base=$(basename "${file%.*}")
    input="1"
    title="\$FormExtract $(basename "$file") · $count \$Pages"
    while true; do
        echo "fe:form $title"
        echo "fe:field pages | text | \$FieldPages | $input"
        read_form || exit 0
        input="${values[0]}"
        parse_pages "$input" && break
        title="\$InvalidPages 1–$count"
    done

    total_pages=$selected
    output="$FE_TEMP_DIR/$(file_name "$base ($pages)")"
    echo 'fe:progress-title $ProgressExtract'
    echo "fe:progress-item $(basename "$file")"
    run_gs -sPageList="$pages" -o "$(gs_path "$output")" "$file" || gs_failed "$file"

    deliver "$(dirname "$file")" "$output"
    finish "\$Created ${delivered[0]}"
}

split() {
    local file="${files[0]}" base folder
    total_pages=$(page_count "$file") || unreadable "$file"
    base=$(basename "${file%.*}")
    folder="$FE_TEMP_DIR/$base"
    mkdir -p -- "$folder"

    echo 'fe:progress-title $ProgressSplit'
    echo "fe:progress-item $(basename "$file")"
    run_gs -o "$(gs_path "$folder/$base") - %0${#total_pages}d.pdf" "$file" || gs_failed "$file"

    deliver "$(dirname "$file")" "$folder"
    finish "\$Created ${delivered[0]}/ · $total_pages \$Pages"
}

case "$FE_ACTION_ID" in
    merge) merge ;;
    compress) compress ;;
    extract) extract ;;
    split) split ;;
esac
