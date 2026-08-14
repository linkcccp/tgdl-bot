namespace TGBot.Download;

/// <summary>
/// 下载并发闸门：普通下载占 1 个槽位，自更新占满全部槽位，
/// 从而实现「更新与下载互斥、下载之间并发受限」。
/// </summary>
public sealed class DownloadGate : IAsyncDisposable
{
    private readonly SemaphoreSlim _sem;
    private readonly int _totalSlots;

    /// <summary>
    /// 初始化 <see cref="DownloadGate"/>。
    /// </summary>
    /// <param name="maxConcurrent">最大并发下载数。</param>
    /// <exception cref="ArgumentOutOfRangeException">参数小于 1 时抛出。</exception>
    public DownloadGate(int maxConcurrent)
    {
        if (maxConcurrent < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrent), "并发数必须大于 0");
        }

        _totalSlots = maxConcurrent;
        _sem = new SemaphoreSlim(maxConcurrent, maxConcurrent);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _sem.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// 获取一个普通下载槽位（排队等待）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>释放器，用毕调用 <see cref="IDisposable.Dispose"/> 释放槽位。</returns>
    public async ValueTask<IAsyncDisposable> AcquireDownloadAsync(CancellationToken cancellationToken)
    {
        await _sem.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Releaser(_sem, 1);
    }

    /// <summary>
    /// 获取全部槽位（自更新用，等待所有下载结束并阻止新下载开始）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>释放器，用毕调用 <see cref="IDisposable.Dispose"/> 释放全部槽位。</returns>
    public async ValueTask<IAsyncDisposable> AcquireExclusiveAsync(CancellationToken cancellationToken)
    {
        for (var i = 0; i < _totalSlots; i++)
        {
            await _sem.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        return new Releaser(_sem, _totalSlots);
    }

    private sealed class Releaser : IAsyncDisposable
    {
        private readonly SemaphoreSlim _sem;
        private readonly int _count;
        private int _released;

        public Releaser(SemaphoreSlim sem, int count)
        {
            _sem = sem;
            _count = count;
        }

        public ValueTask DisposeAsync()
        {
            var toRelease = _count - Interlocked.Exchange(ref _released, _count);
            if (toRelease > 0)
            {
                _sem.Release(toRelease);
            }

            return ValueTask.CompletedTask;
        }
    }
}
