#!/usr/bin/env bash
# Upgrade Limen to its newest npm release as one attributed, verified change.
#
# Limen's generated limen-verify.yml runs the version recorded in
# .echelon/limen.json, and this repository pins an exact version (the
# foundations contract rejects floating specs). This script moves both: it installs the new version
# exact-pinned, lets `limen upgrade` move the installation, updates the
# declared baseline, verifies the result, and records it as a `mechanical`
# ROS work item. It commits on the current branch and never pushes.
#
# Exits 0 without changes when Limen is already current.
# LIMEN_TARGET_VERSION overrides the npm lookup (manual dispatch, tests).
# Writes `changed`, `version` and `work_id` to $GITHUB_OUTPUT when set.
set -euo pipefail

readonly package="@echelon-foundry/limen"
readonly output="${GITHUB_OUTPUT:-/dev/null}"

now() { date -u +%Y-%m-%dT%H:%M:%SZ; }
emit() { printf '%s\n' "$@" >> "$output"; }
installed_version() { node -p 'require("./.echelon/limen.json").installedVersion'; }
newest_version() { npm view "$package" version; }

declare_baseline() {
  node -e '
    const fs = require("fs");
    const path = ".echelon/foundations.json";
    const current = JSON.parse(fs.readFileSync(path, "utf8"));
    const next = {
      ...current,
      capabilities: {
        ...current.capabilities,
        limen: { ...current.capabilities.limen, version: process.argv[1] },
      },
    };
    fs.writeFileSync(path, JSON.stringify(next, null, 2) + "\n");
  ' "$1"
}

verify() {
  npx --no-install limen verify --strict
  node --test tests/site/search-engine.test.mjs
  python3 scripts/knowledge.py build
  node tests/site/portal.browser.mjs
}

main() {
  local installed target work_id base
  installed="$(installed_version)"
  target="${LIMEN_TARGET_VERSION:-$(newest_version)}"

  if [[ "$installed" == "$target" ]]; then
    echo "Limen $installed is already the newest release."
    emit "changed=false" "version=$installed"
    return 0
  fi

  work_id="LIMEN-${target//./-}"
  base="$(git rev-parse HEAD)"
  echo "Upgrading Limen $installed -> $target as $work_id."

  ./ros work start --id "$work_id" --type mechanical --occurred-at "$(now)"

  npm install --save-exact "${package}@${target}"
  npx --no-install limen upgrade
  declare_baseline "$target"
  verify

  git add -A
  git commit -m "Upgrade Limen to ${target} (${work_id})" \
    -m "Installed ${installed} -> ${target} with 'limen upgrade'; .echelon/foundations.json declares ${target}. limen verify --strict, the search engine tests and the portal checks pass."

  ROS_BASE_REF="$base" ./ros work complete --id "$work_id" --occurred-at "$(now)" \
    --conclusion "Limen ${installed} -> ${target}; strict verification and portal checks pass."
  ROS_BASE_REF="$base" ./ros validate

  git add -A
  git commit -m "Record ${work_id} completion"

  emit "changed=true" "version=$target" "work_id=$work_id"
}

main "$@"
