//
//  SetupApplication.cs
//  Citadel setup: the bootstrapper application that drives the WiX engine.
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
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Microsoft.Win32;
using WixToolset.BootstrapperApplicationApi;

namespace Citadel.Setup;

/// <summary>What the setup can do, as offered on its first page.</summary>
internal enum SetupState { Install, Upgrade, Maintenance, Uninstall, Newer }

/// <summary>
/// Bootstrapper application: talks to the WiX engine (detect, plan, apply) and drives the setup window.
/// The engine calls back on its own thread; everything that touches the window goes through the dispatcher.
/// Without a full user interface (/quiet, /passive, or an upgrade removing this version) it works on its own.
/// Citadel's own updates run it as "citadel-setup.exe /quiet /norestart /CITADELWAITPID=&lt;pid&gt; /CITADELRELAUNCH=1":
/// it waits for that Citadel to finish closing before anything is replaced, and opens Citadel again at the end.
/// </summary>
internal sealed class SetupApplication : BootstrapperApplication
{
    /// <summary>Id of the MSI package in the bundle (Bundle.wxs).</summary>
    private const string PackageId = "CitadelMsi";
    /// <summary>Windows Installer error 1602: the user canceled.</summary>
    private const int UserCanceled = unchecked((int)0x80070642);

    public IBootstrapperCommand Command { get; private set; }
    public IEngine Engine => engine;
    public string Version { get; private set; }
    /// <summary>Version of an older Citadel that this setup will replace, if any.</summary>
    public string OlderVersion { get; private set; }
    /// <summary>Version of a newer Citadel already installed, if any.</summary>
    public string NewerVersion { get; private set; }
    public bool Installed { get; private set; }
    public LaunchAction Planned { get; private set; }
    public string LastError { get; private set; }
    public bool Canceled { get; set; }

    private Dispatcher dispatcher;
    private SetupWindow window;
    private IntPtr windowHandle;
    private int exitCode;
    /// <summary>/CITADELWAITPID=&lt;pid&gt;: the Citadel that started this update, closing by itself.</summary>
    private int waitPid;
    /// <summary>/CITADELRELAUNCH=1: open Citadel again when the setup ends.</summary>
    private bool relaunch;

    protected override void OnCreate(CreateEventArgs args)
    {
        base.OnCreate(args);
        Command = args.Command;
    }

    protected override void Run()
    {
        dispatcher = Dispatcher.CurrentDispatcher;
        Version = engine.GetVariableVersion("WixBundleVersion");
        Loc.Current = InitialLanguage();
        ReadUpdateArguments();

        DetectRelatedBundle += OnDetectRelatedBundle;
        DetectPackageComplete += OnDetectPackageComplete;
        DetectComplete += OnDetectComplete;
        PlanComplete += OnPlanComplete;
        Progress += (s, e) => { Report(e.OverallPercentage); e.Cancel = Canceled; };
        ExecuteProgress += (s, e) => { Report(e.OverallPercentage); e.Cancel = Canceled; };
        Error += OnError;
        ApplyComplete += OnApplyComplete;

        // The window always exists, because the engine needs its handle to apply; it is shown only with a
        // full or passive interface.
        window = new SetupWindow(this);
        window.Closed += (s, e) => dispatcher.InvokeShutdown();
        windowHandle = new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
        if (HasWindow) window.Show();
        engine.Detect();
        Dispatcher.Run();
        engine.Quit(exitCode);
    }

    /// <summary>Full or passive display: a window is shown (passive shows progress only).</summary>
    private bool HasWindow => Command.Display == Display.Full || Command.Display == Display.Passive;
    private bool Interactive => Command.Display == Display.Full;

    /// <summary>
    /// The language of an installed Citadel when there is one (uninstall, repair), otherwise the language of
    /// Windows if it is Spanish or Japanese, otherwise English. The user can change it in the window at any time.
    /// </summary>
    private static UiLanguage InitialLanguage()
    {
        try
        {
            using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(@"Software\Citadel");
            if (key?.GetValue("Language") is string code && code.Length > 0) return Loc.FromCode(code);
        }
        catch { }
        return Loc.FromWindows();
    }

    // ---------------- Detect ----------------
    private void OnDetectRelatedBundle(object sender, DetectRelatedBundleEventArgs e)
    {
        if (e.RelationType != RelationType.Upgrade) return;
        int cmp = engine.CompareVersions(e.Version, Version);
        if (cmp > 0) NewerVersion = e.Version;
        else if (cmp < 0) OlderVersion = e.Version;
    }

