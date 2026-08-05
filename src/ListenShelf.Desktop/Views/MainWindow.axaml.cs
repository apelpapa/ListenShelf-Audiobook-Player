using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using ListenShelf.Application.Playback;
using ListenShelf.Desktop.Services;
using ListenShelf.Desktop.ViewModels;

namespace ListenShelf.Desktop.Views
{
    public partial class MainWindow : Window
    {
        private readonly IGlobalMediaKeyService _mediaKeyService =
            GlobalMediaKeyServiceFactory.Create();
        private MainWindowViewModel? _viewModel;

        public MainWindow()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            Opened += OnOpened;
            Closed += OnClosed;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.Handled || _viewModel is null)
            {
                return;
            }

            var action = GetKeyboardAction(e);
            if (action is not null
                && _viewModel.TryHandlePlaybackControl(action.Value))
            {
                e.Handled = true;
            }
        }

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            if (_viewModel is not null)
            {
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }

            _viewModel = DataContext as MainWindowViewModel;
            if (_viewModel is not null)
            {
                _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            }

            RefreshMediaKeyRegistration();
        }

        private void OnOpened(object? sender, EventArgs e)
        {
            _mediaKeyService.Attach(
                this,
                action => _viewModel?.TryHandlePlaybackControl(action) == true);
            RefreshMediaKeyRegistration();
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            DataContextChanged -= OnDataContextChanged;

            if (_viewModel is not null)
            {
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }

            _mediaKeyService.Dispose();
        }

        private void OnViewModelPropertyChanged(
            object? sender,
            PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainWindowViewModel.IsFileLoaded))
            {
                RefreshMediaKeyRegistration();
            }
        }

        private void RefreshMediaKeyRegistration()
        {
            _mediaKeyService.SetEnabled(_viewModel?.IsFileLoaded == true);
        }

        private PlaybackControlAction? GetKeyboardAction(KeyEventArgs e)
        {
            if (!OperatingSystem.IsWindows()
                && e.Key is Key.MediaPlayPause)
            {
                return PlaybackControlAction.TogglePlayPause;
            }

            if (!OperatingSystem.IsWindows()
                && e.Key is Key.MediaPreviousTrack)
            {
                return PlaybackControlAction.SkipBackward;
            }

            if (!OperatingSystem.IsWindows()
                && e.Key is Key.MediaNextTrack)
            {
                return PlaybackControlAction.SkipForward;
            }

            if (!OperatingSystem.IsWindows()
                && e.Key is Key.MediaStop)
            {
                return PlaybackControlAction.Pause;
            }

            if (e.KeyModifiers != KeyModifiers.None)
            {
                return null;
            }

            var focusedControl = FocusManager?.GetFocusedElement();
            return e.Key switch
            {
                Key.Space when focusedControl is not Button and not ComboBox =>
                    PlaybackControlAction.TogglePlayPause,
                Key.K when focusedControl is not ComboBox =>
                    PlaybackControlAction.TogglePlayPause,
                Key.Left when focusedControl is not Slider and not ComboBox =>
                    PlaybackControlAction.SkipBackward,
                Key.J when focusedControl is not ComboBox =>
                    PlaybackControlAction.SkipBackward,
                Key.Right when focusedControl is not Slider and not ComboBox =>
                    PlaybackControlAction.SkipForward,
                Key.L when focusedControl is not ComboBox =>
                    PlaybackControlAction.SkipForward,
                _ => null,
            };
        }
    }
}
