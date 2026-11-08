//
//  MainWindow.cs
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
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using static Citadel.Loc;

namespace Citadel;

/// <summary>The main window: conversations on the left, the chat on the right.</summary>
internal sealed class MainWindow : Window
{
    private readonly ChatModel model;

    // Sidebar
    private readonly StackPanel conversationList = new();
    private TextBox searchBox;

    // Chat
    private TextBlock headerTitle;
    private ComboBox modelPicker;
    private bool updatingPicker;
    private readonly StackPanel messagesPanel = new() { Margin = new Thickness(0, 22, 0, 22) };
    private ScrollViewer messagesScroll;
    private Grid chatArea;
    private readonly Dictionary<Guid, AssistantView> answerViews = [];
    private Guid? shownConversation;
    private int shownCount = -1;
    private bool welcomeShown;

    // Composer
    private TextBox composer;
    private TextBlock placeholder;
    private readonly WrapPanel attachmentPanel = new() { Margin = new Thickness(0, 0, 0, 6) };
    private Button reasonButton, sendButton;
    private TextBlock reasonHint;
    private Ellipse statusDot;
    private TextBlock statusText;
    private Border toast;
    private readonly DispatcherTimer toastTimer = new() { Interval = TimeSpan.FromSeconds(3) };

    // Throttles redraws of a streaming answer.
    private readonly DispatcherTimer streamTimer = new() { Interval = TimeSpan.FromMilliseconds(90) };
    private readonly HashSet<ChatMessage> dirtyAnswers = [];

    public MainWindow(ChatModel model)
    {
        this.model = model;
        Title = "Citadel";
        MinWidth = 820;
        MinHeight = 540;
        Width = 1180;
        Height = 760;
        Background = Theme.Window;
        FontFamily = Theme.Ui;
        Foreground = Theme.Text;
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/citadel.ico"));
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        RestoreSavedBounds();

        model.Changed += OnModelChanged;
        model.StatusChanged += RefreshStatus;
        model.MessageChanged += m => { dirtyAnswers.Add(m); if (!streamTimer.IsEnabled) streamTimer.Start(); };
        model.DraftReady += ShowDraft;
        model.Toast += ShowToast;
        streamTimer.Tick += (s, e) =>
        {
            streamTimer.Stop();
            foreach (var m in dirtyAnswers) if (answerViews.TryGetValue(m.Id, out var v)) v.Update(model.IsGenerating);
            dirtyAnswers.Clear();
            ScrollToEnd();
        };
        toastTimer.Tick += (s, e) => { toastTimer.Stop(); toast.Visibility = Visibility.Collapsed; };
        Loc.Changed += Build;
        Closing += (s, e) => SaveBounds();
        Closed += (s, e) => Loc.Changed -= Build;
        PreviewKeyDown += OnKey;
        Build();
    }

    // Window bounds

    private void RestoreSavedBounds()
    {
        var b = Settings.Current.Window;
        if (b is not { Length: 4 }) return;
        var area = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var r = new Rect(b[0], b[1], Math.Max(MinWidth, b[2]), Math.Max(MinHeight, b[3]));
        if (!area.IntersectsWith(r)) return;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = r.Left; Top = r.Top; Width = r.Width; Height = r.Height;
    }

    private void SaveBounds()
    {
        var r = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        Settings.Current.Window = [r.Left, r.Top, r.Width, r.Height];
        Settings.Current.Save();
    }

    // Layout

    private void Build()
    {
        answerViews.Clear();
        shownConversation = null;
        shownCount = -1;
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(264) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        root.ColumnDefinitions.Add(new ColumnDefinition());
        var sidebar = BuildSidebar();
        root.Children.Add(sidebar);
        var line = new Border { Background = Brushes.Black, Opacity = 0.5 };
        Grid.SetColumn(line, 1);
        root.Children.Add(line);
        var chat = BuildChat();
        Grid.SetColumn(chat, 2);
        root.Children.Add(chat);
        Content = root;
        OnModelChanged();
    }

