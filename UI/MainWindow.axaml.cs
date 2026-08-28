using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace WarpGachaUI
{
    public partial class MainWindow : Window
    {
        private const string SocketPath = "/run/warp-gacha.sock";

        public MainWindow()
        {
            InitializeComponent();
        }

        private async void OnStartClick(object sender, RoutedEventArgs e)
        {
            StatusLabel.Text = "Status: Connecting to Daemon...";
            string target = TargetInput.Text?.Trim() ?? "SIN";

            if (!File.Exists(SocketPath))
            {
                StatusLabel.Text = "Error: Daemon socket missing or pkexec failed.";
                return;
            }

            StatusLabel.Text = $"Status: Rerolling for {target}... (Checking Nodes)";

            string result = await Task.Run(() => SendRequest(target));

            Dispatcher.UIThread.Post(() =>
            {
                StatusLabel.Text = result.Trim() == "SUCCESS" 
                    ? $"Status: LOCKED ONTO {target}!" 
                    : $"Status: Failed to find {target}. Try again.";
            });
        }

        private string SendRequest(string target)
        {
            try
            {
                using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.IP);
                var endPoint = new UnixDomainSocketEndPoint(SocketPath);
                
                // Set long timeout to allow all 20 reroll cycles to complete
                client.ReceiveTimeout = 60000;
                client.SendTimeout = 5000;

                client.Connect(endPoint);
                client.Send(Encoding.UTF8.GetBytes(target));

                var buffer = new byte[256];
                int received = client.Receive(buffer);
                return Encoding.UTF8.GetString(buffer, 0, received);
            }
            catch (Exception ex)
            {
                return $"Daemon error: {ex.Message}";
            }
        }
    }
}
