using PCWheelReceiver.Networking;
using PCWheelReceiver.Output;
using PCWheelReceiver.Protocol;
using PCWheelReceiver.UI;

namespace PCWheelReceiver;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        try
        {
            var configPath = Path.Combine(AppContext.BaseDirectory, "protocol.json");
            var config = ProtocolConfig.Load(configPath);
            using var output = new Xbox360Output(config.Output);
            using var receiver = new UdpReceiverService(config, output);
            Application.Run(new MainForm(config, receiver, output));
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.ToString(),
                "PC Wheel Receiver failed to start",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
