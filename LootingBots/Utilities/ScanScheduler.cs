namespace LootingBots.Utilities;

public static class ScanScheduler
{
    private const int Capacity = 2;
    // TODO: Add config

    private static readonly Stack<int> _tickets = [];

    static ScanScheduler()
    {
        for (var i = 1; i <= Capacity; i++)
        {
            _tickets.Push(i);
        }
    }

    public static bool CanStartScan(out int ticket)
    {
        return _tickets.TryPop(out ticket);
    }

    public static void Return(int ticket)
    {

#if DEBUG
        if (ticket is < 1 or > Capacity)
        {
            throw new ArgumentOutOfRangeException(nameof(ticket), ticket, $"Ticket is less than 1 or more than the capacity ({Capacity})!");
        }

        if (_tickets.Contains(ticket))
        {
            throw new InvalidOperationException($"Ticket {ticket} already exists");
        }
#endif

        _tickets.Push(ticket);
    }
}
