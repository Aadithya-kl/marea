#!/usr/bin/env bash
# Self-test of marea-check on macOS and Linux: builds real model-like files with Python and checks
# the verdicts and exit codes. Usage: build/check-selftest.sh <path to marea-check>
set -u
mc="$1"
dir="$(mktemp -d)"
python3 - "$dir" <<'PY'
import json, os, pickle, struct, sys, zipfile
d = sys.argv[1]
class Evil:
    def __reduce__(self):
        return (os.system, ("echo pwned",))
open(os.path.join(d, "evil.pkl"), "wb").write(pickle.dumps(Evil()))
with zipfile.ZipFile(os.path.join(d, "clean.pt"), "w") as z:
    z.writestr("archive/data.pkl", b"\x80\x02ctorch._utils\n_rebuild_tensor_v2\nq\x00)R.")
h = json.dumps({"w": {"dtype": "F16", "shape": [2, 2], "data_offsets": [0, 8]}}).encode()
h += b" " * ((8 - len(h) % 8) % 8)
open(os.path.join(d, "lora.safetensors"), "wb").write(struct.pack("<Q", len(h)) + h + b"\0" * 8)
PY
fail=0
expect() { "$mc" "$2" > /dev/null; got=$?; if [ "$got" -ne "$1" ]; then echo "FAIL $2: exit $got, expected $1"; fail=1; else echo "ok   $2: exit $got"; fi; }
expect 4 "$dir/evil.pkl"
expect 0 "$dir/clean.pt"
expect 0 "$dir/lora.safetensors"
expect 4 "$dir"
"$mc" "$dir/evil.pkl" --json | grep -q '"safety": "dangerous"' || { echo "FAIL json"; fail=1; }
rm -rf "$dir"
exit $fail
