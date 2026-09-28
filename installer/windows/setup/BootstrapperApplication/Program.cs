using WixToolset.BootstrapperApplicationApi;

namespace GaltekClassroom.Bootstrapper
{
    internal static class Program
    {
        private static int Main()
        {
            ManagedBootstrapperApplication.Run(new GaltekBootstrapperApplication());
            return 0;
        }
    }
}
