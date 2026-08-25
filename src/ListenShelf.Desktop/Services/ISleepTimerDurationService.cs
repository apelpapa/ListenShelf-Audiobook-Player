namespace ListenShelf.Desktop.Services;

public interface ISleepTimerDurationService
{
    Task<int?> ChooseMinutesAsync(int? initialMinutes);
}