    private void OnDetectPackageComplete(object sender, DetectPackageCompleteEventArgs e)
    {
        if (e.PackageId == PackageId && e.State == PackageState.Present) Installed = true;
    }

    private void OnDetectComplete(object sender, DetectCompleteEventArgs e)
    {
        if (e.Status < 0)
        {
            Finish(e.Status);
            return;
        }
        // Without a full interface the requested action goes ahead at once (e.g. an upgrade removing this version).
        if (!Interactive) { Plan(Command.Action); return; }
        SetupState state =
            Command.Action == LaunchAction.Uninstall ? SetupState.Uninstall
            : NewerVersion != null ? SetupState.Newer
            : Installed ? SetupState.Maintenance
            : OlderVersion != null ? SetupState.Upgrade
            : SetupState.Install;
        dispatcher.BeginInvoke(new Action(() => window.ShowStart(state)));
    }

    // ---------------- Plan and apply ----------------
    /// <summary>Starts an install, repair or uninstall; the chosen language goes to the MSI (see Package.wxs).</summary>
    public void Plan(LaunchAction action)
    {
        Planned = action;
        engine.SetVariableString("CitadelLanguage", Loc.Code(Loc.Current), false);
        if (HasWindow) dispatcher.BeginInvoke(new Action(() => window.ShowProgress(action)));
        // Closing Citadel can take a few seconds: it is done off the window's thread.
        System.Threading.Tasks.Task.Run(() =>
        {
            WaitForUpdatingCitadel();
            CloseRunningCitadel();
            try { engine.Plan(action); }
            catch (Exception ex)
            {
                engine.Log(LogLevel.Error, "Citadel setup: plan could not start: " + ex.Message);
                LastError = ex.Message;
                Finish(ex.HResult < 0 ? ex.HResult : unchecked((int)0x80004005));
            }
        });
    }

    // ---------------- Updates started by Citadel ----------------
    /// <summary>Reads /CITADELWAITPID=&lt;pid&gt; and /CITADELRELAUNCH=1 (the engine passes on what it does not know).</summary>
    private void ReadUpdateArguments()
    {
        string[] args;
        try { args = Command.ParseCommandLine().UnknownCommandLineArgs ?? new string[0]; }
        catch { args = new string[0]; }
        foreach (string raw in args)
        {
            string a = raw.TrimStart('/', '-');
            int eq = a.IndexOf('=');
            string name = (eq < 0 ? a : a.Substring(0, eq)).ToUpperInvariant(), value = eq < 0 ? "" : a.Substring(eq + 1);
            if (name == "CITADELWAITPID" && int.TryParse(value, out int pid) && pid > 0) waitPid = pid;
            else if (name == "CITADELRELAUNCH") relaunch = value == "1";
        }
        if (waitPid > 0 || relaunch) engine.Log(LogLevel.Standard, $"Citadel setup: update started by Citadel (wait for {waitPid}, relaunch {relaunch}).");
    }

    /// <summary>
    /// Waits for the Citadel that started the update to close by itself (it first saves its conversations and
    /// asks about an answer in progress), for up to two minutes; CloseRunningCitadel deals with what is left.
    /// </summary>
    private void WaitForUpdatingCitadel()
    {
        if (waitPid <= 0) return;
        try
        {
            using (var p = Process.GetProcessById(waitPid))
            {
                if (!string.Equals(p.ProcessName, "Citadel", StringComparison.OrdinalIgnoreCase)) return;
                bool closed = p.WaitForExit(120000);
                engine.Log(LogLevel.Standard, closed ? "Citadel setup: the updating Citadel has closed." : "Citadel setup: the updating Citadel is still open after 2 minutes.");
            }
        }
        catch (ArgumentException) { }   // already gone
        catch (Exception ex) { engine.Log(LogLevel.Standard, "Citadel setup: could not wait for Citadel: " + ex.Message); }
    }

    /// <summary>
    /// Opens Citadel again after an update, whether it went well or not (a failed or declined update leaves the
    /// previous version installed). This process runs without elevation, so Citadel opens as the user.
    /// </summary>
    private void RelaunchCitadel(bool updated)
    {
        relaunch = false;
        try
        {
            string exe = Path.Combine(InstallFolder, "Citadel.exe");
            if (!File.Exists(exe)) { engine.Log(LogLevel.Standard, "Citadel setup: nothing to open again at " + exe); return; }
            Process.Start(new ProcessStartInfo(exe, updated ? "--updated" : "") { UseShellExecute = false, WorkingDirectory = InstallFolder });
            engine.Log(LogLevel.Standard, "Citadel setup: opened Citadel again.");
        }
        catch (Exception ex) { engine.Log(LogLevel.Standard, "Citadel setup: could not open Citadel again: " + ex.Message); }
    }

