using System.Diagnostics.CodeAnalysis;

namespace Microsoft.AspNetCore.Components.WebView.Win32Aot
{
	internal static class LinkerFlags
	{
		/// <summary>
		/// Flags for a member that is a Razor component type. Matches the annotation used by
		/// <see cref="WebViewManager.AddRootComponentAsync(System.Type, string, ParameterView)"/>.
		/// </summary>
		public const DynamicallyAccessedMemberTypes Component = DynamicallyAccessedMemberTypes.All;
	}
}