    private UIElement BuildSidebar()
    {
        var panel = new DockPanel { Background = Theme.Sidebar };

        var top = new DockPanel { Margin = new Thickness(16, 14, 10, 10) };
        var newButton = Theme.FlatButton(Theme.GlyphEdit, null, model.NewConversation, tr("Conversación nueva (Ctrl+N)", "New conversation (Ctrl+N)"), 15);
        DockPanel.SetDock(newButton, Dock.Right);
        top.Children.Add(newButton);
        top.Children.Add(new TextBlock { Text = "Citadel", FontSize = 16, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        DockPanel.SetDock(top, Dock.Top);
        panel.Children.Add(top);

        searchBox = new TextBox
        {
            Text = model.Search, FontSize = 13, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Foreground = Theme.Text, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(0),
            CaretBrush = Theme.Text,
        };
        var searchHint = new TextBlock { Text = tr("Buscar", "Search"), FontSize = 13, Foreground = Theme.Tertiary, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center };
        searchBox.TextChanged += (s, e) =>
        {
            searchHint.Visibility = searchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            model.Search = searchBox.Text;
            RefreshSidebar();
        };
        searchHint.Visibility = searchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var searchGrid = new Grid();
        searchGrid.Children.Add(searchHint);
        searchGrid.Children.Add(searchBox);
        var search = new Border
        {
            CornerRadius = new CornerRadius(8), Background = Theme.Card, Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(12, 0, 12, 10),
            Child = new DockPanel { Children = { Theme.Icon(Theme.GlyphSearch, 12), Spaced(searchGrid, 8) } },
        };
        DockPanel.SetDock(search, Dock.Top);
        panel.Children.Add(search);

        var bottom = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 6, 10, 12) };
        bottom.Children.Add(Theme.FlatButton(Theme.GlyphSettings, tr("Ajustes", "Settings"), OpenSettings, "Ctrl+,"));
        var language = Theme.FlatButton(Theme.GlyphGlobe, null, null, "Idioma / Language");
        language.Click += (s, e) => LanguageMenu(language);
        bottom.Children.Add(language);
        bottom.Children.Add(Theme.FlatButton(Theme.GlyphInfo, null, OpenAbout, tr("Acerca de Citadel", "About Citadel")));
        DockPanel.SetDock(bottom, Dock.Bottom);
        panel.Children.Add(bottom);
        var separator = new Border { Height = 1, Background = Theme.Stroke, Margin = new Thickness(12, 0, 12, 0) };
        DockPanel.SetDock(separator, Dock.Bottom);
        panel.Children.Add(separator);

        panel.Children.Add(new ScrollViewer
        {
            Content = conversationList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(8, 0, 8, 8),
        });
        return panel;
    }

    private static UIElement Spaced(UIElement e, double left)
    {
        if (e is FrameworkElement f) f.Margin = new Thickness(left, 0, 0, 0);
        return e;
    }

