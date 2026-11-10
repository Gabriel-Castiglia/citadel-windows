//
//  Theme.cs
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
using System.Windows.Input;
using System.Windows.Media;
using static Citadel.Loc;

namespace Citadel;

/// <summary>Colors, fonts and small building blocks shared by every window. The appearance (as Windows,
/// light or dark) and the accent colour are chosen in Settings → Appearance; every window rebuilds
/// itself when they change (Changed), as it does for the language.</summary>
internal static class Theme
{
    /// <summary>The twelve accent colours, as in Wyrmhold, Gryphmark and Citadel for the Mac: a deeper tone
    /// for light windows and a brighter one for dark ones. Each has its icon in accents/&lt;key&gt;.png
    /// (the glow behind the citadel), shown on the window and the taskbar while Citadel runs.</summary>
    public sealed record AccentInfo(string Key, string Es, string En, uint Light, uint Dark)
    {
        public string Name => tr(Es, En);
    }

    public static readonly AccentInfo[] Accents =
    [
        new("green", "Verde", "Green", 0x2A9D57, 0x3CCF75),
        new("gold", "Dorado", "Gold", 0x9A6F00, 0xE2B84C),
        new("purple", "Morado", "Purple", 0x5B2A86, 0xB392E0),
        new("sky", "Celeste", "Sky blue", 0x1A8CD8, 0x5CB8F0),
        new("ember", "Naranja", "Orange", 0xD9652F, 0xF08A5D),
        new("red", "Rojo", "Red", 0xC8102E, 0xF2545B),
        new("yellow", "Amarillo", "Yellow", 0xB88A00, 0xF5C842),
        new("teal", "Verde azulado", "Teal", 0x00796B, 0x4DB6AC),
        new("blue", "Azul", "Blue", 0x1F5FD1, 0x6E9BFF),
        new("pink", "Rosa", "Pink", 0xC2185B, 0xF06292),
        new("black", "Negro", "Black", 0x1D1D1F, 0xC7C7CC),
        new("white", "Blanco", "White", 0x8E8E93, 0xF5F5F7),
    ];

    /// <summary>Raised after the appearance or the accent changes.</summary>
    public static event Action Changed;

    public static void SetAppearance(string appearance) { Settings.Current.Appearance = appearance; Settings.Current.Save(); ApplyAppMode(); Changed?.Invoke(); }
    public static void SetAccent(string key) { Settings.Current.Accent = key; Settings.Current.Save(); Changed?.Invoke(); }

    /// <summary>Called when Windows switches between light and dark: matters only for "as Windows".</summary>
    public static void SystemChanged() { if (Settings.Current.Appearance == "auto") { ApplyAppMode(); Changed?.Invoke(); } }

    /// <summary>The Fluent style of the standard controls (text boxes, scroll bars, tabs) follows the theme.</summary>
    public static void ApplyAppMode()
    {
        if (Application.Current != null) Application.Current.ThemeMode = Dark ? ThemeMode.Dark : ThemeMode.Light;
    }

    public static AccentInfo CurrentAccent => Accents.FirstOrDefault(a => a.Key == Settings.Current.Accent) ?? Accents[0];

    /// <summary>Dark until 1.1.0, so dark stays the default; "auto" follows the Windows app setting.</summary>
    public static bool Dark => (Settings.Current.Appearance ?? "dark") switch
    {
        "light" => false,
        "auto" => SystemDark(),
        _ => true,
    };

