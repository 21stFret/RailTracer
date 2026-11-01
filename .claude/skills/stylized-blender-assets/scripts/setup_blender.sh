#!/usr/bin/env bash
# Installs Blender-as-a-Python-module (bpy) into a private Python 3.11 venv so scripts can be run and
# rendered in REAL Blender headlessly. Idempotent; prints the python path to use as $BPY.
# Needs network access to pypi.org / files.pythonhosted.org / github.com (~400 MB download, ~1-2 min).
set -e
VENV="${KIT_BPY_VENV:-$HOME/.kit_bpy}"
if [ -x "$VENV/bin/python" ] && "$VENV/bin/python" -c "import bpy" 2>/dev/null; then
  echo "$VENV/bin/python"; exit 0
fi
command -v uv >/dev/null 2>&1 || pip install uv --break-system-packages --quiet >/dev/null 2>&1 || pip install uv --quiet >/dev/null
uv python install 3.11 >/dev/null 2>&1
uv venv -p 3.11 "$VENV" --quiet
uv pip install -p "$VENV/bin/python" bpy --quiet
"$VENV/bin/python" -c "import bpy; print('bpy', bpy.app.version_string)" >&2
echo "$VENV/bin/python"
