// This implementation is based on Cysharp's UniTask AutoResetUniTaskCompletionSource<T>:
// https://github.com/Cysharp/UniTask/blob/2.5.11/src/UniTask/Assets/Plugins/UniTask/Runtime/UniTaskCompletionSource.cs#L443
//
// Copyright (c) 2019 Yoshifumi Kawai / Cysharp, Inc.
// UniTask is licensed under the MIT License.
// https://github.com/Cysharp/UniTask/blob/master/LICENSE

using System.Threading.Tasks.Sources;
using Comfort.Common;

namespace LootingBots.Utilities;

public abstract class ReusableTaskCompletionSource<TResult> : IValueTaskSource<TResult>
{
    private ManualResetValueTaskSourceCore<TResult> _core;
    private short _version;

    private CancellationToken _token;
    private CancellationTokenRegistration _registration;

    public ValueTask<TResult> Task
    {
        get { return new ValueTask<TResult>(this, _core.Version); }
    }

    protected void StartInternal(CancellationToken token = default)
    {
        _version = _core.Version;

        if (!token.CanBeCanceled)
        {
            return;
        }

        _token = token;
        _registration = token.Register(static tcs => ((ReusableTaskCompletionSource<TResult>)tcs).SetCanceled(), this);
    }

    public void SetCanceled()
    {
        if (_version != _core.Version)
        {
            return;
        }

        _core.SetException(new OperationCanceledException(_token));
    }

    public void SetException(Exception exception)
    {
        if (_version != _core.Version)
        {
            return;
        }

        _core.SetException(exception);
    }

    public void SetResult(TResult result)
    {
        if (_version != _core.Version)
        {
            return;
        }

        _core.SetResult(result);
    }

    public TResult GetResult(short token)
    {
        try
        {
            return _core.GetResult(token);
        }
        finally
        {
            Reset();
            Release();
        }
    }

    public ValueTaskSourceStatus GetStatus(short token)
    {
        return _core.GetStatus(token);
    }

    public void OnCompleted(Action<object> continuation, object state, short token, ValueTaskSourceOnCompletedFlags flags)
    {
        _core.OnCompleted(continuation, state, token, flags);
    }

    private void Reset()
    {
        _registration.Dispose();
        _registration = default;
        _token = CancellationToken.None;

        _core.Reset();
    }

    /// <summary>
    /// Release this instance to a derived class' pool when Result is given.
    /// </summary>
    protected abstract void Release();
}

public class CallbackTaskCompletionSource : ReusableTaskCompletionSource<IResult>
{
    private static readonly UnityEngine.Pool.ObjectPool<CallbackTaskCompletionSource> _pool = new(
        () => new CallbackTaskCompletionSource(),
        collectionCheck: false,
        defaultCapacity: 2,
        maxSize: 32
    );

    public readonly Callback ResultCallback;

    private CallbackTaskCompletionSource()
    {
        ResultCallback = SetResult;
    }

    public static CallbackTaskCompletionSource Start(CancellationToken token = default)
    {
        var source = _pool.Get();
        source.StartInternal(token);
        return source;
    }

    protected override void Release()
    {
        _pool.Release(this);
    }
}

public class ActionTaskCompletionSource : ReusableTaskCompletionSource<bool>
{
    private static readonly UnityEngine.Pool.ObjectPool<ActionTaskCompletionSource> _pool = new(
        () => new ActionTaskCompletionSource(),
        collectionCheck: false,
        defaultCapacity: 2,
        maxSize: 32
    );

    public readonly Action CompleteAction;

    private ActionTaskCompletionSource()
    {
        CompleteAction = Complete;
    }

    public static ActionTaskCompletionSource Start(CancellationToken token = default)
    {
        var source = _pool.Get();
        source.StartInternal(token);
        return source;
    }

    public void Complete()
    {
        SetResult(true);
    }

    protected override void Release()
    {
        _pool.Release(this);
    }
}
