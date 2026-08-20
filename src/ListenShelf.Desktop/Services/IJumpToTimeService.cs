namespace ListenShelf.Desktop.Services;

public interface IJumpToTimeService
{
    Task<TimeSpan?> ShowAsync(TimeSpan position, TimeSpan duration);
}
