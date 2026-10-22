window.interopSample = {
    // Returns JSON so .NET can deserialize it with a source-generated JsonSerializerContext.
    getBrowserInfo: () => JSON.stringify({
        userAgent: navigator.userAgent,
        language: navigator.language,
        screenWidth: window.screen.width,
        screenHeight: window.screen.height,
        devicePixelRatio: window.devicePixelRatio
    }),

    callDotNet: async (dotNetReference) => {
        await dotNetReference.invokeMethodAsync('ReceiveFromJs', `Called back from JavaScript at ${new Date().toLocaleTimeString()}`);
    },

    fail: () => {
        throw new Error('Thrown from JavaScript.');
    }
};
