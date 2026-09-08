using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Media;
using Android.Media.Session;
using Android.OS;
using PlaybackState = Android.Media.Session.PlaybackState;
using PlayerState = ListenShelf.Application.Playback.PlaybackState;

namespace ListenShelf.Android;

[Service(Name = "org.listenshelf.android.PlaybackService", Exported = false, ForegroundServiceType = ForegroundService.TypeMediaPlayback)]
public sealed class PlaybackService : Service, AudioManager.IOnAudioFocusChangeListener
{
    public const string PlayAction = "org.listenshelf.PLAY";
    private const string PauseAction = "org.listenshelf.PAUSE";
    private const string BackAction = "org.listenshelf.BACK";
    private const string ForwardAction = "org.listenshelf.FORWARD";
    private const string StopAction = "org.listenshelf.STOP";
    private const string Channel = "listenshelf-playback";
    private const int NotificationId = 42;
    private MediaSession? _media;
    private AudioManager? _audio;
    private AudioFocusRequestClass? _focus;
    private PowerManager.WakeLock? _wakeLock;
    private bool _hasFocus;
    private bool _foreground;
    private bool _starting;
    private bool? _lastPlaying;
    private string? _lastTitle;
    private MobileSession Session => MobileSession.Instance;

    public override void OnCreate()
    {
        base.OnCreate();
        MobileSession.Initialize(this);
        _audio = (AudioManager)GetSystemService(AudioService)!;
        _focus = new AudioFocusRequestClass.Builder(AudioFocus.Gain)!
            .SetAudioAttributes(new AudioAttributes.Builder()!.SetUsage(AudioUsageKind.Media)!
                .SetContentType(AudioContentType.Speech)!.Build()!)!
            .SetOnAudioFocusChangeListener(this)!.SetWillPauseWhenDucked(true)!.Build();
        var manager = (NotificationManager)GetSystemService(NotificationService)!;
        manager.CreateNotificationChannel(new NotificationChannel(Channel, "Audiobook playback", NotificationImportance.Low));
        _media = new MediaSession(this, "ListenShelf");
        _media.SetCallback(new SessionCallback(this));
        _media.SetSessionActivity(OpenApp());
        _media.Active = true;
        var power = (PowerManager)GetSystemService(PowerService)!;
        _wakeLock = power.NewWakeLock(WakeLockFlags.Partial, "ListenShelf:playback");
        Session.Changed += Update;
        _noisy = new NoisyReceiver();
        var filter = new IntentFilter(AudioManager.ActionAudioBecomingNoisy);
        if (OperatingSystem.IsAndroidVersionAtLeast(33)) RegisterReceiver(_noisy, filter, ReceiverFlags.NotExported);
        else RegisterReceiver(_noisy, filter);
    }

