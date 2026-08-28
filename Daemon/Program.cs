using System;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    private const string SocketPath = "/run/warp-gacha.sock";
    private static readonly string[] RobloxNodes = { "128.116.50.1", "128.116.97.1", "103.140.28.8" };
    private static CancellationTokenSource? _cts;
    private static readonly object _lock = new object();
    private static bool _isRerolling = false;
    private static string _activeTarget = "";
    private static int _currentAttempt = 0;

    static async Task Main(string[] args)
    {
        CleanupSocket();
        AppDomain.CurrentDomain.ProcessExit += (s, e) => CleanupSocket();

        using var server = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.IP);
        var endPoint = new UnixDomainSocketEndPoint(SocketPath);
        server.Bind(endPoint);

        if (OperatingSystem.IsLinux())
        {
            try
            {
                File.SetUnixFileMode(
                    SocketPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite |
                    UnixFileMode.GroupRead | UnixFileMode.GroupWrite |
                    UnixFileMode.OtherRead | UnixFileMode.OtherWrite
                );
            }
            catch { }
        }

        server.Listen(10);
        Console.WriteLine($"[+] WarpGacha Daemon active on {SocketPath}");

        while (true)
        {
            try
            {
                var client = await server.AcceptAsync();
                _ = Task.Run(() => HandleClientAsync(client));
            }
            catch { }
        }
    }

    private static void CleanupSocket()
    {
        try
        {
            if (File.Exists(SocketPath)) File.Delete(SocketPath);
        }
        catch { }
    }

    private static async Task HandleClientAsync(Socket client)
    {
        try
        {
            var buffer = new byte[1024];
            int received = await client.ReceiveAsync(buffer, SocketFlags.None);
            if (received <= 0) return;

            string cmd = Encoding.UTF8.GetString(buffer, 0, received).Trim().ToUpperInvariant();

            if (cmd == "CHECK")
            {
                lock (_lock)
                {
                    if (_isRerolling)
                    {
                        SendMsg(client, $"BUSY:{_activeTarget}:{_currentAttempt}\n");
                    }
                    else
                    {
                        SendMsg(client, "IDLE\n");
                    }
                }
                return;
            }

            if (cmd == "CANCEL" || cmd == "FORCE_KILL")
            {
                Console.WriteLine("[!] Stop reroll signal received. Terminating sequence immediately...");
                lock (_lock)
                {
                    _cts?.Cancel();
                    _isRerolling = false;
                }
                await SendMsgAsync(client, "STOPPED\n");
                return;
            }

            lock (_lock)
            {
                _cts?.Cancel();
                _cts = new CancellationTokenSource();
                _isRerolling = true;
                _activeTarget = cmd;
                _currentAttempt = 0;
            }

            Console.WriteLine($"[*] Rerolling for target region: {cmd} (Strict 10 Limit)");
            await RerollAsync(cmd, client, _cts.Token);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[!] Error handling client: {ex.Message}");
        }
        finally
        {
            client.Close();
        }
    }

    private static async Task RerollAsync(string target, Socket client, CancellationToken token)
    {
        try
        {
            for (int i = 1; i <= 10; i++)
            {
                lock (_lock) { _currentAttempt = i; }

                if (token.IsCancellationRequested)
                {
                    await SendMsgAsync(client, "CANCELLED\n");
                    return;
                }

                await SendMsgAsync(client, $"PROGRESS:{i}\n");

                await RunCmdAsync("warp-cli", "--accept-tos disconnect");
                
                try { await Task.Delay(600, token); }
                catch (TaskCanceledException) { await SendMsgAsync(client, "CANCELLED\n"); return; }

                if (token.IsCancellationRequested) { await SendMsgAsync(client, "CANCELLED\n"); return; }

                await RunCmdAsync("warp-cli", "--accept-tos connect");

                try { await Task.Delay(2000, token); }
                catch (TaskCanceledException) { await SendMsgAsync(client, "CANCELLED\n"); return; }

                if (token.IsCancellationRequested) { await SendMsgAsync(client, "CANCELLED\n"); return; }

                string? currentColo = await GetCurrentColoAsync();
                Console.WriteLine($"     Result {i}/10: Colo = '{currentColo ?? "CONNECTING..."}'");

                if (!string.IsNullOrEmpty(currentColo) && currentColo.Equals(target, StringComparison.OrdinalIgnoreCase))
                {
                    if (CheckPing())
                    {
                        await SendMsgAsync(client, "SUCCESS\n");
                        return;
                    }
                }
            }

            await SendMsgAsync(client, "FAILED\n");
        }
        finally
        {
            lock (_lock)
            {
                _isRerolling = false;
            }
        }
    }

    private static void SendMsg(Socket client, string message)
    {
        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(message);
            client.Send(bytes);
        }
        catch { }
    }

    private static async Task SendMsgAsync(Socket client, string message)
    {
        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(message);
            await client.SendAsync(bytes, SocketFlags.None);
        }
        catch { }
    }

    private static bool CheckPing()
    {
        using var ping = new Ping();
        foreach (var node in RobloxNodes)
        {
            try
            {
                var reply = ping.Send(node, 1000);
                if (reply.Status == IPStatus.Success) return true;
            }
            catch { }
        }
        return true;
    }

    private static async Task<string?> GetCurrentColoAsync()
    {
        string output = await RunCmdCaptureAsync("warp-cli", "--accept-tos status") ?? "";
        if (!output.Contains("Colo:", StringComparison.OrdinalIgnoreCase))
        {
            output += "\n" + (await RunCmdCaptureAsync("warp-cli", "--accept-tos tunnel stats") ?? "");
        }

        var match = Regex.Match(output, @"Colo:\s*([A-Za-z0-9]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim().ToUpperInvariant() : null;
    }

    private static async Task RunCmdAsync(string exe, string args)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.EnvironmentVariables["PATH"] = "/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin";

        using var p = Process.Start(psi);
        if (p != null) await p.WaitForExitAsync();
    }

    private static async Task<string?> RunCmdCaptureAsync(string exe, string args)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.EnvironmentVariables["PATH"] = "/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin";

        using var p = Process.Start(psi);
        if (p == null) return null;
        string output = await p.StandardOutput.ReadToEndAsync();
        await p.WaitForExitAsync();
        return output;
    }
}
