(function () {
    "use strict";

    window.battleship = {
        focusById: function (id) {
            var element = document.getElementById(id);
            if (!element) {
                return;
            }

            try {
                element.focus({ preventScroll: true });
            } catch (error) {
                element.focus();
            }
        },

        copyText: function (text) {
            if (navigator.clipboard && window.isSecureContext) {
                return navigator.clipboard.writeText(text).then(function () {
                    return true;
                }).catch(function () {
                    return legacyCopy(text);
                });
            }

            return Promise.resolve(legacyCopy(text));
        }
    };

    function legacyCopy(text) {
        var area = document.createElement("textarea");
        area.value = text;
        area.setAttribute("readonly", "");
        area.style.position = "fixed";
        area.style.top = "0";
        area.style.left = "0";
        area.style.opacity = "0";
        document.body.appendChild(area);
        area.select();
        var copied = false;
        try {
            copied = document.execCommand("copy");
        } catch (error) {
            copied = false;
        } finally {
            document.body.removeChild(area);
        }

        return copied;
    }

    document.addEventListener("dragstart", function (event) {
        var target = event.target;
        var card = target && target.closest ? target.closest("[data-ship]") : null;
        if (!card || !event.dataTransfer) {
            return;
        }

        event.dataTransfer.setData("text/plain", card.getAttribute("data-ship") || "");
        event.dataTransfer.effectAllowed = "move";
    });
})();
