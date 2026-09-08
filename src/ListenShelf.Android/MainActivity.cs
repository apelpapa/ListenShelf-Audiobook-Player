using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Avalonia;
using Avalonia.Android;

namespace ListenShelf.Android;

[Activity(Name = "org.listenshelf.android.MainActivity", Label = "ListenShelf", Theme = "@style/ListenShelfTheme", MainLauncher = true,
    Exported = true, ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize
        | ConfigChanges.UiMode | ConfigChanges.KeyboardHidden | ConfigChanges.SmallestScreenSize
        | ConfigChanges.ScreenLayout | ConfigChanges.Density)]
public class MainActivity : AvaloniaMainActivity
{
    internal static MainActivity? Current { get; private set; }
    private TaskCompletionSource<global::Android.Net.Uri?>? _picker;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        Current = this;
        MobileSession.Initialize(ApplicationContext!);
        base.OnCreate(savedInstanceState);
    }

    public Task<global::Android.Net.Uri?> PickBookAsync()
    {
        if (_picker is not null) return _picker.Task;
        _picker = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var intent = new Intent(Intent.ActionOpenDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        // Some document providers report M4B as application/octet-stream.
        intent.SetType("*/*");
        try { StartActivityForResult(intent, 41); }
        catch { _picker = null; throw; }
        return _picker.Task;
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != 41) return;
        var picker = _picker;
        _picker = null;
        picker?.TrySetResult(resultCode == Result.Ok ? data?.Data : null);
    }

    protected override void OnStop()
    {
        if (MobileSession.StartupFailure is null) MobileSession.Instance.SavePosition();
        base.OnStop();
    }

    protected override void OnDestroy()
    {
        _picker?.TrySetResult(null);
        if (Current == this) Current = null;
        base.OnDestroy();
    }
}
