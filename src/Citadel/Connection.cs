//
//  Connection.cs
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
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using static Citadel.Loc;

namespace Citadel;

/// <summary>
/// Where Ollama runs: on this computer, or on another one reached through an SSH tunnel.
/// The tunnel is the system's ssh.exe forwarding a free local port to 127.0.0.1:11434 on the
/// other computer; it uses the user's SSH key and never asks for a password.
/// </summary>
internal static class Connection
{
    public const int OllamaPort = 11434;
    private static readonly Uri LocalBase = new($"http://127.0.0.1:{OllamaPort}/");

    private static Process ssh;
    private static string sshHost;
    private static int sshPort;
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>Last SSH error, shown in the status line and in Settings.</summary>
    public static string LastError { get; private set; }

    /// <summary>The SSH destination in use, or null when Ollama runs on this computer.</summary>
    public static string RemoteHost =>
        Settings.Current.UseSsh && !string.IsNullOrWhiteSpace(Settings.Current.SshHost) ? Settings.Current.SshHost.Trim() : null;

    /// <summary>Base address of the Ollama API, opening or reopening the tunnel when needed.</summary>
    public static async Task<Uri> Base()
    {
        string host = RemoteHost;
        if (host == null)
        {
            Stop();
            return LocalBase;
        }
        await Gate.WaitAsync();
        try
        {
            if (ssh is { HasExited: false } && sshHost == host) return new Uri($"http://127.0.0.1:{sshPort}/");
            Stop();
            return await Open(host);
        }
        finally { Gate.Release(); }
    }

    private static async Task<Uri> Open(string host)
    {
        int port = FreePort();
        var info = new ProcessStartInfo(SshPath())
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true,
            RedirectStandardInput = true, RedirectStandardOutput = true,
        };
        foreach (string a in new[]
        {
            "-N", "-T", "-L", $"127.0.0.1:{port}:127.0.0.1:{OllamaPort}",
            "-o", "BatchMode=yes", "-o", "ExitOnForwardFailure=yes", "-o", "ConnectTimeout=10",
            "-o", "ServerAliveInterval=15", "-o", "ServerAliveCountMax=3", "-o", "StrictHostKeyChecking=accept-new",
            host,
        }) info.ArgumentList.Add(a);

        Process p;
        try { p = Process.Start(info); }
        catch (Exception ex)
        {
            LastError = tr("No se pudo ejecutar ssh.exe: ", "Could not run ssh.exe: ") + ex.Message;
            throw new OllamaException(LastError) { Unreachable = true };
        }
        Job.Add(p);
        var errors = new System.Text.StringBuilder();
        p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (errors) errors.AppendLine(e.Data); };
        p.BeginErrorReadLine();

        // Ready when the local end of the tunnel accepts connections.
        var until = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < until)
        {
            if (p.HasExited)
            {
                string text;
                lock (errors) text = errors.ToString().Trim();
                LastError = tr($"SSH a {host} falló: ", $"SSH to {host} failed: ") +
                            (text.Length > 0 ? text.Split('\n')[^1].Trim() : tr("código ", "exit code ") + p.ExitCode);
                throw new OllamaException(LastError) { Unreachable = true };
            }
            if (await Accepts(port))
            {
                ssh = p; sshHost = host; sshPort = port; LastError = null;
                return new Uri($"http://127.0.0.1:{port}/");
            }
            await Task.Delay(250);
        }
        try { p.Kill(); } catch { }
        LastError = tr($"SSH a {host} no respondió en 20 s.", $"SSH to {host} did not answer within 20 s.");
        throw new OllamaException(LastError) { Unreachable = true };
    }

    /// <summary>Closes the tunnel, if any.</summary>
    public static void Stop()
    {
        if (ssh != null)
        {
            try { if (!ssh.HasExited) ssh.Kill(); } catch { }
            ssh.Dispose();
            ssh = null;
        }
        sshHost = null;
    }

    private static string SshPath()
    {
        string system = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "OpenSSH", "ssh.exe");
        return File.Exists(system) ? system : "ssh.exe";
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private static async Task<bool> Accepts(int port)
    {
        using var client = new TcpClient();
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            await client.ConnectAsync(IPAddress.Loopback, port, cts.Token);
            return true;
        }
        catch { return false; }
    }

    /// <summary>A job object that ends ssh.exe when Citadel ends, even if Citadel crashes.</summary>
    private static class Job
    {
        private static IntPtr handle;

        public static void Add(Process p)
        {
            try
            {
                if (handle == IntPtr.Zero)
                {
                    handle = CreateJobObject(IntPtr.Zero, null);
                    var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                    info.BasicLimitInformation.LimitFlags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                    int size = Marshal.SizeOf(info);
                    IntPtr ptr = Marshal.AllocHGlobal(size);
                    Marshal.StructureToPtr(info, ptr, false);
                    SetInformationJobObject(handle, 9, ptr, (uint)size); // JobObjectExtendedLimitInformation
                    Marshal.FreeHGlobal(ptr);
                }
                AssignProcessToJobObject(handle, p.Handle);
            }
            catch (Exception ex) { Program.Log("Could not attach ssh.exe to a job: " + ex.Message); }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS
        {
            public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateJobObject(IntPtr attributes, string name);

        [DllImport("kernel32.dll")]
        private static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);

        [DllImport("kernel32.dll")]
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    }
}
