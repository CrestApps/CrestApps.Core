/*
 * Shared realtime (speech-to-speech) audio controller for interaction-style chat surfaces.
 *
 * Encapsulates the browser side of a realtime voice conversation: PCM16 mic capture and streaming,
 * PCM playback with volume, the half-duplex echo guard, push-to-talk, the per-device audio settings
 * popover, and switching the input between text and audio-only when a realtime deployment is selected.
 *
 * It is host-agnostic: the host (an MVC inline script or the Blazor chat-interaction Vue app) supplies the
 * SignalR connection, a sendStart callback (so each surface can address its own hub method/identifier),
 * element selectors, and a few display hooks. Reused so realtime lives in one place, not one copy per host.
 *
 * Usage:
 *   var controller = window.CoreAIRealtime.attach({
 *       connection: conn,
 *       ensureConnected: function () { return startPromise; },     // () => Promise<any>
 *       sendStart: function (subject, voice, language, silenceMs, vadThreshold) { ... },
 *       voiceName: 'alloy',
 *       capableDeployments: ['gpt-realtime'],
 *       realtimeEnabled: true,
 *       selectors: { realtimeButton, input, sendButton, micButton, conversationButton, deploymentSelect },
 *       onActivate: function () { ... },        // realtime session started (host: show transcripts)
 *       onDeactivate: function () { ... },       // realtime session stopped
 *       onEnterRealtimeMode: function () { ... } // switched to audio-only (host: stop any STT recording)
 *   });
 *   // then route audio to it: controller.receivePcm(bytes)   // from ReceiveAudioChunk 'audio/pcm'
 */
