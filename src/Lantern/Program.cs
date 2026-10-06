namespace Lantern;

internal static class Program
{
    /// <summary>
    /// Entry point (F1). Shows the startup window; the startup window decides
    /// whether this process becomes the host or a client.
    /// </summary>
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new UI.StartupForm());
    }
}
