#!/usr/bin/env bash

files=("$@")

t() {
    local file="$FE_ADDON_DIR/translations/${FE_LANGUAGE%%-*}.json"
    [ -f "$file" ] || file="$FE_ADDON_DIR/translations/en.json"
    sed -n "s/^ *\"$1\": *\"\(.*\)\",\{0,1\}$/\1/p" "$file"
}

free_path() {
    local path="$1" base="${1%.*}" extension="${1##*.}" n=2
    while [ -e "$path" ]; do
        path="$base ($n).$extension"
        n=$((n + 1))
    done
    printf '%s' "$path"
}

one_line() {
    local text
    text=$(cat)
    printf '%s' "${text//$'\n'/\\n}"
}

new_markdown() {
    local name template path
    echo 'fe:form $FormNew'
    echo "fe:field name | text | \$FieldName | $(t DefaultName)"
    echo "fe:field template | choice | \$FieldTemplate | *$(t TemplateEmpty),$(t TemplateNotes),$(t TemplateReadme)"
    echo "fe:form-show"
    IFS= read -r reply || exit 0
    readarray -t -d $'\t' values < <(printf '%s' "$reply")
    name="${values[0]//\//-}"
    name="${name%.md}"
    name="${name:-$(t DefaultName)}"
    template="${values[1]}"

    path=$(free_path "$FE_CURRENT_DIR/$name.md")
    case "$template" in
        "$(t TemplateNotes)")
            printf '# %s\n\n_%s_\n\n- \n' "$name" "$(date +%F)" > "$path"
            ;;
        "$(t TemplateReadme)")
            printf '# %s\n\n%s\n\n## Installation\n\n## Usage\n\n## License\n' "$name" "$(t ReadmeIntro)" > "$path"
            ;;
        *)
            printf '# %s\n\n' "$name" > "$path"
            ;;
    esac

    echo "fe:reveal $path"
    echo "fe:status \$Created $(basename "$path")"
}

export_files() {
    local extension="$1" path output list="" count=0
    echo 'fe:progress-title $ProgressExport'
    for i in "${!files[@]}"; do
        path="${files[$i]}"
        echo "fe:progress $i/${#files[@]}"
        echo "fe:progress-item $(basename "$path")"
        output=$(free_path "${path%.*}.$extension")
        if ! error=$(pandoc --standalone --metadata "title=$(basename "${path%.*}")" -o "$output" -- "$path" 2>&1); then
            printf '%s\n' "$error" >&2
            echo "\$Failed $(basename "$path")" >&2
            exit 1
        fi

        list="$list | $output"
        count=$((count + 1))
    done

    echo "fe:progress done"
    echo "fe:reveal ${list# | }"
    echo "fe:status \$Exported $count"
}

copy_html() {
    local html
    if ! html=$(pandoc -t html -- "${files[0]}" 2>&1); then
        printf '%s\n' "$html" >&2
        echo "\$Failed $(basename "${files[0]}")" >&2
        exit 1
    fi

    echo "fe:copy $(printf '%s' "$html" | one_line)"
    echo 'fe:status $CopiedHtml'
}

outline() {
    awk '
        /^(```|~~~)/ { fenced = !fenced; next }
        fenced { next }
        /^#{1,6} / {
            level = index($0, " ") - 1
            text = substr($0, level + 2)
            printf "%" (level - 1) * 2 "s- %s\n", "", text
        }
    ' "$1"
}

copy_outline() {
    local text
    text=$(outline "${files[0]}")
    if [ -z "$text" ]; then
        echo 'fe:status $NoHeadings'
        exit 0
    fi

    echo "fe:copy $(printf '%s' "$text" | one_line)"
    echo 'fe:status $CopiedOutline'
}

stats() {
    local path="${files[0]}" words lines headings links minutes
    words=$(wc -w < "$path")
    lines=$(wc -l < "$path")
    headings=$(outline "$path" | wc -l)
    links=$(grep -o '\](' "$path" | wc -l)
    minutes=$(( (words + 199) / 200 ))
    echo "fe:message $(basename "$path")\n\n\$Words $words\n\$Lines $lines\n\$Headings $headings\n\$Links $links\n\n\$ReadingTime $minutes \$Minutes"
}

case "$FE_ACTION_ID" in
    new-markdown) new_markdown ;;
    export-html) export_files html ;;
    export-docx) export_files docx ;;
    export-odt) export_files odt ;;
    copy-html) copy_html ;;
    copy-outline) copy_outline ;;
    stats) stats ;;
esac
