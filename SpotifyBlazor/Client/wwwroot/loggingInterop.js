window.loggingInterop = {
    logInfo: function (msg) {
        console.info("[INFO] " + msg);
    },
    logWarn: function (msg) {
        console.warn("[WARN] " + msg);
    },
    logError: function (msg) {
        console.error("[ERROR] " + msg);
    },
    logDebug: function (msg) {
        console.debug("[DEBUG] " + msg);
    },

    // OPTIONAL: forward logs to your API for searchable logging
    sendToServer: async function (level, msg) {
        try {
            await fetch("/api/logs", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    level: level,
                    message: msg,
                    timestamp: new Date().toISOString()
                })
            });
        } catch (e) {
            console.error("Failed to send log to server", e);
        }
    }
};
