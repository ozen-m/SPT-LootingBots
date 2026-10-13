// This implementation is loosely based on Cysharp's UniTask TimeoutController:
// https://github.com/Cysharp/UniTask/blob/2.5.11/src/UniTask/Assets/Plugins/UniTask/Runtime/TimeoutController.cs
//
// Copyright (c) 2019 Yoshifumi Kawai / Cysharp, Inc.
// UniTask is licensed under the MIT License.
// https://github.com/Cysharp/UniTask/blob/master/LICENSE

namespace LootingBots.Utilities;

/// <summary>
/// This controller manages the creation and cancellation of a <see cref="CancellationTokenSource"/>.
/// It reuses the <see cref="CancellationTokenSource"/> until a timeout occurs, a new source is then created when asked for a new timeout.
/// </summary>
public sealed class TimeoutController : IDisposable
{
    private CancellationTokenSource _timeoutSource = new();
    private float _remainingTime;
    private bool _isRunning;
    private bool _isCanceledExternally;
    private bool _isDisposed;

    public bool IsActive
    {
        get { return _isRunning; }
    }

    public bool IsCanceled
    {
        get { return _timeoutSource.IsCancellationRequested; }
    }

    public bool IsTimeout
    {
        get { return _timeoutSource.IsCancellationRequested && !_isCanceledExternally; }
    }

    /// <summary>
    /// Starts a timeout and returns a CancellationToken
    /// </summary>
    /// <param name="secondsTimeout">The timeout duration, in seconds</param>
    /// <returns>A <see cref="CancellationToken"/> that is canceled when the timeout expires.</returns>
    /// <exception cref="InvalidOperationException">Thrown if a timeout is already running.</exception>
    public CancellationToken Timeout(int secondsTimeout)
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(TimeoutController));
        }
        if (_isRunning)
        {
            throw new InvalidOperationException("Timeout is already running");
        }
        if (IsCanceled)
        {
            _timeoutSource.Dispose();
            _timeoutSource = new CancellationTokenSource();
        }

        _remainingTime = secondsTimeout;
        _isRunning = true;
        _isCanceledExternally = false;

        TimeoutRunner.Instance.Add(this);
        return _timeoutSource.Token;
    }

    /// <summary>
    /// Stops the timeout without canceling its cancellation token.
    /// </summary>
    public void ResetTimer()
    {
        _isRunning = false;
    }

    /// <summary>
    /// Manually cancels the currently running timeout.
    /// This cancellation is different from cancellation caused by the timeout elapsing.
    /// </summary>
    /// <seealso cref="IsCanceled"/>
    /// <seealso cref="IsTimeout"/>
    public void Cancel()
    {
        _isCanceledExternally = true;
        SignalCancellation();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        Cancel();
        _timeoutSource.Dispose();
    }

    internal bool Run(float deltaTime)
    {
        if (!_isRunning)
        {
            return false;
        }

        _remainingTime -= deltaTime;

        if (_remainingTime <= 0f)
        {
            SignalCancellation();
            return false;
        }

        return true;
    }

    private void SignalCancellation()
    {
        _timeoutSource.Cancel();
        _isRunning = false;
    }
}
