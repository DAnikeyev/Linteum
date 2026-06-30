// Recovers the login/signup page when the Blazor interactive circuit never starts.
//
// The "Checking session" card is prerendered into the initial HTML, but the code that
// replaces it (Login/Signup OnAfterRenderAsync) only runs inside a live SignalR circuit.
// If that circuit fails to start (deploy/restart window, network drop, sticky-session 404,
// IPv4/IPv6 routing edge), the card is frozen forever and the in-circuit 10s timeout never
// fires — because it lives in the circuit code that never executes.
//
// This watchdog runs client-side, independent of the circuit. Once the interactive form
// renders, the loading card leaves the DOM and this does nothing. If it is still there past
// the grace window, we swap in a "Connection problem — Retry" card so the user is never
// permanently stuck.
(function () {
    var GRACE_MS = 12000;   // a hair above the in-circuit 10s timeout, so we never false-fire on a live-but-slow circuit
    var POLL_MS = 1000;
    var startPath = window.location.pathname;

    function isAuthPage() {
        return startPath === "/" || startPath === "/login" || startPath === "/signup";
    }

    function loadingCard() {
        return document.querySelector("[data-auth-loading], .auth-loading-card");
    }

    function arm() {
        if (!isAuthPage()) {
            return;
        }

        var fired = false;
        var deadline = Date.now() + GRACE_MS;

        // "Ready" = the real form rendered (loading card gone) or we navigated away
        // (valid-session redirect via NavigateTo changes the path).
        function ready() {
            return !loadingCard() || window.location.pathname !== startPath;
        }

        function tick() {
            if (fired) {
                return;
            }
            if (ready()) {
                fired = true;
                return;
            }
            if (Date.now() >= deadline) {
                fired = true;
                showRecovery();
                return;
            }
            window.setTimeout(tick, POLL_MS);
        }

        window.setTimeout(tick, POLL_MS);
    }

    function showRecovery() {
        var card = loadingCard();
        if (!card) {
            return;
        }

        var replacement = document.createElement("div");
        replacement.className = card.className.replace(/\bauth-loading-card\b/, "").trim();
        replacement.setAttribute("data-auth-loading", "");
        replacement.innerHTML =
            '<div class="auth-header">' +
                '<h3>Connection problem</h3>' +
                '<p>The live connection could not be established. This usually clears up on retry.</p>' +
            '</div>' +
            '<button class="btn btn-primary" type="button" style="width:100%">Retry</button>';

        replacement.querySelector("button").addEventListener("click", function () {
            window.location.reload();
        });

        card.replaceWith(replacement);
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", arm);
    } else {
        arm();
    }
})();
