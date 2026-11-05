using System;
using System.IO;

namespace Microsoft.AspNetCore.Components.WebView;

/// <summary>
/// Finds the host page (<c>HostPage</c>, e.g. <c>wwwroot\index.html</c>) on disk.
/// </summary>
/// <remarks>
/// A relative host page is resolved against the application directory. A single-file app that extracts its content
/// (IncludeAllContentForSelfExtract) reports the extraction folder as <see cref="AppContext.BaseDirectory"/>, while the
/// published <c>wwwroot</c> stays next to the executable, so the page wouldn't be found there. Both folders are tried.
/// </remarks>
internal static class HostPageLocator
{
	/// <summary>
	/// Resolves <paramref name="hostPage"/>: absolute as is; relative against the first of <paramref name="appRootDir"/> and
	/// the executable's folder that contains it, else against <paramref name="appRootDir"/> (e.g. in development, where
	/// the content is served from the static web assets instead of the output folder).
	/// </summary>
	/// <returns>The application directory the page was resolved against, and the page's full path.</returns>
	public static (string AppRootDir, string HostPageFullPath) Resolve(string hostPage, string appRootDir)
	{
		if (Path.IsPathFullyQualified(hostPage))
		{
			return (appRootDir, Path.GetFullPath(hostPage));
		}

		var defaultPath = Path.GetFullPath(Path.Combine(appRootDir, hostPage));
		if (File.Exists(defaultPath))
		{
			return (appRootDir, defaultPath);
		}

		if (GetExecutableDirectory() is { } executableDir)
		{
			var executablePath = Path.GetFullPath(Path.Combine(executableDir, hostPage));
			if (File.Exists(executablePath))
			{
				return (executableDir, executablePath);
			}
		}

		return (appRootDir, defaultPath);
	}

	private static string? GetExecutableDirectory()
	{
		// Not when the app runs through the dotnet host (dotnet app.dll): the executable is then dotnet.exe.
		var processPath = Environment.ProcessPath;
		if (processPath is null || Path.GetFileName(processPath).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}

		return Path.GetDirectoryName(processPath);
	}
}
