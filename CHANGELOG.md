# Changelog

All notable changes to Citadel for Windows. Versions follow [semantic versioning](https://semver.org): major.minor.patch.

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
