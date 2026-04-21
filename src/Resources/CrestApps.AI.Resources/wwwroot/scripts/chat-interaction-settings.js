// Autosave + UI plumbing for the Chat Interaction "Interaction Settings" pane.
// This is the source-of-truth shared between Mvc.Web and Blazor.Web so both
// UIs behave identically.
//
// Usage:
//   window.initializeChatInteractionSettings({
//       itemId: 'abc123',
//       hubUrl: '/hubs/chat-interaction',
//       statusElementId: 'chat-settings-save-status',
//       dataSourceSelectId: 'chat-setting-data-source',
//   });

(function () {
    'use strict';

    function getStatusElement(id) {
        return id ? document.getElementById(id) : null;
    }

    function showSaveIndicator(statusEl, text, cssClass) {
        if (!statusEl) {
            return;
        }

        var statusClasses = ['text-success', 'text-warning', 'text-danger', 'text-bg-light', 'text-bg-success', 'text-bg-warning', 'text-bg-danger'];
        statusEl.classList.remove.apply(statusEl.classList, statusClasses);
        if (cssClass) {
            statusEl.classList.add(cssClass);
        }

        statusEl.textContent = text;
    }

    function getValidationMessage(input) {
        if (!input || input.type !== 'number') {
            return null;
        }

        var value = (input.value || '').trim();
        if (value === '') {
            return null;
        }

        var numericValue = Number(value);
        if (Number.isNaN(numericValue)) {
            return 'Enter a valid number.';
        }

        var min = input.getAttribute('min');
        if (min !== null && min !== '' && numericValue < Number(min)) {
            return 'Value must be at least ' + min + '.';
        }

        var max = input.getAttribute('max');
        if (max !== null && max !== '' && numericValue > Number(max)) {
            return 'Value must be no more than ' + max + '.';
        }

        return null;
    }

    function getOrCreateValidationMessage(input) {
        var message = input.parentElement.querySelector('.invalid-feedback[data-setting-validation]');
        if (!message) {
            message = document.createElement('div');
            message.className = 'invalid-feedback';
            message.dataset.settingValidation = 'true';
            input.parentElement.appendChild(message);
        }

        return message;
    }

    function validateSettingInput(input) {
        var message = getValidationMessage(input);
        var validation = getOrCreateValidationMessage(input);
        var isValid = !message;

        input.classList.toggle('is-invalid', !isValid);
        validation.textContent = message || '';
        validation.style.display = isValid ? 'none' : 'block';

        return isValid;
    }

    function hasInvalidSettings() {
        var isValid = true;
        document.querySelectorAll('.setting-input[type="number"]').forEach(function (input) {
            if (!validateSettingInput(input)) {
                isValid = false;
            }
        });

        return !isValid;
    }

    function collectSettings() {
        var settings = {};

        document.querySelectorAll('.setting-input').forEach(function (el) {
            var key = el.dataset.setting;
            if (!key) {
                return;
            }

            if (el.type === 'checkbox') {
                settings[key] = el.checked;
                return;
            }

            var val = (el.value || '').trim();
            if (el.type === 'number') {
                settings[key] = val === '' ? null : parseFloat(val);
            } else {
                settings[key] = val || null;
            }
        });

        var groups = {};
        document.querySelectorAll('.capability-checkbox').forEach(function (cb) {
            var group = cb.dataset.group;
            if (!group) {
                return;
            }

            if (!groups[group]) {
                groups[group] = [];
            }

            if (cb.checked) {
                groups[group].push(cb.value);
            }
        });

        for (var g in groups) {
            settings[g] = groups[g];
        }

        // Prompt template cards
        var promptTemplates = [];
        document.querySelectorAll('.prompt-template-card').forEach(function (card) {
            var idEl = card.querySelector('.prompt-template-id-input');
            var paramsEl = card.querySelector('.prompt-template-parameters-input');
            var tid = idEl ? idEl.value : (card.dataset.templateId || '');
            if (!tid) {
                return;
            }

            promptTemplates.push({
                templateId: tid,
                promptParameters: paramsEl ? (paramsEl.value || '').trim() : '',
            });
        });

        if (promptTemplates.length > 0) {
            settings.promptTemplateIds = promptTemplates.map(function (t) { return t.templateId; });
            settings.promptTemplates = promptTemplates;
        }

        return settings;
    }

    window.initializeChatInteractionSettings = function (config) {
        config = config || {};

        var itemId = config.itemId;
        if (!itemId) {
            console.warn('initializeChatInteractionSettings: itemId is required.');
            return;
        }

        if (typeof signalR === 'undefined') {
            console.warn('initializeChatInteractionSettings: SignalR client is not loaded.');
            return;
        }

        var hubUrl = config.hubUrl || '/hubs/chat-interaction';
        var statusEl = getStatusElement(config.statusElementId || 'chat-settings-save-status');
        var dataSourceSelect = config.dataSourceSelectId
            ? document.getElementById(config.dataSourceSelectId)
            : document.querySelector('.setting-input[data-setting="dataSourceId"]');

        // Source-dependent visibility (Filter field shows only when a data source is selected).
        function toggleDataSourceDependentFields() {
            var hasDataSource = !!(dataSourceSelect && dataSourceSelect.value && dataSourceSelect.value.trim() !== '');
            document.querySelectorAll('.rag-data-source-filter-dependent').forEach(function (element) {
                element.classList.toggle('d-none', !hasDataSource);
            });
        }

        if (dataSourceSelect) {
            dataSourceSelect.addEventListener('change', toggleDataSourceDependentFields);
        }

        toggleDataSourceDependentFields();

        var connection = new signalR.HubConnectionBuilder()
            .withUrl(hubUrl)
            .withAutomaticReconnect()
            .build();

        var connectionStartPromise = null;

        function ensureConnectionStarted() {
            if (connection.state === signalR.HubConnectionState.Connected) {
                return Promise.resolve();
            }

            if (!connectionStartPromise) {
                connectionStartPromise = connection.start()
                    .catch(function (err) {
                        console.error('SignalR connection error:', err);
                        throw err;
                    })
                    .finally(function () {
                        connectionStartPromise = null;
                    });
            }

            return connectionStartPromise;
        }

        connection.on('SettingsSaved', function (savedItemId) {
            if (savedItemId !== itemId) {
                return;
            }

            showSaveIndicator(statusEl, 'Saved \u2713', 'text-success');
        });

        connection.on('ReceiveError', function (message) {
            console.error('Chat interaction error:', message);
            showSaveIndicator(statusEl, 'Save failed', 'text-danger');
        });

        ensureConnectionStarted().catch(function () {
            showSaveIndicator(statusEl, 'Offline', 'text-danger');
        });

        var debounceTimer = null;

        function debounceSave() {
            clearTimeout(debounceTimer);
            if (hasInvalidSettings()) {
                showSaveIndicator(statusEl, 'Fix errors', 'text-danger');
                return;
            }

            showSaveIndicator(statusEl, 'Saving\u2026', 'text-warning');
            debounceTimer = setTimeout(function () {
                var settings = collectSettings();
                ensureConnectionStarted()
                    .then(function () {
                        return connection.invoke('SaveSettings', itemId, settings);
                    })
                    .catch(function (err) {
                        console.error('Failed to save chat interaction settings:', err);
                        showSaveIndicator(statusEl, 'Save failed', 'text-danger');
                    });
            }, 500);
        }

        document.querySelectorAll('.setting-input').forEach(function (el) {
            if (el.type === 'number') {
                validateSettingInput(el);
            }

            el.addEventListener('input', debounceSave);
            el.addEventListener('change', debounceSave);
            el.addEventListener('blur', debounceSave);
        });

        document.querySelectorAll('.capability-checkbox').forEach(function (cb) {
            cb.addEventListener('change', debounceSave);
        });

        // Expose helpers for callers that add/remove rows dynamically (e.g. prompt templates).
        return {
            scheduleSave: debounceSave,
            collectSettings: collectSettings,
        };
    };
})();