    private NoisyReceiver? _noisy;
    public override IBinder? OnBind(Intent? intent) => null;
    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        // Start promptly, before audio focus is requested (required by Android 15+).
        StartForeground(NotificationId, CreateNotification());
        _foreground = true;
        switch (intent?.Action)
        {
            case PauseAction: _starting = false; Session.Pause(); break;
            case BackAction: Session.Skip(-Session.RewindSeconds); break;
            case ForwardAction: Session.Skip(Session.ForwardSeconds); break;
            case StopAction:
                _starting = false;
                Session.Pause();
                StopForeground(StopForegroundFlags.Remove);
                StopSelf();
                return StartCommandResult.NotSticky;
            case PlayAction:
                if (_audio!.RequestAudioFocus(_focus!) == AudioFocusRequest.Granted)
                {
                    _hasFocus = true;
                    _starting = true;
                    Session.PlayWithAudioFocus();
                }
                break;
        }
        Update();
        return StartCommandResult.NotSticky;
    }

    private void Update()
    {
        var playing = Session.IsPlaying;
        if (Session.State is PlayerState.Playing or PlayerState.Error or PlayerState.Ended) _starting = false;
        var active = playing || _starting;
        if (active && _wakeLock?.IsHeld == false) _wakeLock.Acquire();
        if (!active && _wakeLock?.IsHeld == true) _wakeLock.Release();
        if (!active && _hasFocus)
        {
            _audio?.AbandonAudioFocusRequest(_focus!);
            _hasFocus = false;
        }
        var state = new PlaybackState.Builder()!
            .SetActions(PlaybackState.ActionPlay | PlaybackState.ActionPause | PlaybackState.ActionPlayPause
                | PlaybackState.ActionSeekTo | PlaybackState.ActionRewind | PlaybackState.ActionFastForward
                | PlaybackState.ActionSkipToPrevious | PlaybackState.ActionSkipToNext | PlaybackState.ActionStop)!
            .SetState(playing ? PlaybackStateCode.Playing : PlaybackStateCode.Paused,
                (long)Session.Position.TotalMilliseconds, playing ? (float)Session.Engine.PlaybackRate : 0f)!;
        _media?.SetPlaybackState(state.Build());
        if (_lastTitle != Session.CurrentBook?.Title)
        {
            _media?.SetMetadata(new MediaMetadata.Builder()!
                .PutString(MediaMetadata.MetadataKeyTitle, Session.CurrentBook?.Title ?? "ListenShelf")!
                .PutString(MediaMetadata.MetadataKeyArtist, "ListenShelf")!
                .PutLong(MediaMetadata.MetadataKeyDuration, (long)Session.Engine.Duration.TotalMilliseconds)!.Build());
        }
        if (_lastPlaying != playing || _lastTitle != Session.CurrentBook?.Title)
        {
            ((NotificationManager)GetSystemService(NotificationService)!).Notify(NotificationId, CreateNotification());
            _lastPlaying = playing;
            _lastTitle = Session.CurrentBook?.Title;
        }
        if (!active && _foreground)
        {
            StopForeground(StopForegroundFlags.Detach);
            _foreground = false;
        }
    }

    private PendingIntent OpenApp() => PendingIntent.GetActivity(this, 0,
        new Intent(this, typeof(MainActivity)).AddFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop),
        PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;
    private PendingIntent Command(string action, int id) => PendingIntent.GetForegroundService(this, id,
        new Intent(this, typeof(PlaybackService)).SetAction(action), PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;

    private Notification CreateNotification()
    {
        var style = new Notification.MediaStyle().SetMediaSession(_media?.SessionToken)!.SetShowActionsInCompactView(0, 1, 2);
        return new Notification.Builder(this, Channel)
            .SetSmallIcon(Resource.Drawable.ic_notification)!
            .SetContentTitle(Session.CurrentBook?.Title ?? "ListenShelf")!
            .SetContentText(Session.IsPlaying ? "Listening" : "Paused")!
            .SetContentIntent(OpenApp())!.SetOnlyAlertOnce(true)!.SetOngoing(Session.IsPlaying)!
            .SetVisibility(NotificationVisibility.Public)!
            .AddAction(new Notification.Action.Builder(global::Android.Graphics.Drawables.Icon.CreateWithResource(this, global::Android.Resource.Drawable.IcMediaRew),
                $"Back {Session.RewindSeconds}s", Command(BackAction, 1)).Build())!
            .AddAction(new Notification.Action.Builder(global::Android.Graphics.Drawables.Icon.CreateWithResource(this, Session.IsPlaying ? global::Android.Resource.Drawable.IcMediaPause : global::Android.Resource.Drawable.IcMediaPlay),
                Session.IsPlaying ? "Pause" : "Play", Command(Session.IsPlaying ? PauseAction : PlayAction, 2)).Build())!
            .AddAction(new Notification.Action.Builder(global::Android.Graphics.Drawables.Icon.CreateWithResource(this, global::Android.Resource.Drawable.IcMediaFf),
                $"Forward {Session.ForwardSeconds}s", Command(ForwardAction, 3)).Build())!
            .SetDeleteIntent(Command(StopAction, 4))!.SetStyle(style)!.Build();
    }

    public void OnAudioFocusChange(AudioFocus focusChange)
    {
        // Speech is paused on interruptions; resuming is always an explicit user action.
        if (focusChange is AudioFocus.Loss or AudioFocus.LossTransient or AudioFocus.LossTransientCanDuck) Session.Pause();
    }

    public override void OnDestroy()
    {
        Session.Changed -= Update;
        Session.Pause();
        if (_noisy is not null) UnregisterReceiver(_noisy);
        if (_wakeLock?.IsHeld == true) _wakeLock.Release();
        _wakeLock?.Dispose();
        if (_hasFocus) _audio?.AbandonAudioFocusRequest(_focus!);
        _media?.Release();
        _media?.Dispose();
        base.OnDestroy();
    }

    private sealed class NoisyReceiver : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent) => MobileSession.Instance.Pause();
    }
    private sealed class SessionCallback(PlaybackService service) : MediaSession.Callback
    {
        public override void OnPlay() => service.Session.Play();
        public override void OnPause() => service.Session.Pause();
        public override void OnStop() { service.Session.Pause(); service.StopSelf(); }
        public override void OnSeekTo(long pos) => service.Session.Seek(TimeSpan.FromMilliseconds(pos));
        public override void OnRewind() => service.Session.Skip(-service.Session.RewindSeconds);
        public override void OnFastForward() => service.Session.Skip(service.Session.ForwardSeconds);
        public override void OnSkipToPrevious() => OnRewind();
        public override void OnSkipToNext() => OnFastForward();
    }
}
