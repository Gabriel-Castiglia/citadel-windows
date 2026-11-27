//
//  SetupWindow.cs
//  Citadel setup: the setup window, in English, Spanish and Japanese.
//
//  Copyright (C) 2026 Gabriel Castiglia. Licensed under the GNU GPL, version 2 only.
//  Date: November 9, 2026
//  SPDX-License-Identifier: GPL-2.0-only
//
//  This program is free software: you can redistribute it and/or modify it under the terms of
//  the GNU General Public License, version 2, as published by the Free Software Foundation.
//  It is distributed WITHOUT ANY WARRANTY. See the LICENSE file for details.
//

using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WixToolset.BootstrapperApplicationApi;
using static Citadel.Loc;

namespace Citadel.Setup;

/// <summary>
/// The setup window: a header with the icon, the version and the language menu, one page at a time
/// (start, progress, result) and the buttons of that page. A language change redraws the current page.
/// The window also works without an engine (app == null) to render the pages for the manuals.
/// </summary>
internal sealed class SetupWindow : Window
{
    private const string LicenseUrl = "https://www.gnu.org/licenses/old-licenses/gpl-2.0.html";

    private readonly SetupApplication app;
    private readonly ContentControl page = new();
    private readonly StackPanel buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly ProgressBar bar = new() { Height = 14, Minimum = 0, Maximum = 100, Margin = new Thickness(0, 14, 0, 0) };
    private Action redraw;
    private TextBlock subtitle;

    // Values used by the pages; filled from the engine or, for a preview, by hand.
    public string Version = "", OlderVersion, NewerVersion, InstallFolder = @"C:\Program Files\Citadel", ErrorText;
    public bool Applying { get; private set; }

    public SetupWindow(SetupApplication app)
    {
        this.app = app;
        if (app != null)
        {
            Version = app.Version;
            OlderVersion = app.OlderVersion;
            NewerVersion = app.NewerVersion;
            InstallFolder = app.InstallFolder;
        }
        Width = 560;
        Height = 360;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(0xF6, 0xF7, 0xF9));
        // Japanese characters fall back to the Windows Japanese interface fonts.
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, Yu Gothic UI, Meiryo UI");
        FontSize = 13;
        try { Icon = BitmapFrame.Create(new Uri("pack://application:,,,/citadel.ico")); } catch { }

        var root = new DockPanel { Margin = new Thickness(28, 22, 28, 20) };
        var header = BuildHeader();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(page);
        Content = root;

