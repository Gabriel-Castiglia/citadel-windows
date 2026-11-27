#!/usr/bin/env bash
# manual.sh
# Builds the PDF manuals and the plain-text READMEs from the Markdown READMEs.
#
# Copyright (C) 2026 Gabriel Castiglia. Licensed under the GNU GPL, version 2 only.
# Date: November 9, 2026 (also in Git Bash on Windows: November 20, 2026)
# SPDX-License-Identifier: GPL-2.0-only
#
# This program is free software: you can redistribute it and/or modify it under the terms of
# the GNU General Public License, version 2, as published by the Free Software Foundation.
# It is distributed WITHOUT ANY WARRANTY. See the LICENSE file for details.

# Builds the PDF manuals and the plain-text READMEs from the Markdown READMEs:
#   README.md     -> docs/Citadel-manual-en.pdf and README.txt
#   README.es.md  -> docs/Citadel-manual-es.pdf and README.es.txt
#   README.ja.md  -> docs/Citadel-manual-ja.pdf and README.ja.txt
# The .txt copies are for reading on Windows without a Markdown viewer; they are never edited by hand.
# Requires pandoc (brew install pandoc, or pandoc.org) and Google Chrome, which prints the HTML to PDF.
# Runs on macOS and in Git Bash on Windows; CHROME may name another Chrome (or Edge) executable.
# Run it again after every change to either README or to the images in docs/images.

set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

if [[ -z "${CHROME:-}" ]]; then
    CHROME="/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"
    [[ -x "$CHROME" ]] || CHROME="/c/Program Files/Google/Chrome/Application/chrome.exe"
fi
command -v pandoc >/dev/null || { echo "pandoc is missing: brew install pandoc (or pandoc.org)" >&2; exit 1; }
[[ -x "$CHROME" ]] || { echo "Google Chrome is missing (set CHROME to its executable)" >&2; exit 1; }
# Windows programs (Chrome) want Windows paths; elsewhere a path stays as it is.
native() { if command -v cygpath >/dev/null; then cygpath -m "$1"; else printf '%s' "$1"; fi; }
REPO="https://github.com/Gabriel-Castiglia/citadel-windows"
VERSION=$(sed -n 's|.*<Version>\(.*\)</Version>.*|\1|p' src/Citadel/Citadel.csproj | head -1)
# The line under the title of each README names the version (Muelle's went out three versions behind): it must be
# the program's.
for f in README.md:version README.es.md:versión README.ja.md:バージョン; do
    grep -q "· ${f#*:} $VERSION ·" "${f%%:*}" || { echo "${f%%:*} does not say \"${f#*:} $VERSION\" under its title" >&2; exit 1; }
done

TEMP="$(mktemp -d)"
trap 'rm -rf "$TEMP"' EXIT

# Relative links (LICENSE, CHANGELOG.md, docs/…) point to the repository in the PDF; otherwise they
# would become paths inside the temporary folder of whoever builds it.
cat > "$TEMP/links.lua" <<LUA
function Link(el)
  if not el.target:match("^%a[%w+.-]*:") and not el.target:match("^#") then
    el.target = "$REPO/blob/main/" .. el.target
  end
  return el
end
-- The setup window is small: drawn at the width of the page it would look larger than the main window.
function Image(el)
  if el.src:match("setup%-") then el.classes:insert("narrow") end
  return el
