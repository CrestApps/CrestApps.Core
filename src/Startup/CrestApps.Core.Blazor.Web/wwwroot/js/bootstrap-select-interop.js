(function () {
    "use strict";

    window.crestappsBootstrapSelect = window.crestappsBootstrapSelect || {
        init: function (elementId) {
            var element = document.getElementById(elementId);

            if (!element || typeof window.Selectpicker !== "function" || element.dataset.selectpicker === "true") {
                return;
            }

            element.dataset.selectpicker = "true";
            element.crestappsSelectpicker = new window.Selectpicker(element, { liveSearch: true });
        },
        dispose: function (elementId) {
            var element = document.getElementById(elementId);

            if (!element) {
                return;
            }

            var instance = element.crestappsSelectpicker;

            if (instance && typeof instance.destroy === "function") {
                try {
                    instance.destroy();
                } catch (e) {
                    // Ignore teardown errors so a disconnected circuit never surfaces an exception.
                }
            }

            delete element.crestappsSelectpicker;
            delete element.dataset.selectpicker;
        }
    };
})();
