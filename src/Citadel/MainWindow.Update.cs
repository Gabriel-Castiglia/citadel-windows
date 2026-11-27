//
//  MainWindow.Update.cs
//  Citadel: local AI chat for Windows.
//
//  Copyright (C) 2026 Gabriel Castiglia. Licensed under the GNU GPL, version 2 only.
//  Date: November 20, 2026
//  SPDX-License-Identifier: GPL-2.0-only
//
//  This program is free software: you can redistribute it and/or modify it under the terms of
//  the GNU General Public License, version 2, as published by the Free Software Foundation.
//  It is distributed WITHOUT ANY WARRANTY. See the LICENSE file for details.
//

using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using static Citadel.Loc;

namespace Citadel;

/// <summary>The newest release on GitHub: its version and the files an update needs.</summary>
internal sealed class ReleaseInfo
{
    public string Tag { get; init; } = "";
    public string Page { get; init; }
    public string SetupUrl { get; init; }
    public string SumsUrl { get; init; }
    /// <summary>release.sig: the author's signature of SHA256SUMS.txt (ReleaseSignature.cs).</summary>
    public string SigUrl { get; init; }
}

/// <summary>
/// About → Check for updates (and once a day by itself): when GitHub has a newer release, one question, and
/// then everything happens alone. citadel-setup.exe is downloaded and checked against the release's
/// SHA256SUMS.txt, which must carry the author's signature (release.sig, ReleaseSignature.cs: without a valid one
/// nothing is downloaded); a file that does not match is deleted, never run. An answer being written is waited for
/// or stopped, as the user chooses, so it is saved; the text being typed, its attachments and the open
/// conversation are kept for the next start. The setup runs in silence keeping the language chosen when
/// Citadel was installed, waits for this window to be gone, and opens Citadel again. A stand-alone
/// Citadel.exe (not installed) is only offered the release page.
/// </summary>
internal sealed partial class MainWindow
{
    private const string UpdateRepo = "Gabriel-Castiglia/citadel-windows";
    private const string SetupAsset = "citadel-setup.exe";
    private const string SumsAsset = "SHA256SUMS.txt";

    private static string UpdateDir => Path.Combine(Path.GetTempPath(), "CitadelUpdate");
    /// <summary>The composer, its attachments and the open conversation, kept across the restart.</summary>
    private static string DraftFile => Path.Combine(UpdateDir, "draft.json");
    private static string CheckStamp => Path.Combine(Paths.Data, "update-check.txt");

    private bool updating, windowClosed;
    /// <summary>Shown in the status line instead of Ollama's status while an update downloads or waits.</summary>
    private string updateNote;

    private sealed class Draft
    {
        public Guid? Conversation { get; set; }
        public string Composer { get; set; } = "";
        public List<Attachment> Attachments { get; set; } = [];
    }

