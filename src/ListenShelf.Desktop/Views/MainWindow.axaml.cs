using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using ListenShelf.Application.Playback;
using ListenShelf.Application.Settings;
using ListenShelf.Desktop.Services;
using ListenShelf.Desktop.ViewModels;

namespace ListenShelf.Desktop.Views
{
    public partial class MainWindow : Window
    {
        private readonly IGlobalMediaKeyService _mediaKeyService =
            GlobalMediaKeyServiceFactory.Create();
        private MainWindowViewModel? _viewModel;
        private bool _waitingForWorkToClose;
        private WindowPlacementController? _windowPlacement;

        public MainWindow()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            Opened += OnOpened;
            Closed += OnClosed;
        }

        public void ConfigureWindowPlacement(IAppSettingsStore settings, Action<string, Exception> logError)
        {
            _windowPlacement?.Dispose();
            _windowPlacement = new WindowPlacementController(this, settings, logError);
        }

        protected override async void OnClosing(WindowClosingEventArgs e)
        {
            base.OnClosing(e);
            if (e.Cancel) return;
            if (_viewModel is null || (!_viewModel.Imports.IsRunning && !_viewModel.Verification.IsRunning && !_viewModel.Repair.IsRunning))
            {
                _windowPlacement?.Save();
                return;
            }

            e.Cancel = true;
            if (_waitingForWorkToClose)
            {
                return;
            }

            _waitingForWorkToClose = true;
            try
            {
                if (_viewModel.Imports.IsRunning) await _viewModel.Imports.CancelAndWaitAsync();
                if (_viewModel.Verification.IsRunning) await _viewModel.Verification.CancelAndWaitAsync();
                if (_viewModel.Repair.IsRunning) await _viewModel.Repair.CancelAndWaitAsync();
                Close();
            }
            catch (Exception exception)
            {
                _viewModel.LibraryStatusMessage = $"Background work could not finish closing safely: {exception.Message}";
            }
            finally
            {
                _waitingForWorkToClose = false;
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.Handled || _viewModel is null)
            {
                return;
            }

            var searchAction = LibrarySearchKeyboardShortcuts.GetAction(
                e.Key, e.KeyModifiers, _viewModel.IsLibrarySection && LibrarySection.IsSearchFocused);
            if (searchAction == LibrarySearchKeyboardAction.FocusSearch)
            {
                var viewModel = _viewModel;
                if (!viewModel.IsLibrarySection) viewModel.ShowLibraryCommand.Execute(null);
                // Visibility bindings/layout must settle before focusing a
                // section that was hidden. Never steal focus back from a dialog.
                Dispatcher.UIThread.Post(() =>
                {
                    if (IsActive && ReferenceEquals(_viewModel, viewModel) && viewModel.IsLibrarySection)
                        LibrarySection.FocusSearch();
                }, DispatcherPriority.Loaded);
                e.Handled = true;
                return;
            }

            if (searchAction == LibrarySearchKeyboardAction.ClearSearch)
            {
                _viewModel.ClearLibrarySearchCommand.Execute(null);
                e.Handled = true;
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
            _windowPlacement?.Dispose();
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

            var focus = FocusManager?.GetFocusedElement() switch
            {
                TextBox => PlaybackKeyboardFocus.TextInput,
                ComboBox => PlaybackKeyboardFocus.ComboBox,
                Button => PlaybackKeyboardFocus.Button,
                Slider => PlaybackKeyboardFocus.Slider,
                _ => PlaybackKeyboardFocus.Other,
            };
            return PlaybackKeyboardShortcuts.GetAction(e.Key, e.KeyModifiers, focus);
        }
    }
}