    private static bool SystemDark()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return true; }
    }

    private static Brush Pick(uint dark, uint light) => Frozen(Rgb(Dark ? dark : light));
    private static Color Rgb(uint v) => Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);

    public static Brush Window => Pick(0x202020, 0xF7F7F7);
    public static Brush Sidebar => Pick(0x1A1A1B, 0xEDEDEE);
    public static Brush Card => Pick(0x2B2B2B, 0xE6E6E7);
    public static Brush CardHover => Pick(0x333333, 0xDCDCDD);
    public static Brush Code => Pick(0x161616, 0xEFEFF0);
    public static Brush Stroke => Pick(0x3A3A3A, 0xD2D2D4);
    public static Brush Divider => Pick(0x101010, 0xD2D2D4);
    public static Brush Text => Pick(0xF2F2F2, 0x1B1B1C);
    public static Brush Secondary => Pick(0xA8A8A8, 0x5A5A5E);
    public static Brush Tertiary => Pick(0x747474, 0x8A8A8E);
    public static Brush Warning => Pick(0xF29D38, 0xB25C00);

    public static readonly FontFamily Ui = new("Segoe UI Variable Text, Segoe UI");
    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas");
    /// <summary>Windows 11 icon font, with the Windows 10 one as fallback.</summary>
    public static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    /// <summary>The chosen accent colour, in its tone for the current appearance.</summary>
    public static Color AccentColor => Rgb(Dark ? CurrentAccent.Dark : CurrentAccent.Light);

    public static Brush Accent => Frozen(AccentColor);
    public static Brush AccentSoft => Frozen(Color.FromArgb((byte)(Dark ? 0x55 : 0x33), AccentColor.R, AccentColor.G, AccentColor.B));

    /// <summary>Black or white text, whichever reads better on the accent color.</summary>
    public static Brush OnAccent
    {
        get
        {
            var c = AccentColor;
            return 0.299 * c.R + 0.587 * c.G + 0.114 * c.B > 150 ? Brushes.Black : Brushes.White;
        }
    }

    /// <summary>The icon in the accent colour (256 pixels).</summary>
    public static ImageSource AccentIcon(string key = null) =>
        System.Windows.Media.Imaging.BitmapFrame.Create(new Uri($"pack://application:,,,/accents/{key ?? CurrentAccent.Key}.png"));

    /// <summary>Colours, icon and title bar of a window for the current theme.</summary>
    public static void Apply(Window w)
    {
        w.Background = Window;
        w.Foreground = Text;
        try { w.Icon = AccentIcon(); } catch { }
        var hwnd = new System.Windows.Interop.WindowInteropHelper(w).Handle;
        if (hwnd == IntPtr.Zero) { w.SourceInitialized += (s, e) => DarkTitleBar(w); return; }
        DarkTitleBar(w);
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private static void DarkTitleBar(Window w)
    {
        int on = Dark ? 1 : 0;
        try { DwmSetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(w).Handle, 20, ref on, sizeof(int)); } catch { }
    }

    /// <summary>The largest image of citadel.ico (WPF would otherwise pick the 16-pixel one and blur it).</summary>
    public static ImageSource AppIcon
    {
        get
        {
            var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(new Uri("pack://application:,,,/citadel.ico"),
                System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
            return decoder.Frames.OrderByDescending(f => f.PixelWidth).First();
        }
    }

    public static Brush Frozen(byte r, byte g, byte b) => Frozen(Color.FromRgb(r, g, b));

    public static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    // Segoe Fluent Icons code points.
    public const string GlyphEdit = "\uE70F", GlyphSearch = "\uE721", GlyphSettings = "\uE713", GlyphGlobe = "\uE774",
        GlyphInfo = "\uE946", GlyphSend = "\uE724", GlyphStop = "\uE71A", GlyphCopy = "\uE8C8", GlyphCheck = "\uE73E",
        GlyphBulb = "\uE82F", GlyphChevronRight = "\uE76C", GlyphChevronDown = "\uE70D", GlyphBookmark = "\uE8F1",
        GlyphDocument = "\uE8A5", GlyphTrash = "\uE74D", GlyphFolder = "\uE8B7",
        GlyphRefresh = "\uE72C", GlyphWarning = "\uE7BA", GlyphClose = "\uE711", GlyphDownload = "\uE896";

    public static TextBlock Icon(string glyph, double size = 14, Brush brush = null) => new()
    {
        Text = glyph, FontFamily = Icons, FontSize = size, Foreground = brush ?? Secondary,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>A flat button: icon and optional label, rounded, with a hover background.</summary>
    public static Button FlatButton(string glyph, string label, Action click, string tooltip = null, double size = 13)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        if (glyph != null) row.Children.Add(Icon(glyph, size + 1));
        if (label != null)
            row.Children.Add(new TextBlock
            {
                Text = label, FontSize = size, Foreground = Text, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(glyph != null ? 8 : 0, 0, 0, 0),
            });
        var button = new Button
        {
            Content = row, Cursor = Cursors.Hand, ToolTip = tooltip, Focusable = false,
            Template = FlatTemplate(), Padding = new Thickness(8, 5, 8, 5), Background = Brushes.Transparent,
        };
        button.Click += (s, e) => click?.Invoke();
        return button;
    }

    /// <summary>Template with a rounded background that lights up under the pointer.</summary>
    public static ControlTemplate FlatTemplate(double radius = 6)
    {
        var border = new FrameworkElementFactory(typeof(Border), "bg");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        border.AppendChild(presenter);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, Frozen(Dark ? Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x14, 0, 0, 0)), "bg"));
        template.Triggers.Add(hover);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45));
        template.Triggers.Add(disabled);
        return template;
    }

    /// <summary>A rounded chip with an icon and a short text.</summary>
    public static Border Chip(string glyph, string text) => new()
    {
        CornerRadius = new CornerRadius(12), Background = Card, Padding = new Thickness(10, 4, 12, 4),
        HorizontalAlignment = HorizontalAlignment.Left,
        Child = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                Icon(glyph, 12, Accent),
                new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Text, Margin = new Thickness(6, 0, 0, 0) },
            },
        },
    };
}
