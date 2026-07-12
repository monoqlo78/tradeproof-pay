// Localized client-side (browser) strings for HashKeyChain.
//
// The strings are injected by the server for the current UI culture as
// `window.hashKeyChainResources` (see Components/App.razor). Keys are stable
// English identifiers; only the values are translated. Never hard-code Japanese
// or English display text in client scripts — always resolve it through here so
// the server and client never disagree on wording.
//
// A JSON endpoint (GET /api/clientresources) returns the same data and can be
// used to refresh the strings after a language change without a full reload.

(function () {
    "use strict";

    /**
     * Resolve a localized client string by its stable key.
     * @param {string} key e.g. "walletConnected"
     * @param {string} [fallback] value returned when the key is missing.
     * @returns {string}
     */
    function t(key, fallback) {
        const resources = window.hashKeyChainResources || {};
        if (Object.prototype.hasOwnProperty.call(resources, key)) {
            return resources[key];
        }
        return fallback !== undefined ? fallback : key;
    }

    /**
     * Fetch the localized client strings for the current culture from the server
     * and update window.hashKeyChainResources in place.
     * @returns {Promise<Record<string, string>>}
     */
    async function refresh() {
        const response = await fetch("/api/clientresources", {
            headers: { Accept: "application/json" }
        });
        if (!response.ok) {
            throw new Error("Failed to load client resources: " + response.status);
        }
        const data = await response.json();
        window.hashKeyChainResources = data;
        return data;
    }

    window.hashKeyChainLocalization = { t: t, refresh: refresh };
})();
