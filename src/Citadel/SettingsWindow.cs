//
//  SettingsWindow.cs
//  Citadel: local AI chat for Windows.
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
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using static Citadel.Loc;

namespace Citadel;

/// <summary>Settings: the memory folder and its memories, and the rules sent with every request.</summary>
internal sealed class SettingsWindow : Window
{
    private TabControl tabs;
    private TextBlock folderText, countText, message;
    private StackPanel memoryList;
    private TextBox rulesBox;
    private Button saveRules, revertRules;
    private bool rulesDirty;

    /// <summary>The chat model, so a new Ollama connection can be tested at once.</summary>
    public ChatModel Model { get; init; }

    public SettingsWindow()
    {
        Width = 720;
        Height = 560;
        MinWidth = 600;
        MinHeight = 440;
        Background = Theme.Window;
        Foreground = Theme.Text;
        FontFamily = Theme.Ui;
        FontSize = 13;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Build();
        Loc.Changed += Build;
        Theme.Changed += Build;
        Closed += (s, e) => { Loc.Changed -= Build; Theme.Changed -= Build; };
        PreviewKeyDown += (s, e) =>
        {
            if (e.Key == System.Windows.Input.Key.S && (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0 && rulesDirty)
            {
                SaveRules();
                e.Handled = true;
            }
        };
    }

    /// <summary>Shows the given tab: 0 for Memories, 1 for Rules, 2 for Ollama, 3 for Appearance.</summary>
    public int Tab { set => tabs.SelectedIndex = value; }

    private void Build()
    {
        Title = tr("Ajustes de Citadel", "Citadel Settings");
        Theme.Apply(this);
        int selected = tabs?.SelectedIndex ?? 0;
        tabs = new TabControl { Margin = new Thickness(14, 10, 14, 14), Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        tabs.Items.Add(new TabItem { Header = tr("Memorias", "Memories"), Content = MemoriesTab() });
        tabs.Items.Add(new TabItem { Header = tr("Reglas", "Rules"), Content = RulesTab() });
        tabs.Items.Add(new TabItem { Header = "Ollama", Content = OllamaTab() });
        tabs.Items.Add(new TabItem { Header = tr("Apariencia", "Appearance"), Content = AppearanceTab() });
        tabs.SelectedIndex = selected;
        Content = tabs;
        ReloadMemories();
    }

    private UIElement MemoriesTab()
    {
        var panel = new DockPanel { Margin = new Thickness(6, 14, 6, 6) };
        var top = new StackPanel();
        top.Children.Add(new TextBlock { Text = tr("Carpeta de memorias", "Memory folder"), FontWeight = FontWeights.SemiBold, FontSize = 13.5 });
        folderText = new TextBlock { FontFamily = Theme.Mono, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var choose = new Button { Content = tr("Elegir…", "Choose…"), Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0) };
        choose.Click += (s, e) => ChooseFolder();
        var reset = new Button { Content = tr("Por defecto", "Default"), Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0), ToolTip = Paths.Short(Paths.DefaultMemoryFolder) };
        reset.Click += (s, e) => { Settings.Current.MemoryFolder = null; Settings.Current.Save(); ReloadMemories(); };
        var folderRow = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var folderButtons = new StackPanel { Orientation = Orientation.Horizontal, Children = { choose, reset } };
        DockPanel.SetDock(folderButtons, Dock.Right);
        folderRow.Children.Add(folderButtons);
        folderRow.Children.Add(new Border { CornerRadius = new CornerRadius(6), Background = Theme.Card, Padding = new Thickness(8, 5, 8, 5), Child = folderText });
        top.Children.Add(folderRow);
        top.Children.Add(new TextBlock
        {
            Text = tr("El índice _index.md de esta carpeta se agrega a cada conversación. La app escribe las memorias (con /save), nunca el modelo.",
                      "The _index.md of this folder is added to every conversation. The app writes memories (with /save), never the model."),
            FontSize = 12, Foreground = Theme.Secondary, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0),
        });
        var listHeader = new DockPanel { Margin = new Thickness(0, 16, 0, 8) };
        var open = Theme.FlatButton(Theme.GlyphFolder, tr("Abrir carpeta", "Open folder"), () => Open(MemoryStore.Folder));
        var refresh = Theme.FlatButton(Theme.GlyphRefresh, null, ReloadMemories, tr("Actualizar", "Refresh"));
        var headerButtons = new StackPanel { Orientation = Orientation.Horizontal, Children = { open, refresh } };
        DockPanel.SetDock(headerButtons, Dock.Right);
        listHeader.Children.Add(headerButtons);
        countText = new TextBlock { FontWeight = FontWeights.SemiBold, FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center };
        listHeader.Children.Add(countText);
        top.Children.Add(listHeader);
        DockPanel.SetDock(top, Dock.Top);
        panel.Children.Add(top);

        message = new TextBlock { FontSize = 12, Foreground = Theme.Secondary, Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(message, Dock.Bottom);
        panel.Children.Add(message);

        memoryList = new StackPanel();
        panel.Children.Add(new Border
        {
            BorderBrush = Theme.Stroke, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
            Child = new ScrollViewer { Content = memoryList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
        });
        return panel;
    }

    /// <summary>Appearance (as Windows, light, dark) and the twelve accent colours (Theme.Accents).</summary>
    private UIElement AppearanceTab()
    {
        var panel = new StackPanel { Margin = new Thickness(6, 14, 6, 6) };
        panel.Children.Add(new TextBlock { Text = tr("Apariencia", "Appearance"), FontWeight = FontWeights.SemiBold, FontSize = 13.5 });
        var looks = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        foreach (var (key, label) in new[] { ("auto", tr("Como Windows", "As Windows")), ("light", tr("Claro", "Light")), ("dark", tr("Oscuro", "Dark")) })
        {
            bool on = (Settings.Current.Appearance ?? "dark") == key;
            var b = new Button
            {
                Content = new TextBlock { Text = label, FontSize = 13, Foreground = on ? Theme.OnAccent : Theme.Text },
                Background = on ? Theme.Accent : Theme.Card, Template = Theme.FlatTemplate(6),
                Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 6, 0), Cursor = System.Windows.Input.Cursors.Hand,
            };
            b.Click += (s, e) => Theme.SetAppearance(key);
            looks.Children.Add(b);
        }
        panel.Children.Add(looks);
        panel.Children.Add(new TextBlock
        {
            Text = tr("Color de los botones y del ícono", "Colour of the buttons and the icon"),
            FontWeight = FontWeights.SemiBold, FontSize = 13.5, Margin = new Thickness(0, 18, 0, 0),
        });
        var grid = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var a in Theme.Accents)
        {
            bool on = Theme.CurrentAccent.Key == a.Key;
            var cell = new StackPanel { Width = 84 };
            cell.Children.Add(new Image { Source = Theme.AccentIcon(a.Key), Width = 52, Height = 52 });
            cell.Children.Add(new TextBlock { Text = a.Name, FontSize = 11.5, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 4, 0, 0) });
            var b = new Button
            {
                Content = cell, Template = Theme.FlatTemplate(8), Padding = new Thickness(4), Margin = new Thickness(0, 0, 6, 8),
                Background = on ? Theme.AccentSoft : Brushes.Transparent, BorderBrush = Theme.Accent,
                Cursor = System.Windows.Input.Cursors.Hand, ToolTip = a.Name,
            };
            b.Click += (s, e) => Theme.SetAccent(a.Key);
            grid.Children.Add(b);
        }
        panel.Children.Add(grid);
        panel.Children.Add(new TextBlock
        {
            Text = tr("El color se usa en los botones, la selección y el ícono de la ventana y de la barra de tareas mientras Citadel está abierto. El acceso directo conserva el ícono verde.",
                      "The colour is used for the buttons, the selection and the icon of the window and the taskbar while Citadel is open. The shortcut keeps the green icon."),
            FontSize = 12, Foreground = Theme.Secondary, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0),
        });
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private UIElement OllamaTab()
    {
        var panel = new StackPanel { Margin = new Thickness(6, 14, 6, 6) };
        panel.Children.Add(new TextBlock { Text = tr("Dónde corre Ollama", "Where Ollama runs"), FontWeight = FontWeights.SemiBold, FontSize = 13.5 });
        var local = new RadioButton
        {
            Content = tr("En esta computadora (127.0.0.1:11434)", "On this computer (127.0.0.1:11434)"),
            IsChecked = !Settings.Current.UseSsh, Margin = new Thickness(0, 10, 0, 0), GroupName = "where",
        };
        var remote = new RadioButton
        {
            Content = tr("En otra computadora, por SSH", "On another computer, through SSH"),
            IsChecked = Settings.Current.UseSsh, Margin = new Thickness(0, 8, 0, 0), GroupName = "where",
        };
        var host = new TextBox
        {
            Text = Settings.Current.SshHost ?? "", FontFamily = Theme.Mono, FontSize = 12.5, Padding = new Thickness(6, 4, 6, 4),
            Margin = new Thickness(26, 8, 0, 0), MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 320,
            ToolTip = tr("usuario@equipo, o un Host de ~/.ssh/config", "user@host, or a Host from ~/.ssh/config"),
        };
        host.IsEnabled = remote.IsChecked == true;
        remote.Checked += (s, e) => host.IsEnabled = true;
        local.Checked += (s, e) => host.IsEnabled = false;
        panel.Children.Add(local);
        panel.Children.Add(remote);
        panel.Children.Add(host);
        panel.Children.Add(new TextBlock
        {
            Text = tr("Citadel abre un túnel con el ssh de Windows hasta el puerto 11434 de esa computadora y lo cierra al salir. Usa tu llave SSH: nunca pide contraseña. El manual explica cómo preparar las dos computadoras.",
                      "Citadel opens a tunnel with the Windows ssh command to port 11434 of that computer and closes it on exit. It uses your SSH key and never asks for a password. The manual explains how to prepare both computers."),
            FontSize = 12, Foreground = Theme.Secondary, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0),
        });
        var result = new TextBlock { FontSize = 12, Foreground = Theme.Secondary, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var apply = new Button { Content = tr("Aplicar y probar", "Apply and test"), Padding = new Thickness(12, 4, 12, 4) };
        apply.Click += async (s, e) =>
        {
            Settings.Current.UseSsh = remote.IsChecked == true && host.Text.Trim().Length > 0;
            Settings.Current.SshHost = host.Text.Trim().Length > 0 ? host.Text.Trim() : null;
            Settings.Current.Save();
            Connection.Stop();
            apply.IsEnabled = false;
            result.Text = tr("Conectando…", "Connecting…");
            try
            {
                var models = await Ollama.ListModels();
                result.Text = tr($"Conectado: {models.Count} modelo(s). ", $"Connected: {models.Count} model(s). ") + string.Join(", ", models);
            }
            catch (Exception ex) { result.Text = ex.Message; }
            finally { apply.IsEnabled = true; }
            if (Model != null) await Model.RefreshStatus();
        };
        panel.Children.Add(new DockPanel { Margin = new Thickness(0, 14, 0, 0), Children = { apply, result } });
        return panel;
    }

    private void ReloadMemories()
    {
        if (memoryList == null) return;
        folderText.Text = Paths.Short(MemoryStore.Folder);
        var all = MemoryStore.All();
        countText.Text = tr($"Memorias ({all.Count})", $"Memories ({all.Count})");
        memoryList.Children.Clear();
        foreach (var m in all)
        {
            var row = new DockPanel { Margin = new Thickness(10, 6, 6, 6) };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            buttons.Children.Add(Theme.FlatButton(Theme.GlyphSearch, null, () => ShowInExplorer(m.File), tr("Mostrar en el Explorador", "Show in File Explorer")));
            buttons.Children.Add(Theme.FlatButton(Theme.GlyphTrash, null, () => DeleteMemory(m), tr("Eliminar", "Delete")));
            DockPanel.SetDock(buttons, Dock.Right);
            row.Children.Add(buttons);
            var icon = Theme.Icon(Theme.GlyphDocument, 15, Theme.Accent);
            icon.Margin = new Thickness(0, 0, 10, 0);
            DockPanel.SetDock(icon, Dock.Left);
            row.Children.Add(icon);
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = m.File, FontFamily = Theme.Mono, FontSize = 13, FontWeight = FontWeights.SemiBold });
            text.Children.Add(new TextBlock
            {
                Text = m.Topic.Length == 0 ? tr("(no figura en el índice)", "(not in the index)") : m.Topic,
                FontSize = 12, Foreground = Theme.Secondary, TextTrimming = TextTrimming.CharacterEllipsis,
            });
            row.Children.Add(text);
            memoryList.Children.Add(row);
            memoryList.Children.Add(new Border { Height = 1, Background = Theme.Stroke, Margin = new Thickness(10, 0, 10, 0), Opacity = 0.6 });
        }
    }

    private void ChooseFolder()
    {
        var dialog = new OpenFolderDialog { InitialDirectory = Directory.Exists(MemoryStore.Folder) ? MemoryStore.Folder : null };
        if (dialog.ShowDialog(this) == true)
        {
            Settings.Current.MemoryFolder = dialog.FolderName;
            Settings.Current.Save();
            ReloadMemories();
        }
    }

    private void DeleteMemory(MemoryEntry m)
    {
        if (!Dialogs.Confirm(this, tr("¿Eliminar esta memoria?", "Delete this memory?"),
                             tr($"Se borra el archivo {m.File} y su línea del índice.", $"The file {m.File} and its index line will be removed."),
                             tr("Eliminar", "Delete")))
            return;
        try
        {
            MemoryStore.Delete(m.File);
            message.Text = tr($"Eliminada: {m.File}", $"Deleted: {m.File}");
        }
        catch (Exception ex) { message.Text = ex.Message; }
        ReloadMemories();
    }

    private static void ShowInExplorer(string file)
    {
        try { Process.Start("explorer.exe", $"/select,\"{Path.Combine(MemoryStore.Folder, file)}\""); } catch { }
    }

    private static void Open(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch { }
    }

    private UIElement RulesTab()
    {
        var panel = new DockPanel { Margin = new Thickness(6, 14, 6, 6) };
        var top = new StackPanel();
        top.Children.Add(new TextBlock
        {
            Text = tr("Estas reglas van como mensaje de sistema en cada consulta, junto con el índice de memorias.",
                      "These rules go as the system message of every request, together with the memory index."),
            FontSize = 12, Foreground = Theme.Secondary, TextWrapping = TextWrapping.Wrap,
        });
        top.Children.Add(new TextBlock { Text = Paths.Short(Paths.Rules), FontFamily = Theme.Mono, FontSize = 11.5, Foreground = Theme.Secondary, Margin = new Thickness(0, 4, 0, 8) });
        DockPanel.SetDock(top, Dock.Top);
        panel.Children.Add(top);

        var status = new TextBlock { FontSize = 12, Foreground = Theme.Secondary, VerticalAlignment = VerticalAlignment.Center };
        var restore = new Button { Content = tr("Restaurar las originales", "Restore defaults"), Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0) };
        revertRules = new Button { Content = tr("Descartar cambios", "Revert"), Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0) };
        saveRules = new Button { Content = tr("Guardar", "Save"), Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0), ToolTip = "Ctrl+S" };
        rulesBox = new TextBox
        {
            Text = Rules.Load().Replace("\n", Environment.NewLine), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            FontFamily = Theme.Mono, FontSize = 12.5, Padding = new Thickness(10), VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        void SetDirty(bool dirty)
        {
            rulesDirty = dirty;
            saveRules.IsEnabled = revertRules.IsEnabled = dirty;
        }
        rulesBox.TextChanged += (s, e) => SetDirty(true);
        restore.Click += (s, e) => { rulesBox.Text = Rules.DefaultText.Replace("\n", Environment.NewLine); SetDirty(true); };
        revertRules.Click += (s, e) => { rulesBox.Text = Rules.Load().Replace("\n", Environment.NewLine); SetDirty(false); };
        saveRules.Click += (s, e) => SaveRules();
        rulesStatus = status;

        var bottom = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Children = { restore, revertRules, saveRules } };
        DockPanel.SetDock(buttons, Dock.Right);
        bottom.Children.Add(buttons);
        bottom.Children.Add(status);
        DockPanel.SetDock(bottom, Dock.Bottom);
        panel.Children.Add(bottom);
        panel.Children.Add(rulesBox);
        SetDirty(false);
        return panel;
    }

    private TextBlock rulesStatus;

    private void SaveRules()
    {
        try
        {
            Rules.Save(rulesBox.Text.Replace("\r\n", "\n"));
            rulesStatus.Text = tr("Reglas guardadas.", "Rules saved.");
            rulesDirty = false;
            saveRules.IsEnabled = revertRules.IsEnabled = false;
        }
        catch (Exception ex) { rulesStatus.Text = ex.Message; }
    }
}
