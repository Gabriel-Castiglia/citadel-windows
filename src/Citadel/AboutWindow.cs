//
//  AboutWindow.cs
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
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media.Imaging;
using static Citadel.Loc;

namespace Citadel;

/// <summary>"About Citadel": version, author, copyright, license and repository.</summary>
internal sealed class AboutWindow : Window
{
    private const string Author = "Gabriel Castiglia";
    private const string LicenseUrl = "https://www.gnu.org/licenses/old-licenses/gpl-2.0.html";
    public const string Repository = "https://github.com/Gabriel-Castiglia/citadel-windows";

    public AboutWindow()
    {
        Width = 460;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Theme.Window;
        Foreground = Theme.Text;
        FontFamily = Theme.Ui;
        FontSize = 13;
        Build();
    }

    public static string VersionText
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v == null ? "" : $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";
        }
    }

    private void Build()
    {
        Title = tr("Acerca de Citadel", "About Citadel");
        var root = new StackPanel { Margin = new Thickness(28, 26, 28, 22) };
        root.Children.Add(new Image { Source = Theme.AppIcon, Width = 96, Height = 96 });
        root.Children.Add(new TextBlock { Text = "Citadel", FontSize = 24, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 2) });
        root.Children.Add(Centered(tr("Versión ", "Version ") + VersionText, Theme.Secondary));
        root.Children.Add(Centered(tr("Chat con modelos de lenguaje locales servidos por Ollama.", "Chat with local language models served by Ollama."), null, 8));
        root.Children.Add(new Border { Height = 1, Background = Theme.Stroke, Margin = new Thickness(0, 14, 0, 12) });
        root.Children.Add(Centered(tr("Autor: ", "Author: ") + Author));
        root.Children.Add(Centered("Copyright © 2026 " + Author, Theme.Secondary, 4));
        root.Children.Add(Centered(tr("Licencia: GNU GPL, solo la versión 2 (GPL-2.0-only).", "License: GNU GPL, version 2 only (GPL-2.0-only)."), Theme.Secondary, 4));
        var repo = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
        var link = new Hyperlink(new Run(Repository.Replace("https://", ""))) { NavigateUri = new Uri(Repository), Foreground = Theme.Accent };
        link.RequestNavigate += (s, e) => Open(Repository);
        repo.Inlines.Add(link);
        root.Children.Add(repo);
        root.Children.Add(Centered(tr("Este programa es software libre y se distribuye SIN NINGUNA GARANTÍA.",
                                      "This program is free software and comes with ABSOLUTELY NO WARRANTY."), Theme.Tertiary, 12));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 0) };
        var license = new Button { Content = tr("Ver licencia", "View license"), Padding = new Thickness(14, 5, 14, 5), MinWidth = 96 };
        license.Click += (s, e) => ShowLicense();
        var close = new Button { Content = tr("Cerrar", "Close"), Padding = new Thickness(14, 5, 14, 5), MinWidth = 96, Margin = new Thickness(8, 0, 0, 0), IsDefault = true, IsCancel = true };
        close.Click += (s, e) => Close();
        buttons.Children.Add(license);
        buttons.Children.Add(close);
        root.Children.Add(buttons);
        Content = root;
    }

    private static TextBlock Centered(string text, System.Windows.Media.Brush brush = null, double top = 0) => new()
    {
        Text = text, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
        Foreground = brush ?? Theme.Text, Margin = new Thickness(0, top, 0, 0), FontSize = brush == Theme.Tertiary ? 11.5 : 13,
    };

    /// <summary>Opens the LICENSE.txt shipped next to the executable; falls back to the GNU website.</summary>
    private static void ShowLicense()
    {
        string file = Path.Combine(AppContext.BaseDirectory, "LICENSE.txt");
        Open(File.Exists(file) ? file : LicenseUrl);
    }

    private static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch { }
    }
}
