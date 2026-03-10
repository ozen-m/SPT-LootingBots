using Cysharp.Threading.Tasks;

namespace LootingBots.Utilities;

/// <summary>
/// UniTaskCompletionSource<T> doesn't have a SetResult method :/
/// </summary>
public class UniTaskCompletionSourceEx<T> : UniTaskCompletionSource<T>
{
    public void SetResult(T result)
    {
        if (!TrySetResult(result))
        {
            throw new InvalidOperationException("Already completed.");
        }
    }

    public void SetCanceled()
    {
        SetCanceled(CancellationToken.None);
    }

    public void SetCanceled(CancellationToken token)
    {
        if (!TrySetCanceled(token))
        {
            throw new InvalidOperationException("Already completed.");
        }
    }

    public void SetException(Exception exception)
    {
        if (exception == null)
        {
            throw new ArgumentNullException(nameof(exception));
        }

        if (!TrySetException(exception))
        {
            throw new InvalidOperationException("Already completed.");
        }
    }
}