    private void LanguageMenu(Button anchor)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Top };
        menu.Items.Add(new MenuItem { Header = "Idioma / Language", IsEnabled = false });
        foreach (var (lang, name) in new[] { (UiLanguage.En, "English"), (UiLanguage.Es, "Español") })
        {
            var item = new MenuItem { Header = name, IsCheckable = true, IsChecked = Loc.Current == lang };
            item.Click += (s, e) => SetLanguage(lang);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private static void SetLanguage(UiLanguage lang)
    {
        Loc.Current = lang;
        Settings.Current.Save();
    }

    private UIElement BuildChat()
    {
        chatArea = new Grid { Background = Theme.Window };
        chatArea.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        chatArea.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        chatArea.RowDefinitions.Add(new RowDefinition());
        chatArea.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Header: title and model picker.
        var header = new DockPanel { Margin = new Thickness(22, 10, 16, 10), Height = 34 };
        modelPicker = new ComboBox { MinWidth = 200, FontSize = 12.5, ToolTip = tr("Modelo de Ollama", "Ollama model"), VerticalAlignment = VerticalAlignment.Center };
        modelPicker.DropDownOpened += async (s, e) => await model.RefreshStatus();
        modelPicker.SelectionChanged += (s, e) =>
        {
            if (!updatingPicker && modelPicker.SelectedItem is string name && name != model.SelectedModel) model.SelectedModel = name;
        };
        DockPanel.SetDock(modelPicker, Dock.Right);
        header.Children.Add(modelPicker);
        headerTitle = new TextBlock { FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        header.Children.Add(headerTitle);
        chatArea.Children.Add(header);
        var rule = new Border { Height = 1, Background = Theme.Stroke, Opacity = 0.6 };
        Grid.SetRow(rule, 1);
        chatArea.Children.Add(rule);

        messagesScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = Centered(messagesPanel, 860, 32) };
        Grid.SetRow(messagesScroll, 2);
        chatArea.Children.Add(messagesScroll);

        toast = new Border
        {
            CornerRadius = new CornerRadius(14), Background = Theme.CardHover, Padding = new Thickness(14, 7, 14, 7),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 14, 0, 0),
            Visibility = Visibility.Collapsed, Child = new TextBlock { FontSize = 12.5, Foreground = Theme.Text },
        };
        Grid.SetRow(toast, 2);
        chatArea.Children.Add(toast);

        var composerArea = BuildComposer();
        Grid.SetRow(composerArea, 3);
        chatArea.Children.Add(composerArea);
        return chatArea;
    }

    /// <summary>Centers content in a column of at most <paramref name="width"/> pixels.</summary>
    private static UIElement Centered(FrameworkElement content, double width, double side)
    {
        // A stretched element limited by MaxWidth is centered in its slot.
        content.MaxWidth = width;
        content.HorizontalAlignment = HorizontalAlignment.Stretch;
        return new Border { Padding = new Thickness(side, 0, side, 0), Child = content };
    }

    private UIElement BuildComposer()
    {
        composer = new TextBox
        {
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontSize = 14.5, MinHeight = 26, MaxHeight = 200,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Foreground = Theme.Text, CaretBrush = Theme.Accent, Padding = new Thickness(2, 4, 2, 4), AllowDrop = true,
            Text = model.Composer,
        };
        placeholder = new TextBlock
        {
            Text = tr("Escribe un mensaje…  (/mem tema · /save)", "Message…  (/mem topic · /save)"), FontSize = 14.5,
            Foreground = Theme.Tertiary, IsHitTestVisible = false, Margin = new Thickness(4, 4, 0, 0),
        };
        composer.TextChanged += (s, e) =>
        {
            model.Composer = composer.Text;
            placeholder.Visibility = composer.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            RefreshComposerButtons();
        };
        composer.PreviewKeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & (ModifierKeys.Shift | ModifierKeys.Alt)) == 0 && !e.IsRepeat)
            {
                e.Handled = true;
                model.Send();
            }
            else if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Alt) != 0)
            {
                e.Handled = true;
                int at = composer.CaretIndex;
                composer.Text = composer.Text.Insert(at, Environment.NewLine);
                composer.CaretIndex = at + Environment.NewLine.Length;
            }
        };
        composer.PreviewDragOver += (s, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Any(ChatModel.IsTextFile))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
        };
        composer.PreviewDrop += (s, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            {
                foreach (string f in files) model.AttachFile(f);
                e.Handled = true;
            }
        };
        placeholder.Visibility = composer.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var textGrid = new Grid();
        textGrid.Children.Add(placeholder);
        textGrid.Children.Add(composer);

        reasonButton = new Button { Template = Theme.FlatTemplate(13), Cursor = Cursors.Hand, Focusable = false, Padding = new Thickness(10, 4, 12, 4) };
        reasonButton.Click += (s, e) => model.Reason = !model.Reason;
        reasonButton.ToolTip = tr("Más lento (unos 20 a 90 s) pero mucho más preciso en cuentas, conteos, lógica y fechas.",
                                  "Slower (about 20 to 90 s) but much more accurate on math, counting, logic and dates.");
        var translate = Theme.FlatButton(Theme.GlyphTranslate, tr("Traducir", "Translate"), null, null, 12.5);
        translate.Click += (s, e) =>
        {
            var menu = new ContextMenu { PlacementTarget = translate, Placement = PlacementMode.Top };
            var en = new MenuItem { Header = ChatModel.ToEnglish };
            en.Click += (o, a) => { model.Translate(true); FocusComposerEnd(); };
            var es = new MenuItem { Header = ChatModel.ToSpanish };
            es.Click += (o, a) => { model.Translate(false); FocusComposerEnd(); };
            menu.Items.Add(en);
            menu.Items.Add(es);
            menu.IsOpen = true;
        };
        reasonHint = new TextBlock
        {
            Text = tr("Más lento (20 a 90 s), mucho más preciso en cuentas, conteos, lógica y fechas.",
                      "Slower (20-90 s), much more accurate on math, counting, logic and dates."),
            FontSize = 11.5, Foreground = Theme.Secondary, VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(10, 0, 8, 0),
        };
        sendButton = new Button { Template = Theme.FlatTemplate(15), Width = 30, Height = 30, Cursor = Cursors.Hand, Focusable = false };
        sendButton.Click += (s, e) => { if (model.IsGenerating || model.IsDraftingMemory) model.Stop(); else model.Send(); };

        var tools = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(sendButton, Dock.Right);
        tools.Children.Add(sendButton);
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(reasonButton);
        left.Children.Add(Spaced(translate, 6));
        DockPanel.SetDock(left, Dock.Left);
        tools.Children.Add(left);
        tools.Children.Add(reasonHint);

        var box = new StackPanel();
        box.Children.Add(attachmentPanel);
        box.Children.Add(textGrid);
        box.Children.Add(tools);
        var card = new Border
        {
            CornerRadius = new CornerRadius(16), Background = Theme.Card, BorderBrush = Theme.Stroke, BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 10, 10, 10), Child = box,
        };

        statusDot = new Ellipse { Width = 7, Height = 7, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        statusText = new TextBlock { FontSize = 11.5, Foreground = Theme.Secondary, TextTrimming = TextTrimming.CharacterEllipsis };
        var status = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 7, 0, 0), Children = { statusDot, statusText } };

        var stack = new StackPanel();
        stack.Children.Add(card);
        stack.Children.Add(status);
        stack.MaxWidth = 860;
        return new Border { Padding = new Thickness(24, 4, 24, 12), Child = stack };
    }

    private void FocusComposerEnd()
    {
        composer.Focus();
        composer.CaretIndex = composer.Text.Length;
    }

    // Updates

    private void OnModelChanged()
    {
        RefreshSidebar();
        headerTitle.Text = model.Current?.DisplayTitle ?? "Citadel";
        RefreshPicker();
        RefreshMessages();
        if (composer.Text != model.Composer)
        {
            composer.Text = model.Composer;
            composer.CaretIndex = composer.Text.Length;
        }
        RefreshAttachments();
        RefreshComposerButtons();
        RefreshStatus();
    }

    private void RefreshSidebar()
    {
        conversationList.Children.Clear();
        var list = model.Filtered.ToList();
        foreach (var c in list) conversationList.Children.Add(ConversationRow(c));
        if (list.Count == 0 && model.Search.Length > 0)
            conversationList.Children.Add(new TextBlock { Text = tr("Sin resultados", "No results"), FontSize = 12, Foreground = Theme.Secondary, Margin = new Thickness(12) });
    }

    private UIElement ConversationRow(Conversation c)
    {
        bool selected = c.Id == model.SelectedId;
        var text = new StackPanel();
        text.Children.Add(new TextBlock
        {
            Text = c.DisplayTitle, FontSize = 13, FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Theme.Text,
        });
        text.Children.Add(new TextBlock
        {
            Text = c.Updated.ToLocalTime().ToString(Loc.Current == UiLanguage.Es ? "d MMM, HH:mm" : "MMM d, HH:mm",
                                                    System.Globalization.CultureInfo.GetCultureInfo(Loc.Current == UiLanguage.Es ? "es-ES" : "en-US")),
            FontSize = 11, Foreground = Theme.Secondary, Margin = new Thickness(0, 2, 0, 0),
        });
        var row = new Border
        {
            CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 0, 0, 2),
            Background = selected ? Theme.AccentSoft : Brushes.Transparent, Child = text, Cursor = Cursors.Hand,
        };
        row.MouseEnter += (s, e) => { if (!selected) row.Background = Theme.Card; };
        row.MouseLeave += (s, e) => { if (!selected) row.Background = Brushes.Transparent; };
        row.MouseLeftButtonUp += (s, e) => model.Select(c.Id);
        var menu = new ContextMenu();
        var rename = new MenuItem { Header = tr("Renombrar…", "Rename…") };
        rename.Click += (s, e) =>
        {
            string title = Dialogs.Prompt(this, tr("Renombrar conversación", "Rename conversation"), tr("Título", "Title"), c.DisplayTitle, tr("Renombrar", "Rename"));
            if (title != null) model.Rename(c.Id, title);
        };
        var delete = new MenuItem { Header = tr("Eliminar…", "Delete…") };
        delete.Click += (s, e) =>
        {
            if (Dialogs.Confirm(this, tr("¿Eliminar esta conversación?", "Delete this conversation?"),
                                tr($"«{c.DisplayTitle}» se borrará del disco. No se puede deshacer.", $"“{c.DisplayTitle}” will be removed from disk. This cannot be undone."),
                                tr("Eliminar", "Delete")))
                model.Delete(c.Id);
        };
        menu.Items.Add(rename);
        menu.Items.Add(new Separator());
        menu.Items.Add(delete);
        row.ContextMenu = menu;
        return row;
    }

    private void RefreshPicker()
    {
        updatingPicker = true;
        var items = model.Models.ToList();
        if (!items.Contains(model.SelectedModel)) items.Insert(0, model.SelectedModel);
        if (!modelPicker.Items.Cast<string>().SequenceEqual(items))
        {
            modelPicker.Items.Clear();
            foreach (string m in items) modelPicker.Items.Add(m);
        }
        modelPicker.SelectedItem = model.SelectedModel;
        updatingPicker = false;
    }

    private void RefreshMessages()
    {
        var c = model.Current;
        bool empty = c == null || c.Messages.Count == 0;
        if (empty)
        {
            if (!welcomeShown)
            {
                messagesPanel.Children.Clear();
                answerViews.Clear();
                messagesPanel.Children.Add(Welcome());
                welcomeShown = true;
                shownConversation = c?.Id;
                shownCount = 0;
            }
            return;
        }
        if (welcomeShown || shownConversation != c.Id || shownCount != c.Messages.Count)
        {
            messagesPanel.Children.Clear();
            answerViews.Clear();
            foreach (var m in c.Messages) messagesPanel.Children.Add(MessageView(m, c));
            welcomeShown = false;
            shownConversation = c.Id;
            shownCount = c.Messages.Count;
            ScrollToEnd();
        }
        else
        {
            foreach (var v in answerViews.Values) v.Update(model.IsGenerating && v.Message == c.Messages[^1]);
        }
    }

    private void ScrollToEnd() => Dispatcher.BeginInvoke(new Action(() => messagesScroll.ScrollToEnd()), DispatcherPriority.Background);

    private UIElement MessageView(ChatMessage m, Conversation c)
    {
        switch (m.Role)
        {
            case Role.User:
                var userStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(80, 0, 0, 22) };
                foreach (var a in m.Attachments) userStack.Children.Add(Theme.Chip(Theme.GlyphDocument, a.Name).Also(x => { x.HorizontalAlignment = HorizontalAlignment.Right; x.Margin = new Thickness(0, 0, 0, 6); }));
                if (m.Content.Length > 0)
                    userStack.Children.Add(new Border
                    {
                        CornerRadius = new CornerRadius(16), Background = Theme.AccentSoft, Padding = new Thickness(14, 9, 14, 9),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Child = new TextBox
                        {
                            Text = m.Content, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, FontSize = 14.5, Foreground = Theme.Text,
                            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0),
                        },
                    });
                return userStack;
            case Role.Assistant:
                var view = new AssistantView(m, model);
                answerViews[m.Id] = view;
                view.Update(model.IsGenerating && m == c.Messages[^1]);
                return view.Root;
            case Role.Memory:
                return Theme.Chip(Theme.GlyphBookmark, tr("memoria cargada: ", "memory loaded: ") + m.MemoryFile).Also(x => x.Margin = new Thickness(0, 0, 0, 22));
            default:
                return new StackPanel
                {
                    Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 22),
                    Children =
                    {
                        Theme.Icon(Theme.GlyphInfo, 12),
                        new TextBlock { Text = m.Content, FontSize = 12.5, Foreground = Theme.Secondary, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 0, 0, 0) },
                    },
                };
        }
    }

    private UIElement Welcome()
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 60, 0, 0), MaxWidth = 540 };
        stack.Children.Add(new Image { Source = Theme.AppIcon, Width = 88, Height = 88 });
        stack.Children.Add(new TextBlock { Text = tr("¿En qué te ayudo?", "How can I help?"), FontSize = 26, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 8) });
        stack.Children.Add(new TextBlock
        {
            Text = tr("Todo corre en esta computadora con Ollama: nada de lo que escribas sale de ella.",
                      "Everything runs on this computer through Ollama: nothing you write leaves it."),
            FontSize = 13, Foreground = Theme.Secondary, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
        });
        var hints = new StackPanel();
        void Hint(string glyph, string text) => hints.Children.Add(new DockPanel
        {
            Margin = new Thickness(0, 4, 0, 4),
            Children = { Theme.Icon(glyph, 14, Theme.Accent).Also(i => i.Width = 24), new TextBlock { Text = text, FontSize = 12.5, Foreground = Theme.Secondary, TextWrapping = TextWrapping.Wrap } },
        });
        Hint(Theme.GlyphBulb, tr("Razonar: más lento (20 a 90 s) y mucho más preciso en cuentas, conteos, lógica y fechas.",
                                 "Reason: slower (20 to 90 s) and much more accurate on math, counting, logic and dates."));
        Hint(Theme.GlyphBookmark, tr("/mem <tema> carga una memoria guardada en la conversación.", "/mem <topic> loads a saved memory into the conversation."));
        Hint(Theme.GlyphDownload, tr("/save escribe una memoria con lo importante de esta conversación.", "/save writes a memory with what matters from this conversation."));
        Hint(Theme.GlyphDocument, tr("Arrastra un .txt o .md al cuadro de texto para adjuntarlo.", "Drag a .txt or .md file onto the text box to attach it."));
        stack.Children.Add(new Border { CornerRadius = new CornerRadius(14), Background = Theme.Sidebar, Padding = new Thickness(18, 14, 18, 14), Margin = new Thickness(0, 22, 0, 0), Child = hints });
        return stack;
    }

    private void RefreshAttachments()
    {
        attachmentPanel.Children.Clear();
        attachmentPanel.Visibility = model.Attachments.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        foreach (var a in model.Attachments.ToList())
        {
            var remove = Theme.FlatButton(Theme.GlyphClose, null, () => { model.Attachments.Remove(a); OnModelChanged(); }, tr("Quitar", "Remove"), 10);
            attachmentPanel.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 0),
                Children = { Theme.Chip(Theme.GlyphDocument, $"{a.Name} · {a.Text.Length} " + tr("caracteres", "chars")), remove },
            });
        }
    }

    private void RefreshComposerButtons()
    {
        bool on = model.Reason;
        reasonButton.Background = on ? Theme.Accent : Theme.CardHover;
        var fg = on ? Theme.OnAccent : Theme.Secondary;
        reasonButton.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                Theme.Icon(Theme.GlyphBulb, 13, fg),
                new TextBlock { Text = tr("Razonar", "Reason"), FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = fg, Margin = new Thickness(6, 0, 0, 0) },
            },
        };
        reasonHint.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        bool busy = model.IsGenerating || model.IsDraftingMemory;
        sendButton.IsEnabled = busy || model.CanSend;
        sendButton.Background = busy ? Brushes.WhiteSmoke : model.CanSend ? Theme.Accent : Theme.CardHover;
        sendButton.Content = Theme.Icon(busy ? Theme.GlyphStop : Theme.GlyphSend, 13,
                                        busy ? Brushes.Black : model.CanSend ? Theme.OnAccent : Theme.Secondary);
        sendButton.ToolTip = busy ? tr("Detener (Esc)", "Stop (Esc)") : tr("Enviar (Enter) · Nueva línea (Shift+Enter)", "Send (Enter) · New line (Shift+Enter)");
    }

    private void RefreshStatus()
    {
        statusDot.Fill = model.Reachable switch
        {
            true => model.IsGenerating || model.IsDraftingMemory ? Brushes.Gold : Theme.Accent,
            false => Theme.Warning,
            null => Theme.Tertiary,
        };
        statusText.Text = StatusText();
    }

    private string StatusText()
    {
        if (model.Reachable == null) return tr("Buscando Ollama…", "Looking for Ollama…");
        if (model.Reachable == false)
            return tr("Ollama no responde en 127.0.0.1:11434. Ábrelo desde el menú Inicio o ejecuta: ollama serve",
                      "Ollama is not reachable at 127.0.0.1:11434. Open it from the Start menu or run: ollama serve");
        int n = model.Models.Count;
        var parts = new List<string> { tr("Ollama conectado", "Ollama connected") + $" · {n} " + (n == 1 ? tr("modelo", "model") : tr("modelos", "models")) };
        string name = model.SelectedModel;
        if (model.IsDraftingMemory)
        {
            parts.Add(tr("Escribiendo la memoria…", "Writing the memory…"));
            parts.Add($"{model.Elapsed:0.0} s");
        }
        else if (model.IsGenerating)
        {
            parts.Add(!model.ReceivedFirstToken
                ? (model.ModelIsLoaded ? tr($"{name} cargado", $"{name} loaded") : tr($"cargando {name}…", $"loading {name}…"))
                : model.ReceivingThinking ? tr("razonando…", "reasoning…") : tr("escribiendo…", "writing…"));
            parts.Add($"{model.Elapsed:0.0} s");
        }
        else
        {
            parts.Add(model.ModelIsLoaded ? tr($"{name} cargado", $"{name} loaded") : tr($"{name} se carga con el primer mensaje", $"{name} loads with the first message"));
            if (model.LastStats is { } s)
            {
                if (s.TokensPerSecond > 0) parts.Add($"{s.TokensPerSecond:0.0} tok/s");
                parts.Add($"{s.Seconds:0.0} s");
            }
        }
        return string.Join(" · ", parts).Replace(',', '.');
    }

    private void ShowToast(string text)
    {
        ((TextBlock)toast.Child).Text = text;
        toast.Visibility = Visibility.Visible;
        toastTimer.Stop();
        toastTimer.Start();
    }

    private void ShowDraft(MemoryDraft draft)
    {
        var window = new MemoryPreviewWindow(model, draft) { Owner = this };
        window.ShowDialog();
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        if (ctrl && e.Key == Key.N) { model.NewConversation(); composer.Focus(); e.Handled = true; }
        else if (ctrl && e.Key == Key.OemComma) { OpenSettings(); e.Handled = true; }
        else if (e.Key == Key.Escape && (model.IsGenerating || model.IsDraftingMemory)) { model.Stop(); e.Handled = true; }
    }

    private SettingsWindow settingsWindow;

    public void OpenSettings()
    {
        if (settingsWindow is { IsLoaded: true }) { settingsWindow.Activate(); return; }
        settingsWindow = new SettingsWindow { Owner = this };
        settingsWindow.Show();
    }

    private void OpenAbout() => new AboutWindow { Owner = this }.ShowDialog();
}

