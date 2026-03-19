using System.Text;

namespace SerialWorkbench;

public partial class App : System.Windows.Application
{
    public App()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }
}
