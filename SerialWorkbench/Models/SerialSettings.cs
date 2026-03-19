using System.IO.Ports;

namespace SerialWorkbench.Models;

public sealed class SerialSettings
{
    public string PortName { get; set; } = string.Empty;
    public int BaudRate { get; set; } = 115200;
    public int DataBits { get; set; } = 8;
    public StopBits StopBits { get; set; } = StopBits.One;
    public Parity Parity { get; set; } = Parity.None;
    public Handshake Handshake { get; set; } = Handshake.None;
    public bool DtrEnable { get; set; }
    public bool RtsEnable { get; set; }
    public string EncodingName { get; set; } = "UTF-8";
    public bool SendHex { get; set; }
    public bool ReceiveHex { get; set; }

    public SerialSettings Clone()
    {
        return (SerialSettings)MemberwiseClone();
    }
}
