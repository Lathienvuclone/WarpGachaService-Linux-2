using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Themes.Simple;
using Avalonia.Threading;

namespace UI
{
    class Program
    {
        [STAThread]
        public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace();
    }

    public class App : Application
    {
        public override void Initialize()
        {
            Styles.Add(new SimpleTheme());
            RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow();
            }
            base.OnFrameworkInitializationCompleted();
        }
    }

    public class MainWindow : Window
    {
        private readonly TextBox _targetInput;
        private readonly Button _startBtn;
        private readonly Button _stopBtn;
        private readonly TextBlock _statusLabel;
        private readonly TextBlock _subStatusLabel;
        private readonly Border _statusCard;

        public MainWindow()
        {
            Title = "Cloudflare WARP Gacha";
            Width = 460;
            Height = 440;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = SolidColorBrush.Parse("#0d1117");

            Closing += (sender, e) =>
            {
                try
                {
                    var psiDaemon = new ProcessStartInfo("sudo", "-n pkill -9 -f Daemon")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    Process.Start(psiDaemon)?.WaitForExit(500);

                    var psiWarp = new ProcessStartInfo("warp-cli", "--accept-tos connect")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    Process.Start(psiWarp)?.WaitForExit(500);
                }
                catch { }

                Process.GetCurrentProcess().Kill();
            };

            var mainContainer = new Border
            {
                Background = SolidColorBrush.Parse("#161b22"),
                CornerRadius = new CornerRadius(12),
                Margin = new Thickness(16),
                Padding = new Thickness(20),
                BorderBrush = SolidColorBrush.Parse("#30363d"),
                BorderThickness = new Thickness(1)
            };

            var mainLayout = new StackPanel { Spacing = 16 };

            var headerBlock = new StackPanel { Spacing = 4 };
            headerBlock.Children.Add(new TextBlock
            {
                Text = "WARP EDGE ROUTER",
                FontSize = 18,
                FontWeight = FontWeight.Black,
                Foreground = SolidColorBrush.Parse("#58a6ff"),
                LetterSpacing = 1.2
            });
            headerBlock.Children.Add(new TextBlock
            {
                Text = "Cloudflare WireGuard Node Selector",
                FontSize = 11,
                Foreground = SolidColorBrush.Parse("#8b949e")
            });
            mainLayout.Children.Add(headerBlock);

            var inputGroup = new StackPanel { Spacing = 6 };
            inputGroup.Children.Add(new TextBlock
            {
                Text = "TARGET COLO CODE",
                FontSize = 10,
                FontWeight = FontWeight.Bold,
                Foreground = SolidColorBrush.Parse("#8f9ba8")
            });

            _targetInput = new TextBox
            {
                Text = "SIN",
                FontSize = 16,
                FontWeight = FontWeight.Bold,
                Background = SolidColorBrush.Parse("#21262d"),
                Foreground = SolidColorBrush.Parse("#ffffff"),
                BorderBrush = SolidColorBrush.Parse("#30363d"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 8)
            };
            inputGroup.Children.Add(_targetInput);

            var presetWrap = new WrapPanel { Orientation = Orientation.Horizontal };
            string[] presets = { "HKG", "SIN", "NRT", "KUL", "ICN", "LAX" };
            foreach (var colo in presets)
            {
                var btn = new Button
                {
                    Content = colo,
                    Margin = new Thickness(0, 0, 6, 6),
                    Padding = new Thickness(10, 4),
                    FontSize = 11,
                    FontWeight = FontWeight.Bold,
                    Background = SolidColorBrush.Parse("#21262d"),
                    Foreground = SolidColorBrush.Parse("#58a6ff"),
                    BorderBrush = SolidColorBrush.Parse("#30363d"),
                    BorderThickness = new Thickness(1)
                };
                btn.Click += (s, e) => _targetInput.Text = colo;
                presetWrap.Children.Add(btn);
            }
            inputGroup.Children.Add(presetWrap);
            mainLayout.Children.Add(inputGroup);

            var btnGrid = new Grid();
            btnGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            btnGrid.ColumnDefinitions.Add(new ColumnDefinition(12, GridUnitType.Pixel));
            btnGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));

            _startBtn = new Button
            {
                Content = "EXECUTE",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Padding = new Thickness(10),
                FontSize = 12,
                FontWeight = FontWeight.Black,
                Background = SolidColorBrush.Parse("#238636"),
                Foreground = Brushes.White,
                CornerRadius = new CornerRadius(6)
            };
            _startBtn.Click += OnStartClick;
            Grid.SetColumn(_startBtn, 0);

            _stopBtn = new Button
            {
                Content = "STOP REROLL",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Padding = new Thickness(10),
                FontSize = 12,
                FontWeight = FontWeight.Black,
                Background = SolidColorBrush.Parse("#da3633"),
                Foreground = Brushes.White,
                CornerRadius = new CornerRadius(6),
                IsEnabled = false
            };
            _stopBtn.Click += OnStopClick;
            Grid.SetColumn(_stopBtn, 2);

            btnGrid.Children.Add(_startBtn);
            btnGrid.Children.Add(_stopBtn);
            mainLayout.Children.Add(btnGrid);

            _statusCard = new Border
            {
                Background = SolidColorBrush.Parse("#0d1117"),
                BorderBrush = SolidColorBrush.Parse("#30363d"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(14)
            };

            var statusStack = new StackPanel { Spacing = 4 };
            _statusLabel = new TextBlock
            {
                Text = "CHECKING SYSTEM STATUS...",
                FontSize = 12,
                FontWeight = FontWeight.Bold,
                Foreground = SolidColorBrush.Parse("#8b949e"),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            _subStatusLabel = new TextBlock
            {
                Text = "Scanning active rerolls...",
                FontSize = 11,
                Foreground = SolidColorBrush.Parse("#484f58"),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            statusStack.Children.Add(_statusLabel);
            statusStack.Children.Add(_subStatusLabel);
            _statusCard.Child = statusStack;

            mainLayout.Children.Add(_statusCard);
            mainContainer.Child = mainLayout;
            Content = mainContainer;

            Task.Run(() => CheckActiveRerollOnStartup());
        }

        private void CheckActiveRerollOnStartup()
        {
            try
            {
                using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.IP);
                var endPoint = new UnixDomainSocketEndPoint("/run/warp-gacha.sock");
                client.Connect(endPoint);
                client.Send(Encoding.UTF8.GetBytes("CHECK"));

                byte[] buffer = new byte[1024];
                int received = client.Receive(buffer);
                string response = Encoding.UTF8.GetString(buffer, 0, received).Trim();

                if (response.StartsWith("BUSY:"))
                {
                    string[] parts = response.Split(':');
                    string target = parts.Length > 1 ? parts[1] : "UNKNOWN";
                    string currentAttempt = parts.Length > 2 ? parts[2] : "1";

                    Dispatcher.UIThread.Post(() =>
                    {
                        _targetInput.Text = target;
                        _startBtn.IsEnabled = false;
                        _stopBtn.IsEnabled = true;
                        UpdateStatus($"ACTIVE REROLL DETECTED [{target}]", $"Reroll attempt {currentAttempt}/10", "#d29922");
                    });
                }
                else
                {
                    UpdateStatus("SYSTEM IDLE", "Select a region and hit execute.", "#8b949e");
                }
            }
            catch
            {
                UpdateStatus("SYSTEM READY", "Select a region and hit execute.", "#8b949e");
            }
        }

        private async void OnStartClick(object? sender, EventArgs e)
        {
            string targetRegion = _targetInput.Text?.Trim().ToUpperInvariant() ?? "";
            if (string.IsNullOrEmpty(targetRegion))
            {
                UpdateStatus("INVALID REGION", "Please enter a 3-letter Colo code", "#d29922");
                return;
            }

            _startBtn.IsEnabled = false;
            _stopBtn.IsEnabled = true;
            UpdateStatus($"REROLLING FOR [{targetRegion}]", "Connecting to stream...", "#58a6ff");

            await Task.Run(() => StreamRerollRequest(targetRegion));
        }

        private async void OnStopClick(object? sender, EventArgs e)
        {
            _stopBtn.IsEnabled = false;
            UpdateStatus("STOPPING REROLL...", "Sending cancellation signal...", "#d29922");
            await Task.Run(() => SendCancelSignal());
        }

        private void UpdateStatus(string title, string subtitle, string hexColor)
        {
            Dispatcher.UIThread.Post(() =>
            {
                var color = SolidColorBrush.Parse(hexColor);
                _statusLabel.Text = title;
                _statusLabel.Foreground = color;
                _subStatusLabel.Text = subtitle;
                _statusCard.BorderBrush = color;
            });
        }

        private void StreamRerollRequest(string targetRegion)
        {
            try
            {
                using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.IP);
                var endPoint = new UnixDomainSocketEndPoint("/run/warp-gacha.sock");
                client.Connect(endPoint);

                byte[] message = Encoding.UTF8.GetBytes(targetRegion);
                client.Send(message);

                using var stream = new NetworkStream(client);
                using var reader = new StreamReader(stream, Encoding.UTF8);

                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    line = line.Trim();

                    if (line.StartsWith("PROGRESS:"))
                    {
                        string attemptNum = line.Substring(9);
                        UpdateStatus($"REROLLING FOR [{targetRegion}]", $"Reroll attempt {attemptNum}/10", "#58a6ff");
                    }
                    else if (line == "SUCCESS")
                    {
                        UpdateStatus($"LOCKED ONTO [{targetRegion}]", "Routing active & latency verified!", "#3fb950");
                        break;
                    }
                    else if (line == "CANCELLED" || line == "STOPPED")
                    {
                        UpdateStatus("REROLL STOPPED", "Reroll sequence terminated by user.", "#d29922");
                        break;
                    }
                    else if (line == "FAILED")
                    {
                        UpdateStatus("REROLL FAILED", "Reached 10 attempts without matching region.", "#f85149");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                UpdateStatus("CONNECTION ERROR", ex.Message, "#f85149");
            }
            finally
            {
                Dispatcher.UIThread.Post(() =>
                {
                    _startBtn.IsEnabled = true;
                    _stopBtn.IsEnabled = false;
                });
            }
        }

        private void SendCancelSignal()
        {
            try
            {
                using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.IP);
                var endPoint = new UnixDomainSocketEndPoint("/run/warp-gacha.sock");
                client.Connect(endPoint);
                client.Send(Encoding.UTF8.GetBytes("CANCEL"));
            }
            catch { }
        }
    }
}
