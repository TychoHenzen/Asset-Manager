using AssetManager.Core;

namespace AssetManager.Tests;

public class RetryPolicyTests
{
	[Fact]
	public void ANetworkError_IsRetriedUntilItSucceeds()
	{
		var calls = 0;
		var retried = new List<int>();

		var ok = RetryPolicy.Immediate(3).TryRun(() => ++calls < 3 ? throw new IOException("net") : calls,
			CancellationToken.None, out var result, out var error, (attempt, _) => retried.Add(attempt));

		Assert.True(ok);
		Assert.Equal(3, result);
		Assert.Null(error);
		Assert.Equal([1, 2], retried);
	}

	[Fact]
	public void ANetworkError_GivesUpAfterTheLastRetry()
	{
		var calls = 0;

		var ok = RetryPolicy.Immediate(2).TryRun<int>(() => { calls++; throw new IOException("net"); },
			CancellationToken.None, out _, out var error);

		Assert.False(ok);
		Assert.Equal(3, calls);
		Assert.IsType<IOException>(error);
	}

	[Fact]
	public void AnErrorARetryCannotFix_IsNotRetried()
	{
		var calls = 0;

		var ok = RetryPolicy.Immediate(5).TryRun<int>(() => { calls++; throw new PathTooLongException(); },
			CancellationToken.None, out _, out _);

		Assert.False(ok);
		Assert.Equal(1, calls);
	}

	[Fact]
	public void Cancelling_StopsTheWaitBetweenAttempts()
	{
		using var cancel = new CancellationTokenSource();
		var policy = new RetryPolicy([TimeSpan.FromMinutes(10)]);

		Assert.Throws<OperationCanceledException>(() =>
			policy.TryRun<int>(() => { cancel.Cancel(); throw new IOException("net"); }, cancel.Token, out _, out _));
	}
}