        Loc.Changed += Redraw;
        Closed += (s, e) => Loc.Changed -= Redraw;
        Closing += OnClosing;
        ShowLoading();
    }

    private void Redraw()
    {
        Language = System.Windows.Markup.XmlLanguage.GetLanguage(Loc.CultureName);
        Title = tr("Instalación de Citadel", "Citadel Setup", "Citadel セットアップ");
        if (subtitle != null) subtitle.Text = tr("Chat con IA local para Windows  ·  versión ", "Local AI chat for Windows  ·  version ", "Windows 用ローカル AI チャット  ·  バージョン ") + Version;
        redraw?.Invoke();
    }

    private FrameworkElement BuildHeader()
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 18) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        try
        {
            grid.Children.Add(new Image
            {
                // The largest image of the icon; the first one is 16 pixels and would look blurred.
                Source = BitmapDecoder.Create(new Uri("pack://application:,,,/citadel.ico"), BitmapCreateOptions.None, BitmapCacheOption.OnLoad)
                    .Frames.OrderByDescending(f => f.PixelWidth).First(),
                Width = 48, Height = 48, Margin = new Thickness(0, 0, 14, 0),
            });
        }
        catch { /* the window still works without the icon */ }
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock { Text = "Citadel", FontSize = 24, FontWeight = FontWeights.SemiBold });
        subtitle = new TextBlock { Foreground = Brushes.DimGray };
        titles.Children.Add(subtitle);
        Grid.SetColumn(titles, 1);
        grid.Children.Add(titles);

        // The language menu: its label carries the three languages, so it can be found from any of them.
        var language = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        language.Children.Add(new TextBlock { Text = Loc.MenuLabel, FontSize = 11.5, Foreground = Brushes.DimGray, Margin = new Thickness(0, 0, 0, 3) });
        var combo = new ComboBox { Width = 120 };
        foreach (var l in Loc.All) combo.Items.Add(Loc.NativeName(l));
        combo.SelectedIndex = Array.IndexOf(Loc.All, Loc.Current);
        combo.SelectionChanged += (s, e) => { if (combo.SelectedIndex >= 0) Loc.Current = Loc.All[combo.SelectedIndex]; };
        language.Children.Add(combo);
        Grid.SetColumn(language, 2);
        grid.Children.Add(language);

        return grid;
    }

    // ---------------- Pages ----------------
    private void Show(Action build)
    {
        redraw = build;
        Redraw();
    }

    public void ShowLoading() => Show(() =>
    {
        Body(tr("Revisando este equipo…", "Checking this computer…", "このコンピューターを確認しています…"));
        Buttons();
    });

    public void ShowStart(SetupState state)
    {
        switch (state)
        {
            case SetupState.Install:
                Show(() =>
                {
                    Body(tr("Citadel se instalará para todos los usuarios en:", "Citadel will be installed for all users in:", "Citadel を、すべてのユーザー向けに次の場所へインストールします:"),
                         InstallFolder, null, LicenseLine());
                    Buttons((tr("Instalar", "Install", "インストール"), () => app?.Plan(LaunchAction.Install), true), CancelButton());
                });
                break;
            case SetupState.Upgrade:
                Show(() =>
                {
                    Body(string.Format(tr("Citadel {0} está instalado. Se actualizará a la versión {1}; las conversaciones y los ajustes de cada usuario se conservan.",
                                          "Citadel {0} is installed. It will be updated to version {1}; each user's conversations and settings are kept.",
                                          "Citadel {0} がインストールされています。バージョン {1} に更新します。各ユーザーの会話と設定はそのまま残ります。"),
                                       OlderVersion, Version), null, null, LicenseLine());
                    Buttons((tr("Actualizar", "Update", "更新"), () => app?.Plan(LaunchAction.Install), true), CancelButton());
                });
                break;
            case SetupState.Maintenance:
                Show(() =>
                {
                    Body(string.Format(tr("Citadel {0} ya está instalado en este equipo.", "Citadel {0} is already installed on this computer.", "Citadel {0} は、このコンピューターにすでにインストールされています。"), Version),
                         null, tr("Reparar vuelve a copiar sus archivos; desinstalar lo quita del equipo.",
                                  "Repair copies its files again; uninstall removes it from the computer.",
                                  "修復すると、ファイルをもう一度コピーします。アンインストールすると、コンピューターから削除します。"));
                    Buttons((tr("Reparar", "Repair", "修復"), () => app?.Plan(LaunchAction.Repair), false),
                            (tr("Desinstalar", "Uninstall", "アンインストール"), () => app?.Plan(LaunchAction.Uninstall), false),
                            CloseButton(true));
                });
                break;
            case SetupState.Uninstall:
                Show(() =>
                {
                    Body(tr("Citadel se quitará de este equipo.", "Citadel will be removed from this computer.", "Citadel をこのコンピューターから削除します。"), null,
                         tr("Las conversaciones, memorias y ajustes de cada usuario (carpeta %APPDATA%\\Citadel) se conservan.",
                            "Each user's conversations, memories and settings (the %APPDATA%\\Citadel folder) are kept.",
                            "各ユーザーの会話、メモリ、設定 (%APPDATA%\\Citadel フォルダー) はそのまま残ります。"));
                    Buttons((tr("Desinstalar", "Uninstall", "アンインストール"), () => app?.Plan(LaunchAction.Uninstall), true), CancelButton());
                });
                break;
            case SetupState.Newer:
                Show(() =>
                {
                    Body(string.Format(tr("Ya está instalada una versión más nueva de Citadel ({0}). Esta versión no se puede instalar encima.",
                                          "A newer version of Citadel ({0}) is already installed. This version cannot be installed over it.",
                                          "より新しいバージョンの Citadel ({0}) がすでにインストールされています。このバージョンを上書きしてインストールすることはできません。"),
                                       NewerVersion));
                    Buttons(CloseButton(true));
                });
                break;
        }
    }

    public void ShowProgress(LaunchAction action)
    {
        Applying = true;
        Show(() =>
        {
            string text = action switch
            {
                LaunchAction.Uninstall => tr("Desinstalando Citadel…", "Uninstalling Citadel…", "Citadel をアンインストールしています…"),
                LaunchAction.Repair => tr("Reparando Citadel…", "Repairing Citadel…", "Citadel を修復しています…"),
                _ => OlderVersion != null ? tr("Actualizando Citadel…", "Updating Citadel…", "Citadel を更新しています…") : tr("Instalando Citadel…", "Installing Citadel…", "Citadel をインストールしています…"),
            };
            var panel = BodyPanel(text);
            if (bar.Parent is Panel old) old.Children.Remove(bar);
            panel.Children.Add(bar);
            page.Content = panel;
            Buttons((tr("Cancelar", "Cancel", "キャンセル"), () => { if (app != null) app.Canceled = true; }, false));
        });
    }

    public void SetProgress(int percent) => bar.Value = Math.Max(bar.Value, Math.Min(100, Math.Max(0, percent)));

    public void ShowResult(bool ok, bool canceled, bool restart)
    {
        Applying = false;
        var action = app?.Planned ?? LaunchAction.Install;
        Show(() =>
        {
            if (canceled)
            {
                Body(tr("Se canceló. El equipo quedó como estaba.", "Canceled. The computer was left as it was.", "キャンセルしました。コンピューターは元の状態のままです。"));
                Buttons(CloseButton(true));
                return;
            }
            if (!ok)
            {
                Body(tr("La operación no se pudo completar.", "The operation could not be completed.", "操作を完了できませんでした。"),
                     null, ErrorText ?? app?.LastError);
                Buttons((tr("Ver registro", "View log", "ログを表示"), OpenLog, false), CloseButton(true));
                return;
            }
            string done = action switch
            {
                LaunchAction.Uninstall => tr("Citadel se desinstaló.", "Citadel was uninstalled.", "Citadel をアンインストールしました。"),
                LaunchAction.Repair => tr("Citadel quedó reparado.", "Citadel was repaired.", "Citadel を修復しました。"),
                _ => tr("Citadel quedó instalado. Está en el menú Inicio.", "Citadel is installed. It is in the Start menu.", "Citadel をインストールしました。スタート メニューから起動できます。"),
            };
            Body(done, null, restart ? tr("Hay que reiniciar Windows para terminar.", "Windows must be restarted to finish.", "完了するには Windows の再起動が必要です。") : null);
            if (action == LaunchAction.Install)
                Buttons((tr("Abrir Citadel", "Open Citadel", "Citadel を開く"), LaunchCitadel, true), CloseButton(false));
            else
                Buttons(CloseButton(true));
        });
    }

    // ---------------- Building blocks ----------------
    private StackPanel BodyPanel(string text)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 14 });
        return panel;
    }

    /// <summary>Page body: a main text, an optional highlighted line, an optional note and an optional extra block.</summary>
    private void Body(string text, string highlight = null, string note = null, UIElement extra = null)
    {
        var panel = BodyPanel(text);
        if (highlight != null)
            panel.Children.Add(new TextBlock { Text = highlight, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap });
        if (note != null)
            panel.Children.Add(new TextBlock { Text = note, Foreground = Brushes.DimGray, Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap });
        if (extra != null) panel.Children.Add(extra);
        page.Content = panel;
    }

    private UIElement LicenseLine()
    {
        var link = new Hyperlink(new Run(tr("Licencia Pública General GNU, versión 2", "GNU General Public License, version 2", "GNU 一般公衆利用許諾書 バージョン 2")))
        {
            NavigateUri = new Uri(LicenseUrl),
        };
        link.RequestNavigate += (s, e) => Open(LicenseUrl);
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 16, 0, 0) };
        text.Inlines.Add(new Run(tr("Citadel es software libre, bajo la ", "Citadel is free software, under the ", "Citadel は ")));
        text.Inlines.Add(link);
        text.Inlines.Add(new Run(tr(".", ".", " に基づくフリーソフトウェアです。")));
        return text;
    }

    private void Buttons(params (string text, Action action, bool isDefault)[] list)
    {
        buttons.Children.Clear();
        foreach (var (text, action, isDefault) in list)
        {
            var b = new Button { Content = text, MinWidth = 96, Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(8, 0, 0, 0), IsDefault = isDefault };
            b.Click += (s, e) => action();
            buttons.Children.Add(b);
        }
    }

    private (string, Action, bool) CancelButton() => (tr("Cancelar", "Cancel", "キャンセル"), Close, false);
    private (string, Action, bool) CloseButton(bool isDefault) => (tr("Cerrar", "Close", "閉じる"), Close, isDefault);

    private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!Applying || app == null)
        {
            if (app != null && app.Planned == LaunchAction.Unknown) app.CancelBeforeStart();
            return;
        }
        // While installing, closing the window means canceling; the engine rolls the changes back.
        e.Cancel = true;
        var answer = MessageBox.Show(this, tr("¿Cancelar la operación? Se deshará lo hecho hasta ahora.", "Cancel the operation? What was done so far will be undone.", "操作をキャンセルしますか? ここまでの変更は元に戻されます。"),
                                     "Citadel", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Yes && app != null) app.Canceled = true;
    }

    private void LaunchCitadel()
    {
        Open(Path.Combine(InstallFolder, "Citadel.exe"));
        Close();
    }

    private void OpenLog()
    {
        string log = app?.LogPath;
        if (!string.IsNullOrEmpty(log)) Open(log);
    }

    private static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch { }
    }
}