    /// <summary>Called from the constructor, before the window is built.</summary>
    private void InitUpdate()
    {
        Closed += (s, e) => windowClosed = true;
        if (!Settings.Current.Persist) return;   // self-test and screenshots: never
        RestoreDraft();
        if (Environment.GetCommandLineArgs().Contains("--updated"))
            Loaded += (s, e) => ShowToast(tr("Citadel se actualizó a la versión {0}.", "Citadel was updated to version {0}.", "Citadel をバージョン {0} に更新しました。", AboutWindow.VersionText));
        Loaded += async (s, e) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(8));
            await CheckUpdatesAsync(false);
        };
    }

    // Pure parts, checked by the self-test

    /// <summary>True when the release tag ("v1.3.0" or "1.3.0") is a later version than <paramref name="mine"/>.</summary>
    internal static bool IsNewer(string tag, string mine) =>
        System.Version.TryParse((tag ?? "").Trim().TrimStart('v', 'V'), out var a) && System.Version.TryParse(mine, out var b) && a > b;

    /// <summary>The hash of one file in a SHA256SUMS.txt ("hash  name" or "hash *name" per line), or null.</summary>
    internal static string HashFor(string sums, string fileName)
    {
        foreach (var raw in sums.Split('\n'))
        {
            var line = raw.Trim();
            int sp = line.IndexOfAny([' ', '\t']);
            if (sp != 64 || !line[..64].All(Uri.IsHexDigit)) continue;
            var name = line[sp..].Trim().TrimStart('*');
            if (string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase)) return line[..64].ToLowerInvariant();
        }
        return null;
    }

    /// <summary>The tag, the page and the setup and hash files of a GitHub "latest release" answer.</summary>
    internal static ReleaseInfo ParseRelease(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        string setup = null, sums = null, sig = null;
        if (root.TryGetProperty("assets", out var assets))
            foreach (var a in assets.EnumerateArray())
            {
                string name = a.GetProperty("name").GetString() ?? "";
                string url = a.GetProperty("browser_download_url").GetString();
                if (name.Equals(SetupAsset, StringComparison.OrdinalIgnoreCase)) setup = url;
                else if (name.Equals(SumsAsset, StringComparison.OrdinalIgnoreCase)) sums = url;
                else if (name.Equals(ReleaseSignature.AssetName, StringComparison.OrdinalIgnoreCase)) sig = url;
            }
        return new ReleaseInfo
        {
            Tag = (root.GetProperty("tag_name").GetString() ?? "").Trim().TrimStart('v', 'V'),
            Page = root.TryGetProperty("html_url", out var page) ? page.GetString() : null,
            SetupUrl = setup,
            SumsUrl = sums,
            SigUrl = sig,
        };
    }

    /// <summary>True when the file has the expected SHA-256; otherwise the file is deleted.</summary>
    internal static async Task<bool> VerifyDownload(string file, string expected)
    {
        string actual;
        await using (var fs = File.OpenRead(file)) actual = Convert.ToHexString(await SHA256.HashDataAsync(fs)).ToLowerInvariant();
        if (expected != null && actual == expected.ToLowerInvariant()) return true;
        try { File.Delete(file); } catch { }
        return false;
    }

    /// <summary>True when this Citadel.exe is the one the setup installed (not a stand-alone copy).</summary>
    internal static bool IsInstalledCopy()
    {
        try
        {
            using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(@"Software\Citadel");
            if (key?.GetValue("InstallFolder") is not string folder || folder.Length == 0) return false;
            static string Norm(string p) => Path.GetFullPath(p).TrimEnd('\\');
            return string.Equals(Norm(folder), Norm(AppContext.BaseDirectory), StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    // Checking

    private static bool DailyCheckDue()
    {
        try { return !File.Exists(CheckStamp) || (DateTime.UtcNow - File.GetLastWriteTimeUtc(CheckStamp)).TotalHours > 24; }
        catch { return false; }
    }

    private static void StampCheck()
    {
        try { Directory.CreateDirectory(Paths.Data); File.WriteAllText(CheckStamp, DateTime.UtcNow.ToString("o")); } catch { }
    }

    private static HttpClient NewHttp(TimeSpan timeout)
    {
        var http = new HttpClient { Timeout = timeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"Citadel/{AboutWindow.VersionText}");
        return http;
    }

    private void Info(string message) => Dialogs.Choose(this, "Citadel", message, [tr("Aceptar", "OK", "OK")], 0);

    /// <summary>Looks for a newer release; <paramref name="interactive"/> when asked from the About window.</summary>
    public async Task CheckUpdatesAsync(bool interactive)
    {
        if (updating) return;
        if (!interactive && !DailyCheckDue()) return;
        string mine = AboutWindow.VersionText;
        try
        {
            StampCheck();   // even if it fails (no network): at most one try a day
            ReleaseInfo release;
            using (var http = NewHttp(TimeSpan.FromSeconds(10)))
                release = ParseRelease(await http.GetStringAsync($"https://api.github.com/repos/{UpdateRepo}/releases/latest"));
            if (!IsNewer(release.Tag, mine))
            {
                if (interactive) Info(tr("Citadel {0} es la versión más reciente.", "Citadel {0} is the latest version.", "Citadel {0} は最新バージョンです。", mine));
                return;
            }
            // A window that is busy with a dialog of its own is not interrupted by the daily check.
            if (!interactive && OwnedWindows.Cast<Window>().Any(w => w is MemoryPreviewWindow)) return;
            string title = tr("Hay una versión nueva de Citadel: {0}", "A new version of Citadel is available: {0}", "Citadel の新しいバージョンがあります: {0}", release.Tag);
            if (!IsInstalledCopy() || release.SetupUrl == null || release.SumsUrl == null || release.SigUrl == null)
            {
                // A stand-alone Citadel.exe, or a release without its setup or its hashes: only the page.
                string why = IsInstalledCopy()
                    ? tr("Esta versión no trae el instalador para actualizar sola.", "This release does not include the setup needed to update by itself.", "このリリースには、自動で更新するためのセットアップが含まれていません。")
                    : tr("Este Citadel.exe no está instalado, así que no puede actualizarse solo: descarga la versión nueva desde su página.",
                         "This Citadel.exe is not installed, so it cannot update itself: download the new version from its page.",
                         "この Citadel.exe はインストールされていないため、自動では更新できません。新しいバージョンはリリース ページからダウンロードしてください。");
                if (Dialogs.Choose(this, title, tr("Tienes la {0}. ", "You have {0}. ", "現在のバージョンは {0} です。", mine) + why,
                                   [tr("Más tarde", "Later", "後で"), tr("Abrir la página", "Open the page", "ページを開く")], 1) == 1 && release.Page != null)
                    OpenLink(release.Page);
                return;
            }
            int r = Dialogs.Choose(this, title,
                                   tr("Tienes la {0}. Citadel puede descargarla, comprobarla, instalarla y volver a abrirse solo. Tus conversaciones se conservan.",
                                      "You have {0}. Citadel can download it, check it, install it and open again by itself. Your conversations are kept.",
                                      "現在のバージョンは {0} です。Citadel は新しいバージョンのダウンロード、確認、インストールを行い、自動で再起動します。会話はそのまま残ります。", mine),
                                   [tr("Más tarde", "Later", "後で"), tr("Ver novedades", "What's new", "新機能を見る"), tr("Actualizar ahora", "Update now", "今すぐ更新")], 2);
            if (r == 1 && release.Page != null) { OpenLink(release.Page); return; }
            if (r != 2) return;
            await InstallUpdateAsync(release);
        }
        catch (Exception ex)
        {
            Program.Log("Update check failed: " + ex.Message);
            if (interactive) Info(tr("No se pudo buscar actualizaciones: {0}", "Could not check for updates: {0}", "更新を確認できませんでした: {0}", ex.Message));
        }
    }

    private static void OpenLink(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    private void SetUpdateNote(string text)
    {
        updateNote = text;
        RefreshStatus();
    }

    // Installing

    private async Task InstallUpdateAsync(ReleaseInfo release)
    {
        updating = true;
        try
        {
            Directory.CreateDirectory(UpdateDir);
            string file = Path.Combine(UpdateDir, SetupAsset);
            using (var http = NewHttp(TimeSpan.FromMinutes(15)))
            {
                // The author's signature of the hashes first (ReleaseSignature.cs): without a valid one nothing is
                // downloaded or run, so a release someone else put on the page never installs itself.
                byte[] sums = await http.GetByteArrayAsync(release.SumsUrl);
                if (!ReleaseSignature.Valid(sums, await http.GetStringAsync(release.SigUrl)))
                    throw new InvalidOperationException(tr("La versión publicada no trae una firma válida del autor: no se instaló nada. Se puede bajar a mano desde su página.",
                                                           "The release does not carry a valid signature of the author: nothing was installed. It can be downloaded by hand from its page.",
                                                           "このリリースには作者の有効な署名がないため、何もインストールしませんでした。リリース ページから手動でダウンロードできます。"));
                string expected = HashFor(System.Text.Encoding.UTF8.GetString(sums), SetupAsset)
                    ?? throw new InvalidOperationException(tr("{0} no trae el hash de {1}.", "{0} has no hash for {1}.", "{0} に {1} のハッシュがありません。", SumsAsset, SetupAsset));
                using (var resp = await http.GetAsync(release.SetupUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    resp.EnsureSuccessStatusCode();
                    long total = resp.Content.Headers.ContentLength ?? -1, done = 0;
                    await using var input = await resp.Content.ReadAsStreamAsync();
                    await using var output = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.None);
                    var buffer = new byte[81920];
                    int n, lastPct = -1;
                    while ((n = await input.ReadAsync(buffer)) > 0)
                    {
                        await output.WriteAsync(buffer.AsMemory(0, n));
                        done += n;
                        int pct = total > 0 ? (int)(done * 100 / total) : -1;
                        if (pct != lastPct)
                        {
                            lastPct = pct;
                            string amount = pct >= 0 ? pct + " %" : (done >> 20) + " MB";
                            SetUpdateNote(tr("Descargando Citadel {0}… {1}", "Downloading Citadel {0}… {1}", "Citadel {0} をダウンロードしています… {1}", release.Tag, amount));
                        }
                    }
                }
                if (!await VerifyDownload(file, expected))
                    throw new InvalidOperationException(tr("El archivo descargado no coincide con su hash: se borró y no se instaló nada.",
                                                           "The downloaded file does not match its hash: it was deleted and nothing was installed.",
                                                           "ダウンロードしたファイルがハッシュと一致しないため、削除しました。何もインストールしていません。"));
            }

            // An answer being written is waited for or stopped (and so saved) before closing.
            if (!await ReadyToClose()) { CancelUpdate(); return; }
            SaveDraft();
            var psi = new ProcessStartInfo(file) { UseShellExecute = true, WorkingDirectory = UpdateDir };
            foreach (string a in new[] { "/quiet", "/norestart", "/CITADELWAITPID=" + Environment.ProcessId, "/CITADELRELAUNCH=1" })
                psi.ArgumentList.Add(a);
            Process.Start(psi);
            Program.Log($"Update to {release.Tag}: setup started; closing.");
            Close();
        }
        catch (Exception ex)
        {
            Program.Log("Update failed: " + ex.Message);
            try { File.Delete(DraftFile); } catch { }
            // A download cut halfway (or not verified) is never left behind to be run later.
            try { File.Delete(Path.Combine(UpdateDir, SetupAsset)); } catch { }
            CancelUpdate();
            Info(tr("No se pudo actualizar: {0}", "Could not update: {0}", "更新できませんでした: {0}", ex.Message));
        }
    }

    private void CancelUpdate()
    {
        updating = false;
        SetUpdateNote(null);
    }

    /// <summary>
    /// Nothing is lost by closing: with an answer (or a memory draft) in progress the user chooses to wait for
    /// it or to stop it, and an open memory preview is finished first. False when the user cancels.
    /// </summary>
    private async Task<bool> ReadyToClose()
    {
        if (model.IsGenerating || model.IsDraftingMemory)
        {
            int r = Dialogs.Choose(this, tr("Hay una respuesta en curso", "An answer is being written", "回答を作成中です"),
                                   tr("Citadel se cerrará para instalar la versión nueva. La respuesta se guarda en la conversación tanto si esperas a que termine como si la detienes.",
                                      "Citadel will close to install the new version. The answer is saved in the conversation whether you wait for it to finish or stop it.",
                                      "新しいバージョンをインストールするため、Citadel を終了します。完了を待っても停止しても、回答は会話に保存されます。"),
                                   [tr("Cancelar", "Cancel", "キャンセル"), tr("Detenerla y actualizar", "Stop it and update", "停止して更新"), tr("Esperar a que termine", "Wait for it to finish", "完了を待つ")], 2);
            if (r <= 0) return false;
            if (r == 1) model.Stop();
            else SetUpdateNote(tr("La actualización espera a que termine la respuesta (Esc la detiene)…", "The update waits for the answer to finish (Esc stops it)…", "回答が完了してから更新します (Esc で停止)…"));
        }
        // The answer is saved when its stream ends; a memory draft opens its preview, which is finished first.
        while (model.IsGenerating || model.IsDraftingMemory || OwnedWindows.Cast<Window>().Any(w => w is MemoryPreviewWindow))
        {
            if (windowClosed) return false;   // closed by hand meanwhile: the update does not go ahead
            await Task.Delay(250);
        }
        if (windowClosed) return false;
        SetUpdateNote(tr("Instalando la versión nueva…", "Installing the new version…", "新しいバージョンをインストールしています…"));
        return true;
    }

    private void SaveDraft()
    {
        try
        {
            if (model.Composer.Trim().Length == 0 && model.Attachments.Count == 0 && model.SelectedId == null) return;
            var draft = new Draft { Conversation = model.Current is { Messages.Count: > 0 } ? model.SelectedId : null, Composer = model.Composer, Attachments = [.. model.Attachments] };
            Paths.WriteText(DraftFile, JsonSerializer.Serialize(draft));
        }
        catch (Exception ex) { Program.Log("Could not keep the draft for the update: " + ex.Message); }
    }

    /// <summary>After an update (or one that did not go ahead): the text being typed and the open conversation come back.</summary>
    private void RestoreDraft()
    {
        try
        {
            if (!File.Exists(DraftFile)) return;
            bool recent = DateTime.UtcNow - File.GetLastWriteTimeUtc(DraftFile) < TimeSpan.FromHours(6);
            var draft = recent ? JsonSerializer.Deserialize<Draft>(File.ReadAllText(DraftFile)) : null;
            File.Delete(DraftFile);
            if (draft == null) return;
            if (draft.Conversation is Guid id && model.Conversations.Any(c => c.Id == id))
            {
                model.SelectedId = id;
                model.Conversations.RemoveAll(c => c.Messages.Count == 0 && c.Id != id);
            }
            if (model.Composer.Length == 0) model.Composer = draft.Composer ?? "";
            model.Attachments.AddRange(draft.Attachments ?? []);
        }
        catch (Exception ex) { Program.Log("Could not restore the draft after the update: " + ex.Message); }
    }
}
