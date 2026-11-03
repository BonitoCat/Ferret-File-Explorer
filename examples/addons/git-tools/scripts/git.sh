#!/usr/bin/env bash

target="${1:-$FE_CURRENT_DIR}"

run_git() {
    local output
    if ! output=$(git -C "$FE_CURRENT_DIR" "$@" 2>&1); then
        echo "${output##*$'\n'}" >&2
        exit 1
    fi
    echo "fe:refresh"
}

case "$FE_ACTION_ID" in
    init)
        git -C "$target" init >/dev/null 2>&1 || { echo "git init failed" >&2; exit 1; }
        echo "fe:status Initialized git repository in $target"
        echo "fe:refresh"
        ;;
    add)
        run_git add -- "$@"
        ;;
    unstage)
        run_git restore --staged -- "$@"
        ;;
    discard)
        run_git restore -- "$@"
        ;;
esac
