# Changelog

All notable changes to Citadel for Windows. Versions follow [semantic versioning](https://semver.org): major.minor.patch.

English · [Español](CHANGELOG.es.md) · [日本語](CHANGELOG.ja.md)

## 1.4.0 - 2026-11-27
### Added

- **Japanese interface**: Citadel and its setup window are now in English, Spanish and Japanese. Japanese text
  uses the Japanese fonts of Windows (Yu Gothic UI, or Meiryo UI).
- The interface language can also be chosen in Settings → Appearance, under "Idioma / Language / 言語".
- Starting Citadel while it is already open brings its window to the front (restored if it was minimized),
  instead of doing nothing.
- The window opens maximized if it was maximized when Citadel closed.
- Every button, including the icon buttons (new conversation, Settings, About, export, language, Reason,
  send, Copy, the reasoning block, the colours), can be reached with Tab and pressed with Space or Enter; the
  one with the focus has a ring in the accent colour. A click with the mouse still leaves the focus in the
  text box.
- Links in answers show their address when you point at them.
- Icon-only buttons (new conversation, About, export, language, send and stop, Reason, Copy, delete) have names
  that screen readers such as Narrator read aloud.
- The installation also leaves `README.ja.txt`, the Japanese README, in the program folder.
- README, PDF manual and changelog in Japanese, and the changelog in Spanish.

### Changed

- Without a language chosen in the setup (the stand-alone `Citadel.exe`), the first start uses the language of
  Windows when it is Spanish or Japanese, instead of always English.
- An attachment can be at most 2 MB; a larger file is refused with a message. The text of an attachment is read
  in its own encoding (byte order mark, UTF-8, or the Windows code page for non-Unicode programs), so a file that
  is not UTF-8 no longer shows broken characters.
- `scripts\build.ps1` no longer writes `dist\SHA256SUMS.txt`: the hashes and their signature (`release.sig`)
  are computed and published separately when a version is released.
- `--screenshot` shows the paths of the folder given with `--data-folder` as `%APPDATA%\Citadel`, so the images
  of the manuals show the real path.
- `--self-test` also checks that every interface text is in the three languages with the same markers, the
  `<think>` tags split across pieces of an answer and the encodings of attachments.
- Conversations, settings, rules and memories are written to disk completely before they replace the old file,
  so a power cut can no longer leave an empty file.

### Fixed

- Changing the language or the theme with Settings open discarded rules being edited and not yet saved.
- A `<think>` tag that arrived split between two pieces of an answer was shown as text and the reasoning was
  mixed with the answer.
- A conversation deleted while its answer was being written came back on the next start; its answer now stops.
- A link with an invalid address could stop an answer from being drawn; it is now shown as plain text.
- Exporting a conversation whose title is a name Windows reserves (CON, NUL, COM1…) now saves it as
  "Citadel - <title>.md", and a long title with emoji is no longer cut in the middle of a character.
- With Ollama or the SSH computer unreachable, the status checks no longer pile up.
- An answer from Ollama that cannot be read (for example from another program on its port) now gives the message
  "Ollama returned an answer that cannot be read." instead of a technical error.
- The search in the messages shows at most 200 results, as intended (it could show 201).

## 1.3.2 - 2026-11-27

### Changed

- **A new icon**: a castle with its keep and two towers, and a speech bubble for a pennant, drawn as a flat picture,
  cream with a black outline on the chosen colour; it still follows the colour chosen in the settings, in all twelve
  colours. The drawing is in `icon/citadel.svg`.

## 1.3.1 - 2026-11-25

### Changed

- **Updates signed by the author**: the `SHA256SUMS.txt` of a release must now carry the author's signature
  (`release.sig`, ECDSA P-256, checked against two keys built into Citadel) before the setup is downloaded. Before,
  the setup was checked only against hashes from the same page, so a release put there by someone else would have
  installed itself. A release without a valid signature is offered as a page, to download by hand. Anyone can still
  download, install and share Citadel; Citadel 1.3.0 and older install this one as usual.

## 1.3.0 - 2026-11-20

### Added

- Updates that install themselves, as in Gryphmark: once a day (and from About → Check for updates)
  Citadel looks for a newer release on GitHub and asks once: Update now, What's new or Later. Update now
  downloads `citadel-setup.exe`, checks it against the release's `SHA256SUMS.txt` (a file that does not
  match is deleted and never run), waits for or stops an answer in progress as the user chooses, closes,
  installs in silence keeping the language chosen at installation, and opens Citadel again with the text
  being typed and the open conversation. The stand-alone `Citadel.exe` only offers the release page.
