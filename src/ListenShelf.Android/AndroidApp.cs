using Android.Runtime;
using Avalonia;
using Avalonia.Android;

namespace ListenShelf.Android;

[global::Android.App.Application]
public sealed class AndroidApp(nint javaReference, JniHandleOwnership transfer)
    : AvaloniaAndroidApplication<App>(javaReference, transfer)
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) => base.CustomizeAppBuilder(builder).WithInterFont();
}
