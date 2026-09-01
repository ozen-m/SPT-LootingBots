// This implementation is loosely based on Cysharp's UniTask TimeoutController:
// https://github.com/Cysharp/UniTask/blob/2.5.11/src/UniTask/Assets/Plugins/UniTask/Runtime/TimeoutController.cs
//
// Copyright (c) 2019 Yoshifumi Kawai / Cysharp, Inc.
// UniTask is licensed under the MIT License.
// https://github.com/Cysharp/UniTask/blob/master/LICENSE

using UnityEngine;

namespace LootingBots.Utilities;

/// <summary>
/// This controller manages the creation and cancellation of a <see cref="CancellationTokenSource"/>.
/// It reuses the <see cref="CancellationTokenSource"/> until a timeout occurs, a new source is then created when asked for a new timeout.
/// </summary>
public sealed class TimeoutController : MonoBehaviour
{
    private CancellationTokenSource _timeoutSource;
    private float _remainingTime;
    private bool _isRunning;
    private bool _isCanceledExternally;

    public bool IsActive => _isRunning;
    public bool IsCanceled => _timeoutSource.IsCancellationRequested;
    public bool IsTimeout => _timeoutSource.IsCancellationRequested && !_isCanceledExternally;

    /// <summary>
    /// Starts a timeout and returns a CancellationToken
    /// </summary>
    /// <param name="secondsTimeout">The timeout duration, in seconds</param>
    /// <returns>A <see cref="CancellationToken"/> that is canceled when the timeout expires.</returns>
    /// <exception cref="InvalidOperationException">Thrown if a timeout is already running.</exception>
    public CancellationToken Timeout(float secondsTimeout)
    {
        if (_isRunning)
        {
            throw new InvalidOperationException("A timeout is already running.");
        }
        if (IsTimeout)
        {
            _timeoutSource.Dispose();
            _timeoutSource = new CancellationTokenSource();
        }

        _remainingTime = secondsTimeout;
        _isRunning = true;
        _isCanceledExternally = false;
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
    /// This cancellation is distinguished from cancellation caused by the timeout elapsing.
    /// </summary>
    public void Cancel()
    {
        _isCanceledExternally = true;
        SignalCancellation();
    }

    private void Awake()
    {
        _timeoutSource = new CancellationTokenSource();
    }

    private void Update()
    {
        if (!_isRunning)
        {
            return;
        }

        _remainingTime -= Time.deltaTime;

        if (_remainingTime <= 0)
        {
            SignalCancellation();
        }
    }

    private void SignalCancellation()
    {
        _timeoutSource.Cancel();
        _isRunning = false;
    }

    private void OnDestroy()
    {
        Cancel();
        _timeoutSource.Dispose();
    }
}