- Search in the text of every saved conversation, ignoring case, accents and line breaks: the results
  show the conversation, the date and the words around the match, highlighted, and a click opens the
  conversation at that message. Ctrl+F goes to the search field.
- Export a conversation to Markdown, from the header or from the sidebar menu: title, date, model and
  each turn under its own heading, with code blocks intact; "Open after exporting" opens the file with
  the app Windows uses for `.md` files.
- The setup accepts `/CITADELWAITPID=<pid>` and `/CITADELRELAUNCH=1`, used by the updates.
- `scripts\build.ps1` also writes `dist\SHA256SUMS.txt`.
- `--self-test` checks the search, the export and the update checks first, without Ollama;
  `--self-test --offline` runs only those.

### Changed

- The search field, which only looked at titles, also lists the matches in the messages.

## 1.2.0 - 2026-11-10

### Added

- Theme and colour, as in Wyrmhold and Gryphmark: Settings → Appearance chooses as Windows, light or dark (the
  default), and one of twelve colours for the buttons, the selection and the icon of the window and the taskbar
  (green is the original). The standard controls and the title bar follow the appearance, and "as Windows"
  follows a change in Windows at once. The accent no longer comes from the Windows accent colour.

## 1.1.0 - 2026-11-09

### Added

- Ollama on another computer through SSH: Settings → Ollama opens a tunnel with the Windows `ssh`
  command to port 11434 of that computer and closes it on exit.
- Models that cannot reason are marked "(no Reason)" in the model list, and the Reason button is
  dimmed while one of them is selected.

### Changed

- The language button moved from the bottom of the sidebar to the top right, next to the model list,
  and shows the current language.

### Fixed

- The language button did nothing when clicked.
- Changing the language from the main window could leave it half rebuilt, and the welcome text stayed
  in the previous language.

### Removed

- The Translate menu.

## 1.0.0 - 2026-11-09

First release.

### Added

- Chat with the models installed in Ollama, streamed as they are written and rendered as Markdown, with
  Copy buttons for each answer and each code block.
- Reason button: the model thinks first and its reasoning is shown in a folded block. Answers are capped
  at 8,000 tokens with Reason on and 3,000 with it off.
- Stop button (Esc), which closes the connection so that Ollama stops generating.
- Status line: whether Ollama answers, whether the model is loaded, what it is doing, elapsed seconds and
  the speed of the last answer.
- Conversations in a sidebar, saved as JSON files in the same format as the macOS version, with search by
  title, rename and delete.
- Rules file sent at the start of every request, editable in Settings.
- Memories: `/mem <topic>` loads a saved memory into the conversation; `/save` drafts one with the model,
  shows it for review and writes it, with its index line, only after confirmation, checking both on disk.
- Attachments of `.txt` and `.md` files by drag and drop, and a Translate menu.
- English and Spanish interface, chosen from the language menu (Idioma / Language).
- Dark Windows 11 style with the Windows accent color.
- Setup program in English and Spanish, an MSI package for silent installs, and a stand-alone
  `Citadel.exe`.
- `--self-test` and `--screenshot` modes for maintenance and for the images of the manual.
- README and PDF manual in English and Spanish.
