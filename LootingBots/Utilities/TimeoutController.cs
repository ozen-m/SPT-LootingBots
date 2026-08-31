// This implementation is loosely based on Cysharp's UniTask TimeoutController:
// https://github.com/Cysharp/UniTask/blob/2.5.11/src/UniTask/Assets/Plugins/UniTask/Runtime/TimeoutController.cs
//
// Copyright (c) 2019 Yoshifumi Kawai / Cysharp, Inc.
// UniTask is licensed under the MIT License.
// https://github.com/Cysharp/UniTask/blob/master/LICENSE

using UnityEngine;

namespace LootingBots.Utilities;

public sealed class TimeoutController : MonoBehaviour
{
    private CancellationTokenSource _timeoutSource;
    private float _remainingTime;
    private bool _isRunning;

    public bool IsTimeout => _timeoutSource.IsCancellationRequested;

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
        return _timeoutSource.Token;
    }

    public void ResetTimer()
    {
        _isRunning = false;
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
        _timeoutSource.Cancel();
        _timeoutSource.Dispose();
    }
}