end
-- A title is never left at the foot of a page away from what it introduces: a title, the text right under it and
-- a picture, table or code block that follows within the next two blocks go together on one page.
local function is_picture(b)
  return b and ((b.t == "Para" and #b.content == 1 and b.content[1].t == "Image") or b.t == "Table" or b.t == "CodeBlock")
end
function Blocks(blocks)
  local out, i = {}, 1
  while i <= #blocks do
    local b = blocks[i]
    local last = nil
    if b.t == "Header" then
      if is_picture(blocks[i + 1]) then last = i + 1
      elseif blocks[i + 1] and blocks[i + 1].t == "Para" and is_picture(blocks[i + 2]) then last = i + 2 end
    end
    if last then
      local group = {}
      for j = i, last do group[#group + 1] = blocks[j] end
      out[#out + 1] = pandoc.Div(group, pandoc.Attr("", {"keep"}))
      i = last + 1
    else
      out[#out + 1] = b
      i = i + 1
    end
  end
  return out
end
-- The links to the other languages and to this very PDF only make sense on GitHub.
function Para(el)
  for _, inline in ipairs(el.content) do
    if inline.t == "Link" and (inline.target:match("README[%.%a]*%.md$") or inline.target:match("Citadel%-manual%-%a+%.pdf$")) then
      return {}
    end
  end
  return el
end
LUA

# Plain text: no images, titles underlined, links to the other README point to its .txt copy, and web
# links keep their address after the text.
cat > "$TEMP/text.lua" <<'LUA'
-- Width on screen: Japanese characters (and full-width punctuation) take two columns, so the rule under a
-- Japanese title is as long as the title.
local function width(s)
  local n = 0
  for _, c in utf8.codes(s) do
    if (c >= 0x1100 and c <= 0x115F) or (c >= 0x2E80 and c <= 0xA4CF) or (c >= 0xAC00 and c <= 0xD7A3)
       or (c >= 0xF900 and c <= 0xFAFF) or (c >= 0xFE30 and c <= 0xFE4F) or (c >= 0xFF00 and c <= 0xFF60)
       or (c >= 0xFFE0 and c <= 0xFFE6) then n = n + 2 else n = n + 1 end
  end
  return n
end
function Image(el) return {} end
function Header(el)
  local text = pandoc.utils.stringify(el.content)
  local rule = string.rep(el.level == 1 and "=" or "-", width(text))
  return pandoc.Plain({pandoc.Str(text), pandoc.LineBreak(), pandoc.Str(rule)})
end
function Link(el)
  local target = el.target:gsub("^README%.es%.md$", "README.es.txt"):gsub("^README%.ja%.md$", "README.ja.txt"):gsub("^README%.md$", "README.txt")
  local text = pandoc.utils.stringify(el.content)
  if text == target then return pandoc.Str(target) end
  -- A link to another section of the same README: in plain text the name of the section is enough.
  if target:match("^#") then return pandoc.Str(text) end
  if not target:match("^https?:") then return pandoc.Str(text .. " (" .. target .. ")") end
  return pandoc.Str(text .. " <" .. target .. ">")
end
LUA

text() {
    local source=$1 output=$2 wrap=--columns=100
    # Japanese has no spaces to break lines at: its paragraphs stay on one line, which Notepad wraps.
    [[ $source == *.ja.md ]] && wrap=--wrap=none
    pandoc "$source" --from gfm --to plain "$wrap" --lua-filter "$TEMP/text.lua" --output "$output"
    echo "Text: $output"
}

build() {
    local source=$1 lang=$2 title=$3 subtitle=$4 contents=$5 output=$6
    # Cover block with the icon, the name and the version.
    cat > "$TEMP/cover-$lang.html" <<HTML
<div class="cover">
  <img src="docs/images/icon.png" alt="">
  <div>
    <p class="name">Citadel</p>
    <p class="subtitle">$subtitle $VERSION · <a href="$REPO">${REPO#https://}</a></p>
  </div>
</div>
HTML
    # Self-contained HTML: stylesheet and images embedded, no references to local files.
    # In Japanese a line break inside a paragraph must not become a space (east_asian_line_breaks).
    pandoc "$source" --from gfm+east_asian_line_breaks --to html5 --standalone --embed-resources \
        --lua-filter "$TEMP/links.lua" \
        --css docs/manual.css \
        --include-before-body "$TEMP/cover-$lang.html" \
        --toc --toc-depth=2 --metadata toc-title="$contents" \
        --metadata pagetitle="$title" \
        --metadata lang="$lang" \
        --output "$TEMP/manual-$lang.html"
    # No header or footer: Chrome would print the local path of the HTML file in them. A profile of its own,
    # so a Chrome that is already open is left alone.
    "$CHROME" --headless --disable-gpu --no-pdf-header-footer --user-data-dir="$(native "$TEMP/chrome")" \
        --print-to-pdf="$(native "$ROOT/$output")" "file:///$(native "$TEMP/manual-$lang.html" | sed 's|^/||')" 2>/dev/null
    echo "Manual: $output"
}

# Language, title of the HTML, line under the name on the cover, title of the contents, PDF.
build README.md    en "Citadel · Manual"             "Local AI chat for Windows · version"         Contents docs/Citadel-manual-en.pdf
build README.es.md es "Citadel · Manual de usuario"  "Chat con IA local para Windows · versión"    Índice   docs/Citadel-manual-es.pdf
build README.ja.md ja "Citadel · ユーザーマニュアル" "Windows 用ローカル AI チャット · バージョン" 目次     docs/Citadel-manual-ja.pdf

text README.md    README.txt
text README.es.md README.es.txt
text README.ja.md README.ja.txt
