window.googleAuth = (function () {
    let dotNetRef = null;
    let initializedClientId = null;
    let codeClient = null;

    function isGoogleReady() {
        return !!(window.google && window.google.accounts && window.google.accounts.oauth2);
    }

    return {
        initialize: function (clientId, dotNetObjectRef) {
            dotNetRef = dotNetObjectRef;

            if (!clientId || !isGoogleReady()) {
                return false;
            }

            if (initializedClientId !== clientId) {
                codeClient = google.accounts.oauth2.initCodeClient({
                    client_id: clientId,
                    callback: function (response) {
                        if (!dotNetRef) {
                            return;
                        }

                        if (response && response.code) {
                            dotNetRef.invokeMethodAsync("OnGoogleCodeReceived", response.code);
                            return;
                        }

                        if (response && response.error) {
                            dotNetRef.invokeMethodAsync("OnGoogleError", response.error);
                        }
                    },
                    error_callback: function (error) {
                        if (!dotNetRef) {
                            return;
                        }

                        const message = (error && (error.type || error.message)) || "google_oauth_error";
                        dotNetRef.invokeMethodAsync("OnGoogleError", message);
                    },
                    scope: "openid email profile",
                    ux_mode: "popup"
                });

                initializedClientId = clientId;
            }

            return true;
        },

        prompt: function () {
            if (!isGoogleReady() || !codeClient) {
                throw new Error("Google Identity Services is not loaded.");
            }

            codeClient.requestCode();
        }
    };
})();