/// <summary>One answer: folded reasoning, the Markdown text and a footer with Copy and the stats.</summary>
internal sealed class AssistantView
{
    public readonly ChatMessage Message;
    public readonly StackPanel Root = new() { Margin = new Thickness(0, 0, 0, 22) };
    private readonly ChatModel model;
    private readonly StackPanel thinkingBlock = new() { Margin = new Thickness(0, 0, 0, 10) };
    private readonly TextBlock thinkingTitle = new() { FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Theme.Secondary, Margin = new Thickness(6, 0, 0, 0) };
    private readonly TextBlock chevron = Theme.Icon(Theme.GlyphChevronRight, 10);
    private readonly TextBlock thinkingLast = new() { FontSize = 12, Foreground = Theme.Tertiary, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(14, 4, 0, 0) };
    private readonly TextBox thinkingText = new()
    {
        IsReadOnly = true, TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Foreground = Theme.Secondary, Background = Brushes.Transparent,
        BorderThickness = new Thickness(0), Padding = new Thickness(12, 0, 0, 0),
    };
    private readonly Border thinkingBody;
    private readonly TextBlock waiting = new() { FontSize = 12.5, Foreground = Theme.Secondary };
    private readonly RichTextBox body = new()
    {
        IsReadOnly = true, IsDocumentEnabled = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent,
        Padding = new Thickness(0), Foreground = Theme.Text,
    };
    private readonly StackPanel footer = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
    private bool expanded;
    private string renderedContent;

