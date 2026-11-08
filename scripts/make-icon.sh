#!/bin/zsh
# make-icon.sh
# Builds src/Citadel/citadel.ico from icon/citadel-1024.png (macOS: uses sips).
#
# Copyright (C) 2026 Gabriel Castiglia. Licensed under the GNU GPL, version 2 only.
# Date: November 9, 2026
# SPDX-License-Identifier: GPL-2.0-only
#
# This program is free software: you can redistribute it and/or modify it under the terms of
# the GNU General Public License, version 2, as published by the Free Software Foundation.
# It is distributed WITHOUT ANY WARRANTY. See the LICENSE file for details.

# icon/citadel-1024.png is the macOS icon, drawn from icon/citadel.svg with a 100-pixel margin. Windows
# icons fill their square, so the margin is cropped before the sizes are made. The .ico holds PNG
# images at 16, 24, 32, 48, 64, 128 and 256 pixels.
set -euo pipefail
root=${0:A:h:h}
cd $root
tmp=$(mktemp -d)
trap 'rm -rf $tmp' EXIT
sips -c 840 840 --cropOffset 92 92 icon/citadel-1024.png --out $tmp/square.png >/dev/null
for s in 16 24 32 48 64 128 256; do
    sips -z $s $s $tmp/square.png --out $tmp/$s.png >/dev/null
done
python3 - $tmp src/Citadel/citadel.ico <<'PY'
import struct, sys
tmp, out = sys.argv[1], sys.argv[2]
sizes = [16, 24, 32, 48, 64, 128, 256]
pngs = [open(f"{tmp}/{s}.png", "rb").read() for s in sizes]
data = struct.pack("<HHH", 0, 1, len(sizes))
offset = 6 + 16 * len(sizes)
for s, p in zip(sizes, pngs):
    d = 0 if s >= 256 else s
    data += struct.pack("<BBBBHHII", d, d, 0, 0, 1, 32, len(p), offset)
    offset += len(p)
open(out, "wb").write(data + b"".join(pngs))
PY
print "Icon written: src/Citadel/citadel.ico"
