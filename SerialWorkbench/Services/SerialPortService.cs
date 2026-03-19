using System.IO.Ports;
using System.Text;
using SerialWorkbench.Models;

namespace SerialWorkbench.Services;

public sealed class SerialPortService : IDisposable
{
    private readonly SerialPort _serialPort = new();

    public event EventHandler<SerialDataFrame>? FrameReceived;
    public event EventHandler<bool>? ConnectionChanged;
    public event EventHandler<string>? ErrorOccurred;

    public bool IsOpen => _serialPort.IsOpen;

    public IReadOnlyList<string> ScanPorts()
    {
        return SerialPort.GetPortNames().OrderBy(port => port).ToArray();
    }

    public void Connect(SerialSettings settings)
    {
        if (IsOpen)
        {
            Disconnect();
        }

        try
        {
            _serialPort.PortName = settings.PortName;
            _serialPort.BaudRate = settings.BaudRate;
            _serialPort.DataBits = settings.DataBits;
            _serialPort.StopBits = settings.StopBits;
            _serialPort.Parity = settings.Parity;
            _serialPort.Handshake = settings.Handshake;
            _serialPort.DtrEnable = settings.DtrEnable;
            _serialPort.RtsEnable = settings.RtsEnable;
            _serialPort.Encoding = Encoding.GetEncoding(settings.EncodingName);
            _serialPort.DataReceived -= OnDataReceived;
            _serialPort.DataReceived += OnDataReceived;
            _serialPort.Open();
            ConnectionChanged?.Invoke(this, true);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"串口连接失败：{ex.Message}");
            ConnectionChanged?.Invoke(this, false);
        }
    }

    public void Disconnect()
    {
        try
        {
            _serialPort.DataReceived -= OnDataReceived;
            if (_serialPort.IsOpen)
            {
                _serialPort.Close();
            }
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"串口关闭失败：{ex.Message}");
        }
        finally
        {
            ConnectionChanged?.Invoke(this, false);
        }
    }

    public void Send(string text, SerialSettings settings)
    {
        if (!IsOpen)
        {
            throw new InvalidOperationException("串口未连接。");
        }

        var payload = settings.SendHex
            ? ConvertHexString(text)
            : Encoding.GetEncoding(settings.EncodingName).GetBytes(text);

        _serialPort.Write(payload, 0, payload.Length);
        FrameReceived?.Invoke(this, new SerialDataFrame
        {
            Direction = "TX",
            Payload = payload,
            PreviewText = settings.SendHex ? BitConverter.ToString(payload).Replace('-', ' ') : text
        });
    }

    private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        try
        {
            var count = _serialPort.BytesToRead;
            if (count <= 0)
            {
                return;
            }

            var buffer = new byte[count];
            _serialPort.Read(buffer, 0, count);
            FrameReceived?.Invoke(this, new SerialDataFrame
            {
                Direction = "RX",
                Payload = buffer,
                PreviewText = BitConverter.ToString(buffer).Replace('-', ' ')
            });
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"串口接收失败：{ex.Message}");
        }
    }

    private static byte[] ConvertHexString(string text)
    {
        var normalized = text.Replace(" ", string.Empty).Replace("-", string.Empty);
        if (normalized.Length % 2 != 0)
        {
            throw new FormatException("HEX 字符串长度必须为偶数。");
        }

        var data = new byte[normalized.Length / 2];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = Convert.ToByte(normalized.Substring(i * 2, 2), 16);
        }

        return data;
    }

    public void Dispose()
    {
        Disconnect();
        _serialPort.Dispose();
    }
}
