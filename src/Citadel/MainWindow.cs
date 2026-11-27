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
internal sealed partial class MainWindow : Window
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

    // Search in the text of the conversations: waits for a pause in typing and keeps its last results.
    private readonly DispatcherTimer searchTimer = new() { Interval = TimeSpan.FromMilliseconds(220) };
    private string hitsKey;
    private List<SearchHit> hits = [];
    /// <summary>The view of each message shown, to scroll to a search result.</summary>
    private readonly Dictionary<Guid, FrameworkElement> messageViews = [];

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
        Theme.Apply(this);
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
        Theme.Changed += Build;
        Closing += (s, e) => SaveBounds();
        Closed += (s, e) => { Loc.Changed -= Build; Theme.Changed -= Build; };
        PreviewKeyDown += OnKey;
        searchTimer.Tick += (s, e) => { searchTimer.Stop(); RefreshSidebar(); };
        InitUpdate();
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
        // Maximized on the screen where its normal bounds are; restoring it gives those bounds back.
        if (Settings.Current.Maximized) WindowState = WindowState.Maximized;
    }

    private void SaveBounds()
    {
        var r = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        Settings.Current.Window = [r.Left, r.Top, r.Width, r.Height];
        Settings.Current.Maximized = WindowState == WindowState.Maximized;
        Settings.Current.Save();
    }

    // Layout

    private void Build()
    {
        // These panels outlive a rebuild (language change): free them from the old layout first.
        foreach (var kept in new FrameworkElement[] { conversationList, messagesPanel, attachmentPanel }) Detach(kept);
        Theme.Apply(this);
        answerViews.Clear();
        shownConversation = null;
        shownCount = -1;
        welcomeShown = false;
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(264) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        root.ColumnDefinitions.Add(new ColumnDefinition());
        var sidebar = BuildSidebar();
        root.Children.Add(sidebar);
        var line = new Border { Background = Theme.Divider, Opacity = Theme.Dark ? 0.5 : 1 };
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
        var newButton = Theme.FlatButton(Theme.GlyphEdit, null, model.NewConversation, tr("Conversación nueva (Ctrl+N)", "New conversation (Ctrl+N)", "新しい会話 (Ctrl+N)"), 15);
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
        var searchHint = new TextBlock { Text = tr("Buscar", "Search", "検索"), FontSize = 13, Foreground = Theme.Tertiary, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center };
        searchBox.TextChanged += (s, e) =>
        {
            searchHint.Visibility = searchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            model.Search = searchBox.Text;
            // The titles and the text of every conversation are searched once typing pauses.
            searchTimer.Stop();
            searchTimer.Start();
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
        bottom.Children.Add(Theme.FlatButton(Theme.GlyphSettings, tr("Ajustes", "Settings", "設定"), OpenSettings, "Ctrl+,"));
        bottom.Children.Add(Theme.FlatButton(Theme.GlyphInfo, null, OpenAbout, tr("Acerca de Citadel", "About Citadel", "Citadel について")));
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
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        menu.Items.Add(new MenuItem { Header = Loc.MenuLabel, IsEnabled = false });
        foreach (var lang in Loc.All)
        {
            var item = new MenuItem { Header = Loc.NativeName(lang), IsCheckable = true, IsChecked = Loc.Current == lang };
            item.Click += (s, e) => SetLanguage(lang);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    public static void SetLanguage(UiLanguage lang)
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

        // Header: title, export, language and model picker.
        var header = new DockPanel { Margin = new Thickness(22, 10, 16, 10), Height = 34 };
        modelPicker = new ComboBox { MinWidth = 200, FontSize = 12.5, ToolTip = tr("Modelo de Ollama", "Ollama model", "Ollama のモデル"), VerticalAlignment = VerticalAlignment.Center };
        modelPicker.DropDownOpened += async (s, e) => await model.RefreshStatus();
        modelPicker.SelectionChanged += (s, e) =>
        {
            if (!updatingPicker && modelPicker.SelectedItem is ComboBoxItem { Tag: string name } && name != model.SelectedModel) model.SelectedModel = name;
        };
        DockPanel.SetDock(modelPicker, Dock.Right);
        header.Children.Add(modelPicker);
        // Interface language, next to the model so it is easy to find.
        var language = Theme.FlatButton(Theme.GlyphGlobe, Loc.NativeName(Loc.Current), null, Loc.MenuLabel, 12.5);
        language.Margin = new Thickness(0, 0, 8, 0);
        System.Windows.Automation.AutomationProperties.SetName(language, Loc.MenuLabel);
        language.Click += (s, e) => LanguageMenu(language);
        DockPanel.SetDock(language, Dock.Right);
        header.Children.Add(language);
        // Export the conversation shown to a Markdown file.
        var export = Theme.FlatButton(Theme.GlyphExport, null, null, tr("Exportar a Markdown", "Export to Markdown", "Markdown にエクスポート"), 13);
        export.Margin = new Thickness(0, 0, 4, 0);
        export.Click += (s, e) =>
        {
            var menu = new ContextMenu { PlacementTarget = export, Placement = PlacementMode.Bottom };
            AddExportItems(menu, () => model.Current);
            ((MenuItem)menu.Items[0]).IsEnabled = model.Current is { Messages.Count: > 0 };
            menu.IsOpen = true;
        };
        DockPanel.SetDock(export, Dock.Right);
        header.Children.Add(export);
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

    private static void Detach(FrameworkElement element)
    {
        switch (element.Parent)
        {
            case Decorator d: d.Child = null; break;
            case ContentControl c: c.Content = null; break;
            case Panel p: p.Children.Remove(element); break;
        }
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
            Text = tr("Escribe un mensaje…  (/mem tema · /save)", "Message…  (/mem topic · /save)", "メッセージを入力…  (/mem トピック · /save)"), FontSize = 14.5,
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

        reasonButton = new Button { Template = Theme.FlatTemplate(13), Cursor = Cursors.Hand, Padding = new Thickness(10, 4, 12, 4) };
        Theme.KeyboardReachable(reasonButton, null, 13);
        reasonButton.Click += (s, e) => model.Reason = !model.Reason;
        reasonHint = new TextBlock
        {
            Text = tr("Más lento (20 a 90 s), mucho más preciso en cuentas, conteos, lógica y fechas.",
                      "Slower (20-90 s), much more accurate on math, counting, logic and dates.",
                      "時間はかかります (20～90 秒) が、計算、数え上げ、論理、日付がはるかに正確になります。"),
            FontSize = 11.5, Foreground = Theme.Secondary, VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(10, 0, 8, 0),
        };
        sendButton = new Button { Template = Theme.FlatTemplate(15), Width = 30, Height = 30, Cursor = Cursors.Hand };
        Theme.KeyboardReachable(sendButton, null, 15);
        sendButton.Click += (s, e) => { if (model.IsGenerating || model.IsDraftingMemory) model.Stop(); else model.Send(); };

        var tools = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(sendButton, Dock.Right);
        tools.Children.Add(sendButton);
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(reasonButton);
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

        // The text of every saved conversation: one row per message that contains the search.
        var found = SearchHits();
        if (found.Count > 0)
        {
            conversationList.Children.Add(new TextBlock
            {
                Text = tr("En los mensajes", "In the messages", "メッセージ内") + $" ({found.Count}{(found.Count >= MaxHits ? "+" : "")})",
                FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = Theme.Secondary, Margin = new Thickness(10, list.Count > 0 ? 12 : 2, 0, 6),
            });
            foreach (var h in found) conversationList.Children.Add(HitRow(h));
        }
        if (list.Count == 0 && found.Count == 0 && model.Search.Trim().Length > 0)
            conversationList.Children.Add(new TextBlock { Text = tr("Sin resultados", "No results", "一致する結果はありません"), FontSize = 12, Foreground = Theme.Secondary, Margin = new Thickness(12) });
    }

    private const int MaxHits = 200;

    /// <summary>Puts a search in the sidebar and shows its results at once (screenshots).</summary>
    public void ShowSearch(string text)
    {
        searchBox.Text = text;
        searchTimer.Stop();
        RefreshSidebar();
    }

    /// <summary>The search results in the messages, searched again only when the query or the conversations change.</summary>
    private List<SearchHit> SearchHits()
    {
        string q = model.Search.Trim();
        if (ConversationSearch.FoldQuery(q).Length < ConversationSearch.MinLength) { hitsKey = null; hits = []; return hits; }
        string key = $"{q}\n{model.Conversations.Count}\n{model.Conversations.Sum(c => c.Messages.Count)}\n{model.Conversations.Sum(c => c.Messages.Sum(m => m.Content.Length))}";
        if (key != hitsKey)
        {
            hits = ConversationSearch.Find(model.Conversations.Where(c => c.Messages.Count > 0), q, 5, MaxHits);
            hitsKey = key;
        }
        return hits;
    }

    private static string ShortDate(DateTime utc) =>
        utc.ToLocalTime().ToString(Loc.Current switch { UiLanguage.Es => "d MMM yyyy, HH:mm", UiLanguage.Ja => "yyyy年M月d日 H:mm", _ => "MMM d yyyy, HH:mm" },
                                   System.Globalization.CultureInfo.GetCultureInfo(Loc.CultureName));

    /// <summary>A search result: the conversation, the date of the message and the text around the match, highlighted.</summary>
    private UIElement HitRow(SearchHit h)
    {
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = h.Conversation.DisplayTitle, FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Theme.Text });
        string who = h.Message.Role == Role.User ? tr("Tú", "You", "あなた") : tr("Respuesta", "Answer", "回答");
        text.Children.Add(new TextBlock { Text = $"{who} · {ShortDate(h.Message.Date)}", FontSize = 11, Foreground = Theme.Secondary, Margin = new Thickness(0, 1, 0, 2) });
        var fragment = new TextBlock { FontSize = 12, Foreground = Theme.Secondary, TextWrapping = TextWrapping.Wrap, MaxHeight = 50, TextTrimming = TextTrimming.CharacterEllipsis };
        fragment.Inlines.Add(new System.Windows.Documents.Run(h.Before));
        fragment.Inlines.Add(new System.Windows.Documents.Run(h.Match) { FontWeight = FontWeights.SemiBold, Foreground = Theme.Text, Background = Theme.AccentSoft });
        fragment.Inlines.Add(new System.Windows.Documents.Run(h.After));
        text.Children.Add(fragment);
        var row = new Border
        {
            CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 6, 10, 7), Margin = new Thickness(0, 0, 0, 2),
            Background = Brushes.Transparent, Child = text, Cursor = Cursors.Hand,
            ToolTip = tr("Abrir la conversación en este mensaje", "Open the conversation at this message", "このメッセージの位置で会話を開く"),
        };
        row.MouseEnter += (s, e) => row.Background = Theme.Card;
        row.MouseLeave += (s, e) => row.Background = Brushes.Transparent;
        row.MouseLeftButtonUp += (s, e) => GoToMessage(h.Conversation.Id, h.Message.Id);
        return row;
    }

    /// <summary>Opens a conversation and scrolls to one of its messages, which blinks once.</summary>
    private void GoToMessage(Guid conversation, Guid message)
    {
        model.Select(conversation);
        // After the messages are rebuilt and the scroll to the end they ask for.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!messageViews.TryGetValue(message, out var view) || messagesScroll.Content is not UIElement content) return;
            double y = view.TranslatePoint(new Point(0, 0), content).Y;
            messagesScroll.ScrollToVerticalOffset(Math.Max(0, y - 16));
            view.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(900)));
        }), DispatcherPriority.ContextIdle);
    }

    // Export

    /// <summary>Saves a conversation as a Markdown file, and opens it after when that option is on.</summary>
    private void ExportMarkdown(Conversation c)
    {
        if (c == null || c.Messages.Count == 0) { ShowToast(tr("Esta conversación todavía está vacía.", "This conversation is still empty.", "この会話はまだ空です。")); return; }
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = tr("Exportar a Markdown", "Export to Markdown", "Markdown にエクスポート"), FileName = MarkdownExport.FileName(c), DefaultExt = ".md",
            Filter = "Markdown (*.md)|*.md", AddExtension = true, OverwritePrompt = true,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            Paths.WriteText(dialog.FileName, MarkdownExport.Render(c, Loc.Current, TimeZoneInfo.Local, AboutWindow.VersionText));
        }
        catch (Exception ex)
        {
            ShowToast(tr("No se pudo guardar: ", "Could not save: ", "保存できませんでした: ") + ex.Message);
            return;
        }
        ShowToast(tr("Guardada: ", "Saved: ", "保存しました: ") + Path.GetFileName(dialog.FileName));
        if (Settings.Current.OpenAfterExport)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dialog.FileName) { UseShellExecute = true }); }
            catch (Exception ex) { ShowToast(tr("No se pudo abrir: ", "Could not open: ", "開けませんでした: ") + ex.Message); }
        }
    }

    /// <summary>"Export to Markdown…" and the "Open after exporting" option, for a menu.</summary>
    private void AddExportItems(ItemsControl menu, Func<Conversation> which)
    {
        var export = new MenuItem { Header = tr("Exportar a Markdown…", "Export to Markdown…", "Markdown にエクスポート…") };
        export.Click += (s, e) => ExportMarkdown(which());
        var open = new MenuItem { Header = tr("Abrir después de exportar", "Open after exporting", "エクスポート後に開く"), IsCheckable = true, IsChecked = Settings.Current.OpenAfterExport, StaysOpenOnClick = true };
        open.Click += (s, e) => { Settings.Current.OpenAfterExport = open.IsChecked; Settings.Current.Save(); };
        menu.Items.Add(export);
        menu.Items.Add(open);
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
            Text = c.Updated.ToLocalTime().ToString(Loc.Current switch { UiLanguage.Es => "d MMM, HH:mm", UiLanguage.Ja => "M月d日 H:mm", _ => "MMM d, HH:mm" },
                                                    System.Globalization.CultureInfo.GetCultureInfo(Loc.CultureName)),
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
        var rename = new MenuItem { Header = tr("Renombrar…", "Rename…", "名前の変更…") };
        rename.Click += (s, e) =>
        {
            string title = Dialogs.Prompt(this, tr("Renombrar conversación", "Rename conversation", "会話の名前の変更"), tr("Título", "Title", "タイトル"), c.DisplayTitle, tr("Renombrar", "Rename", "変更"));
            if (title != null) model.Rename(c.Id, title);
        };
        var delete = new MenuItem { Header = tr("Eliminar…", "Delete…", "削除…") };
        delete.Click += (s, e) =>
        {
            if (Dialogs.Confirm(this, tr("¿Eliminar esta conversación?", "Delete this conversation?", "この会話を削除しますか?"),
                                tr("«{0}» se borrará del disco. No se puede deshacer.", "“{0}” will be removed from disk. This cannot be undone.", "「{0}」をディスクから削除します。元に戻すことはできません。", c.DisplayTitle),
                                tr("Eliminar", "Delete", "削除")))
                model.Delete(c.Id);
        };
        menu.Items.Add(rename);
        if (c.Messages.Count > 0) AddExportItems(menu, () => c);
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
        // Models that cannot reason are marked, so the choice is clear before sending.
        string Label(string name) =>
            model.ReasoningModels.TryGetValue(name, out bool can) && !can ? name + tr("  (sin Razonar)", "  (no Reason)", "  (推論なし)") : name;
        var current = modelPicker.Items.Cast<ComboBoxItem>().Select(i => (string)i.Tag + "\n" + i.Content).ToList();
        if (!current.SequenceEqual(items.Select(m => m + "\n" + Label(m))))
        {
            modelPicker.Items.Clear();
            foreach (string m in items) modelPicker.Items.Add(new ComboBoxItem { Content = Label(m), Tag = m });
        }
        modelPicker.SelectedItem = modelPicker.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == model.SelectedModel);
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
            messageViews.Clear();
            foreach (var m in c.Messages)
            {
                var view = MessageView(m, c);
                messagesPanel.Children.Add(view);
                if (view is FrameworkElement element) messageViews[m.Id] = element;
            }
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
                return Theme.Chip(Theme.GlyphBookmark, tr("memoria cargada: ", "memory loaded: ", "読み込んだメモリ: ") + m.MemoryFile).Also(x => x.Margin = new Thickness(0, 0, 0, 22));
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
        stack.Children.Add(new TextBlock { Text = tr("¿En qué te ayudo?", "How can I help?", "何をお手伝いしましょうか?"), FontSize = 26, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 8) });
        stack.Children.Add(new TextBlock
        {
            Text = tr("Todo corre en esta computadora con Ollama: nada de lo que escribas sale de ella.",
                      "Everything runs on this computer through Ollama: nothing you write leaves it.",
                      "すべてこのコンピューター上の Ollama で動作します。入力した内容が外に出ることはありません。"),
            FontSize = 13, Foreground = Theme.Secondary, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
        });
        var hints = new StackPanel();
        void Hint(string glyph, string text) => hints.Children.Add(new DockPanel
        {
            Margin = new Thickness(0, 4, 0, 4),
            Children = { Theme.Icon(glyph, 14, Theme.Accent).Also(i => i.Width = 24), new TextBlock { Text = text, FontSize = 12.5, Foreground = Theme.Secondary, TextWrapping = TextWrapping.Wrap } },
        });
        Hint(Theme.GlyphBulb, tr("Razonar: más lento (20 a 90 s) y mucho más preciso en cuentas, conteos, lógica y fechas.",
                                 "Reason: slower (20 to 90 s) and much more accurate on math, counting, logic and dates.",
                                 "推論: 時間はかかります (20～90 秒) が、計算、数え上げ、論理、日付がはるかに正確になります。"));
        Hint(Theme.GlyphBookmark, tr("/mem <tema> carga una memoria guardada en la conversación.", "/mem <topic> loads a saved memory into the conversation.", "/mem <トピック> で、保存したメモリを会話に読み込みます。"));
        Hint(Theme.GlyphDownload, tr("/save escribe una memoria con lo importante de esta conversación.", "/save writes a memory with what matters from this conversation.", "/save で、この会話の大事な点をメモリに書き出します。"));
        Hint(Theme.GlyphDocument, tr("Arrastra un .txt o .md al cuadro de texto para adjuntarlo.", "Drag a .txt or .md file onto the text box to attach it.", ".txt または .md のファイルを入力欄にドラッグすると添付できます。"));
        stack.Children.Add(new Border { CornerRadius = new CornerRadius(14), Background = Theme.Sidebar, Padding = new Thickness(18, 14, 18, 14), Margin = new Thickness(0, 22, 0, 0), Child = hints });
        return stack;
    }

    private void RefreshAttachments()
    {
        attachmentPanel.Children.Clear();
        attachmentPanel.Visibility = model.Attachments.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        foreach (var a in model.Attachments.ToList())
        {
            var remove = Theme.FlatButton(Theme.GlyphClose, null, () => { model.Attachments.Remove(a); OnModelChanged(); }, tr("Quitar", "Remove", "削除"), 10);
            attachmentPanel.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 0),
                Children = { Theme.Chip(Theme.GlyphDocument, $"{a.Name} · {a.Text.Length} " + tr("caracteres", "chars", "文字")), remove },
            });
        }
    }

    private static string ReasonTip => tr("Más lento (unos 20 a 90 s) pero mucho más preciso en cuentas, conteos, lógica y fechas.",
                                          "Slower (about 20 to 90 s) but much more accurate on math, counting, logic and dates.",
                                          "時間はかかります (約 20～90 秒) が、計算、数え上げ、論理、日付がはるかに正確になります。");

    private void RefreshComposerButtons()
    {
        bool on = model.Reason && model.CanReason;
        reasonButton.IsEnabled = model.CanReason;
        reasonButton.Opacity = model.CanReason ? 1 : 0.45;
        reasonButton.ToolTip = model.CanReason ? ReasonTip
            : tr("{0} no puede razonar: elige otro modelo para usar Razonar.",
                 "{0} cannot reason: choose another model to use Reason.",
                 "{0} は推論できません。推論を使うには別のモデルを選んでください。", model.SelectedModel);
        reasonButton.Background = on ? Theme.Accent : Theme.CardHover;
        var fg = on ? Theme.OnAccent : Theme.Secondary;
        reasonButton.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                Theme.Icon(Theme.GlyphBulb, 13, fg),
                new TextBlock { Text = tr("Razonar", "Reason", "推論"), FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = fg, Margin = new Thickness(6, 0, 0, 0) },
            },
        };
        reasonHint.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        bool busy = model.IsGenerating || model.IsDraftingMemory;
        sendButton.IsEnabled = busy || model.CanSend;
        sendButton.Background = busy ? Theme.Text : model.CanSend ? Theme.Accent : Theme.CardHover;
        sendButton.Content = Theme.Icon(busy ? Theme.GlyphStop : Theme.GlyphSend, 13,
                                        busy ? Theme.Window : model.CanSend ? Theme.OnAccent : Theme.Secondary);
        sendButton.ToolTip = busy ? tr("Detener (Esc)", "Stop (Esc)", "停止 (Esc)") : tr("Enviar (Enter) · Nueva línea (Shift+Enter)", "Send (Enter) · New line (Shift+Enter)", "送信 (Enter) · 改行 (Shift+Enter)");
        System.Windows.Automation.AutomationProperties.SetName(sendButton, busy ? tr("Detener", "Stop", "停止") : tr("Enviar", "Send", "送信"));
        System.Windows.Automation.AutomationProperties.SetName(reasonButton, tr("Razonar", "Reason", "推論"));
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
        if (updateNote != null) return updateNote;
        if (model.Reachable == null) return tr("Buscando Ollama…", "Looking for Ollama…", "Ollama を探しています…");
        string remote = Connection.RemoteHost;
        if (model.Reachable == false)
            return remote != null ? OllamaException.NotReachable().Message
                : tr("Ollama no responde en 127.0.0.1:11434. Ábrelo desde el menú Inicio o ejecuta: ollama serve",
                     "Ollama is not reachable at 127.0.0.1:11434. Open it from the Start menu or run: ollama serve",
                     "127.0.0.1:11434 の Ollama に接続できません。スタート メニューから起動するか、次のコマンドを実行してください: ollama serve");
        int n = model.Models.Count;
        string where = remote == null ? tr("Ollama conectado", "Ollama connected", "Ollama に接続済み") : tr("Ollama en {0} (SSH)", "Ollama on {0} (SSH)", "{0} の Ollama (SSH)", remote);
        var parts = new List<string> { where + $" · {n} " + (n == 1 ? tr("modelo", "model", "個のモデル") : tr("modelos", "models", "個のモデル")) };
        string name = model.SelectedModel;
        if (model.IsDraftingMemory)
        {
            parts.Add(tr("Escribiendo la memoria…", "Writing the memory…", "メモリを書いています…"));
            parts.Add($"{model.Elapsed:0.0} s");
        }
        else if (model.IsGenerating)
        {
            parts.Add(!model.ReceivedFirstToken
                ? (model.ModelIsLoaded ? tr("{0} cargado", "{0} loaded", "{0} 読み込み済み", name) : tr("cargando {0}…", "loading {0}…", "{0} を読み込んでいます…", name))
                : model.ReceivingThinking ? tr("razonando…", "reasoning…", "推論中…") : tr("escribiendo…", "writing…", "回答中…"));
            parts.Add($"{model.Elapsed:0.0} s");
        }
        else
        {
            parts.Add(model.ModelIsLoaded ? tr("{0} cargado", "{0} loaded", "{0} 読み込み済み", name) : tr("{0} se carga con el primer mensaje", "{0} loads with the first message", "{0} は最初のメッセージで読み込まれます", name));
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
        else if (ctrl && e.Key == Key.F) { searchBox.Focus(); searchBox.SelectAll(); e.Handled = true; }
        else if (e.Key == Key.Escape && (model.IsGenerating || model.IsDraftingMemory)) { model.Stop(); e.Handled = true; }
    }

    private SettingsWindow settingsWindow;

    public void OpenSettings()
    {
        if (settingsWindow is { IsLoaded: true }) { settingsWindow.Activate(); return; }
        settingsWindow = new SettingsWindow { Owner = this, Model = model };
        settingsWindow.Show();
    }

    private void OpenAbout()
    {
        var about = new AboutWindow { Owner = this };
        about.ShowDialog();
        if (about.CheckForUpdates) _ = CheckUpdatesAsync(true);
    }
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
            Template = Theme.FlatTemplate(4), Background = Brushes.Transparent, Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(2, 2, 6, 2),
            Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { chevron, Theme.Icon(Theme.GlyphBulb, 13).Also(i => i.Margin = new Thickness(6, 0, 0, 0)), thinkingTitle } },
        };
        Theme.KeyboardReachable(head, tr("Razonamiento", "Reasoning", "推論の過程"), 4);
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
        thinkingTitle.Text = live ? tr("Razonando…", "Reasoning…", "推論中…") : tr("Razonamiento", "Reasoning", "推論の過程");
        chevron.Text = expanded ? Theme.GlyphChevronDown : Theme.GlyphChevronRight;
        thinkingBody.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        if (expanded && thinkingText.Text.Length != m.Thinking.Trim().Length) thinkingText.Text = m.Thinking.Trim();
        string lastLine = m.Thinking.Trim().Split('\n').LastOrDefault(l => l.Trim().Length > 0) ?? "";
        thinkingLast.Text = lastLine;
        thinkingLast.Visibility = live && !expanded && lastLine.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        waiting.Visibility = streaming && m.Content.Length == 0 && m.Thinking.Length == 0 && !m.Reasoned ? Visibility.Visible : Visibility.Collapsed;
        waiting.Text = model.ModelIsLoaded ? tr("Pensando la respuesta…", "Working on it…", "回答を考えています…") : tr("Cargando el modelo…", "Loading the model…", "モデルを読み込んでいます…");

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
            var label = new TextBlock { Text = tr("Copiar", "Copy", "コピー"), FontSize = 12, Foreground = Theme.Secondary, Margin = new Thickness(6, 0, 0, 0) };
            var icon = Theme.Icon(Theme.GlyphCopy, 12);
            var copy = new Button
            {
                Template = Theme.FlatTemplate(4), Background = Brushes.Transparent, Cursor = Cursors.Hand, Padding = new Thickness(4, 2, 6, 2),
                Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { icon, label } },
            };
            Theme.KeyboardReachable(copy, tr("Copiar", "Copy", "コピー"), 4);
            copy.Click += async (s, e) =>
            {
                Clip.Set(m.Content);
                icon.Text = Theme.GlyphCheck;
                label.Text = tr("Copiado", "Copied", "コピーしました");
                await Task.Delay(1500);
                icon.Text = Theme.GlyphCopy;
                label.Text = tr("Copiar", "Copy", "コピー");
            };
            footer.Children.Add(copy);
        }
        if (m.Stopped) footer.Children.Add(Small(tr("Detenido", "Stopped", "停止しました")));
        if (m.Stats is { } s)
        {
            var parts = new List<string> { s.Model };
            if (s.TokensPerSecond > 0) parts.Add($"{s.TokensPerSecond:0.0} tok/s".Replace(',', '.'));
            parts.Add($"{s.Seconds:0.0} s".Replace(',', '.'));
            if (m.Reasoned) parts.Add(tr("razonado", "reasoned", "推論あり"));
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
