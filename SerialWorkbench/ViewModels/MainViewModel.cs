using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using SerialWorkbench.Infrastructure;
using SerialWorkbench.Models;
using SerialWorkbench.Services;

namespace SerialWorkbench.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly SerialPortService _serialPortService;
    private readonly ProtocolParserService _protocolParserService;
    private readonly RecordFileService _recordFileService;
    private readonly ReplayService _replayService;
    private readonly AutoSaveService _autoSaveService;
    private readonly List<SerialDataFrame> _allFrames = new();
    private readonly CancellationTokenSource _disposeCts = new();

    private string _selectedPort = string.Empty;
    private int _selectedBaudRate = 115200;
    private int _selectedDataBits = 8;
    private StopBits _selectedStopBits = StopBits.One;
    private Parity _selectedParity = Parity.None;
    private Handshake _selectedHandshake = Handshake.None;
    private string _selectedEncoding = "UTF-8";
    private bool _dtrEnable;
    private bool _rtsEnable;
    private bool _sendAsHex;
    private bool _receiveAsHex;
    private bool _autoSaveEnabled;
    private bool _keepReplayInterval = true;
    private bool _isConnected;
    private string _sendText = "AA 01 02 10 00 B9";
    private string _statusMessage = "等待连接串口。";
    private string _receiveText = string.Empty;
    private string _autoSaveFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "SerialWorkbenchLogs");
    private double _latestValue;
    private double _maxValue;
    private double _minValue;

    public MainViewModel()
        : this(new SerialPortService(), new ProtocolParserService(), new RecordFileService(), new ReplayService())
    {
    }

    public MainViewModel(
        SerialPortService serialPortService,
        ProtocolParserService protocolParserService,
        RecordFileService recordFileService,
        ReplayService replayService)
    {
        _serialPortService = serialPortService;
        _protocolParserService = protocolParserService;
        _recordFileService = recordFileService;
        _replayService = replayService;
        _autoSaveService = new AutoSaveService(_recordFileService);

        AvailablePorts = new ObservableCollection<string>();
        BaudRates = new ObservableCollection<int>(new[] { 9600, 19200, 38400, 57600, 115200, 230400 });
        DataBitsOptions = new ObservableCollection<int>(new[] { 5, 6, 7, 8 });
        StopBitsOptions = new ObservableCollection<StopBits>(Enum.GetValues<StopBits>().Where(item => item != StopBits.None));
        ParityOptions = new ObservableCollection<Parity>(Enum.GetValues<Parity>());
        HandshakeOptions = new ObservableCollection<Handshake>(Enum.GetValues<Handshake>());
        EncodingOptions = new ObservableCollection<string>(new[] { "UTF-8", "ASCII", "Unicode", "GB2312" });
        CommunicationLog = new ObservableCollection<string>();
        ParsedPackets = new ObservableCollection<ProtocolPacket>();
        VisualizationPoints = new ObservableCollection<DataPointModel>();

        RefreshPortsCommand = new RelayCommand(RefreshPorts);
        ConnectCommand = new RelayCommand(Connect, () => !_isConnected);
        DisconnectCommand = new RelayCommand(Disconnect, () => _isConnected);
        SendCommand = new RelayCommand(SendData, () => _isConnected && !string.IsNullOrWhiteSpace(_sendText));
        SaveNowCommand = new RelayCommand(async () => await SaveNowAsync());
        LoadAndReplayCommand = new RelayCommand(async () => await LoadAndReplayAsync());
        ClearCommand = new RelayCommand(ClearData);

        _serialPortService.FrameReceived += OnFrameReceived;
        _serialPortService.ConnectionChanged += OnConnectionChanged;
        _serialPortService.ErrorOccurred += (_, message) => UpdateStatus(message);

        RefreshPorts();
        UpdateStatistics();
    }

    public ObservableCollection<string> AvailablePorts { get; }
    public ObservableCollection<int> BaudRates { get; }
    public ObservableCollection<int> DataBitsOptions { get; }
    public ObservableCollection<StopBits> StopBitsOptions { get; }
    public ObservableCollection<Parity> ParityOptions { get; }
    public ObservableCollection<Handshake> HandshakeOptions { get; }
    public ObservableCollection<string> EncodingOptions { get; }
    public ObservableCollection<string> CommunicationLog { get; }
    public ObservableCollection<ProtocolPacket> ParsedPackets { get; }
    public ObservableCollection<DataPointModel> VisualizationPoints { get; }

    public RelayCommand RefreshPortsCommand { get; }
    public RelayCommand ConnectCommand { get; }
    public RelayCommand DisconnectCommand { get; }
    public RelayCommand SendCommand { get; }
    public RelayCommand SaveNowCommand { get; }
    public RelayCommand LoadAndReplayCommand { get; }
    public RelayCommand ClearCommand { get; }

    public string SelectedPort
    {
        get => _selectedPort;
        set => SetProperty(ref _selectedPort, value);
    }

    public int SelectedBaudRate
    {
        get => _selectedBaudRate;
        set => SetProperty(ref _selectedBaudRate, value);
    }

    public int SelectedDataBits
    {
        get => _selectedDataBits;
        set => SetProperty(ref _selectedDataBits, value);
    }

    public StopBits SelectedStopBits
    {
        get => _selectedStopBits;
        set => SetProperty(ref _selectedStopBits, value);
    }

    public Parity SelectedParity
    {
        get => _selectedParity;
        set => SetProperty(ref _selectedParity, value);
    }

    public Handshake SelectedHandshake
    {
        get => _selectedHandshake;
        set => SetProperty(ref _selectedHandshake, value);
    }

    public string SelectedEncoding
    {
        get => _selectedEncoding;
        set => SetProperty(ref _selectedEncoding, value);
    }

    public bool DtrEnable
    {
        get => _dtrEnable;
        set => SetProperty(ref _dtrEnable, value);
    }

    public bool RtsEnable
    {
        get => _rtsEnable;
        set => SetProperty(ref _rtsEnable, value);
    }

    public bool SendAsHex
    {
        get => _sendAsHex;
        set => SetProperty(ref _sendAsHex, value);
    }

    public bool ReceiveAsHex
    {
        get => _receiveAsHex;
        set => SetProperty(ref _receiveAsHex, value);
    }

    public bool AutoSaveEnabled
    {
        get => _autoSaveEnabled;
        set => SetProperty(ref _autoSaveEnabled, value);
    }

    public bool KeepReplayInterval
    {
        get => _keepReplayInterval;
        set => SetProperty(ref _keepReplayInterval, value);
    }

    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (SetProperty(ref _isConnected, value))
            {
                ConnectCommand.NotifyCanExecuteChanged();
                DisconnectCommand.NotifyCanExecuteChanged();
                SendCommand.NotifyCanExecuteChanged();
                RaisePropertyChanged(nameof(ConnectionText));
            }
        }
    }

    public string ConnectionText => _isConnected ? "已连接" : "未连接";

    public string SendText
    {
        get => _sendText;
        set
        {
            if (SetProperty(ref _sendText, value))
            {
                SendCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string ReceiveText
    {
        get => _receiveText;
        private set => SetProperty(ref _receiveText, value);
    }

    public string AutoSaveFolder
    {
        get => _autoSaveFolder;
        set => SetProperty(ref _autoSaveFolder, value);
    }

    public double LatestValue
    {
        get => _latestValue;
        private set => SetProperty(ref _latestValue, value);
    }

    public double MaxValue
    {
        get => _maxValue;
        private set => SetProperty(ref _maxValue, value);
    }

    public double MinValue
    {
        get => _minValue;
        private set => SetProperty(ref _minValue, value);
    }

    public void Dispose()
    {
        _disposeCts.Cancel();
        _serialPortService.Dispose();
        _disposeCts.Dispose();
    }

    private void RefreshPorts()
    {
        AvailablePorts.Clear();
        foreach (var port in _serialPortService.ScanPorts())
        {
            AvailablePorts.Add(port);
        }

        if (string.IsNullOrWhiteSpace(SelectedPort))
        {
            SelectedPort = AvailablePorts.FirstOrDefault() ?? string.Empty;
        }

        UpdateStatus(AvailablePorts.Count == 0 ? "未发现串口，请检查设备连接。" : $"扫描到 {AvailablePorts.Count} 个串口。");
    }

    private void Connect()
    {
        if (string.IsNullOrWhiteSpace(SelectedPort))
        {
            UpdateStatus("请先选择串口。 ");
            return;
        }

        _serialPortService.Connect(BuildSettings());
    }

    private void Disconnect()
    {
        _serialPortService.Disconnect();
    }

    private void SendData()
    {
        try
        {
            _serialPortService.Send(SendText, BuildSettings());
            UpdateStatus("发送数据成功。");
        }
        catch (Exception ex)
        {
            UpdateStatus($"发送失败：{ex.Message}");
        }
    }

    private async Task SaveNowAsync()
    {
        try
        {
            var dialog = new SaveFileDialog
            {
                Filter = "记录文件|*.log|所有文件|*.*",
                FileName = $"record_{DateTime.Now:yyyyMMdd_HHmmss}.log"
            };

            if (dialog.ShowDialog() == true)
            {
                await _recordFileService.SaveFramesAsync(_allFrames, dialog.FileName, _disposeCts.Token);
                UpdateStatus($"数据已保存到 {dialog.FileName}");
            }
        }
        catch (Exception ex)
        {
            UpdateStatus($"手动保存失败：{ex.Message}");
        }
    }

    private async Task LoadAndReplayAsync()
    {
        try
        {
            var dialog = new OpenFileDialog
            {
                Filter = "记录文件|*.log|所有文件|*.*"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            var frames = await _recordFileService.LoadFramesAsync(dialog.FileName, _disposeCts.Token);
            await _replayService.ReplayAsync(frames, frame =>
            {
                AppendFrame(frame, isReplay: true);
                return Task.CompletedTask;
            }, KeepReplayInterval, _disposeCts.Token);

            UpdateStatus($"已完成回放：{dialog.FileName}");
        }
        catch (Exception ex)
        {
            UpdateStatus($"回放失败：{ex.Message}");
        }
    }

    private void ClearData()
    {
        _allFrames.Clear();
        CommunicationLog.Clear();
        ParsedPackets.Clear();
        VisualizationPoints.Clear();
        ReceiveText = string.Empty;
        UpdateStatistics();
        UpdateStatus("界面数据已清空。");
    }

    private void OnFrameReceived(object? sender, SerialDataFrame frame)
    {
        Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            AppendFrame(frame, isReplay: false);

            if (AutoSaveEnabled)
            {
                await _autoSaveService.TryAutoSaveAsync(_allFrames, AutoSaveFolder, TimeSpan.FromMinutes(1), _disposeCts.Token);
                UpdateStatus($"自动保存已检查：{AutoSaveFolder}");
            }
        });
    }

    private void AppendFrame(SerialDataFrame frame, bool isReplay)
    {
        _allFrames.Add(frame);

        var displayText = ReceiveAsHex || frame.Direction == "TX"
            ? BitConverter.ToString(frame.Payload).Replace('-', ' ')
            : Encoding.GetEncoding(SelectedEncoding).GetString(frame.Payload);

        CommunicationLog.Add($"[{frame.Timestamp:HH:mm:ss.fff}] {frame.Direction} {displayText}");
        ReceiveText = string.Join(Environment.NewLine, CommunicationLog.TakeLast(100));

        if (frame.Direction == "RX")
        {
            var packet = _protocolParserService.Parse(frame);
            ParsedPackets.Add(packet);
            if (packet.NumericValue is double numeric)
            {
                VisualizationPoints.Add(new DataPointModel { Timestamp = packet.Timestamp, Value = numeric });
                while (VisualizationPoints.Count > 30)
                {
                    VisualizationPoints.RemoveAt(0);
                }
            }

            UpdateStatistics();
        }

        if (isReplay)
        {
            UpdateStatus("正在执行历史记录回放。");
        }
    }

    private void OnConnectionChanged(object? sender, bool connected)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            IsConnected = connected;
            UpdateStatus(connected ? $"串口 {SelectedPort} 已连接。" : "串口已断开。");
        });
    }

    private SerialSettings BuildSettings()
    {
        return new SerialSettings
        {
            PortName = SelectedPort,
            BaudRate = SelectedBaudRate,
            DataBits = SelectedDataBits,
            StopBits = SelectedStopBits,
            Parity = SelectedParity,
            Handshake = SelectedHandshake,
            DtrEnable = DtrEnable,
            RtsEnable = RtsEnable,
            EncodingName = SelectedEncoding,
            SendHex = SendAsHex,
            ReceiveHex = ReceiveAsHex
        };
    }

    private void UpdateStatistics()
    {
        if (VisualizationPoints.Count == 0)
        {
            LatestValue = 0;
            MaxValue = 0;
            MinValue = 0;
            return;
        }

        LatestValue = VisualizationPoints.Last().Value;
        MaxValue = VisualizationPoints.Max(item => item.Value);
        MinValue = VisualizationPoints.Min(item => item.Value);
    }

    private void UpdateStatus(string message)
    {
        StatusMessage = $"{DateTime.Now:HH:mm:ss} {message}";
    }
}
