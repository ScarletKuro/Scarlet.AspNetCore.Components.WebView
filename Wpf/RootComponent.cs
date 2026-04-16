// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.AspNetCore.Components.WebView.WebView2;

namespace Microsoft.AspNetCore.Components.WebView.Wpf
{
	/// <summary>
	/// Describes a root component that can be added to a <see cref="BlazorWebView"/>.
	/// </summary>
	/// <remarks>
	/// Inherits from <see cref="FrameworkElement"/> so that items added to <see cref="RootComponentsCollection"/>
	/// can be wired into the hosting <see cref="BlazorWebView"/>'s logical tree. This enables
	/// <c>DataContext</c> inheritance and makes XAML bindings on <see cref="Selector"/>,
	/// <see cref="ComponentType"/>, and <see cref="Parameters"/> resolve against the host's DataContext
	/// without requiring a BindingProxy or explicit <c>ElementName</c>/<c>Source</c>.
	/// </remarks>
	public class RootComponent : FrameworkElement
	{
		/// <summary>
		/// Identifies the <see cref="Selector"/> dependency property.
		/// </summary>
		public static readonly DependencyProperty SelectorProperty = DependencyProperty.Register(
			nameof(Selector),
			typeof(string),
			typeof(RootComponent),
			new PropertyMetadata(null));

		/// <summary>
		/// Identifies the <see cref="ComponentType"/> dependency property.
		/// </summary>
		public static readonly DependencyProperty ComponentTypeProperty = DependencyProperty.Register(
			nameof(ComponentType),
			typeof(Type),
			typeof(RootComponent),
			new PropertyMetadata(null));

		/// <summary>
		/// Identifies the <see cref="Parameters"/> dependency property.
		/// </summary>
		public static readonly DependencyProperty ParametersProperty = DependencyProperty.Register(
			nameof(Parameters),
			typeof(IDictionary<string, object?>),
			typeof(RootComponent),
			new PropertyMetadata(null, OnParametersChanged));

		private WebViewManager? _attachedManager;

		/// <summary>
		/// Gets or sets the CSS selector string that specifies where in the document the component should be placed.
		/// This must be unique among the root components within the <see cref="BlazorWebView"/>.
		/// </summary>
		public string Selector
		{
			get => (string)GetValue(SelectorProperty);
			set => SetValue(SelectorProperty, value);
		}

		/// <summary>
		/// Gets or sets the type of the root component. This type must implement <see cref="IComponent"/>.
		/// </summary>
		public Type ComponentType
		{
			get => (Type)GetValue(ComponentTypeProperty);
			set => SetValue(ComponentTypeProperty, value);
		}

		/// <summary>
		/// Gets or sets an optional dictionary of parameters to pass to the root component.
		/// </summary>
		public IDictionary<string, object?>? Parameters
		{
			get => (IDictionary<string, object?>?)GetValue(ParametersProperty);
			set => SetValue(ParametersProperty, value);
		}

		internal async Task AddToWebViewManagerAsync(WebViewManager webViewManager)
		{
			// As a characteristic of XAML,we can't rely on non-default constructors. So we have to
			// validate that the required properties were set. We could skip validating this and allow
			// the lower-level renderer code to throw, but that would be harder for developers to understand.

			if (string.IsNullOrWhiteSpace(Selector))
			{
				throw new InvalidOperationException($"{nameof(RootComponent)} requires a value for its {nameof(Selector)} property, but no value was set.");
			}

			if (ComponentType is null)
			{
				throw new InvalidOperationException($"{nameof(RootComponent)} requires a value for its {nameof(ComponentType)} property, but no value was set.");
			}

			_attachedManager = webViewManager;

			var parameterView = Parameters == null ? ParameterView.Empty : ParameterView.FromDictionary(Parameters);
			await webViewManager.AddRootComponentAsync(ComponentType, Selector, parameterView);
		}

		internal Task RemoveFromWebViewManagerAsync(WebView2WebViewManager webviewManager)
		{
			_attachedManager = null;
			return webviewManager.RemoveRootComponentAsync(Selector);
		}

		private static async void OnParametersChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			// When a binding resolves late (e.g. after DataContext inheritance kicks in), re-seed
			// the root component with the new parameters. The WebViewManager API does not expose an
			// in-place parameter update, so we remove and re-add.
			var self = (RootComponent)d;
			var manager = self._attachedManager;
			if (manager is null || string.IsNullOrWhiteSpace(self.Selector) || self.ComponentType is null)
			{
				return;
			}

			var parameterView = self.Parameters == null ? ParameterView.Empty : ParameterView.FromDictionary(self.Parameters);
			await manager.RemoveRootComponentAsync(self.Selector);
			await manager.AddRootComponentAsync(self.ComponentType, self.Selector, parameterView);
		}
	}
}