    // ---------------- Closing a running Citadel ----------------
    private const uint WM_CLOSE = 0x0010;
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>
    /// Closes the Citadel of this user that runs from the install folder, before its files are replaced or
    /// removed. The MSI also closes it (util:CloseApplication), but only after removing files, which on an
    /// uninstall would leave the executable in use until a restart. Citadel quits when one of its windows
    /// is asked to close; a version that does not do so is ended after a few seconds.
    /// </summary>
    private void CloseRunningCitadel()
    {
        string folder;
        try { folder = Path.GetFullPath(InstallFolder).TrimEnd('\\') + "\\"; } catch { return; }
        foreach (var p in Process.GetProcessesByName("Citadel"))
        {
            using (p)
            {
                try
                {
                    string exe = p.MainModule?.FileName;
                    if (exe == null || !exe.StartsWith(folder, StringComparison.OrdinalIgnoreCase)) continue;
                    EnumWindows((h, l) =>
                    {
                        GetWindowThreadProcessId(h, out uint pid);
                        if (pid == (uint)p.Id) PostMessage(h, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                        return true;
                    }, IntPtr.Zero);
                    if (!p.WaitForExit(8000))
                    {
                        engine.Log(LogLevel.Standard, "Citadel setup: Citadel did not close in 8 s; ending it.");
                        p.Kill();
                        p.WaitForExit(3000);
                    }
                    else engine.Log(LogLevel.Standard, "Citadel setup: closed the running Citadel.");
                }
                catch (Exception ex) { engine.Log(LogLevel.Standard, "Citadel setup: could not close Citadel: " + ex.Message); }
            }
        }
    }

    private void OnPlanComplete(object sender, PlanCompleteEventArgs e)
    {
        if (e.Status < 0) { Finish(e.Status); return; }
        try { engine.Apply(windowHandle); }
        catch (Exception ex)
        {
            // Without this, a failed call would leave the setup waiting forever.
            engine.Log(LogLevel.Error, "Citadel setup: apply could not start: " + ex.Message);
            LastError = ex.Message;
            Finish(ex.HResult < 0 ? ex.HResult : unchecked((int)0x80004005));
        }
    }

    private void Report(int percent)
    {
        if (HasWindow) dispatcher.BeginInvoke(new Action(() => window.SetProgress(percent)));
    }

    private void OnError(object sender, WixToolset.BootstrapperApplicationApi.ErrorEventArgs e)
    {
        // The message is shown on the final page; the engine's recommended answer is kept.
        if (e.ErrorCode != 1602 && !string.IsNullOrWhiteSpace(e.ErrorMessage)) LastError = e.ErrorMessage.Trim();
        engine.Log(LogLevel.Standard, "Citadel setup: error " + e.ErrorCode + ": " + e.ErrorMessage);
    }

    private void OnApplyComplete(object sender, ApplyCompleteEventArgs e)
    {
        Finish(e.Status, e.Restart == ApplyRestart.RestartRequired);
    }

    /// <summary>Ends the operation: shows the result page or, without a full interface, closes.</summary>
    private void Finish(int status, bool restart = false)
    {
        exitCode = status;
        if (relaunch) RelaunchCitadel(status >= 0);
        bool canceled = Canceled || status == UserCanceled;
        if (!Interactive)
        {
            dispatcher.BeginInvoke(new Action(() => dispatcher.InvokeShutdown()));
            return;
        }
        dispatcher.BeginInvoke(new Action(() => window.ShowResult(status >= 0, canceled, restart)));
    }

    /// <summary>Called when the user closes the window before anything was done.</summary>
    public void CancelBeforeStart() => exitCode = UserCanceled;

    /// <summary>Path of the setup log, for the "View log" button.</summary>
    public string LogPath => engine.ContainsVariable("WixBundleLog") ? engine.GetVariableString("WixBundleLog") : null;

    /// <summary>Where Citadel is (or will be) installed.</summary>
    public string InstallFolder => engine.FormatString(@"[ProgramFiles64Folder]Citadel");
}
