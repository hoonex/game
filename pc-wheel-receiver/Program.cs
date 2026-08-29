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

            IControllerOutput output;
            try
            {
                output = new Xbox360Output(config.Output);
            }
            catch (Exception ex)
            {
                output = new DiagnosticOutput(
                    "Virtual Xbox output unavailable. Install ViGEmBus, then restart. " +
                    $"Driver/API error: {ex.Message}");
            }

            using (output)
            using (var receiver = new UdpReceiverService(config, output))
            {
                Application.Run(new MainForm(configPath, config, receiver, output));
            }
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
