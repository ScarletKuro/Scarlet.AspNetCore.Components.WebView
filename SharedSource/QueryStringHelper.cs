// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See LICENSE in the project root for license information.

#nullable enable

using System;

namespace Microsoft.AspNetCore.Components.WebView
{
	internal static class QueryStringHelper
	{
		public static string RemovePossibleQueryString(string? url)
		{
			if (string.IsNullOrEmpty(url))
			{
				return string.Empty;
			}
			var indexOfQueryString = url.IndexOf('?', StringComparison.Ordinal);
			return (indexOfQueryString == -1)
				? url
				: url.Substring(0, indexOfQueryString);
		}
	}
}
