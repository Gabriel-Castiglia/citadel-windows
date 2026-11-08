//
//  Dialogs.cs
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

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static Citadel.Loc;

namespace Citadel;

/// <summary>Small dark dialogs: a confirmation and a one-line text prompt.</summary>
internal static class Dialogs
{
    private static Window Make(Window owner, string title)
    {
        return new Window
        {
            Title = title, Owner = owner, Width = 420, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            Background = Theme.Window, Foreground = Theme.Text, FontFamily = Theme.Ui, ShowInTaskbar = false,
        };
    }

    private static StackPanel Buttons(Window w, string okText, bool destructive, Func<bool> onOk)
    {
        var ok = new Button { Content = okText, IsDefault = true, MinWidth = 90, Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(8, 0, 0, 0) };
        if (destructive) ok.Foreground = Theme.Warning;
        ok.Click += (s, e) => { if (onOk()) w.DialogResult = true; };
        var cancel = new Button { Content = tr("Cancelar", "Cancel"), IsCancel = true, MinWidth = 90, Padding = new Thickness(14, 5, 14, 5) };
        return new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0), Children = { cancel, ok } };
    }

    public static bool Confirm(Window owner, string title, string message, string okText)
    {
        var w = Make(owner, "Citadel");
        var panel = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = message, FontSize = 13, Foreground = Theme.Secondary, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
        panel.Children.Add(Buttons(w, okText, true, () => true));
        w.Content = panel;
        return w.ShowDialog() == true;
    }

    /// <summary>Asks for one line of text; returns null when canceled.</summary>
    public static string Prompt(Window owner, string title, string label, string initial, string okText)
    {
        var w = Make(owner, title);
        var box = new TextBox { Text = initial, FontSize = 13.5, Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 6, 0, 0) };
        var panel = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = label, FontSize = 12.5, Foreground = Theme.Secondary, Margin = new Thickness(0, 10, 0, 0) });
        panel.Children.Add(box);
        panel.Children.Add(Buttons(w, okText, false, () => box.Text.Trim().Length > 0));
        w.Content = panel;
        w.Loaded += (s, e) => { box.Focus(); box.SelectAll(); };
        return w.ShowDialog() == true ? box.Text.Trim() : null;
    }
}

/// <summary>Review of a memory drafted by /save: file name, index line and text, all editable.</summary>
internal sealed class MemoryPreviewWindow : Window
{
    public MemoryPreviewWindow(ChatModel model, MemoryDraft draft)
    {
        Title = tr("Guardar memoria", "Save memory");
        Width = 700;
        Height = 620;
        MinWidth = 560;
        MinHeight = 440;
        Background = Theme.Window;
        Foreground = Theme.Text;
        FontFamily = Theme.Ui;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var file = new TextBox { Text = draft.File, FontSize = 13.5, Padding = new Thickness(6, 4, 6, 4) };
        var index = new TextBox { Text = draft.IndexLine, FontSize = 13.5, Padding = new Thickness(6, 4, 6, 4) };
        var content = new TextBox
        {
            Text = draft.Content.Replace("\n", Environment.NewLine), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            FontFamily = Theme.Mono, FontSize = 13, Padding = new Thickness(10), VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        var warning = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var warningText = new TextBlock { FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Theme.Warning, Margin = new Thickness(8, 0, 0, 0) };
        warning.Children.Add(Theme.Icon(Theme.GlyphWarning, 13, Theme.Warning));
        warning.Children.Add(warningText);
        var lines = new TextBlock { FontSize = 11.5, Foreground = Theme.Secondary, VerticalAlignment = VerticalAlignment.Center };
        var error = new TextBlock { FontSize = 12, Foreground = Theme.Warning, TextWrapping = TextWrapping.Wrap };

        void Refresh()
        {
            string name = MemoryStore.SanitizeFileName(file.Text);
            bool exists = MemoryStore.Exists(name);
            warning.Visibility = exists ? Visibility.Visible : Visibility.Collapsed;
            warningText.Text = tr($"Ya existe {name}: se va a reemplazar.", $"{name} already exists: it will be replaced.");
            lines.Text = content.Text.Split('\n').Length + " " + tr("líneas", "lines");
        }
        file.TextChanged += (s, e) => Refresh();
        content.TextChanged += (s, e) => Refresh();

        var grid = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition());
        void Row(int r, string label, UIElement field)
        {
            var l = new TextBlock { Text = label, Foreground = Theme.Secondary, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 8) };
            Grid.SetRow(l, r);
            grid.Children.Add(l);
            if (field is FrameworkElement f) f.Margin = new Thickness(0, 0, 0, 8);
            Grid.SetRow(field, r);
            Grid.SetColumn(field, 1);
            grid.Children.Add(field);
        }
        Row(0, tr("Archivo", "File"), file);
        Row(1, tr("Índice", "Index"), index);

        var save = new Button { Content = tr("Guardar", "Save"), IsDefault = true, MinWidth = 90, Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(8, 0, 0, 0) };
        var cancel = new Button { Content = tr("Cancelar", "Cancel"), IsCancel = true, MinWidth = 90, Padding = new Thickness(14, 5, 14, 5) };
        save.Click += (s, e) =>
        {
            var d = new MemoryDraft { File = file.Text, IndexLine = index.Text, Content = content.Text.Replace("\r\n", "\n") };
            if (model.ConfirmMemory(d, out string message)) Close();
            else error.Text = message;
        };
        cancel.Click += (s, e) => { model.PendingDraft = null; Close(); };

        var bottom = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Children = { cancel, save } };
        DockPanel.SetDock(buttons, Dock.Right);
        bottom.Children.Add(buttons);
        bottom.Children.Add(lines);

        var top = new StackPanel();
        top.Children.Add(new TextBlock { Text = tr("Guardar memoria", "Save memory"), FontSize = 18, FontWeight = FontWeights.SemiBold });
        top.Children.Add(new TextBlock { Text = Paths.Short(MemoryStore.Folder), FontSize = 11.5, Foreground = Theme.Secondary, Margin = new Thickness(0, 4, 0, 0) });
        top.Children.Add(grid);
        top.Children.Add(warning);
        top.Children.Add(error);

        var root = new DockPanel { Margin = new Thickness(22, 18, 22, 18) };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(bottom);
        content.Margin = new Thickness(0, 12, 0, 0);
        root.Children.Add(content);
        Content = root;
        Refresh();
    }
}