(function (window, document) {
    'use strict';

    var REALTIME_SAMPLE_RATE = 24000;
    // While the assistant is playing back, plus this hangover for the room echo tail, the microphone uplink is
    // muted so the model never hears (and answers) its own voice through open speakers.
    var REALTIME_ECHO_HANGOVER_SEC = 0.25;

    function attach(opts) {
        opts = opts || {};
        var connection = opts.connection;
        var sel = opts.selectors || {};
        var realtimeVoiceName = opts.voiceName || '';
        var realtimeCapableDeployments = (opts.capableDeployments || []).map(function (n) { return (n || '').toLowerCase(); });
        var isRealtimeMode = !!opts.realtimeEnabled;
        var noop = function () { };
        var onActivate = opts.onActivate || noop;
        var onDeactivate = opts.onDeactivate || noop;
        var onEnterRealtimeMode = opts.onEnterRealtimeMode || noop;
        var ensureConnected = opts.ensureConnected || function () { return Promise.resolve(true); };
        var sendStart = opts.sendStart || noop;
        // Optional: resolve the assistant voice live at session start (e.g. from a settings picker) so it
        // reflects the current selection rather than the value captured when the controller was attached.
        var getVoiceName = opts.getVoiceName || null;
        // Optional server-relay WebRTC transport: when the server advertises it and the callback is supplied, the
        // controller uses WebRTC (real acoustic echo cancellation) instead of the PCM-over-SignalR path.
        var sendStartWebRtc = opts.sendStartWebRtc || null;
        var webRtcEnabled = opts.webRtcEnabled === true && typeof window.RTCPeerConnection === 'function' && !!sendStartWebRtc;
        var webRtcIceServers = Array.isArray(opts.webRtcIceServers) && opts.webRtcIceServers.length
            ? opts.webRtcIceServers
            : [{ urls: 'stun:stun.l.google.com:19302' }];

        function q(name) { var s = sel[name]; return s ? document.querySelector(s) : null; }

        var isRealtimeActive = false;
        var realtimeStream = null, realtimeAudioCtx = null, realtimeSubject = null, realtimeProcessor = null,
            realtimeMicSource = null, realtimeZeroGain = null, realtimeGain = null, realtimePlayHead = 0, realtimeSources = [];
        // WebRTC transport state.
        var realtimeIsWebRtc = false, realtimePc = null, realtimeRemoteAudioEl = null, realtimeWebRtcHandlersBound = false,
            realtimeRemoteDescriptionSet = false, realtimePendingIce = [];

        // Per-device audio preferences (barge-in, echo guard, push-to-talk, volume, mic, etc.).
        var realtimeBargeIn = true, realtimeHangoverSec = 0.25, realtimePushToTalk = false, realtimePttActive = false,
            realtimePttBound = false, realtimePttKeyDown = null, realtimePttKeyUp = null, realtimePttButton = null, realtimePttUiEl = null,
            realtimeVolume = 1, realtimeMicDeviceId = '', realtimeNoiseSuppression = true, realtimeAutoGain = true, realtimeLanguage = '',
            realtimeTuneTurnDetection = false, realtimeSilenceMs = 500, realtimeVadThreshold = 0.5, realtimeAudioSettingsBuilt = false;

        // Per-device audio preferences live in localStorage because they depend on the listener's hardware
        // (headset vs open speakers), not on the interaction.
        function loadRealtimeAudioPrefs() {
            var prefs = { bargeIn: true, hangoverMs: 500, pushToTalk: false, volume: 1, micDeviceId: '', noiseSuppression: true, autoGain: true, language: '', tuneTurnDetection: false, silenceMs: 500, vadThreshold: 0.5 };
            try {
                var raw = window.localStorage.getItem('coreai.realtime.audioPrefs');
                if (raw) {
                    var parsed = JSON.parse(raw);
                    if (typeof parsed.bargeIn === 'boolean') { prefs.bargeIn = parsed.bargeIn; }
                    if (typeof parsed.pushToTalk === 'boolean') { prefs.pushToTalk = parsed.pushToTalk; }
                    if (typeof parsed.noiseSuppression === 'boolean') { prefs.noiseSuppression = parsed.noiseSuppression; }
                    if (typeof parsed.autoGain === 'boolean') { prefs.autoGain = parsed.autoGain; }
                    if (typeof parsed.tuneTurnDetection === 'boolean') { prefs.tuneTurnDetection = parsed.tuneTurnDetection; }
                    if (typeof parsed.hangoverMs === 'number' && isFinite(parsed.hangoverMs)) { prefs.hangoverMs = Math.min(2000, Math.max(0, parsed.hangoverMs)); }
                    if (typeof parsed.silenceMs === 'number' && isFinite(parsed.silenceMs)) { prefs.silenceMs = Math.min(2000, Math.max(100, parsed.silenceMs)); }
                    if (typeof parsed.vadThreshold === 'number' && isFinite(parsed.vadThreshold)) { prefs.vadThreshold = Math.min(1, Math.max(0, parsed.vadThreshold)); }
                    if (typeof parsed.volume === 'number' && isFinite(parsed.volume)) { prefs.volume = Math.min(1, Math.max(0, parsed.volume)); }
                    if (typeof parsed.micDeviceId === 'string') { prefs.micDeviceId = parsed.micDeviceId; }
                    if (typeof parsed.language === 'string') { prefs.language = parsed.language; }
                }
            } catch (err) { /* storage unavailable or blocked */ }
            return prefs;
        }

        function saveRealtimeAudioPrefs(prefs) {
            try { window.localStorage.setItem('coreai.realtime.audioPrefs', JSON.stringify(prefs)); } catch (err) { }
        }

        function applyRealtimeAudioPrefs(prefs) {
            realtimeBargeIn = !!prefs.bargeIn;
            realtimeHangoverSec = (typeof prefs.hangoverMs === 'number' ? prefs.hangoverMs : 500) / 1000;
            realtimePushToTalk = !!prefs.pushToTalk;
            realtimeVolume = (typeof prefs.volume === 'number') ? prefs.volume : 1;
            realtimeMicDeviceId = prefs.micDeviceId || '';
            realtimeNoiseSuppression = prefs.noiseSuppression !== false;
            realtimeAutoGain = prefs.autoGain !== false;
            realtimeLanguage = prefs.language || '';
            realtimeTuneTurnDetection = !!prefs.tuneTurnDetection;
            realtimeSilenceMs = (typeof prefs.silenceMs === 'number') ? prefs.silenceMs : 500;
            realtimeVadThreshold = (typeof prefs.vadThreshold === 'number') ? prefs.vadThreshold : 0.5;
            if (realtimeGain) { realtimeGain.gain.value = realtimeVolume; }
        }

        // Push-to-talk: hold Space to open the mic. Bound only while a realtime session is active so it never
        // interferes with normal typing. Capture phase + preventDefault so Space never scrolls the page or
        // activates a focused button (Space-up on a focused button fires a click that would toggle the session).
        function attachRealtimePushToTalk() {
            if (realtimePttBound) { return; }
            realtimePttBound = true;
            realtimePttActive = false;
            realtimePttKeyDown = function (e) {
                if (realtimePushToTalk && (e.code === 'Space' || e.key === ' ')) {
                    e.preventDefault();
                    if (!e.repeat) { setRealtimePttActive(true); }
                }
            };
            realtimePttKeyUp = function (e) {
                if (realtimePushToTalk && (e.code === 'Space' || e.key === ' ')) {
                    e.preventDefault();
                    setRealtimePttActive(false);
                }
            };
            document.addEventListener('keydown', realtimePttKeyDown, true);
            document.addEventListener('keyup', realtimePttKeyUp, true);
        }

        function detachRealtimePushToTalk() {
            if (!realtimePttBound) { return; }
            realtimePttBound = false;
            realtimePttActive = false;
            try { document.removeEventListener('keydown', realtimePttKeyDown, true); } catch (err) { }
            try { document.removeEventListener('keyup', realtimePttKeyUp, true); } catch (err) { }
        }

        function setRealtimePttActive(active) {
            realtimePttActive = active;
            if (realtimePttButton) {
                realtimePttButton.classList.toggle('btn-danger', active);
                realtimePttButton.classList.toggle('btn-outline-primary', !active);
                realtimePttButton.innerHTML = active
                    ? '<i class="bi bi-mic-fill me-1"></i> Listening…'
                    : '<i class="bi bi-mic me-1"></i> Hold to talk';
            }
        }

        // Shows a "hold to talk" control + hint while a push-to-talk realtime session is active.
        function buildRealtimePttUi() {
            var realtimeBtn = q('realtimeButton');
            if (!realtimeBtn || realtimePttUiEl) { return; }
            var coarse = window.matchMedia && window.matchMedia('(pointer: coarse)').matches;
            var bar = document.createElement('div');
            bar.className = 'text-center mt-2';
            var btn = document.createElement('button');
            btn.type = 'button';
            btn.className = 'btn btn-outline-primary btn-sm';
            btn.innerHTML = '<i class="bi bi-mic me-1"></i> Hold to talk';
            var hint = document.createElement('div');
            hint.className = 'form-text mt-1 mb-0';
            hint.innerHTML = coarse
                ? 'Push-to-talk is on — press and hold the button to speak.'
                : 'Push-to-talk is on — hold <kbd>Space</kbd> or this button to speak.';
            bar.appendChild(btn);
            bar.appendChild(hint);
            var row = realtimeBtn.parentElement;
            if (row && row.parentElement) { row.parentElement.appendChild(bar); }
            else { realtimeBtn.insertAdjacentElement('afterend', bar); }
            realtimePttButton = btn;
            realtimePttUiEl = bar;
            btn.addEventListener('pointerdown', function (e) { e.preventDefault(); setRealtimePttActive(true); });
            btn.addEventListener('pointerup', function () { setRealtimePttActive(false); });
            btn.addEventListener('pointerleave', function () { setRealtimePttActive(false); });
            btn.addEventListener('pointercancel', function () { setRealtimePttActive(false); });
            setRealtimePttActive(false);
        }

        function removeRealtimePttUi() {
            realtimePttButton = null;
            if (realtimePttUiEl) { try { realtimePttUiEl.remove(); } catch (err) { } realtimePttUiEl = null; }
        }

        // Builds a gear-triggered popover next to the realtime button with the per-device audio settings.
        function setupRealtimeAudioSettings() {
            var realtimeBtn = q('realtimeButton');
            if (!realtimeBtn || realtimeAudioSettingsBuilt) { return; }
            realtimeAudioSettingsBuilt = true;

            var prefs = loadRealtimeAudioPrefs();
            applyRealtimeAudioPrefs(prefs);

            var wrap = document.createElement('span');
            wrap.id = 'realtime-audio-settings';
            wrap.style.position = 'relative';
            wrap.style.display = 'inline-flex';

            var gear = document.createElement('button');
            gear.type = 'button';
            gear.className = 'btn btn-outline-secondary';
            gear.title = 'Realtime audio settings';
            gear.innerHTML = '<i class="bi bi-gear"></i>';

            var langs = [['en', 'English'], ['es', 'Spanish'], ['fr', 'French'], ['de', 'German'], ['it', 'Italian'], ['pt', 'Portuguese'], ['nl', 'Dutch'], ['zh', 'Chinese'], ['ja', 'Japanese'], ['ko', 'Korean'], ['ar', 'Arabic'], ['hi', 'Hindi'], ['ru', 'Russian']];
            var browserPrimary = (navigator.language || 'en').split('-')[0].toLowerCase();
            var browserName = (langs.filter(function (l) { return l[0] === browserPrimary; })[0] || [browserPrimary, browserPrimary.toUpperCase()])[1];
            var langOptions = '<option value=""' + (prefs.language === '' ? ' selected' : '') + '>Auto (' + browserName + ')</option>' +
                langs.map(function (l) { return '<option value="' + l[0] + '"' + (prefs.language === l[0] ? ' selected' : '') + '>' + l[1] + '</option>'; }).join('');

            var panel = document.createElement('div');
            panel.className = 'card shadow-sm';
            panel.style.cssText = 'position:absolute;bottom:calc(100% + 6px);right:0;z-index:1080;width:290px;max-height:70vh;overflow:auto;display:none;';
            panel.innerHTML =
                '<div class="card-body p-3">' +
                '<div class="fw-semibold mb-2" style="font-size:0.85rem;">Realtime audio</div>' +
                '<div class="form-check form-switch mb-1">' +
                '<input class="form-check-input js-barge" type="checkbox"' + (prefs.bargeIn ? ' checked' : '') + '>' +
                '<label class="form-check-label">Allow interruptions (barge-in)</label>' +
                '</div>' +
                '<div class="form-text mb-2">Echo cancellation always runs, including with barge-in on. On (default) keeps the mic open so you can talk over the assistant. Turn <strong>off</strong> to also mute the mic while it speaks (below) — best for loud open speakers.</div>' +
                '<div class="js-hangover-wrap mb-2">' +
                '<label class="form-label mb-1 d-block" style="font-size:0.8rem;">Echo guard delay: <strong class="js-hangover-val">' + prefs.hangoverMs + '</strong> ms</label>' +
                '<input type="range" class="form-range js-hangover" min="0" max="1000" step="50" value="' + prefs.hangoverMs + '">' +
                '</div>' +
                '<div class="form-check form-switch mb-1">' +
                '<input class="form-check-input js-ptt" type="checkbox"' + (prefs.pushToTalk ? ' checked' : '') + '>' +
                '<label class="form-check-label">Push-to-talk</label>' +
                '</div>' +
                '<div class="form-text mb-2">Hold <kbd>Space</kbd> to talk (desktop). Best for very noisy rooms.</div>' +
                '<label class="form-label mb-1 d-block" style="font-size:0.8rem;">Assistant volume: <strong class="js-vol-val">' + Math.round(prefs.volume * 100) + '</strong>%</label>' +
                '<input type="range" class="form-range js-vol mb-2" min="0" max="100" step="5" value="' + Math.round(prefs.volume * 100) + '">' +
                '<label class="form-label mb-1 d-block" style="font-size:0.8rem;">Microphone</label>' +
                '<select class="form-select form-select-sm js-mic mb-2"><option value="">Default microphone</option></select>' +
                '<label class="form-label mb-1 d-block" style="font-size:0.8rem;">Language</label>' +
                '<select class="form-select form-select-sm js-lang mb-2">' + langOptions + '</select>' +
                '<div class="form-check form-switch mb-1">' +
                '<input class="form-check-input js-tune" type="checkbox"' + (prefs.tuneTurnDetection ? ' checked' : '') + '>' +
                '<label class="form-check-label">Fine-tune turn detection</label>' +
                '</div>' +
                '<div class="js-tune-wrap mb-2"' + (prefs.tuneTurnDetection ? '' : ' style="display:none;"') + '>' +
                '<label class="form-label mb-1 d-block" style="font-size:0.8rem;">Reply after silence: <strong class="js-silence-val">' + prefs.silenceMs + '</strong> ms</label>' +
                '<input type="range" class="form-range js-silence" min="100" max="2000" step="100" value="' + prefs.silenceMs + '">' +
                '<label class="form-label mb-1 d-block" style="font-size:0.8rem;">Speech detection threshold: <strong class="js-thr-val">' + prefs.vadThreshold.toFixed(2) + '</strong></label>' +
                '<input type="range" class="form-range js-thr" min="0" max="1" step="0.05" value="' + prefs.vadThreshold + '">' +
                '<div class="form-text">Longer silence lets you pause without being cut off. Higher threshold needs louder speech, rejecting noise and echo. Applies to your next session.</div>' +
                '</div>' +
                '<div class="form-check form-switch mb-1">' +
                '<input class="form-check-input js-ns" type="checkbox"' + (prefs.noiseSuppression ? ' checked' : '') + '>' +
                '<label class="form-check-label">Noise suppression</label>' +
                '</div>' +
                '<div class="form-check form-switch mb-1">' +
                '<input class="form-check-input js-agc" type="checkbox"' + (prefs.autoGain ? ' checked' : '') + '>' +
                '<label class="form-check-label">Automatic gain</label>' +
                '</div>' +
                '<div class="form-text">Microphone, noise, gain and language changes apply to your next voice session.</div>' +
                '</div>';

            wrap.appendChild(gear);
            wrap.appendChild(panel);
            realtimeBtn.insertAdjacentElement('afterend', wrap);

            var bargeInput = panel.querySelector('.js-barge');
            var hangoverInput = panel.querySelector('.js-hangover');
            var hangoverVal = panel.querySelector('.js-hangover-val');
            var hangoverWrap = panel.querySelector('.js-hangover-wrap');
            var pttInput = panel.querySelector('.js-ptt');
            var volInput = panel.querySelector('.js-vol');
            var volVal = panel.querySelector('.js-vol-val');
            var micSelect = panel.querySelector('.js-mic');
            var langSelect = panel.querySelector('.js-lang');
            var nsInput = panel.querySelector('.js-ns');
            var agcInput = panel.querySelector('.js-agc');
            var tuneInput = panel.querySelector('.js-tune');
            var tuneWrap = panel.querySelector('.js-tune-wrap');
            var silenceInput = panel.querySelector('.js-silence');
            var silenceVal = panel.querySelector('.js-silence-val');
            var thrInput = panel.querySelector('.js-thr');
            var thrVal = panel.querySelector('.js-thr-val');

            function reflect() {
                var guardActive = !bargeInput.checked && !pttInput.checked;
                hangoverWrap.style.opacity = guardActive ? '1' : '0.5';
                hangoverInput.disabled = !guardActive;
                tuneWrap.style.display = tuneInput.checked ? '' : 'none';
            }
            reflect();

            function persist() {
                var next = {
                    bargeIn: bargeInput.checked,
                    hangoverMs: parseInt(hangoverInput.value, 10) || 0,
                    pushToTalk: pttInput.checked,
                    volume: (parseInt(volInput.value, 10) || 0) / 100,
                    micDeviceId: micSelect.value || '',
                    noiseSuppression: nsInput.checked,
                    autoGain: agcInput.checked,
                    language: langSelect.value || '',
                    tuneTurnDetection: tuneInput.checked,
                    silenceMs: parseInt(silenceInput.value, 10) || 500,
                    vadThreshold: parseFloat(thrInput.value)
                };
                applyRealtimeAudioPrefs(next);
                saveRealtimeAudioPrefs(next);
            }

            function populateMics() {
                if (!navigator.mediaDevices || !navigator.mediaDevices.enumerateDevices) { return; }
                navigator.mediaDevices.enumerateDevices().then(function (devices) {
                    var options = ['<option value="">Default microphone</option>'];
                    devices.filter(function (d) { return d.kind === 'audioinput'; }).forEach(function (d, i) {
                        var label = d.label || ('Microphone ' + (i + 1));
                        options.push('<option value="' + d.deviceId + '"' + (d.deviceId === realtimeMicDeviceId ? ' selected' : '') + '>' + label.replace(/</g, '') + '</option>');
                    });
                    micSelect.innerHTML = options.join('');
                }).catch(function () { });
            }
            populateMics();

            function constrainPanelHeight() {
                // Keep the popover inside its container: cap its height to the space above the button up to
                // the nearest scroll/fixed ancestor, so it scrolls instead of spilling out the top.
                var rect = gear.getBoundingClientRect();
                var boundaryTop = 8;
                var node = panel.parentElement ? panel.parentElement.parentElement : null;
                while (node && node !== document.body && node !== document.documentElement) {
                    var s = window.getComputedStyle(node);
                    if (s.overflowY === 'auto' || s.overflowY === 'scroll' || s.overflowY === 'hidden' || s.position === 'fixed') {
                        boundaryTop = Math.max(boundaryTop, node.getBoundingClientRect().top);
                        break;
                    }
                    node = node.parentElement;
                }
                panel.style.maxHeight = Math.max(160, rect.top - boundaryTop - 10) + 'px';
            }
            gear.addEventListener('click', function (e) {
                e.stopPropagation();
                var showing = panel.style.display === 'none';
                if (showing) { constrainPanelHeight(); populateMics(); }
                panel.style.display = showing ? 'block' : 'none';
            });
            panel.addEventListener('click', function (e) { e.stopPropagation(); });
            document.addEventListener('click', function () { panel.style.display = 'none'; });
            bargeInput.addEventListener('change', function () { reflect(); persist(); });
            pttInput.addEventListener('change', function () { reflect(); persist(); });
            hangoverInput.addEventListener('input', function () { hangoverVal.textContent = hangoverInput.value; persist(); });
            volInput.addEventListener('input', function () { volVal.textContent = volInput.value; persist(); });
            micSelect.addEventListener('change', persist);
            langSelect.addEventListener('change', persist);
            nsInput.addEventListener('change', persist);
            agcInput.addEventListener('change', persist);
            tuneInput.addEventListener('change', function () { reflect(); persist(); });
            silenceInput.addEventListener('input', function () { silenceVal.textContent = silenceInput.value; persist(); });
            thrInput.addEventListener('input', function () { thrVal.textContent = parseFloat(thrInput.value).toFixed(2); persist(); });
        }

        function updateRealtimeButton() {
            var realtimeBtn = q('realtimeButton');
            if (!realtimeBtn) { return; }
            if (isRealtimeActive) {
                realtimeBtn.classList.add('active', 'btn-primary');
                realtimeBtn.classList.remove('btn-dark', 'btn-outline-dark', 'btn-outline-secondary');
                realtimeBtn.title = realtimeBtn.getAttribute('data-end-title') || 'End Conversation';
                var endHtml = realtimeBtn.getAttribute('data-end-html');
                if (endHtml) { realtimeBtn.replaceChildren(window.DOMPurify.sanitize(endHtml, { RETURN_DOM_FRAGMENT: true })); }
            } else {
                realtimeBtn.classList.remove('active', 'btn-primary', 'btn-dark', 'btn-outline-secondary');
                realtimeBtn.classList.add('btn-outline-dark');
                realtimeBtn.blur();
                realtimeBtn.title = realtimeBtn.getAttribute('data-start-title') || 'Start speaking';
                var startHtml = realtimeBtn.getAttribute('data-start-html');
                if (startHtml) { realtimeBtn.replaceChildren(window.DOMPurify.sanitize(startHtml, { RETURN_DOM_FRAGMENT: true })); }
            }
        }

        // Switches the input between text and audio-only (realtime). Called at load and whenever the selected
        // deployment changes to/from a realtime model.
        function applyRealtimeMode(enable) {
            isRealtimeMode = enable;

            var realtimeBtn = q('realtimeButton');
            var micBtn = q('micButton');
            var conversationBtn = q('conversationButton');
            var inputEl = q('input');
            var sendBtn = q('sendButton');
            var audioSettings = document.getElementById('realtime-audio-settings');

            if (enable) {
                onEnterRealtimeMode();
                if (inputEl) { inputEl.hidden = true; }
                if (sendBtn) { sendBtn.hidden = true; }
                if (micBtn) { micBtn.hidden = true; }
                if (conversationBtn) { conversationBtn.hidden = true; }
                if (realtimeBtn) { realtimeBtn.hidden = false; }
                if (audioSettings) { audioSettings.hidden = false; }
            } else {
                if (isRealtimeActive) { stopRealtimeConversation(); }
                if (realtimeBtn) { realtimeBtn.hidden = true; }
                if (audioSettings) { audioSettings.hidden = true; }
                if (inputEl) { inputEl.hidden = false; }
                if (sendBtn) { sendBtn.hidden = false; }
                if (micBtn) { micBtn.hidden = false; }
                if (conversationBtn) { conversationBtn.hidden = false; }
            }

            updateRealtimeButton();
        }

        function startRealtimeConversation() {
            if (!isRealtimeMode || isRealtimeActive || !connection) { return; }

            applyRealtimeAudioPrefs(loadRealtimeAudioPrefs());

            // Prefer the WebRTC transport when the server advertises it: the browser's echo canceller references
            // the assistant's media track, so the model can ignore its own voice with the mic open (open rooms).
            if (webRtcEnabled) {
                startRealtimeWebRtcConversation();

                return;
            }

            attachRealtimePushToTalk();
            // Drop focus from the just-clicked button so Space acts as push-to-talk, not a re-click.
            var focusedBtn = q('realtimeButton');
            if (focusedBtn) { try { focusedBtn.blur(); } catch (err) { } }

            var audioConstraints = {
                channelCount: 1,
                echoCancellation: { ideal: true },
                noiseSuppression: realtimeNoiseSuppression !== false,
                autoGainControl: realtimeAutoGain !== false,
                // Ask for the strongest available acoustic echo cancellation so the model can ignore its own
                // voice in an open room (loud speakers + open mic). These are Chromium hints requested as
                // "ideal", so any browser that doesn't support them simply ignores them — never a hard failure.
                // echoCancellationType 'system' prefers the OS/hardware AEC over the browser's software AEC.
                echoCancellationType: { ideal: 'system' },
                voiceIsolation: { ideal: true }
            };
            if (realtimeMicDeviceId) { audioConstraints.deviceId = { exact: realtimeMicDeviceId }; }

            navigator.mediaDevices.getUserMedia({ audio: audioConstraints })
                .then(function (stream) {
                    ensureConnected()
                        .then(function () {
                            realtimeStream = stream;
                            isRealtimeActive = true;
                            onActivate();
                            updateRealtimeButton();

                            var AudioContextCtor = window.AudioContext || window.webkitAudioContext;
                            realtimeAudioCtx = new AudioContextCtor({ sampleRate: REALTIME_SAMPLE_RATE });
                            realtimePlayHead = 0;
                            realtimeSources = [];
                            realtimeGain = realtimeAudioCtx.createGain();
                            realtimeGain.gain.value = (realtimeVolume != null) ? realtimeVolume : 1;
                            realtimeGain.connect(realtimeAudioCtx.destination);
                            realtimeSubject = new window.signalR.Subject();

                            var source = realtimeAudioCtx.createMediaStreamSource(stream);
                            var processor = realtimeAudioCtx.createScriptProcessor(4096, 1, 1);
                            realtimeProcessor = processor;
                            realtimeMicSource = source;

                            processor.onaudioprocess = function (event) {
                                var input = event.inputBuffer.getChannelData(0);
                                // Always send a frame (silence when muted) so the server keeps a continuous audio
                                // stream and its voice-activity detector promptly notices the pause and responds.
                                // Muted cases: push-to-talk not held, or the echo guard while the assistant plays back.
                                var muted;
                                if (realtimePushToTalk) {
                                    muted = !realtimePttActive;
                                } else {
                                    muted = !realtimeBargeIn && realtimeAudioCtx && realtimeAudioCtx.currentTime < realtimePlayHead + realtimeHangoverSec;
                                }
                                var pcm = new Int16Array(input.length);
                                if (!muted) {
                                    for (var i = 0; i < input.length; i++) {
                                        var s = Math.max(-1, Math.min(1, input[i]));
                                        pcm[i] = s < 0 ? s * 0x8000 : s * 0x7FFF;
                                    }
                                }
                                var bytes = new Uint8Array(pcm.buffer);
                                var binary = '';
                                for (var b = 0; b < bytes.length; b++) { binary += String.fromCharCode(bytes[b]); }
                                try { realtimeSubject.next(btoa(binary)); } catch (err) { /* completed */ }
                            };

                            // A zero-gain node keeps the processor alive without echoing the mic to the speakers.
                            var zeroGain = realtimeAudioCtx.createGain();
                            zeroGain.gain.value = 0;
                            realtimeZeroGain = zeroGain;
                            source.connect(processor);
                            processor.connect(zeroGain);
                            zeroGain.connect(realtimeAudioCtx.destination);

                            var language = realtimeLanguage || navigator.language || document.documentElement.lang || 'en-US';
                            var silenceMs = realtimeTuneTurnDetection ? realtimeSilenceMs : null;
                            var vadThreshold = realtimeTuneTurnDetection ? realtimeVadThreshold : null;
                            var voice = (getVoiceName && getVoiceName()) || realtimeVoiceName || '';
                            sendStart(realtimeSubject, voice, language, silenceMs, vadThreshold, realtimeBargeIn);
                            if (realtimePushToTalk) { buildRealtimePttUi(); }
                        })
                        .catch(function (err) {
                            stream.getTracks().forEach(function (track) { track.stop(); });
                            isRealtimeActive = false;
                            onDeactivate();
                            updateRealtimeButton();
                            console.error('The realtime conversation could not start because the chat connection is not available.', err);
                        });
                })
                .catch(function (err) {
                    console.error('Microphone access denied:', err);
                    isRealtimeActive = false;
                    onDeactivate();
                    updateRealtimeButton();
                });
        }

        // --- WebRTC (server-relay) transport ---

        function startRealtimeWebRtcConversation() {
            var micConstraints = {
                echoCancellation: { ideal: true },
                noiseSuppression: realtimeNoiseSuppression !== false,
                autoGainControl: realtimeAutoGain !== false,
                echoCancellationType: { ideal: 'system' },
                voiceIsolation: { ideal: true }
            };
            if (realtimeMicDeviceId) { micConstraints.deviceId = { exact: realtimeMicDeviceId }; }

            navigator.mediaDevices.getUserMedia({ audio: micConstraints })
                .then(function (stream) {
                    ensureConnected()
                        .then(function () {
                            realtimeStream = stream;
                            isRealtimeActive = true;
                            realtimeIsWebRtc = true;
                            onActivate();
                            updateRealtimeButton();

                            realtimeRemoteDescriptionSet = false;
                            realtimePendingIce = [];
                            var pc = new RTCPeerConnection({ iceServers: webRtcIceServers });
                            realtimePc = pc;

                            stream.getAudioTracks().forEach(function (track) { pc.addTrack(track, stream); });

                            // Play the assistant's remote track through a hidden <audio> element. This gives the
                            // browser's echo canceller a reference to remove the assistant's voice from the mic.
                            var audioEl = document.createElement('audio');
                            audioEl.autoplay = true;
                            audioEl.style.display = 'none';
                            document.body.appendChild(audioEl);
                            realtimeRemoteAudioEl = audioEl;
                            pc.ontrack = function (e) { if (e.streams && e.streams[0]) { audioEl.srcObject = e.streams[0]; } };

                            pc.onicecandidate = function (e) {
                                if (e.candidate) {
                                    try { connection.send('AddRealtimeIceCandidate', e.candidate.candidate, e.candidate.sdpMid || '', e.candidate.sdpMLineIndex || 0); } catch (err) { }
                                }
                            };
                            pc.onconnectionstatechange = function () {
                                if ((pc.connectionState === 'failed' || pc.connectionState === 'closed') && isRealtimeActive) {
                                    stopRealtimeConversation();
                                }
                            };

                            bindWebRtcSignalingHandlers();

                            pc.createOffer()
                                .then(function (offer) { return pc.setLocalDescription(offer).then(function () { return offer; }); })
                                .then(function (offer) {
                                    var language = realtimeLanguage || navigator.language || document.documentElement.lang || 'en-US';
                                    var silenceMs = realtimeTuneTurnDetection ? realtimeSilenceMs : null;
                                    var vadThreshold = realtimeTuneTurnDetection ? realtimeVadThreshold : null;
                                    var voice = (getVoiceName && getVoiceName()) || realtimeVoiceName || '';
                                    sendStartWebRtc(offer.sdp, voice, language, silenceMs, vadThreshold, realtimeBargeIn);
                                })
                                .catch(function (err) {
                                    console.error('Failed to create the WebRTC offer.', err);
                                    stopRealtimeConversation();
                                });
                        })
                        .catch(function (err) {
                            stream.getTracks().forEach(function (t) { t.stop(); });
                            isRealtimeActive = false;
                            realtimeIsWebRtc = false;
                            onDeactivate();
                            updateRealtimeButton();
                            console.error('The realtime WebRTC conversation could not start because the chat connection is not available.', err);
                        });
                })
                .catch(function (err) {
                    console.error('Microphone access denied:', err);
                    isRealtimeActive = false;
                    realtimeIsWebRtc = false;
                    onDeactivate();
                    updateRealtimeButton();
                });
        }

        function bindWebRtcSignalingHandlers() {
            if (realtimeWebRtcHandlersBound) { return; }
            realtimeWebRtcHandlersBound = true;

            connection.on('ReceiveRealtimeAnswer', function (sdp) {
                if (!realtimePc) { return; }
                realtimePc.setRemoteDescription({ type: 'answer', sdp: sdp })
                    .then(function () {
                        realtimeRemoteDescriptionSet = true;
                        // Flush any ICE candidates that arrived before the answer was applied.
                        var pending = realtimePendingIce;
                        realtimePendingIce = [];
                        pending.forEach(function (init) {
                            try { realtimePc.addIceCandidate(init); } catch (err) { }
                        });
                    })
                    .catch(function (err) { console.error('Failed to apply the WebRTC answer.', err); });
            });
            connection.on('ReceiveRealtimeIceCandidate', function (candidate, sdpMid, sdpMLineIndex) {
                if (!realtimePc || !candidate) { return; }
                var init = { candidate: candidate, sdpMid: sdpMid, sdpMLineIndex: sdpMLineIndex };
                // A candidate can only be added after the remote description (answer) is set; buffer until then.
                if (realtimeRemoteDescriptionSet) {
                    try { realtimePc.addIceCandidate(init); } catch (err) { }
                } else {
                    realtimePendingIce.push(init);
                }
            });
        }

        function stopRealtimeWebRtc() {
            if (realtimePc) {
                try { realtimePc.close(); } catch (err) { }
                realtimePc = null;
            }
            if (realtimeRemoteAudioEl) {
                try { realtimeRemoteAudioEl.srcObject = null; realtimeRemoteAudioEl.remove(); } catch (err) { }
                realtimeRemoteAudioEl = null;
            }
            realtimeIsWebRtc = false;
        }

        function stopRealtimeConversation() {
            if (!isRealtimeActive) { return; }

            isRealtimeActive = false;
            onDeactivate();
            updateRealtimeButton();
            detachRealtimePushToTalk();
            removeRealtimePttUi();

            if (realtimeIsWebRtc) {
                stopRealtimeWebRtc();
                try { if (realtimeStream) { realtimeStream.getTracks().forEach(function (t) { t.stop(); }); } } catch (err) { }
                realtimeStream = null;

                return;
            }

            try { if (realtimeSubject) { realtimeSubject.complete(); } } catch (err) { /* already completed */ }
            realtimeSubject = null;

            try { if (realtimeProcessor) { realtimeProcessor.disconnect(); realtimeProcessor.onaudioprocess = null; } } catch (err) { }
            try { if (realtimeMicSource) { realtimeMicSource.disconnect(); } } catch (err) { }
            try { if (realtimeZeroGain) { realtimeZeroGain.disconnect(); } } catch (err) { }
            try { if (realtimeGain) { realtimeGain.disconnect(); } } catch (err) { }
            try { if (realtimeStream) { realtimeStream.getTracks().forEach(function (t) { t.stop(); }); } } catch (err) { }
            realtimeProcessor = null;
            realtimeMicSource = null;
            realtimeZeroGain = null;
            realtimeGain = null;
            realtimeStream = null;

            flushRealtimePlayback();
            try { if (realtimeAudioCtx) { realtimeAudioCtx.close(); } } catch (err) { }
            realtimeAudioCtx = null;
        }

        function toggleRealtime() {
            if (isRealtimeActive) { stopRealtimeConversation(); return; }
            startRealtimeConversation();
        }

        function playRealtimePcm(bytes) {
            if (!realtimeAudioCtx || !bytes || bytes.length < 2) { return; }
            var ctx = realtimeAudioCtx;
            var sampleCount = Math.floor(bytes.length / 2);
            var pcm = new Int16Array(bytes.buffer, bytes.byteOffset, sampleCount);
            var f32 = new Float32Array(sampleCount);
            for (var i = 0; i < sampleCount; i++) { f32[i] = pcm[i] / 0x8000; }

            var buffer = ctx.createBuffer(1, sampleCount, ctx.sampleRate);
            buffer.copyToChannel(f32, 0);
            var src = ctx.createBufferSource();
            src.buffer = buffer;
            src.connect(realtimeGain || ctx.destination);

            var now = ctx.currentTime;
            if (realtimePlayHead < now) { realtimePlayHead = now; }
            src.start(realtimePlayHead);
            realtimePlayHead += buffer.duration;
            realtimeSources.push(src);
            src.onended = function () { realtimeSources = realtimeSources.filter(function (s) { return s !== src; }); };
        }

        function flushRealtimePlayback() {
            realtimeSources.forEach(function (s) { try { s.stop(); } catch (err) { } });
            realtimeSources = [];
            if (realtimeAudioCtx) { realtimePlayHead = realtimeAudioCtx.currentTime; }
        }

        function isRealtimeDeployment(deploymentName) {
            if (!deploymentName) { return false; }
            return realtimeCapableDeployments.indexOf(deploymentName.toLowerCase()) !== -1;
        }

        // --- Wire up ---
        var realtimeBtn = q('realtimeButton');
        if (realtimeBtn) {
            realtimeBtn.addEventListener('click', function (e) {
                // Ignore keyboard-synthesized clicks (Space/Enter) during an active realtime session, so a
                // push-to-talk Space press can never toggle the session off.
                if (e && e.detail === 0 && isRealtimeActive) { return; }
                toggleRealtime();
            });
            setupRealtimeAudioSettings();
        }

        var deploymentSelect = q('deploymentSelect');
        if (deploymentSelect) {
            deploymentSelect.addEventListener('change', function () {
                applyRealtimeMode(isRealtimeDeployment(deploymentSelect.value));
            });
        }

        // Apply the initial input mode: audio-only when the interaction already uses a realtime deployment.
        applyRealtimeMode(isRealtimeMode || (deploymentSelect && isRealtimeDeployment(deploymentSelect.value)));

        return {
            toggle: toggleRealtime,
            start: startRealtimeConversation,
            stop: stopRealtimeConversation,
            applyMode: applyRealtimeMode,
            isRealtimeDeployment: isRealtimeDeployment,
            receivePcm: playRealtimePcm,
            isActive: function () { return isRealtimeActive; }
        };
    }

    window.CoreAIRealtime = { attach: attach };
})(window, document);
