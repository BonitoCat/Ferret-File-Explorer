#!/usr/bin/env bash

target="${1:-$FE_CURRENT_DIR}"

case "$FE_ACTION_ID" in
    status)
        output=$(git -C "$FE_CURRENT_DIR" status --short --branch 2>&1)
        echo "fe:message ${output//$'\n'/\\n}"
        ;;
    init)
        git -C "$target" init >/dev/null 2>&1 || { echo "git init failed" >&2; exit 1; }
        echo "fe:status Initialized git repository in $target"
        echo "fe:refresh"
        ;;
esac
