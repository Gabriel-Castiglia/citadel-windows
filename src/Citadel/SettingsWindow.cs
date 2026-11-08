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
        Closed += (s, e) => Loc.Changed -= Build;
        PreviewKeyDown += (s, e) =>
        {
            if (e.Key == System.Windows.Input.Key.S && (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0 && rulesDirty)
            {
                SaveRules();
                e.Handled = true;
            }
        };
    }

    /// <summary>Shows the given tab: 0 for Memories, 1 for Rules.</summary>
    public int Tab { set => tabs.SelectedIndex = value; }

    private void Build()
    {
        Title = tr("Ajustes de Citadel", "Citadel Settings");
        int selected = tabs?.SelectedIndex ?? 0;
        tabs = new TabControl { Margin = new Thickness(14, 10, 14, 14), Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        tabs.Items.Add(new TabItem { Header = tr("Memorias", "Memories"), Content = MemoriesTab() });
        tabs.Items.Add(new TabItem { Header = tr("Reglas", "Rules"), Content = RulesTab() });
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
