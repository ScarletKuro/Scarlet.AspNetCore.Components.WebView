// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See LICENSE in the project root for license information.

using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.AspNetCore.Components.WebView.Wpf
{
	internal class WpfBlazorWebViewBuilder : IWpfBlazorWebViewBuilder
	{
		public IServiceCollection Services { get; }

		public WpfBlazorWebViewBuilder(IServiceCollection services)
		{
			Services = services;
		}
	}
}
