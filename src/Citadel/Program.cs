//
//  Program.cs
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
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Citadel;

internal static class Program
{
    /// <summary>Largest size of citadel.log before it is rotated to citadel.old.log.</summary>
    private const long MaxLogBytes = 512 * 1024;

    [STAThread]
    private static int Main(string[] args)
    {
        // Maintenance options (not shown to users):
        //   --data-folder <folder>     use this folder instead of %APPDATA%\Citadel
        //   --memory-folder <folder>   use this memory folder
        //   --language en|es           interface language for this run
        //   --self-test                run the main features against Ollama and print PASS/FAIL lines
        //   --screenshot <file.png>    render a window to a PNG and exit; --window main|settings|about|save
        string shot = null, window = "main", language = null;
        bool selfTest = false;
        for (int i = 0; i < args.Length; i++)
        {
            string next = i + 1 < args.Length ? args[i + 1] : null;
            switch (args[i])
            {
                case "--data-folder" when next != null: Paths.DataOverride = Path.GetFullPath(next); i++; break;
                case "--memory-folder" when next != null: Paths.MemoryOverride = Path.GetFullPath(next); i++; break;
                case "--language" when next != null: language = next; i++; break;
                case "--screenshot" when next != null: shot = Path.GetFullPath(next); i++; break;
                case "--window" when next != null: window = next; i++; break;
                case "--self-test": selfTest = true; break;
            }
        }
        bool test = selfTest || shot != null;

        // One window at a time; tests run separately.
        Mutex single = null;
        if (!test)
        {
            single = new Mutex(true, @"Local\Citadel.Single", out bool created);
            if (!created) return 0;
        }

        var app = new Application { ThemeMode = ThemeMode.Dark, ShutdownMode = test ? ShutdownMode.OnExplicitShutdown : ShutdownMode.OnMainWindowClose };
        app.DispatcherUnhandledException += (s, e) => { Log(e.Exception.ToString()); e.Handled = true; };
        Settings.Load();
        Settings.Current.Persist = !test;
        if (language != null) Loc.Current = Loc.FromCode(language);

        var model = new ChatModel { Persist = !test };
        model.Start(loadSaved: !selfTest);
        if (selfTest)
        {
            // No window: the memory preview would wait for a click.
            app.Startup += async (s, e) => app.Shutdown(await SelfTest.Run(model) ? 0 : 1);
            return app.Run();
        }
        var main = new MainWindow(model);
        app.MainWindow = main;
        if (shot != null)
        {
            app.Startup += async (s, e) => app.Shutdown(await Screenshot(model, main, window, shot) ? 0 : 1);
            return app.Run();
        }
        main.Show();
        int rc = app.Run(main);
        GC.KeepAlive(single);
        return rc;
    }

    /// <summary>Shows one window off to the side, waits for Ollama's status, and saves its picture.</summary>
    private static async Task<bool> Screenshot(ChatModel model, MainWindow main, string which, string path)
    {
        main.Width = 1180;
        main.Height = 760;
        main.WindowStartupLocation = WindowStartupLocation.Manual;
        main.Left = 0;
        main.Top = 0;
        main.Show();
        await model.RefreshStatus();
        Window target = main;
        switch (which)
        {
            case "settings":
                target = new SettingsWindow { Owner = main };
                target.Show();
                break;
            case "about":
                target = new AboutWindow { Owner = main };
                target.Show();
                break;
            case "save":
                // The preview opens as a dialog when the draft is ready; wait for it to appear.
                model.StartMemoryDraft();
                var until = DateTime.UtcNow.AddMinutes(5);
                while ((target = Application.Current.Windows.OfType<MemoryPreviewWindow>().FirstOrDefault()) == null)
                {
                    if (DateTime.UtcNow > until || (!model.IsDraftingMemory && model.PendingDraft == null))
                    {
                        Console.Error.WriteLine("no memory preview: " + model.Current?.Messages.LastOrDefault()?.Content);
                        return false;
                    }
                    await Task.Delay(200);
                }
                break;
        }
        await Task.Delay(1500);
        await target.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        bool ok = Save(target, path);
        Console.WriteLine(ok ? "screenshot saved: " + path : "screenshot failed");
        foreach (Window w in Application.Current.Windows.OfType<Window>().ToList()) if (w is not MainWindow) w.Close();
        return ok;
    }

    /// <summary>Renders a window's content at twice its size to a PNG file.</summary>
    private static bool Save(Window window, string path)
    {
        try
        {
            var content = (FrameworkElement)window.Content;
            double width = content.ActualWidth + content.Margin.Left + content.Margin.Right;
            double height = content.ActualHeight + content.Margin.Top + content.Margin.Bottom;
            const double scale = 2;
            var rtb = new RenderTargetBitmap((int)Math.Ceiling(width * scale), (int)Math.Ceiling(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
                dc.DrawRectangle(window.Background ?? Theme.Window, null, new Rect(0, 0, width, height));
            rtb.Render(visual);
            rtb.Render(content);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using var fs = File.Create(path);
            encoder.Save(fs);
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return false;
        }
    }

    /// <summary>Appends a line to citadel.log, keeping the file small.</summary>
    public static void Log(string text)
    {
        try
        {
            Directory.CreateDirectory(Paths.Data);
            var info = new FileInfo(Paths.Log);
            if (info.Exists && info.Length > MaxLogBytes)
                File.Move(Paths.Log, Path.Combine(Paths.Data, "citadel.old.log"), true);
            File.AppendAllText(Paths.Log, DateTime.Now.ToString("s") + "  " + text + Environment.NewLine);
        }
        catch { }
    }
}
