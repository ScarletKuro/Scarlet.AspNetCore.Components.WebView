// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See LICENSE in the project root for license information.

using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.AspNetCore.Components.WebView.Wpf
{
	/// <summary>
	/// A builder for WPF Blazor WebViews.
	/// </summary>
	public interface IWpfBlazorWebViewBuilder
	{
		/// <summary>
		/// Gets the builder service collection.
		/// </summary>
		IServiceCollection Services { get; }
	}
}
