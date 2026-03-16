namespace LootingBots.Utilities;

public static class ScanScheduler
{
    private static readonly Stack<int> _tickets = [];
    private static bool _init;
    private static int _capacity;

    public static void Init()
    {
        if (_init)
        {
            return;
        }

        _capacity = LootingBots.MaxConcurrentScans.Value;
        if (_capacity > 0)
        {
            for (var i = 1; i <= _capacity; i++)
            {
                _tickets.Push(i);
            }
        }
        _init = true;
    }

    public static void Reset()
    {
        _init = false;
        _tickets.Clear();
    }

    public static bool CanStartScan(out int ticket)
    {
        ticket = 0;

        if (_capacity == 0)
        {
            return true;
        }

        return _init && _tickets.TryPop(out ticket);
    }

    public static void Return(int ticket)
    {
        if (_capacity == 0)
        {
            return;
        }

        if (!_init)
        {
            return;
        }

#if DEBUG
        if (ticket < 1 || ticket > _capacity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ticket),
                ticket,
                $"Ticket is less than 1 or more than the capacity ({_capacity})!"
            );
        }

        if (_tickets.Contains(ticket))
        {
            throw new InvalidOperationException($"Ticket {ticket} already exists");
        }
#endif

        _tickets.Push(ticket);
    }
}
