namespace NetworkShares;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
#pragma warning disable WFO5001 // dark mode is still flagged experimental
        Application.SetColorMode(SystemColorMode.Dark);
#pragma warning restore WFO5001
        Application.Run(new MainForm());
    }
}
