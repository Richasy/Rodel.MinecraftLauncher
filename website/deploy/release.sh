#!/usr/bin/env bash
set -euo pipefail

action=$1
root=$2
release=$3
[[ "$root" =~ ^/home/www/[a-zA-Z0-9_-]+$ ]]
[[ "$release" =~ ^[0-9]+-[0-9]+-[a-f0-9]{12}$ ]]
incoming="$root/incoming/$release"
target="$root/releases/$release"

switch_current() {
    ln -sT "$1" "$root/current-$release.tmp"
    mv -Tf "$root/current-$release.tmp" "$root/current"
}

case "$action" in
    publish)
        digest=$4
        [[ "$digest" =~ ^[a-f0-9]{64}$ ]]
        [[ ! -e "$target" ]]
        previous=$(readlink "$root/current")
        [[ "$previous" =~ ^releases/[a-zA-Z0-9_-]+$ ]]
        printf '%s  %s\n' "$digest" "$incoming/site.tar.gz" | sha256sum --check --status
        mkdir "$target"
        tar -xzf "$incoming/site.tar.gz" --no-same-owner --no-same-permissions -C "$target"
        [[ -f "$target/index.html" && -f "$target/deployment.json" ]]
        [[ -z "$(find "$target" -type l -print -quit)" ]]
        find "$target" -type d -exec chmod 755 {} +
        find "$target" -type f -exec chmod 644 {} +
        switch_current "releases/$release"
        printf '%s\n' "$previous"
        ;;
    rollback)
        previous=$4
        [[ "$previous" =~ ^releases/[a-zA-Z0-9_-]+$ ]]
        [[ -d "$root/$previous" ]]
        [[ "$(readlink "$root/current")" == "releases/$release" ]]
        switch_current "$previous"
        printf 'Rolled back to %s\n' "$previous"
        ;;
    *) exit 2 ;;
esac
