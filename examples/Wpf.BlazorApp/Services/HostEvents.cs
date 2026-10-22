using System;
using System.Threading;

namespace Wpf.BlazorApp.Services;

/// <summary>
/// Events raised by the native host and observed by Razor components.
/// </summary>
public sealed class HostEvents
{
	private int _activationCount;

	public event Action? Activated;

	public int ActivationCount => _activationCount;

	public void NotifyActivated()
	{
		Interlocked.Increment(ref _activationCount);
		Activated?.Invoke();
	}
}
