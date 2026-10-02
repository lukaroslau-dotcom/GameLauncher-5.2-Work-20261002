using System.Diagnostics;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (!args.Contains("--child"))
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--child") { UseShellExecute = false });
            Thread.Sleep(1200);
            return;
        }
        ApplicationConfiguration.Initialize();
        using var window = new Form
        {
            Text = "GameLauncher process fixture", Width = 560, Height = 280,
            StartPosition = FormStartPosition.CenterScreen, BackColor = Color.FromArgb(27, 32, 32),
            ForeColor = Color.White
        };
        window.Controls.Add(new Label { Text = "Test-Spielprozess", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 24) });
        using var timer = new System.Windows.Forms.Timer { Interval = 14000 };
        timer.Tick += (_, _) => window.Close();
        timer.Start();
        Application.Run(window);
    }
}
