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

namespace Citadel;

/// <summary>Colors, fonts and small building blocks shared by every window. Dark only.</summary>
internal static class Theme
{
    public static readonly Brush Window = Frozen(0x20, 0x20, 0x20);
    public static readonly Brush Sidebar = Frozen(0x1A, 0x1A, 0x1B);
    public static readonly Brush Card = Frozen(0x2B, 0x2B, 0x2B);
    public static readonly Brush CardHover = Frozen(0x33, 0x33, 0x33);
    public static readonly Brush Code = Frozen(0x16, 0x16, 0x16);
    public static readonly Brush Stroke = Frozen(0x3A, 0x3A, 0x3A);
    public static readonly Brush Text = Frozen(0xF2, 0xF2, 0xF2);
    public static readonly Brush Secondary = Frozen(0xA8, 0xA8, 0xA8);
    public static readonly Brush Tertiary = Frozen(0x74, 0x74, 0x74);
    public static readonly Brush Warning = Frozen(0xF2, 0x9D, 0x38);

    public static readonly FontFamily Ui = new("Segoe UI Variable Text, Segoe UI");
    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas");
    /// <summary>Windows 11 icon font, with the Windows 10 one as fallback.</summary>
    public static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    /// <summary>The Windows accent color, lightened for a dark background (green when none is set).</summary>
    public static Color AccentColor
    {
        get
        {
            try
            {
                var c = SystemColors.AccentColorLight2;
                if (c.A > 0) return c;
            }
            catch { }
            return Color.FromRgb(0x6C, 0xCB, 0x5F);
        }
    }

    public static Brush Accent => Frozen(AccentColor);
    public static Brush AccentSoft => Frozen(Color.FromArgb(0x55, AccentColor.R, AccentColor.G, AccentColor.B));

    /// <summary>Black or white text, whichever reads better on the accent color.</summary>
    public static Brush OnAccent
    {
        get
        {
            var c = AccentColor;
            return 0.299 * c.R + 0.587 * c.G + 0.114 * c.B > 150 ? Brushes.Black : Brushes.White;
        }
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
        GlyphDocument = "\uE8A5", GlyphTranslate = "\uE8C1", GlyphTrash = "\uE74D", GlyphFolder = "\uE8B7",
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
        button.Click += (s, e) => click();
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
        hover.Setters.Add(new Setter(Border.BackgroundProperty, Frozen(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)), "bg"));
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
