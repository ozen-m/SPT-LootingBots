namespace LootingBots.Utilities;

/// <inheritdoc/>
/// <summary>
/// Extended to return void on calls to try and set a result. Supports a cancellation token.
/// </summary>
public class CallbackTaskCompletionSource<TResult> : TaskCompletionSource<TResult>, IDisposable
{
    private readonly CancellationToken _token;
    private readonly CancellationTokenRegistration _registration;

    /// <inheritdoc/>
    /// <param name="token">A <see cref="CancellationToken"/> that can cancel the underlying <see cref="TaskCompletionSource{TResult}"/>.Task</param>
    public CallbackTaskCompletionSource(CancellationToken token = default)
    {
        if (!token.CanBeCanceled)
        {
            return;
        }

        _token = token;
        _registration = token.Register(static tcs => ((CallbackTaskCompletionSource<TResult>)tcs).TrySetCanceled(), this);
    }

    /// <returns></returns>
    /// <inheritdoc cref="TaskCompletionSource{TResult}.TrySetCanceled()" />
    public new void TrySetCanceled()
    {
        base.TrySetCanceled(_token);
    }

    /// <returns></returns>
    /// <inheritdoc cref="TaskCompletionSource{TResult}.TrySetException(IEnumerable{Exception})" />
    public new void TrySetException(IEnumerable<Exception> exceptions)
    {
        base.TrySetException(exceptions);
    }

    /// <returns></returns>
    /// <inheritdoc cref="TaskCompletionSource{TResult}.TrySetException(Exception)" />
    public new void TrySetException(Exception exception)
    {
        base.TrySetException(exception);
    }

    /// <returns></returns>
    /// <inheritdoc cref="TaskCompletionSource{TResult}.TrySetResult(TResult)" />
    public new void TrySetResult(TResult result)
    {
        base.TrySetResult(result);
    }

    public void Dispose()
    {
        _registration.Dispose();
    }
}
