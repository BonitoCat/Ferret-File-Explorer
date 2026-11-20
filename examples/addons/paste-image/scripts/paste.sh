#!/usr/bin/env bash

source=""
default_format="png"
default_name="pasted-$(date +%Y-%m-%d-%H%M%S)"

if [[ " $FE_CLIPBOARD " == *" files "* ]]; then
    echo "fe:clipboard-files"
    IFS= read -r reply
    readarray -t -d $'\t' parts < <(printf '%s' "$reply")
    if [ "${parts[0]}" != copy ] || [ "${#parts[@]}" -ne 2 ]; then
        echo "fe:pass"
        exit 0
    fi

    source="${parts[1]}"
    base="$(basename "$source")"
    default_name="${base%.*}"
    default_format="$(printf '%s' "${base##*.}" | tr '[:upper:]' '[:lower:]')"
    [ "$default_format" = jpeg ] && default_format=jpg
fi

formats=""
for format in png jpg webp gif bmp tiff; do
    [ "$format" = "$default_format" ] && formats+="*"
    formats+="$format,"
done

echo "fe:form Paste the image as"
echo "fe:field name | text | File name | $default_name.$default_format"
echo "fe:field format | choice | Format | ${formats%,}"
echo "fe:field-select name | name"
echo "fe:form-show live"

name=""
while IFS= read -r line; do
    readarray -t -d $'\t' values < <(printf '%s' "$line")
    case "${values[0]}" in
        change)
            if [ "${values[1]}" = format ]; then
                echo "fe:field-set name | ${values[2]%.*}.${values[3]}"
            fi
            ;;
        submit)
            name="${values[1]}"
            format="${values[2]}"
            break
            ;;
    esac
done

[ -n "$name" ] || exit 0
[[ "$name" == *.* ]] || name="$name.$format"

if [ -z "$source" ]; then
    source="$FE_TEMP_DIR/clipboard.png"
    echo "fe:clipboard-image $source"
    IFS= read -r saved
    if [ "$saved" != yes ]; then
        echo "fe:status The clipboard no longer contains an image"
        exit 0
    fi
fi

normalize() {
    local extension
    extension="$(printf '%s' "${1##*.}" | tr '[:upper:]' '[:lower:]')"
    case "$extension" in
        jpeg) echo jpg ;;
        tif) echo tiff ;;
        *) echo "$extension" ;;
    esac
}

if [ "$(normalize "$source")" = "$(normalize "$name")" ]; then
    cp -- "$source" "$FE_TEMP_DIR/$name" || exit 1
else
    convert "$source" "$FE_TEMP_DIR/$name" || exit 1
fi

echo "fe:move-to $FE_CURRENT_DIR | $FE_TEMP_DIR/$name"
IFS= read -r moved
[ "$moved" = yes ] && echo "fe:reveal $FE_CURRENT_DIR/$name"
