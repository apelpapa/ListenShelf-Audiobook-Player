using Avalonia;
using System;
using ListenShelf.Playback.LibVlc;

namespace ListenShelf.Desktop
{
    internal sealed class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static int Main(string[] args)
        {
            if (args.Length == 1
                && string.Equals(
                    args[0],
                    "--verify-native-runtime",
                    StringComparison.Ordinal))
            {
                return VerifyNativeRuntime();
            }

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            return 0;
        }

        private static int VerifyNativeRuntime()
        {
            try
            {
                using var audioEngine = new LibVlcAudioEngine();
                Console.WriteLine(
                    $"LibVLC initialized successfully for {LibVlcRuntimeLocator.RuntimeDescription}.");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
#if DEBUG
                .WithDeveloperTools()
#endif
                .WithInterFont()
                .LogToTrace();
    }
}
