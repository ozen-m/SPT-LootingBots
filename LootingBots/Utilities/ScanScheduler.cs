namespace LootingBots.Utilities;

public static class ScanScheduler
{
    private const int Capacity = 2;
    // TODO: Add config

    private static readonly Stack<int> _tickets = [];
    private static bool _init;

    public static bool CanStartScan(out int ticket)
    {
        if (!_init)
        {
            Init();
        }

        return _tickets.TryPop(out ticket);
    }

    public static void Return(int ticket)
    {
        _tickets.Push(ticket);
    }

    private static void Init()
    {
        for (var i = 1; i < Capacity + 1; i++)
        {
            _tickets.Push(i);
        }

        _init = true;
    }
}
