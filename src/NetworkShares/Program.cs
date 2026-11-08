namespace NetworkShares;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // The elevated helper the window starts for the work that needs administrator rights (see Backend.cs).
        if (args is ["--helper", var pipe, var parent] && int.TryParse(parent, out int parentPid))
            return Helper.Run(pipe, parentPid);

        ApplicationConfiguration.Initialize();
#pragma warning disable WFO5001 // dark mode is still flagged experimental
        Application.SetColorMode(SystemColorMode.Dark);
#pragma warning restore WFO5001
        Application.Run(new MainForm());
        return 0;
    }
}
