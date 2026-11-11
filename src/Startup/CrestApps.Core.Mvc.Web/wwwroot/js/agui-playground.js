/*
 * AG-UI shared state on the WebMCP playground. The "Order details" card is a JSON object the user and the assistant
 * share: the chat sends it with each message (window.crestAppsAGUI.registerStateProvider), and the assistant's
 * changes come back as AG-UI state events and fill the card. The user reviews them and can change them again.
 */
(function () {
    'use strict';

    // ai-chat.js keeps what a script registered before it loaded.
    var registry = window.crestAppsAGUI = window.crestAppsAGUI || {};

    function setStateProvider(provider) {
        if (typeof registry.registerStateProvider === 'function') {
            registry.registerStateProvider(provider);
        } else {
            registry._stateProvider = provider;
        }
    }

    function start() {
        var form = document.getElementById('agui-playground-state');

        if (!form) {
            return;
        }

        var fields = Array.prototype.slice.call(form.querySelectorAll('[data-state-field]'));

        function read() {
            var details = {};

            fields.forEach(function (field) {
                details[field.getAttribute('data-state-field')] = field.value;
            });

            return { orderDetails: details };
        }

        function apply(state) {
            var details = (state && state.orderDetails) || {};

            fields.forEach(function (field) {
                var name = field.getAttribute('data-state-field');
                var value = details[name] == null ? '' : String(details[name]);

                if (field.value !== value) {
                    field.value = value;
                    field.classList.add('border-success');

                    setTimeout(function () {
                        field.classList.remove('border-success');
                    }, 2000);
                }
            });
        }

        setStateProvider({ get: read, apply: apply });
    }

    // A page that renders the card later (a Blazor page) calls start() itself.
    window.aguiPlayground = { start: start };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', start);
    } else {
        start();
    }
})();