    public AssistantView(ChatMessage message, ChatModel model)
    {
        Message = message;
        this.model = model;
        var head = new Button
        {
            Template = Theme.FlatTemplate(4), Background = Brushes.Transparent, Cursor = Cursors.Hand, Focusable = false,
            HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(2, 2, 6, 2),
            Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { chevron, Theme.Icon(Theme.GlyphBulb, 13).Also(i => i.Margin = new Thickness(6, 0, 0, 0)), thinkingTitle } },
        };
        head.Click += (s, e) => { expanded = !expanded; Update(model.IsGenerating && IsLast); };
        thinkingBody = new Border { BorderBrush = Theme.Tertiary, BorderThickness = new Thickness(2, 0, 0, 0), Margin = new Thickness(8, 6, 0, 0), Child = thinkingText };
        thinkingBlock.Children.Add(head);
        thinkingBlock.Children.Add(thinkingLast);
        thinkingBlock.Children.Add(thinkingBody);
        Root.Children.Add(thinkingBlock);
        Root.Children.Add(waiting);
        Root.Children.Add(body);
        Root.Children.Add(footer);
    }

    private bool IsLast => model.Current?.Messages.LastOrDefault() == Message;

    public void Update(bool streaming)
    {
        var m = Message;
        bool live = streaming && m.Content.Length == 0;
        bool showThinking = m.Thinking.Length > 0 || (streaming && m.Reasoned && m.Content.Length == 0);
        thinkingBlock.Visibility = showThinking ? Visibility.Visible : Visibility.Collapsed;
        thinkingTitle.Text = live ? tr("Razonando…", "Reasoning…") : tr("Razonamiento", "Reasoning");
        chevron.Text = expanded ? Theme.GlyphChevronDown : Theme.GlyphChevronRight;
        thinkingBody.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        if (expanded && thinkingText.Text.Length != m.Thinking.Trim().Length) thinkingText.Text = m.Thinking.Trim();
        string lastLine = m.Thinking.Trim().Split('\n').LastOrDefault(l => l.Trim().Length > 0) ?? "";
        thinkingLast.Text = lastLine;
        thinkingLast.Visibility = live && !expanded && lastLine.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        waiting.Visibility = streaming && m.Content.Length == 0 && m.Thinking.Length == 0 && !m.Reasoned ? Visibility.Visible : Visibility.Collapsed;
        waiting.Text = model.ModelIsLoaded ? tr("Pensando la respuesta…", "Working on it…") : tr("Cargando el modelo…", "Loading the model…");

        body.Visibility = m.Content.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (m.Content != renderedContent)
        {
            body.Document = Markdown.Render(m.Content);
            renderedContent = m.Content;
        }

        footer.Children.Clear();
        footer.Visibility = streaming ? Visibility.Collapsed : Visibility.Visible;
        if (streaming) return;
        if (m.Content.Length > 0)
        {
            var label = new TextBlock { Text = tr("Copiar", "Copy"), FontSize = 12, Foreground = Theme.Secondary, Margin = new Thickness(6, 0, 0, 0) };
            var icon = Theme.Icon(Theme.GlyphCopy, 12);
            var copy = new Button
            {
                Template = Theme.FlatTemplate(4), Background = Brushes.Transparent, Cursor = Cursors.Hand, Focusable = false, Padding = new Thickness(4, 2, 6, 2),
                Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { icon, label } },
            };
            copy.Click += async (s, e) =>
            {
                Clip.Set(m.Content);
                icon.Text = Theme.GlyphCheck;
                label.Text = tr("Copiado", "Copied");
                await Task.Delay(1500);
                icon.Text = Theme.GlyphCopy;
                label.Text = tr("Copiar", "Copy");
            };
            footer.Children.Add(copy);
        }
        if (m.Stopped) footer.Children.Add(Small(tr("Detenido", "Stopped")));
        if (m.Stats is { } s)
        {
            var parts = new List<string> { s.Model };
            if (s.TokensPerSecond > 0) parts.Add($"{s.TokensPerSecond:0.0} tok/s".Replace(',', '.'));
            parts.Add($"{s.Seconds:0.0} s".Replace(',', '.'));
            if (m.Reasoned) parts.Add(tr("razonado", "reasoned"));
            footer.Children.Add(Small(string.Join(" · ", parts)));
        }
    }

    private static TextBlock Small(string text) => new() { Text = text, FontSize = 12, Foreground = Theme.Secondary, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
}

internal static class Fluent
{
    /// <summary>Runs an action on an object and returns it, for building trees inline.</summary>
    public static T Also<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }
}
