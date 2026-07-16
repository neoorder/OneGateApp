#!/bin/sh

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd) || exit 1

is_compatible_node() {
  "$1" -e "const major=Number(process.versions.node.split('.')[0]);process.exit(major>=22&&typeof WebSocket==='function'?0:1)" >/dev/null 2>&1
}

node_executable=""
if [ -n "${ONEGATE_NODE:-}" ]; then
  if is_compatible_node "$ONEGATE_NODE"; then
    node_executable=$ONEGATE_NODE
  else
    printf '%s\n' "OneGate requires Node.js 22 or newer with built-in WebSocket support." >&2
    printf '%s\n' "ONEGATE_NODE does not point to a compatible executable: $ONEGATE_NODE" >&2
    exit 127
  fi
elif command -v node >/dev/null 2>&1 && is_compatible_node "$(command -v node)"; then
  node_executable=$(command -v node)
else
  for candidate in "$HOME"/.cache/codex-runtimes/*/dependencies/node/bin/node; do
    if [ -x "$candidate" ] && is_compatible_node "$candidate"; then
      node_executable=$candidate
      break
    fi
  done
fi

if [ -z "$node_executable" ]; then
  printf '%s\n' "OneGate requires Node.js 22 or newer with built-in WebSocket support." >&2
  printf '%s\n' "Install a current Node.js release or set ONEGATE_NODE to its executable path." >&2
  exit 127
fi

exec "$node_executable" "$script_dir/onegate.mjs" "$@"
